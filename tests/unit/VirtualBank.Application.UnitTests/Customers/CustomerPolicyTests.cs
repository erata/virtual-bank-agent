using System.Collections.Frozen;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Customers.Authorization;
using Xunit;

namespace VirtualBank.Application.UnitTests.Customers;

public sealed class CustomerPolicyTests
{
    private static readonly ResourceAuthorizationService Service = new(CustomerPolicies.Create());

    private static PrincipalContext Principal(string subject, string? branch, params Permission[] permissions) => new()
    {
        SubjectId = subject,
        Username = subject,
        Type = PrincipalType.Human,
        Permissions = permissions.ToFrozenSet(),
        Branch = branch,
        IsActive = true,
    };

    private static CustomerResource Customer(string branch, params string[] analysts) =>
        new("C-1002", branch, analysts.ToHashSet(StringComparer.Ordinal));

    [Fact]
    public void Branch_permission_without_a_branch_attribute_is_denied_and_hidden()
    {
        var decision = Service.Authorize(Principal("anna", branch: null, KnownPermissions.CustomerRead), Customer("STR"), KnownPermissions.CustomerRead);

        Assert.False(decision.IsAllowed);
        Assert.Equal(DisclosureMode.None, decision.Disclosure);
    }

    [Fact]
    public void Assignment_path_allows_an_assigned_analyst_in_another_branch()
    {
        var decision = Service.Authorize(Principal("mehmet", "STR", KnownPermissions.CustomerReadAssigned), Customer("MUC", "mehmet"), KnownPermissions.CustomerRead);
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Sensitive_read_requires_ordinary_read_access_first()
    {
        var decision = Service.Authorize(
            Principal("anna", "STR", KnownPermissions.CustomerReadSensitive), Customer("STR"), KnownPermissions.CustomerReadSensitive);

        Assert.Equal(ReasonCodes.MissingPermission, decision.ReasonCode);
    }

    [Fact]
    public void Customer_service_registers_no_credit_policies()
    {
        var decision = Service.Authorize(Principal("max", "STR", KnownPermissions.CreditApprove), Customer("STR"), KnownPermissions.CreditApprove);
        Assert.Equal(ReasonCodes.NoPolicy, decision.ReasonCode);
    }
}
