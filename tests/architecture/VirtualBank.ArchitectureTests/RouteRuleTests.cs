using VirtualBank.Presentation.Http;
using VirtualBank.Specifications;
using VirtualBank.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace VirtualBank.ArchitectureTests;

/// <summary>
/// Route rules checked on the endpoints the real composition root registers (same code path as Program.cs), read
/// from the routing EndpointDataSource — not by searching source text. No server is started and no database or
/// identity provider is contacted.
/// </summary>
public sealed class RouteRuleTests
{
    private sealed record Route(string Method, string Pattern, bool RequiresAuthorization, bool HasPrincipalContext)
    {
        public override string ToString() => $"{Method} {Pattern}";
    }

    private static readonly Lazy<IReadOnlyList<Route>> ApiRoutes = new(() =>
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "RouteTests" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:Issuer"] = "http://keycloak.test/realms/route-tests",
            ["Authorization:CatalogPath"] = RepositoryPaths.Permissions,
            ["ConnectionStrings:CustomerDb"] = "Host=unused.test;Database=unused",
            ["ConnectionStrings:CreditDb"] = "Host=unused.test;Database=unused",
            ["ConnectionStrings:DocumentsDb"] = "Host=unused.test;Database=unused",
            ["ConnectionStrings:DocumentWriterDb"] = "Host=unused.test;Database=unused",
            ["ConnectionStrings:DocumentAclAdminDb"] = "Host=unused.test;Database=unused",
        });
        builder.Services.AddVirtualBank(builder.Configuration);
        var app = builder.Build().UseVirtualBank();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"]).Select(method => new Route(
                method,
                "/" + endpoint.RoutePattern.RawText!.Trim('/'),
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any() && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null,
                endpoint.Metadata.GetMetadata<PrincipalContextRequired>() is not null)))
            .OrderBy(r => r.Pattern, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal)
            .ToList();
    });

    private static readonly string[] HealthRoutes = ["GET /health/live", "GET /health/ready"];

    [Fact]
    public void Api_exposes_exactly_the_allowlisted_routes_and_approval_only_with_confirmation()
    {
        // Allowlist: any new route (an approval endpoint in particular) must be added here deliberately, in M6.
        var expected = new[]
        {
            "GET /api/customers",
            "GET /api/customers/{customerId}",
            "GET /api/customers/{customerId}/accounts",
            "GET /api/credit/applications",
            "GET /api/credit/applications/{applicationId}",
            "POST /api/credit/applications/{applicationId}/notes",
            "POST /api/credit/applications/{applicationId}/approval",
            "POST /api/credit/applications/{applicationId}/rejection",
            "GET /api/credit/applications/{applicationId}/notes",
            "POST /api/actions/{actionId:guid}/confirm",
            "DELETE /api/actions/{actionId:guid}",
            "POST /api/self/transfers",
            "POST /api/self/internal-transfers",
            "POST /api/self/standing-orders",
            "POST /api/customers/{customerId}/accounts/{accountId}/deposits",
            "POST /api/customers/{customerId}/accounts/{accountId}/withdrawals",
            "POST /api/credit/requests",
            "POST /api/self/payments/{paymentId}/cancellation",
            "POST /api/self/standing-orders/{orderId}/pause",
            "POST /api/self/standing-orders/{orderId}/resume",
            "POST /api/assistant/chat",
            "GET /api/assistant/capabilities",
            "GET /api/self/profile",
            "GET /api/self/accounts",
            "GET /api/self/balances",
            "GET /api/self/payments/upcoming",
            "GET /api/self/transactions/recent",
            "GET /api/self/standing-orders",
            "GET /api/access",
            "GET /api/acl/documents",
            "GET /api/documents",
            "GET /api/documents/{id}",
            "GET /api/documents/{id}/file",
            "GET /api/documents/search",
            "GET /api/documents/{id}/excerpt",
            "POST /api/documents/ingest",
            "GET /api/documents/{id}/acl",
            "GET /api/documents/{id}/acl/effective",
            "PUT /api/documents/{id}/acl",
            "GET /api/documents/{id}/acl/history",
        }.Concat(HealthRoutes).Order(StringComparer.Ordinal);

        Assert.Equal(expected, ApiRoutes.Value.Select(r => r.ToString()).Order(StringComparer.Ordinal));
        // M6: a credit decision route only PREPARES a card; execution exists exactly once, behind /api/actions.
        Assert.Equal(["POST /api/credit/applications/{applicationId}/approval"],
            ApiRoutes.Value.Where(r => r.Pattern.Contains("approv", StringComparison.OrdinalIgnoreCase)).Select(r => r.ToString()));
        Assert.Equal(["POST /api/actions/{actionId:guid}/confirm"],
            ApiRoutes.Value.Where(r => r.Pattern.EndsWith("/confirm", StringComparison.Ordinal)).Select(r => r.ToString()));
        Assert.DoesNotContain(ApiRoutes.Value, r => r.Pattern.EndsWith("/approve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_business_route_requires_authentication_and_the_principal_filter()
    {
        var business = ApiRoutes.Value.Where(r => r.Pattern.StartsWith("/api/", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(business);
        Assert.Empty(business.Where(r => !r.RequiresAuthorization).Select(r => r.ToString()));
        Assert.Empty(business.Where(r => !r.HasPrincipalContext).Select(r => r.ToString()));
    }

    [Fact]
    public void Only_health_routes_are_anonymous()
    {
        var anonymous = ApiRoutes.Value.Where(r => !r.RequiresAuthorization).Select(r => r.ToString()).Order(StringComparer.Ordinal);
        Assert.Equal(HealthRoutes.Order(StringComparer.Ordinal), anonymous);
    }
}
