using System.Collections.Frozen;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Model.Credit;
using Xunit;
using static VirtualBank.Application.UnitTests.Credit.CreditUseCases;

namespace VirtualBank.Application.UnitTests.Credit;

/// <summary>The approval policy translates the domain rules into reason codes and notice codes.</summary>
public sealed class CreditPolicyTests
{
    private static PrincipalContext Approver(decimal? limit, bool active = true) => new()
    {
        SubjectId = "max",
        Username = "max",
        Type = PrincipalType.Human,
        Permissions = new[] { KnownPermissions.CreditApprove }.ToFrozenSet(),
        ApprovalLimit = limit,
        IsActive = active,
    };

    private static CreditApplicationResource Submitted(decimal amount, string preparedBy = "someone-else") =>
        new(CreditApplication.Rehydrate(CreditApplicationId.Parse("K-900"), "C-1002", Money.Eur(amount), CreditApplicationStatus.Submitted, "Test", preparedBy, "analyst"));

    private static AuthorizationDecision Approve(PrincipalContext principal, ResourceDescriptor resource) =>
        PolicyEngine.Authorize(principal, resource, KnownPermissions.CreditApprove);

    [Fact]
    public void Approval_without_a_limit_fails_closed()
    {
        Assert.Equal(ReasonCodes.ApprovalLimitExceeded, Approve(Approver(null), Submitted(1m)).ReasonCode);
    }

    [Fact]
    public void Approval_exactly_at_the_limit_is_allowed()
    {
        Assert.True(Approve(Approver(50000m), Submitted(50000m)).IsAllowed);
    }

    [Fact]
    public void Four_eyes_is_checked_before_the_limit()
    {
        Assert.Equal(ReasonCodes.FourEyesViolation, Approve(Approver(50000m), Submitted(90000m, preparedBy: "max")).ReasonCode);
    }

    [Fact]
    public void Wrong_resource_type_for_a_policy_is_denied()
    {
        var decision = Approve(Approver(50000m), new NamedResource(CreditApplicationResource.ResourceType, "K-100"));
        Assert.Equal(ReasonCodes.ResourceTypeMismatch, decision.ReasonCode);
    }

    [Fact]
    public void Inactive_principal_is_denied_even_with_permissions()
    {
        Assert.Equal(ReasonCodes.PrincipalInactive, Approve(Approver(50000m, active: false), Submitted(100m)).ReasonCode);
    }

    [Fact]
    public void Four_eyes_denial_maps_to_its_notice_code()
    {
        var decision = Approve(Approver(50000m), Submitted(10m, preparedBy: "max"));
        Assert.Equal("CREDIT_FOUR_EYES_REQUIRED", Notices.FromDecision(decision)!.Code);
    }

    [Fact]
    public void Credit_service_registers_no_customer_policies()
    {
        var decision = PolicyEngine.Authorize(Approver(1m), new NamedResource("customer", "C-1002"), KnownPermissions.CustomerRead);
        Assert.Equal(ReasonCodes.NoPolicy, decision.ReasonCode);
    }
}
