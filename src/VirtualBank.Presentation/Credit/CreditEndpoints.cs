using VirtualBank.Application.Credit.Commands.RequestCredit;
using VirtualBank.Application.Credit.Queries.ListCreditNotes;
using VirtualBank.Application.Credit.Commands.ApproveCreditApplication;
using VirtualBank.Application.Credit.Commands.AddCreditNote;
using VirtualBank.Application.Credit.Queries.GetCreditApplication;
using VirtualBank.Application.Credit.Queries.ListCreditApplications;
using VirtualBank.Presentation.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace VirtualBank.Presentation.Credit;

/// <summary>
/// Thin endpoints: bind input, take the trusted principal, call one use case, map the outcome.
/// Approval is intentionally not exposed: it requires the human-confirmation workflow of M6
/// (pending action, explicit confirmation, re-validation, idempotency).
/// </summary>
/// <summary>Optional comment (approval) or required reason (rejection).</summary>
public sealed record CreditDecisionRequest(string? Note);

public sealed record CreditRequestBody(decimal Amount, int TermMonths, string? Purpose);

public static class CreditEndpoints
{
    public static IEndpointRouteBuilder MapCreditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var applications = endpoints.MapGroup("/api/credit/applications").RequirePrincipalContext().WithTags("Credit");

        applications.MapGet("/", async (HttpContext http, ListCreditApplicationsHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListCreditApplicationsQuery(http.GetPrincipalContext()), ct))
                .ToHttp(list => TypedResults.Ok(list.Select(CreditApplicationResponse.From).ToList())));

        applications.MapGet("/{applicationId}", async (string applicationId, HttpContext http, GetCreditApplicationHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new GetCreditApplicationQuery(http.GetPrincipalContext(), applicationId), ct))
                .ToHttp(view => TypedResults.Ok(CreditApplicationResponse.From(view))));

        // Credit decisions (M6): these only PREPARE a confirmation card; nothing changes until POST /api/actions/{id}/confirm.
        applications.MapPost("/{applicationId}/approval", async (string applicationId, CreditDecisionRequest? request, HttpContext http,
                CreditApproval approval, CancellationToken ct) =>
            (await approval.PrepareApprovalAsync(http.GetPrincipalContext(), applicationId, request?.Note, ct)).ToHttp(value => TypedResults.Ok(value)));
        applications.MapPost("/{applicationId}/rejection", async (string applicationId, CreditDecisionRequest? request, HttpContext http,
                CreditApproval approval, CancellationToken ct) =>
            (await approval.PrepareRejectionAsync(http.GetPrincipalContext(), applicationId, request?.Note, ct)).ToHttp(value => TypedResults.Ok(value)));
        // Customer credit request: prepare a card; confirming creates a SUBMITTED application prepared by the customer.
        endpoints.MapGroup("/api/credit/requests").RequirePrincipalContext().WithTags("Credit")
            .MapPost("/", async (CreditRequestBody request, HttpContext http, CreditRequests requests, CancellationToken ct) =>
                (await requests.PrepareAsync(http.GetPrincipalContext(), request.Amount, request.TermMonths, request.Purpose, ct)).ToHttp(value => TypedResults.Ok(value)));
        applications.MapGet("/{applicationId}/notes", async (string applicationId, HttpContext http, ListCreditNotesHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListCreditNotesQuery(http.GetPrincipalContext(), applicationId), ct)).ToHttp(value => TypedResults.Ok(value)));
        applications.MapPost("/{applicationId}/notes", async (string applicationId, AddCreditNoteRequest request, HttpContext http, AddCreditNoteHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AddCreditNoteCommand(http.GetPrincipalContext(), applicationId, request.Text), ct))
                .ToHttp(note => TypedResults.Created(
                    $"/api/credit/applications/{note.ApplicationId.Value}/notes/{note.Id}", CreditNoteResponse.From(note))));

        return endpoints;
    }
}
