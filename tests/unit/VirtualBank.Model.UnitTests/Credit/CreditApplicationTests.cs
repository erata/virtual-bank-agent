using VirtualBank.Model.Common;
using VirtualBank.Model.Credit;
using Xunit;

namespace VirtualBank.Model.UnitTests.Credit;

public sealed class CreditApplicationTests
{
    private static CreditApplicationId Id(string value) => CreditApplicationId.Parse(value);

    private static CreditApplication Submitted(decimal amount, string preparedBy = "preparer") =>
        CreditApplication.Rehydrate(Id("K-900"), "C-1002", Money.Eur(amount), CreditApplicationStatus.Submitted, "Test", preparedBy, "analyst");

    [Fact]
    public void Approval_within_the_limit_by_another_person_is_allowed()
    {
        Assert.Equal(ApprovalCheck.Allowed, Submitted(50000m).CheckApproval("approver", Money.Eur(50000m)));
    }

    [Fact]
    public void Missing_limit_fails_closed()
    {
        Assert.Equal(ApprovalCheck.LimitExceeded, Submitted(1m).CheckApproval("approver", approvalLimit: null));
    }

    [Fact]
    public void Limit_in_another_currency_fails_closed()
    {
        Assert.Equal(ApprovalCheck.LimitExceeded, Submitted(1m).CheckApproval("approver", Money.Of(1000m, "USD")));
    }

    [Fact]
    public void Four_eyes_is_checked_before_the_limit()
    {
        Assert.Equal(ApprovalCheck.FourEyesViolation, Submitted(90000m, preparedBy: "approver").CheckApproval("approver", Money.Eur(50000m)));
    }

    [Fact]
    public void Only_submitted_applications_can_be_approved()
    {
        var draft = CreditApplication.Rehydrate(Id("K-901"), "C-1002", Money.Eur(10m), CreditApplicationStatus.Draft, "Test", "preparer", null);
        Assert.Equal(ApprovalCheck.NotSubmitted, draft.CheckApproval("approver", Money.Eur(50000m)));
    }

    [Fact]
    public void Approve_transitions_state_records_approver_and_bumps_version()
    {
        var application = Submitted(1000m);
        application.Approve("approver", Money.Eur(50000m), DateTimeOffset.UnixEpoch);

        Assert.Equal(CreditApplicationStatus.Approved, application.Status);
        Assert.Equal("approver", application.ApprovedBy);
        Assert.Equal(2, application.Version);
    }

    [Fact]
    public void Approve_rechecks_the_rules_and_refuses_a_violation()
    {
        var application = Submitted(1000m, preparedBy: "approver");
        Assert.Throws<DomainRuleViolationException>(() => application.Approve("approver", Money.Eur(50000m), DateTimeOffset.UnixEpoch));
        Assert.Equal(CreditApplicationStatus.Submitted, application.Status);
    }

    [Fact]
    public void Rehydrate_rejects_an_approved_application_without_approver()
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            CreditApplication.Rehydrate(Id("K-902"), "C-1002", Money.Eur(10m), CreditApplicationStatus.Approved, "Test", "p", null, approvedAt: DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("K-100", true)]
    [InlineData("K-1000", false)]
    [InlineData("k-100", false)]
    [InlineData("K-100'--", false)]
    public void Application_ids_have_a_strict_format(string id, bool valid)
    {
        Assert.Equal(valid, CreditApplicationId.TryParse(id, out _));
    }

    [Fact]
    public void Parsing_an_invalid_id_is_a_rule_violation()
    {
        Assert.Throws<DomainRuleViolationException>(() => CreditApplicationId.Parse("X-1"));
    }

    [Fact]
    public void Ids_compare_by_value()
    {
        Assert.Equal(Id("K-100"), Id("K-100"));
    }

    [Fact]
    public void Rehydrate_rejects_an_approved_application_without_timestamp()
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            CreditApplication.Rehydrate(Id("K-903"), "C-1002", Money.Eur(10m), CreditApplicationStatus.Approved, "Test", "p", null, approvedBy: "a"));
    }

    [Fact]
    public void Rehydrate_rejects_an_approval_timestamp_on_an_unapproved_application()
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            CreditApplication.Rehydrate(Id("K-904"), "C-1002", Money.Eur(10m), CreditApplicationStatus.Submitted, "Test", "p", null, approvedAt: DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rehydrate_rejects_a_non_positive_version(int version)
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            CreditApplication.Rehydrate(Id("K-905"), "C-1002", Money.Eur(10m), CreditApplicationStatus.Draft, "Test", "p", null, version: version));
    }

    [Fact]
    public void Rehydrate_rejects_a_zero_amount_although_money_allows_zero()
    {
        Assert.Equal(0m, Money.Eur(0m).Amount);
        Assert.Throws<DomainRuleViolationException>(() =>
            CreditApplication.Rehydrate(Id("K-906"), "C-1002", Money.Eur(0m), CreditApplicationStatus.Draft, "Test", "p", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rehydrate_rejects_a_blank_approver(string approver)
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            CreditApplication.Rehydrate(Id("K-908"), "C-1002", Money.Eur(10m), CreditApplicationStatus.Approved, "Test", "p", null,
                approvedBy: approver, approvedAt: DateTimeOffset.UnixEpoch));
        Assert.Throws<DomainRuleViolationException>(() =>
            CreditApplication.Rehydrate(Id("K-909"), "C-1002", Money.Eur(10m), CreditApplicationStatus.Submitted, "Test", "p", null,
                approvedBy: approver));
    }

    [Fact]
    public void Rehydrate_rejects_an_undefined_status()
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            CreditApplication.Rehydrate(Id("K-910"), "C-1002", Money.Eur(10m), (CreditApplicationStatus)999, "Test", "p", null));
    }

    [Fact]
    public void Approve_requires_an_approver_identity()
    {
        var application = Submitted(1000m);
        Assert.Throws<DomainRuleViolationException>(() => application.Approve(" ", Money.Eur(50000m), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Rehydrate_accepts_a_consistent_approved_application()
    {
        var application = CreditApplication.Rehydrate(Id("K-907"), "C-1002", Money.Eur(10m), CreditApplicationStatus.Approved, "Test", "p", null,
            approvedBy: "a", approvedAt: DateTimeOffset.UnixEpoch, version: 2);
        Assert.Equal(CreditApplicationStatus.Approved, application.Status);
    }
}
