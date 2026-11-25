using System.Security.Claims;

namespace VirtualBank.Application.Authorization.Principals;

/// <summary>
/// Port: turns the claims of an already validated access token into a trusted <see cref="PrincipalContext"/>.
/// Implemented in Infrastructure (Keycloak claim conventions); used by Presentation once per request.
/// </summary>
public interface IPrincipalResolver
{
    /// <exception cref="PrincipalResolutionException">The token lacks claims required to identify the caller.</exception>
    PrincipalContext Resolve(ClaimsPrincipal principal);
}

/// <summary>The token cannot be mapped to a principal; the request must be rejected.</summary>
public sealed class PrincipalResolutionException(string message) : Exception(message);
