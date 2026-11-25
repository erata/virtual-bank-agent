using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace VirtualBank.Infrastructure.Identity;

public static class JwtAuthentication
{
    internal const string KeycloakMetadataClient = "keycloak-metadata";

    /// <summary>
    /// Validates signature (RS256 only), issuer, audience and lifetime of Keycloak access tokens.
    /// Only tokens issued for this API's audience (Keycloak:Audience, default virtual-bank-api) are accepted.
    /// </summary>
    public static IServiceCollection AddKeycloakJwtAuthentication(this IServiceCollection services, IConfiguration configuration, string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        var keycloak = configuration.GetSection("Keycloak").Get<KeycloakOptions>() ?? new KeycloakOptions();
        if (string.IsNullOrWhiteSpace(keycloak.Issuer))
        {
            throw new InvalidOperationException("Configuration value 'Keycloak:Issuer' is required.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MetadataAddress = keycloak.ResolveMetadataAddress();
            options.RequireHttpsMetadata = !keycloak.AllowInsecureDevelopmentHttp;
            options.MapInboundClaims = false;       // keep "sub", "groups", "realm_access" as issued
            options.IncludeErrorDetails = false;    // no token diagnostics in WWW-Authenticate headers

            if (!string.IsNullOrWhiteSpace(keycloak.PublicBaseUrl) && !string.IsNullOrWhiteSpace(keycloak.InternalBaseUrl))
            {
                options.BackchannelHttpHandler = new BackchannelRewriteHandler(
                    new Uri(keycloak.PublicBaseUrl), new Uri(keycloak.InternalBaseUrl));
            }

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = keycloak.Issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                NameClaimType = "preferred_username",
                ClockSkew = TimeSpan.FromSeconds(15),
            };
        });

        services.AddAuthorization();

        // Same network path as token validation, used by the readiness probe.
        services.AddSingleton(keycloak);
        services.AddHttpClient(KeycloakMetadataClient, client => client.Timeout = TimeSpan.FromSeconds(3))
            .ConfigurePrimaryHttpMessageHandler(() =>
                !string.IsNullOrWhiteSpace(keycloak.PublicBaseUrl) && !string.IsNullOrWhiteSpace(keycloak.InternalBaseUrl)
                    ? new BackchannelRewriteHandler(new Uri(keycloak.PublicBaseUrl), new Uri(keycloak.InternalBaseUrl))
                    : new HttpClientHandler());
        return services;
    }
}
