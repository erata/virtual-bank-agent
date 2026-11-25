using System.Collections.Frozen;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Infrastructure.Identity;

/// <summary>
/// Builds a <see cref="PrincipalContext"/> from the claims of an already validated Keycloak access token.
/// Fails closed: malformed values grant nothing, and inactive principals receive no permissions.
/// </summary>
/// <remarks>
/// The principal type comes from the signed <c>principal_type</c> claim, which Keycloak adds only for
/// confidential clients restricted to the client-credentials grant (hardcoded claim mapper). The user name is
/// never used to decide the type, and the claim is honored only for a trusted client (<see cref="MachineIdentityOptions"/>).
/// A token that looks like a service account but lacks the claim, carries an unknown type, or carries the machine
/// claim for an untrusted or missing client is a configuration error and is resolved as inactive.
/// </remarks>
public sealed class PrincipalResolver(PermissionCatalog catalog, MachineIdentityOptions machines) : IPrincipalResolver
{
    public const string PrincipalTypeClaim = "principal_type";

    public const string MachineType = "machine";

    // Used only to detect a missing mapper (fail closed), never to grant the machine type.
    private const string KeycloakServiceAccountPrefix = "service-account-";

    private static readonly FrozenSet<string> AdministrativeRoles =
        new[] { "platform-admin", "security-admin", "auditor" }.ToFrozenSet(StringComparer.Ordinal);

    public PrincipalContext Resolve(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = Single(principal, "sub")
            ?? throw new PrincipalResolutionException("The token does not contain a subject ('sub') claim.");
        var username = Single(principal, "preferred_username") ?? subject;
        var roles = ReadRealmRoles(principal);
        var clientId = Single(principal, "azp");
        var (type, typeIsConsistent) = ResolveType(principal, username, clientId, roles);
        var isActive = typeIsConsistent && string.Equals(Single(principal, "employment_status"), "active", StringComparison.Ordinal);

        var groups = principal.FindAll("groups")
            .Select(c => GroupPath.TryParse(c.Value, out var path) ? path : null)
            .OfType<GroupPath>()
            .SelectMany(p => p.SelfAndAncestors())
            .ToFrozenSet();

        return new PrincipalContext
        {
            SubjectId = subject,
            Username = username,
            Type = type,
            ClientId = clientId,
            Roles = roles,
            Groups = groups,
            Permissions = isActive ? catalog.PermissionsFor(roles) : FrozenSet<Permission>.Empty,
            Branch = NullIfEmpty(Single(principal, "branch")),
            Department = NullIfEmpty(Single(principal, "department")),
            Clearance = ClearanceParser.ParseOrLowest(Single(principal, "clearance")),
            ApprovalLimit = decimal.TryParse(Single(principal, "approval_limit"), NumberStyles.Number, CultureInfo.InvariantCulture, out var limit)
                ? limit
                : null,
            IsActive = isActive,
        };
    }

    private (PrincipalType Type, bool IsConsistent) ResolveType(
        ClaimsPrincipal principal, string username, string? clientId, FrozenSet<string> roles)
    {
        var humanType = roles.Overlaps(AdministrativeRoles) ? PrincipalType.Administrative : PrincipalType.Human;
        var declared = principal.FindAll(PrincipalTypeClaim).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();

        return declared switch
        {
            // Machine identity: declared by the issuing client AND that client is on the trusted list.
            [MachineType] => (PrincipalType.Machine, machines.IsTrusted(clientId)),
            // Human token. A service-account user name without the claim means the mapper is missing: fail closed.
            [] => (humanType, !username.StartsWith(KeycloakServiceAccountPrefix, StringComparison.Ordinal)),
            // Unknown or ambiguous value.
            _ => (humanType, false),
        };
    }

    private static FrozenSet<string> ReadRealmRoles(ClaimsPrincipal principal)
    {
        // Keycloak puts realm roles into the JSON object claim "realm_access": { "roles": [...] }.
        var raw = Single(principal, "realm_access");
        if (string.IsNullOrEmpty(raw))
        {
            return FrozenSet<string>.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                return roles.EnumerateArray()
                    .Where(r => r.ValueKind == JsonValueKind.String)
                    .Select(r => r.GetString()!)
                    .ToFrozenSet(StringComparer.Ordinal);
            }
        }
        catch (JsonException)
        {
            // Malformed claim: grant no roles.
        }

        return FrozenSet<string>.Empty;
    }

    private static string? Single(ClaimsPrincipal principal, string type)
    {
        var values = principal.FindAll(type).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        // An ambiguous single-valued claim is treated as absent rather than picking one.
        return values.Count == 1 ? values[0] : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
