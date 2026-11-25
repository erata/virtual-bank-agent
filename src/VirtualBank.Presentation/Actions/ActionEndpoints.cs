using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Presentation.Http;

namespace VirtualBank.Presentation.Actions;

/// <summary>
/// The ONLY way to execute a prepared high-impact action (credit decision, payment order ...): the same user confirms
/// the single-use card before it expires. Used by the menu screens and by cards the assistant prepared.
/// </summary>
public static class ActionEndpoints
{
    public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/actions").RequirePrincipalContext().WithTags("Actions");
        group.MapPost("/{actionId:guid}/confirm", async (Guid actionId, HttpContext http, ActionConfirmation confirmation, CancellationToken ct) =>
            (await confirmation.ConfirmAsync(http.GetPrincipalContext(), actionId, ct)).ToHttp(value => TypedResults.Ok(value)));
        group.MapDelete("/{actionId:guid}", (Guid actionId, HttpContext http, ActionConfirmation confirmation) =>
            confirmation.Discard(http.GetPrincipalContext(), actionId).ToHttp(_ => TypedResults.NoContent()));
        return endpoints;
    }
}
