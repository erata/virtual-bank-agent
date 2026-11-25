using System.Globalization;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Model.Credit;

namespace VirtualBank.Application.Credit.Commands.ApproveCreditApplication;

public enum CreditDecision
{
    Approve,
    Reject,
}

public enum ApprovalExecutionOutcome
{
    Approved,
    Rejected,
    /// <summary>The same confirmation was already executed (idempotency key exists); nothing changed again.</summary>
    AlreadyExecuted,
    /// <summary>The application changed after the decider reviewed it.</summary>
    VersionChanged,
    /// <summary>Database rules (RLS: permission, state, limit, four-eyes) or domain rules refused the change.</summary>
    Refused,
}

public sealed record ApprovalExecution(ApprovalExecutionOutcome Outcome, int Version);

/// <summary>
/// Performs the decision under the dedicated credit_approval database role, the only role allowed to UPDATE an
/// application. RLS re-checks permission, SUBMITTED state, the decider's limit and four-eyes; the optional note
/// (rejection reason / approval comment) is written in the same transaction.
/// </summary>
public interface ICreditApprovalExecutor
{
    Task<ApprovalExecution> DecideAsync(PrincipalContext principal, CreditApplicationId applicationId, int expectedVersion,
        Guid idempotencyKey, CreditDecision decision, string? note, DateTimeOffset decidedAt, CancellationToken cancellationToken);
}

/// <summary>
/// Credit decisions with human confirmation (M6). Preparing checks the rules and returns a confirmation card;
/// nothing changes until the same approver confirms it (<see cref="ActionConfirmation"/>), which re-checks
/// everything with current data. The assistant may prepare a card but can never confirm it.
/// </summary>
public sealed class CreditApproval(
    CreditApplicationReader reader, IResourceAuthorizationService authorization, IAuthorizationAudit audit,
    NoticeCatalog notices, PendingActionFactory actions)
{
    public const string ApproveKind = "credit.approve";
    public const string RejectKind = "credit.reject";
    public const int MaxNoteLength = 500;

    public Task<UseCaseResult<PendingActionView>> PrepareApprovalAsync(PrincipalContext principal, string applicationId,
        string? comment, CancellationToken ct) => PrepareAsync(principal, applicationId, CreditDecision.Approve, comment, ct);

    public Task<UseCaseResult<PendingActionView>> PrepareRejectionAsync(PrincipalContext principal, string applicationId,
        string? reason, CancellationToken ct) => PrepareAsync(principal, applicationId, CreditDecision.Reject, reason, ct);

    private async Task<UseCaseResult<PendingActionView>> PrepareAsync(PrincipalContext principal, string applicationId,
        CreditDecision decision, string? note, CancellationToken ct)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (decision == CreditDecision.Reject && note is null)
            return UseCaseResult<PendingActionView>.Invalid("reason", "A rejection needs a reason.");
        if (note is { Length: > MaxNoteLength })
            return UseCaseResult<PendingActionView>.Invalid("reason", $"At most {MaxNoteLength} characters.");

        var (application, problem) = await AuthorizeAsync<PendingActionView>(principal, applicationId, ct);
        if (problem is not null) return problem;

        var facts = new List<ActionFact>
        {
            new("application", application!.Id.Value),
            new("customer", application.CustomerId),
            new("amount", Money(application.Amount.Amount, application.Amount.Currency)),
            new("purpose", application.Purpose),
            new("limit", principal.ApprovalLimit is { } limit ? Money(limit, "EUR") : "—"),
            new("checks", "submitted, within-limit, four-eyes"),
        };
        if (note is not null) facts.Add(new(decision == CreditDecision.Reject ? "reason" : "comment", note));

        var arguments = new Dictionary<string, string>
        {
            ["applicationId"] = application.Id.Value,
            ["version"] = application.Version.ToString(CultureInfo.InvariantCulture),
        };
        if (note is not null) arguments["note"] = note;
        var action = actions.Create(principal, decision == CreditDecision.Approve ? ApproveKind : RejectKind, arguments, facts);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    /// <summary>Executes a confirmed decision. Called only by the confirmation handlers below.</summary>
    internal async Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action,
        CreditDecision decision, ICreditApprovalExecutor executor, TimeProvider clock, CancellationToken ct)
    {
        var applicationId = action.Arguments["applicationId"];
        var version = int.Parse(action.Arguments["version"], CultureInfo.InvariantCulture);
        action.Arguments.TryGetValue("note", out var note);

        // Re-check with the CURRENT token and data: the limit, assignment or state may have changed meanwhile.
        var (application, problem) = await AuthorizeAsync<ActionOutcome>(principal, applicationId, ct);
        if (problem is not null) return problem;
        if (application!.Version != version)
            return UseCaseResult<ActionOutcome>.Invalid("version", "The application changed after you reviewed it. Review it again.");

        var result = await executor.DecideAsync(principal, application.Id, version, action.Id, decision, note, clock.GetUtcNow(), ct);
        var status = decision == CreditDecision.Approve ? "APPROVED" : "REJECTED";
        return result.Outcome switch
        {
            ApprovalExecutionOutcome.Approved or ApprovalExecutionOutcome.Rejected or ApprovalExecutionOutcome.AlreadyExecuted =>
                UseCaseResult<ActionOutcome>.Success(new ActionOutcome(action.Kind, status, application.Id.Value,
                    [new("status", status), new("version", result.Version.ToString(CultureInfo.InvariantCulture))])),
            ApprovalExecutionOutcome.VersionChanged =>
                UseCaseResult<ActionOutcome>.Invalid("version", "The application changed after you reviewed it. Review it again."),
            _ => UseCaseResult<ActionOutcome>.Forbidden("CREDIT_APPLICATION_NOT_APPROVABLE"),
        };
    }

    /// <summary>Permission → visibility (RLS + read policy) → decision policy (state, four-eyes, limit), audited.</summary>
    private async Task<(CreditApplication? Application, UseCaseResult<T>? Problem)> AuthorizeAsync<T>(
        PrincipalContext principal, string applicationId, CancellationToken ct)
    {
        if (!principal.HasPermission(KnownPermissions.CreditApprove) || principal.Type == PrincipalType.Machine)
            return (null, UseCaseResult<T>.Forbidden(notices.HintCodeFor(KnownPermissions.CreditApprove, ReasonCodes.MissingPermission)));
        var application = await reader.FindReadableAsync(principal, applicationId, ct);
        if (application is null) return (null, UseCaseResult<T>.NotFound());
        var decision = await authorization.AuthorizeAsync(principal, new CreditApplicationResource(application),
            KnownPermissions.CreditApprove, ct);
        audit.Record(principal, decision);
        return decision.IsAllowed
            ? (application, null)
            : (null, UseCaseResult<T>.Forbidden(notices.FromDecision(decision)?.Code));
    }

    private static string Money(decimal amount, string currency) =>
        amount.ToString("N2", CultureInfo.InvariantCulture) + " " + currency;
}

/// <summary>Confirms "credit.approve" cards.</summary>
public sealed class CreditApproveAction(CreditApproval approval, ICreditApprovalExecutor executor, TimeProvider clock)
    : IConfirmableAction
{
    public string Kind => CreditApproval.ApproveKind;

    public Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct) =>
        approval.ExecuteAsync(principal, action, CreditDecision.Approve, executor, clock, ct);
}

/// <summary>Confirms "credit.reject" cards.</summary>
public sealed class CreditRejectAction(CreditApproval approval, ICreditApprovalExecutor executor, TimeProvider clock)
    : IConfirmableAction
{
    public string Kind => CreditApproval.RejectKind;

    public Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct) =>
        approval.ExecuteAsync(principal, action, CreditDecision.Reject, executor, clock, ct);
}
