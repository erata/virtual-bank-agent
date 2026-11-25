using VirtualBank.Application.Authorization.Principals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VirtualBank.Presentation.Http;

/// <summary>
/// Endpoint filter that turns the validated token into a <see cref="PrincipalContext"/> once per request.
/// Resolution failures and inactive principals end the request with 403; nothing falls back to an
/// anonymous or default context.
/// </summary>
public sealed class PrincipalContextFilter : IEndpointFilter
{
    internal const string ItemKey = "virtual-bank.principal";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true)
        {
            return TypedResults.Unauthorized();
        }

        var resolver = http.RequestServices.GetRequiredService<IPrincipalResolver>();
        PrincipalContext principal;
        try
        {
            principal = resolver.Resolve(http.User);
        }
        catch (PrincipalResolutionException ex)
        {
            http.RequestServices.GetRequiredService<ILogger<PrincipalContextFilter>>()
                .LogWarning("Rejected authenticated request: {Reason}", ex.Message);
            return ApiResults.Forbidden();
        }

        if (!principal.IsActive)
        {
            return ApiResults.Forbidden();
        }

        http.Items[ItemKey] = principal;
        return await next(context);
    }
}

public static class PrincipalContextExtensions
{
    /// <summary>Requires a valid token and a resolved, active principal for the endpoint group.</summary>
    public static TBuilder RequirePrincipalContext<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter(new PrincipalContextFilter());
        builder.WithMetadata(PrincipalContextRequired.Instance);
        return builder;
    }

    public static PrincipalContext GetPrincipalContext(this HttpContext context) =>
        context.Items.TryGetValue(PrincipalContextFilter.ItemKey, out var value) && value is PrincipalContext principal
            ? principal
            : throw new InvalidOperationException("No principal context; the endpoint is missing RequirePrincipalContext().");
}

/// <summary>Endpoint metadata marking that the principal filter runs, so tests can verify it on every business route.</summary>
public sealed class PrincipalContextRequired
{
    public static readonly PrincipalContextRequired Instance = new();

    private PrincipalContextRequired()
    {
    }
}
