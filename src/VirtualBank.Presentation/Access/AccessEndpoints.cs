using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VirtualBank.Presentation.Http;

namespace VirtualBank.Presentation.Access;

/// <summary>The caller's OWN effective permissions, resolved server-side from the role catalog.</summary>
public sealed record AccessResponse(IReadOnlyList<string> Permissions);

/// <summary>
/// Lets the UI show only the menus a user can use. It is a convenience, not a security boundary:
/// every endpoint still authorizes each request itself, and the response says nothing about other users.
/// </summary>
public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/access").RequirePrincipalContext().WithTags("Access")
            .MapGet("/", (HttpContext http) =>
            {
                var principal = http.GetPrincipalContext();
                var permissions = principal.IsActive
                    ? principal.Permissions.Select(p => p.Id).Order(StringComparer.Ordinal).ToArray()
                    : [];
                return TypedResults.Ok(new AccessResponse(permissions));
            });
        return endpoints;
    }
}
