using VirtualBank.Application.Customers.Commands.CashDesk;
using VirtualBank.Application.Customers.Queries.GetCustomer;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.ListCustomers;
using VirtualBank.Presentation.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace VirtualBank.Presentation.Customers;

/// <summary>Thin endpoints for the customer queries; every decision is made in the Application layer.</summary>
public sealed record CashRequest(decimal Amount);

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customers = endpoints.MapGroup("/api/customers").RequirePrincipalContext().WithTags("Customers");

        customers.MapGet("/", async (HttpContext http, ListCustomersHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListCustomersQuery(http.GetPrincipalContext()), ct))
                .ToHttp(list => TypedResults.Ok(list.Select(CustomerSummaryResponse.From).ToList())));

        customers.MapGet("/{customerId}", async (string customerId, HttpContext http, GetCustomerHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new GetCustomerQuery(http.GetPrincipalContext(), customerId), ct))
                .ToHttp(view => TypedResults.Ok(CustomerDetailResponse.From(view))));

        customers.MapGet("/{customerId}/accounts", async (string customerId, HttpContext http, ListCustomerAccountsHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListCustomerAccountsQuery(http.GetPrincipalContext(), customerId), ct))
                .ToHttp(list => TypedResults.Ok(list.Select(AccountResponse.From).ToList())));

        // Branch cash desk (account.cash): prepare a confirmation card; POST /api/actions/{id}/confirm books it.
        customers.MapPost("/{customerId}/accounts/{accountId}/deposits", async (string customerId, string accountId, CashRequest request,
                HttpContext http, CashDesk desk, CancellationToken ct) =>
            (await desk.PrepareAsync(http.GetPrincipalContext(), customerId, accountId, request.Amount, deposit: true, ct)).ToHttp(x => TypedResults.Ok(x)));
        customers.MapPost("/{customerId}/accounts/{accountId}/withdrawals", async (string customerId, string accountId, CashRequest request,
                HttpContext http, CashDesk desk, CancellationToken ct) =>
            (await desk.PrepareAsync(http.GetPrincipalContext(), customerId, accountId, request.Amount, deposit: false, ct)).ToHttp(x => TypedResults.Ok(x)));
        return endpoints;
    }
}
