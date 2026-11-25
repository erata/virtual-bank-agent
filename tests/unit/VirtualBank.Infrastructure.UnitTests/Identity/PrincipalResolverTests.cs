using System.Security.Claims;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Specifications;
using VirtualBank.Infrastructure.Identity;
using Xunit;

namespace VirtualBank.Application.UnitTests.Authorization;

public sealed class PrincipalResolverTests
{
    private const string MissingClaim = "<missing>";

    private static readonly PrincipalResolver Resolver = new(PermissionCatalog.FromFile(RepositoryPaths.Permissions), TrustedMachineClients.FromServiceSettings());

    private static ClaimsPrincipal Token(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void Group_claims_are_expanded_to_ancestors()
    {
        var principal = Resolver.Resolve(Token(("sub", "u1"), ("employment_status", "active"), ("groups", "/Credit/Analysis")));

        Assert.True(principal.IsMemberOf(GroupPath.Parse("/Credit/Analysis")));
        Assert.True(principal.IsMemberOf(GroupPath.Parse("/Credit")));
        Assert.False(principal.IsMemberOf(GroupPath.Parse("/Credit/Approval")));
    }

    [Fact]
    public void Malformed_group_claims_grant_nothing()
    {
        var principal = Resolver.Resolve(Token(("sub", "u1"), ("employment_status", "active"), ("groups", "Credit//x")));
        Assert.Empty(principal.Groups);
    }

    [Fact]
    public void Roles_map_to_permissions_through_the_catalog()
    {
        var principal = Resolver.Resolve(Token(
            ("sub", "u1"), ("employment_status", "active"), ("realm_access", """{"roles":["credit-analyst","unknown-role"]}""")));

        Assert.True(principal.HasPermission(KnownPermissions.CreditRead));
        Assert.True(principal.HasPermission(KnownPermissions.CustomerReadAssigned));
        Assert.False(principal.HasPermission(KnownPermissions.CreditApprove));
        Assert.False(principal.HasPermission(KnownPermissions.CustomerRead));
    }

    [Theory]
    [InlineData(MissingClaim)]
    [InlineData("terminated")]
    [InlineData("Active")]
    public void Principal_without_active_employment_status_has_no_permissions(string status)
    {
        var claims = new List<(string, string)> { ("sub", "u1"), ("realm_access", """{"roles":["platform-admin"]}""") };
        if (status != MissingClaim)
        {
            claims.Add(("employment_status", status));
        }

        var principal = Resolver.Resolve(Token(claims.ToArray()));

        Assert.False(principal.IsActive);
        Assert.Empty(principal.Permissions);
        Assert.False(principal.HasPermission(KnownPermissions.SystemConfigure));
    }

    [Fact]
    public void Malformed_realm_access_grants_no_roles()
    {
        var principal = Resolver.Resolve(Token(("sub", "u1"), ("employment_status", "active"), ("realm_access", "{not json")));
        Assert.Empty(principal.Roles);
    }

    [Fact]
    public void Ambiguous_single_valued_attribute_is_ignored()
    {
        var principal = Resolver.Resolve(Token(("sub", "u1"), ("employment_status", "active"), ("branch", "STR"), ("branch", "MUC")));
        Assert.Null(principal.Branch);
    }

    [Fact]
    public void Missing_subject_is_rejected()
    {
        Assert.Throws<PrincipalResolutionException>(() => Resolver.Resolve(Token(("preferred_username", "anna.berater"))));
    }

    [Fact]
    public void Machine_type_comes_from_the_principal_type_claim()
    {
        var principal = Resolver.Resolve(Token(
            ("sub", "s1"), ("azp", "svc-ingestion"), ("principal_type", "machine"), ("employment_status", "active"),
            ("realm_access", """{"roles":["svc-ingestion"]}""")));

        Assert.Equal(PrincipalType.Machine, principal.Type);
        Assert.True(principal.HasPermission(KnownPermissions.DocumentIngest));
    }

    [Fact]
    public void A_human_is_never_a_machine_even_with_a_service_like_name()
    {
        // A user who manages to be called "service-account-…" gets no machine type, and no permissions.
        var principal = Resolver.Resolve(Token(
            ("sub", "u1"), ("azp", "virtual-bank-cli"), ("preferred_username", "service-account-svc-ingestion"),
            ("employment_status", "active"), ("realm_access", """{"roles":["svc-ingestion"]}""")));

        Assert.NotEqual(PrincipalType.Machine, principal.Type);
        Assert.False(principal.IsActive);
        Assert.False(principal.HasPermission(KnownPermissions.DocumentIngest));
    }

    [Fact]
    public void Ordinary_human_token_is_human()
    {
        var principal = Resolver.Resolve(Token(("sub", "u1"), ("azp", "virtual-bank-cli"), ("employment_status", "active")));

        Assert.Equal(PrincipalType.Human, principal.Type);
        Assert.True(principal.IsActive);
    }

    [Theory]
    [InlineData("robot")]
    [InlineData("MACHINE")]
    [InlineData("human")]
    public void Unknown_principal_type_fails_closed(string value)
    {
        var principal = Resolver.Resolve(Token(
            ("sub", "s1"), ("azp", "svc-ingestion"), ("principal_type", value), ("employment_status", "active"),
            ("realm_access", """{"roles":["svc-ingestion"]}""")));

        Assert.False(principal.IsActive);
        Assert.Empty(principal.Permissions);
    }

    [Theory]
    [InlineData("svc-ingestion")]
    [InlineData("svc-reporting")]
    public void Machine_claim_from_a_trusted_service_client_is_accepted(string client)
    {
        var principal = Resolver.Resolve(Token(("sub", "s1"), ("azp", client), ("principal_type", "machine"), ("employment_status", "active")));

        Assert.Equal(PrincipalType.Machine, principal.Type);
        Assert.True(principal.IsActive);
    }

    [Theory]
    [InlineData("virtual-bank-cli")]
    [InlineData("virtual-bank-ui")]
    [InlineData("agent-service")]
    [InlineData("unknown-client")]
    [InlineData("SVC-INGESTION")]
    public void Machine_claim_from_an_untrusted_client_fails_closed(string client)
    {
        // E.g. a mapper accidentally added to the UI or CLI client in Keycloak: the application still refuses.
        var principal = Resolver.Resolve(Token(
            ("sub", "u1"), ("azp", client), ("principal_type", "machine"), ("employment_status", "active"),
            ("realm_access", """{"roles":["svc-ingestion"]}""")));

        Assert.False(principal.IsActive);
        Assert.Empty(principal.Permissions);
    }

    [Fact]
    public void Machine_claim_without_client_fails_closed()
    {
        var principal = Resolver.Resolve(Token(("sub", "s1"), ("principal_type", "machine"), ("employment_status", "active")));
        Assert.False(principal.IsActive);
    }

    [Fact]
    public void Without_trusted_clients_no_machine_principal_exists()
    {
        var resolver = new PrincipalResolver(PermissionCatalog.FromFile(RepositoryPaths.Permissions), MachineIdentityOptions.None);
        var principal = resolver.Resolve(Token(("sub", "s1"), ("azp", "svc-ingestion"), ("principal_type", "machine"), ("employment_status", "active")));

        Assert.False(principal.IsActive);
    }

    [Fact]
    public void Realm_service_accounts_resolve_as_machines_and_personas_as_non_machines()
    {
        var tokens = PersonaTokens.Load(RepositoryPaths.Realm);
        foreach (var username in tokens.SubjectIds.Keys)
        {
            var principal = Resolver.Resolve(tokens.For(username));
            var expectMachine = username.StartsWith("service-account-", StringComparison.Ordinal);
            Assert.True(expectMachine == (principal.Type == PrincipalType.Machine), $"{username} resolved as {principal.Type}");
            Assert.True(principal.IsActive, $"{username} should be active");
        }
    }

    [Fact]
    public void Administrative_role_sets_category_but_grants_no_business_data()
    {
        var principal = Resolver.Resolve(Token(
            ("sub", "a1"), ("employment_status", "active"), ("realm_access", """{"roles":["platform-admin"]}""")));

        Assert.Equal(PrincipalType.Administrative, principal.Type);
        Assert.False(principal.HasPermission(KnownPermissions.CustomerRead));
        Assert.False(principal.HasPermission(KnownPermissions.EmployeeSalaryRead));
    }

    [Fact]
    public void Approval_limit_is_parsed_invariantly_and_unknown_clearance_is_lowest()
    {
        var principal = Resolver.Resolve(Token(
            ("sub", "u1"), ("employment_status", "active"), ("approval_limit", "50000"), ("clearance", "top-secret")));

        Assert.Equal(50000m, principal.ApprovalLimit);
        Assert.Equal(Clearance.Public, principal.Clearance);
    }
}
