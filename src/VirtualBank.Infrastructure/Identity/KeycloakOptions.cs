namespace VirtualBank.Infrastructure.Identity;

/// <summary>Resource-server settings for validating Keycloak access tokens (section "Keycloak").</summary>
public sealed class KeycloakOptions
{
    /// <summary>Expected <c>iss</c> claim, e.g. http://localhost:8080/realms/virtual-bank. Required.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Discovery document address. Defaults to the issuer's well-known endpoint.</summary>
    public string? MetadataAddress { get; set; }

    /// <summary>
    /// Optional pair for container networks: backchannel requests (discovery, JWKS) addressed to
    /// <see cref="PublicBaseUrl"/> are sent to <see cref="InternalBaseUrl"/>. Issuer validation is unchanged.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public string? InternalBaseUrl { get; set; }

    /// <summary>The discovery address actually used: <see cref="MetadataAddress"/> or the issuer's well-known endpoint.</summary>
    public string ResolveMetadataAddress() =>
        MetadataAddress ?? $"{Issuer.TrimEnd('/')}/.well-known/openid-configuration";

    /// <summary>Allows plain-HTTP metadata. Development only; never enable outside a local machine.</summary>
    public bool AllowInsecureDevelopmentHttp { get; set; }
}
