using VirtualBank.Application.Authorization;
using System.Collections.Frozen;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;
using Xunit;

namespace VirtualBank.Application.UnitTests.Authorization;

/// <summary>The policy engine mechanism, independent of any business context.</summary>
public sealed class CoreEngineTests
{
    // Test-local policy: the core ships the mechanism (PermissionPolicy), not concrete capability rules.
    private static readonly ResourceAuthorizationService Service =
        new([new PermissionPolicy("test.diagnostics.read.v1", "diagnostics", KnownPermissions.DiagnosticsRead, DisclosureMode.Generic)]);

    private static PrincipalContext Principal(bool active, params Permission[] permissions) => new()
    {
        SubjectId = "s1",
        Username = "s1",
        Type = PrincipalType.Human,
        Permissions = permissions.ToFrozenSet(),
        IsActive = active,
    };

    private static readonly NamedResource Diagnostics = new("diagnostics", "platform");

    [Fact]
    public void Permission_policy_allows_holders_of_the_permission()
    {
        Assert.True(Service.Authorize(Principal(true, KnownPermissions.DiagnosticsRead), Diagnostics, KnownPermissions.DiagnosticsRead).IsAllowed);
    }

    [Fact]
    public void Permission_policy_denies_without_the_permission()
    {
        var decision = Service.Authorize(Principal(true), Diagnostics, KnownPermissions.DiagnosticsRead);
        Assert.Equal(ReasonCodes.MissingPermission, decision.ReasonCode);
    }

    [Fact]
    public void Unknown_action_has_no_policy_and_is_denied()
    {
        var decision = Service.Authorize(Principal(true, KnownPermissions.DocumentRead), new NamedResource("document", "DOC-1"), KnownPermissions.DocumentRead);

        Assert.False(decision.IsAllowed);
        Assert.Equal(ReasonCodes.NoPolicy, decision.ReasonCode);
    }

    [Fact]
    public void Inactive_principal_is_denied_even_with_permissions()
    {
        var decision = Service.Authorize(Principal(false, KnownPermissions.DiagnosticsRead), Diagnostics, KnownPermissions.DiagnosticsRead);
        Assert.Equal(ReasonCodes.PrincipalInactive, decision.ReasonCode);
    }

    [Fact]
    public void Duplicate_policies_are_rejected_at_startup()
    {
        var policy = new PermissionPolicy("p.v1", "audit", KnownPermissions.AuditRead, DisclosureMode.Generic);
        Assert.Throws<InvalidOperationException>(() => new ResourceAuthorizationService([policy, policy]));
    }
}
