using VirtualBank.Application.Customers.Commands.SelfService;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VirtualBank.Application.Customers.Queries.GetMyBanking;
using VirtualBank.Presentation.Http;

namespace VirtualBank.Presentation.Customers;

/// <summary>No customer-id path parameter: the validated access token is the only identity input.</summary>
public sealed record InternalTransferRequest(string? FromAccountId, string? ToAccountId, decimal Amount);

public static class SelfServiceEndpoints
{
    public static IEndpointRouteBuilder MapSelfServiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/api/self").RequirePrincipalContext().WithTags("Self Service");
        me.MapGet("/profile", async (HttpContext http, GetMyBankingHandler h, CancellationToken ct) =>
            (await h.ProfileAsync(http.GetPrincipalContext(), ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapGet("/accounts", async (HttpContext http, GetMyBankingHandler h, CancellationToken ct) =>
            (await h.AccountsAsync(http.GetPrincipalContext(), ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapGet("/balances", async (HttpContext http, GetMyBankingHandler h, CancellationToken ct) =>
            (await h.BalanceAsync(http.GetPrincipalContext(), ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapGet("/payments/upcoming", async (int? days, HttpContext http, GetMyBankingHandler h, CancellationToken ct) =>
            (await h.UpcomingAsync(http.GetPrincipalContext(), days ?? 30, ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapGet("/transactions/recent", async (int? days, HttpContext http, GetMyBankingHandler h, CancellationToken ct) =>
            (await h.RecentAsync(http.GetPrincipalContext(), days ?? 30, ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapGet("/standing-orders", async (HttpContext http, GetMyBankingHandler h, CancellationToken ct) =>
            (await h.StandingOrdersAsync(http.GetPrincipalContext(), ct)).ToHttp(x => TypedResults.Ok(x)));
        // Self-service orders: prepare a confirmation card only; POST /api/actions/{id}/confirm executes.
        me.MapPost("/transfers", async (TransferRequest request, HttpContext http, MyPaymentOrders orders, CancellationToken ct) =>
            (await orders.PrepareTransferAsync(http.GetPrincipalContext(), request, ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapPost("/internal-transfers", async (InternalTransferRequest request, HttpContext http, MyPaymentOrders orders, CancellationToken ct) =>
            (await orders.PrepareInternalTransferAsync(http.GetPrincipalContext(), request.FromAccountId ?? "", request.ToAccountId ?? "",
                request.Amount, ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapPost("/standing-orders", async (StandingOrderRequest request, HttpContext http, MyPaymentOrders orders, CancellationToken ct) =>
            (await orders.PrepareCreateStandingOrderAsync(http.GetPrincipalContext(), request, ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapPost("/payments/{paymentId}/cancellation", async (string paymentId, HttpContext http, MyPaymentOrders orders, CancellationToken ct) =>
            (await orders.PrepareCancelPaymentAsync(http.GetPrincipalContext(), paymentId, ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapPost("/standing-orders/{orderId}/pause", async (string orderId, HttpContext http, MyPaymentOrders orders, CancellationToken ct) =>
            (await orders.PrepareStandingOrderAsync(http.GetPrincipalContext(), orderId, false, ct)).ToHttp(x => TypedResults.Ok(x)));
        me.MapPost("/standing-orders/{orderId}/resume", async (string orderId, HttpContext http, MyPaymentOrders orders, CancellationToken ct) =>
            (await orders.PrepareStandingOrderAsync(http.GetPrincipalContext(), orderId, true, ct)).ToHttp(x => TypedResults.Ok(x)));
        return endpoints;
    }
}
