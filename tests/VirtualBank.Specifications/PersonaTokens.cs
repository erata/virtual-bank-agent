using System.Security.Claims;
using System.Text.Json;

namespace VirtualBank.Specifications;

/// <summary>
/// Builds the access-token claims Keycloak issues for each realm user, following the protocol
/// mappers defined in realm-virtual-bank.json (realm_access, full-path groups, attribute claims).
/// Unit-level stand-in for real tokens; identity integration tests verify the same claims
/// against a running Keycloak.
/// </summary>
public sealed class PersonaTokens
{
    private static readonly (string Attribute, string Claim)[] AttributeClaims =
    [
        ("branch", "branch"),
        ("department", "department"),
        ("clearance", "clearance"),
        ("approvalLimit", "approval_limit"),
        ("employmentStatus", "employment_status"),
    ];

    private readonly Dictionary<string, ClaimsPrincipal> _principals = new(StringComparer.Ordinal);

    private PersonaTokens()
    {
    }

    public IReadOnlyDictionary<string, string> SubjectIds { get; private init; } = new Dictionary<string, string>();

    public static PersonaTokens Load(string realmPath)
    {
        using var realm = JsonDocument.Parse(File.ReadAllText(realmPath));

        // Hardcoded-claim mappers per client, applied to tokens issued to that client (e.g. principal_type).
        var hardcodedClaims = realm.RootElement.GetProperty("clients").EnumerateArray()
            .ToDictionary(
                c => c.GetProperty("clientId").GetString()!,
                c => (c.TryGetProperty("protocolMappers", out var mappers) ? mappers.EnumerateArray().ToList() : [])
                    .Where(m => m.GetProperty("protocolMapper").GetString() == "oidc-hardcoded-claim-mapper")
                    .Where(m => m.GetProperty("config").GetProperty("access.token.claim").GetString() == "true")
                    .Select(m => new Claim(m.GetProperty("config").GetProperty("claim.name").GetString()!, m.GetProperty("config").GetProperty("claim.value").GetString()!))
                    .ToList(),
                StringComparer.Ordinal);
        var tokens = new PersonaTokens
        {
            SubjectIds = realm.RootElement.GetProperty("users").EnumerateArray()
                .ToDictionary(u => u.GetProperty("username").GetString()!, u => u.GetProperty("id").GetString()!, StringComparer.Ordinal),
        };

        foreach (var user in realm.RootElement.GetProperty("users").EnumerateArray())
        {
            var username = user.GetProperty("username").GetString()!;
            var azp = user.TryGetProperty("serviceAccountClientId", out var client) ? client.GetString()! : "virtual-bank-cli";
            var claims = new List<Claim>
            {
                new("sub", user.GetProperty("id").GetString()!),
                new("preferred_username", username),
                new("azp", azp),
                new("realm_access", JsonSerializer.Serialize(new { roles = user.GetProperty("realmRoles").EnumerateArray().Select(r => r.GetString()).ToArray() })),
            };

            claims.AddRange(hardcodedClaims.TryGetValue(azp, out var fromClient) ? fromClient : []);

            if (user.TryGetProperty("groups", out var groups))
            {
                claims.AddRange(groups.EnumerateArray().Select(g => new Claim("groups", g.GetString()!)));
            }

            var attributes = user.GetProperty("attributes");
            foreach (var (attribute, claim) in AttributeClaims)
            {
                if (attributes.TryGetProperty(attribute, out var values) && values.GetArrayLength() == 1)
                {
                    claims.Add(new Claim(claim, values[0].GetString()!));
                }
            }

            tokens._principals[username] = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "simulated-keycloak-token"));
        }

        return tokens;
    }

    public ClaimsPrincipal For(string username) =>
        _principals.TryGetValue(username, out var principal)
            ? principal
            : throw new KeyNotFoundException($"Persona '{username}' does not exist in the realm.");
}
