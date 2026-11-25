using VirtualBank.Model.Common;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Credit;
using VirtualBank.Application.Credit.Commands.ApproveCreditApplication;
using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Model.Credit;
using Xunit;

namespace VirtualBank.Application.UnitTests.Credit;

/// <summary>M6: credit decisions only via a prepared card that the same approver confirms before it expires.</summary>
public sealed class CreditApprovalTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingExecutor : ICreditApprovalExecutor
    {
        public List<(string Application, int Version, CreditDecision Decision, string? Note)> Calls { get; } = [];
        public ApprovalExecutionOutcome? Outcome { get; set; }

        public Task<ApprovalExecution> DecideAsync(PrincipalContext principal, CreditApplicationId applicationId, int expectedVersion,
            Guid idempotencyKey, CreditDecision decision, string? note, DateTimeOffset decidedAt, CancellationToken cancellationToken)
        {
            Calls.Add((applicationId.Value, expectedVersion, decision, note));
            var outcome = Outcome ?? (decision == CreditDecision.Approve ? ApprovalExecutionOutcome.Approved : ApprovalExecutionOutcome.Rejected);
            return Task.FromResult(new ApprovalExecution(outcome, expectedVersion + 1));
        }
    }

    private sealed record Setup(CreditApproval Approval, ActionConfirmation Confirmation, RecordingExecutor Executor, Clock Clock);

    private static Setup Create(params CreditApplication[] applications)
    {
        var useCases = new CreditUseCases(applications);
        var clock = new Clock();
        var executor = new RecordingExecutor();
        var store = new InMemoryPendingActionStore(clock);
        var reader = new CreditApplicationReader(useCases.Repository, CreditUseCases.PolicyEngine, useCases.Audit);
        var approval = new CreditApproval(reader, CreditUseCases.PolicyEngine, useCases.Audit, CreditUseCases.Notices,
            new PendingActionFactory(store, clock));
        var confirmation = new ActionConfirmation(store,
            [new CreditApproveAction(approval, executor, clock), new CreditRejectAction(approval, executor, clock)], clock);
        return new Setup(approval, confirmation, executor, clock);
    }

    private static CreditApplication Submitted(string id = "K-100", string preparedBy = "someone") =>
        CreditUseCases.NewApplication(id, CreditApplicationStatus.Submitted, analyst: null, preparedBy);

    private static readonly PrincipalContext Max = Personas.Get("max.approver");

    [Fact]
    public async Task Prepare_changes_nothing_and_confirm_executes_exactly_once()
    {
        var s = Create(Submitted());

        var card = await s.Approval.PrepareApprovalAsync(Max, "K-100", null, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, card.Status);
        Assert.Equal(CreditApproval.ApproveKind, card.Value!.Kind);
        Assert.Contains(card.Value.Facts, f => f.Key == "amount" && f.Value == "1,000.00 EUR");
        Assert.Empty(s.Executor.Calls);

        var confirmed = await s.Confirmation.ConfirmAsync(Max, card.Value.Id, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, confirmed.Status);
        Assert.Equal("APPROVED", confirmed.Value!.Outcome);
        Assert.Equal(("K-100", 1, CreditDecision.Approve, (string?)null), Assert.Single(s.Executor.Calls));

        Assert.Equal(UseCaseStatus.NotFound, (await s.Confirmation.ConfirmAsync(Max, card.Value.Id, CancellationToken.None)).Status);
        Assert.Single(s.Executor.Calls);
    }

    [Fact]
    public async Task Rejection_needs_a_reason_and_records_it_with_the_decision()
    {
        var s = Create(Submitted());

        var missing = await s.Approval.PrepareRejectionAsync(Max, "K-100", "   ", CancellationToken.None);
        Assert.Equal(UseCaseStatus.Invalid, missing.Status);

        var card = await s.Approval.PrepareRejectionAsync(Max, "K-100", "Income not verifiable", CancellationToken.None);
        Assert.Contains(card.Value!.Facts, f => f.Key == "reason" && f.Value == "Income not verifiable");
        var result = await s.Confirmation.ConfirmAsync(Max, card.Value.Id, CancellationToken.None);

        Assert.Equal("REJECTED", result.Value!.Outcome);
        Assert.Equal(("K-100", 1, CreditDecision.Reject, "Income not verifiable"), Assert.Single(s.Executor.Calls));
    }

    [Fact]
    public async Task Card_is_bound_to_the_approver_and_survives_someone_elses_attempt()
    {
        var s = Create(Submitted());
        var card = await s.Approval.PrepareApprovalAsync(Max, "K-100", null, CancellationToken.None);

        var other = Max with { SubjectId = "another-approver" };
        Assert.Equal(UseCaseStatus.NotFound, (await s.Confirmation.ConfirmAsync(other, card.Value!.Id, CancellationToken.None)).Status);
        Assert.Empty(s.Executor.Calls);
        Assert.Equal(UseCaseStatus.Succeeded, (await s.Confirmation.ConfirmAsync(Max, card.Value.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Expired_card_executes_nothing()
    {
        var s = Create(Submitted());
        var card = await s.Approval.PrepareApprovalAsync(Max, "K-100", null, CancellationToken.None);

        s.Clock.Now += PendingActionFactory.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Equal(UseCaseStatus.NotFound, (await s.Confirmation.ConfirmAsync(Max, card.Value!.Id, CancellationToken.None)).Status);
        Assert.Empty(s.Executor.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Four_eyes_applies_to_approval_and_rejection_before_any_card_exists(bool approve)
    {
        var s = Create(Submitted(preparedBy: Max.SubjectId));

        var result = approve
            ? await s.Approval.PrepareApprovalAsync(Max, "K-100", null, CancellationToken.None)
            : await s.Approval.PrepareRejectionAsync(Max, "K-100", "reason", CancellationToken.None);

        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.Equal("CREDIT_FOUR_EYES_REQUIRED", result.NoticeCode);
    }

    [Theory]
    [InlineData("mehmet.kredit")]  // analyst: reads and writes notes, never decides
    [InlineData("anna.berater")]   // no credit access at all
    public async Task Roles_without_credit_approve_cannot_prepare(string persona)
    {
        var s = Create(Submitted());
        var result = await s.Approval.PrepareApprovalAsync(Personas.Get(persona), "K-100", null, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.Empty(s.Executor.Calls);
    }

    [Fact]
    public async Task Database_refusal_is_reported_and_nothing_counts_as_decided()
    {
        var s = Create(Submitted());
        var card = await s.Approval.PrepareApprovalAsync(Max, "K-100", null, CancellationToken.None);
        s.Executor.Outcome = ApprovalExecutionOutcome.Refused;

        var result = await s.Confirmation.ConfirmAsync(Max, card.Value!.Id, CancellationToken.None);

        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.Equal("CREDIT_APPLICATION_NOT_APPROVABLE", result.NoticeCode);
    }

    [Fact]
    public void Domain_rejection_follows_the_same_rules_as_approval()
    {
        var app = Submitted(preparedBy: "analyst-1");
        Assert.Throws<DomainRuleViolationException>(() => app.Reject("analyst-1", Money.Eur(50_000m))); // four-eyes
        app.Reject("approver-1", Money.Eur(50_000m));
        Assert.Equal(CreditApplicationStatus.Rejected, app.Status);
        Assert.Equal(2, app.Version);
        Assert.Null(app.ApprovedBy);
    }
}
