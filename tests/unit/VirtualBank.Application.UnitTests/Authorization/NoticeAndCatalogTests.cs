using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Specifications;
using Xunit;

namespace VirtualBank.Application.UnitTests.Authorization;

public sealed class NoticeAndCatalogTests
{
    private static AuthorizationDecision Denied(string action, string reason, DisclosureMode disclosure) => new()
    {
        IsAllowed = false,
        ReasonCode = reason,
        Action = new Permission(action),
        ResourceType = "credit.application",
        ResourceId = "K-100",
        PolicyId = "test",
        CorrelationId = "c1",
        Disclosure = disclosure,
    };

    private static readonly NoticeCatalog Catalog = new([new NoticeRule(new Permission("credit.read"), ReasonCodes.MissingPermission, "CREDIT_READ_REQUIRED")]);

    [Fact]
    public void Permission_hint_maps_to_a_specific_notice_code()
    {
        var notice = Catalog.FromDecision(Denied("credit.read", ReasonCodes.MissingPermission, DisclosureMode.PermissionHint));

        Assert.NotNull(notice);
        Assert.Equal("CREDIT_READ_REQUIRED", notice!.Code);
    }

    [Fact]
    public void Disclosure_none_produces_no_notice()
    {
        Assert.Null(Catalog.FromDecision(Denied("customer.read", ReasonCodes.BranchMismatch, DisclosureMode.None)));
    }

    [Fact]
    public void Unmapped_hint_degrades_to_the_generic_notice()
    {
        var notice = Catalog.FromDecision(Denied("credit.read", ReasonCodes.AclStale, DisclosureMode.PermissionHint));

        Assert.Equal(NoticeCatalog.GenericPartialResult, notice!.Code);
        Assert.Equal(DisclosureMode.Generic, notice.Level);
    }

    [Fact]
    public void Hint_lookup_without_a_rule_returns_null()
    {
        Assert.Null(Catalog.HintCodeFor(new Permission("customer.read"), ReasonCodes.MissingPermission));
    }

    [Fact]
    public void Duplicate_notice_rules_are_rejected()
    {
        var rule = new NoticeRule(new Permission("a.read"), ReasonCodes.MissingPermission, "A");
        Assert.Throws<InvalidOperationException>(() => new NoticeCatalog([rule, rule]));
    }

    [Fact]
    public void Catalog_rejects_roles_with_unknown_permissions()
    {
        const string json = """{ "permissions": [ { "id": "a.read" } ], "roles": { "r": [ "a.read", "b.write" ] } }""";
        Assert.Throws<InvalidOperationException>(() => PermissionCatalog.FromJson(json));
    }

    [Fact]
    public void Every_realm_role_has_a_catalog_entry()
    {
        var catalog = PermissionCatalog.FromFile(RepositoryPaths.Permissions);
        using var realm = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryPaths.Realm));
        var realmRoles = realm.RootElement.GetProperty("roles").GetProperty("realm").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(realmRoles.Except(catalog.Roles));
        Assert.Empty(catalog.Roles.Except(realmRoles));
    }

    [Fact]
    public void Machine_claim_is_only_issued_by_clients_that_cannot_log_in_humans()
    {
        using var realm = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryPaths.Realm));
        foreach (var client in realm.RootElement.GetProperty("clients").EnumerateArray())
        {
            var issuesMachineClaim = client.TryGetProperty("protocolMappers", out var mappers) && mappers.EnumerateArray().Any(m =>
                m.GetProperty("config").TryGetProperty("claim.name", out var name) && name.GetString() == "principal_type");
            if (!issuesMachineClaim)
            {
                continue;
            }

            var id = client.GetProperty("clientId").GetString();
            Assert.True(client.GetProperty("serviceAccountsEnabled").GetBoolean(), $"{id}: machine claim without service account");
            Assert.False(client.GetProperty("publicClient").GetBoolean(), $"{id}: machine claim on a public client");
            Assert.False(client.GetProperty("standardFlowEnabled").GetBoolean(), $"{id}: machine claim on a browser login client");
            Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean(), $"{id}: machine claim on a password-grant client");
            Assert.False(client.GetProperty("implicitFlowEnabled").GetBoolean(), $"{id}: machine claim on an implicit-flow client");
        }
    }

    [Fact]
    public void Trusted_machine_clients_match_the_realm_in_every_service()
    {
        using var realm = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryPaths.Realm));
        var machineClients = realm.RootElement.GetProperty("clients").EnumerateArray()
            .Where(c => c.TryGetProperty("protocolMappers", out var mappers) && mappers.EnumerateArray().Any(m =>
                m.GetProperty("config").TryGetProperty("claim.name", out var name) && name.GetString() == "principal_type"))
            .Select(c => c.GetProperty("clientId").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(machineClients);
        foreach (var settings in TrustedMachineClients.ServiceSettings)
        {
            Assert.Equal(machineClients, TrustedMachineClients.Read(settings).Order(StringComparer.Ordinal).ToList());
        }
    }
}
