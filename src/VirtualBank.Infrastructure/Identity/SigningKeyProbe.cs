using System.Text.Json;

namespace VirtualBank.Infrastructure.Identity;

/// <summary>
/// Checks that incoming tokens can actually be verified: the discovery document (same backchannel path as
/// JwtBearer) names the configured issuer, its <c>jwks_uri</c> lies under that issuer, and the key set contains
/// at least one RSA signing key usable for RS256 (the only accepted algorithm). A reachable but foreign or
/// misconfigured identity provider counts as "not available".
/// </summary>
public static class SigningKeyProbe
{
    public static async Task<bool> AvailableAsync(HttpClient client, KeycloakOptions keycloak, CancellationToken cancellationToken)
    {
        try
        {
            using var discovery = JsonDocument.Parse(await client.GetStringAsync(keycloak.ResolveMetadataAddress(), cancellationToken));
            var root = discovery.RootElement;
            var issuer = keycloak.Issuer.TrimEnd('/');

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("issuer", out var advertisedIssuer) || advertisedIssuer.ValueKind != JsonValueKind.String
                || !string.Equals(advertisedIssuer.GetString()!.TrimEnd('/'), issuer, StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("jwks_uri", out var jwksValue) || jwksValue.ValueKind != JsonValueKind.String
                || !Uri.TryCreate(jwksValue.GetString(), UriKind.Absolute, out var jwksUri)
                || !AllowedJwksPrefixes(keycloak, issuer).Any(prefix => jwksUri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return false;
            }

            using var jwks = JsonDocument.Parse(await client.GetStringAsync(jwksUri, cancellationToken));
            return jwks.RootElement.ValueKind == JsonValueKind.Object
                && jwks.RootElement.TryGetProperty("keys", out var keys)
                && keys.ValueKind == JsonValueKind.Array
                && keys.EnumerateArray().Any(IsRs256SigningKey);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The key set must be published under the issuer. With a dynamic backchannel (containers), Keycloak advertises
    /// backend URLs on the internal host, so the issuer path on <see cref="KeycloakOptions.InternalBaseUrl"/> is accepted too.
    /// </summary>
    internal static IEnumerable<string> AllowedJwksPrefixes(KeycloakOptions keycloak, string issuer)
    {
        yield return issuer + "/";
        if (keycloak.PublicBaseUrl is { Length: > 0 } publicBase && keycloak.InternalBaseUrl is { Length: > 0 } internalBase
            && issuer.StartsWith(publicBase.TrimEnd('/') + "/", StringComparison.Ordinal))
        {
            yield return internalBase.TrimEnd('/') + issuer[publicBase.TrimEnd('/').Length..] + "/";
        }
    }

    private static bool IsRs256SigningKey(JsonElement key)
    {
        static string? Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        return key.ValueKind == JsonValueKind.Object
            && Text(key, "kty") == "RSA"
            && Text(key, "use") is null or "sig"
            && Text(key, "alg") is null or "RS256"
            && !string.IsNullOrEmpty(Text(key, "kid"))
            && !string.IsNullOrEmpty(Text(key, "n"))
            && !string.IsNullOrEmpty(Text(key, "e"));
    }
}
