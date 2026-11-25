using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VirtualBank.Application.Assistant.Commands.AskAssistant;
using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Abstractions;
using VirtualBank.Application.Assistant.Authorization;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Presentation.Http;

namespace VirtualBank.Presentation.Assistant;

/// <summary>
/// One endpoint: a single assistant turn for the authenticated user. The principal comes from the validated token,
/// never from the request body, so neither the client nor the model can choose whose rights the tools use.
/// </summary>
public static class AssistantEndpoints
{
    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var assistant = endpoints.MapGroup("/api/assistant").RequirePrincipalContext().WithTags("Assistant");

        assistant.MapPost("/chat", async (AssistantChatRequest request, HttpContext http, AskAssistantHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AskAssistantCommand(http.GetPrincipalContext(), request.Message, request.ConversationId), ct))
                .ToHttp(reply => TypedResults.Ok(AssistantChatResponse.From(reply))));

        assistant.MapGet("/capabilities", async (string? lang, HttpContext http,
            AssistantToolCatalog catalog, AssistantLimits limits, IAssistantRuntime runtime,
            IResourceAuthorizationService authorization, CancellationToken ct) =>
        {
            var principal = http.GetPrincipalContext();
            var decision = await authorization.AuthorizeAsync(principal, AssistantResource.Chat,
                KnownPermissions.AssistantUse, ct);
            return decision.IsAllowed
                ? (IResult)TypedResults.Ok(AssistantCapabilitiesBuilder.Build(principal, catalog, limits, runtime.Mode, lang))
                : ApiResults.Forbidden("ASSISTANT_USE_REQUIRED");
        });

        return endpoints;
    }
}
