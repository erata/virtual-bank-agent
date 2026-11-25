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

namespace VirtualBank.Application.Credit.Commands.AddCreditNote;

/// <summary>
/// A note proposed by the assistant becomes a confirmation card: the analyst sees the exact text and saves it with
/// one click, or discards it. The model can never write a note by itself (prompt injection cannot persist text).
/// Confirming runs the normal <see cref="AddCreditNoteHandler"/>, i.e. every check again plus RLS WITH CHECK.
/// </summary>
public sealed class CreditNoteCards(CreditApplicationReader reader, IResourceAuthorizationService authorization,
    IAuthorizationAudit audit, NoticeCatalog notices, PendingActionFactory actions, AddCreditNoteHandler addNote)
{
    public const string Kind = "credit.note";

    public async Task<UseCaseResult<PendingActionView>> PrepareAsync(PrincipalContext principal, string applicationId, string? text,
        CancellationToken ct)
    {
        if (!principal.HasPermission(KnownPermissions.CreditNoteWrite))
            return UseCaseResult<PendingActionView>.Forbidden(notices.HintCodeFor(KnownPermissions.CreditNoteWrite, ReasonCodes.MissingPermission));
        if (!CreditNoteText.TryCreate(text, out var note))
            return UseCaseResult<PendingActionView>.Invalid("text", $"A note must contain 1 to {CreditNoteText.MaxLength} characters.");
        var application = await reader.FindReadableAsync(principal, applicationId, ct);
        if (application is null) return UseCaseResult<PendingActionView>.NotFound();
        var decision = await authorization.AuthorizeAsync(principal, new CreditApplicationResource(application),
            KnownPermissions.CreditNoteWrite, ct);
        audit.Record(principal, decision);
        if (!decision.IsAllowed) return UseCaseResult<PendingActionView>.Forbidden(notices.FromDecision(decision)?.Code);

        var action = actions.Create(principal, Kind,
            new Dictionary<string, string> { ["applicationId"] = application.Id.Value, ["text"] = note!.Value },
            [new("application", application.Id.Value), new("note", note.Value)]);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    internal async Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct)
    {
        var result = await addNote.HandleAsync(new AddCreditNoteCommand(principal, action.Arguments["applicationId"], action.Arguments["text"]), ct);
        return result.Status switch
        {
            UseCaseStatus.Succeeded => UseCaseResult<ActionOutcome>.Success(new ActionOutcome(Kind, "SAVED",
                result.Value!.ApplicationId.Value, [new("note", "#" + result.Value.Id)])),
            UseCaseStatus.Forbidden => UseCaseResult<ActionOutcome>.Forbidden(result.NoticeCode),
            UseCaseStatus.Invalid => UseCaseResult<ActionOutcome>.Invalid("text", "The note is not valid."),
            _ => UseCaseResult<ActionOutcome>.NotFound(),
        };
    }
}

/// <summary>Confirms "credit.note" cards.</summary>
public sealed class CreditNoteAction(CreditNoteCards cards) : IConfirmableAction
{
    public string Kind => CreditNoteCards.Kind;

    public Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct) =>
        cards.ExecuteAsync(principal, action, ct);
}
