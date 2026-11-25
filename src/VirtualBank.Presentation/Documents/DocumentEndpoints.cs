using VirtualBank.Application.Documents.Commands.IngestDocuments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VirtualBank.Application.Documents;
using VirtualBank.Model.Documents;
using VirtualBank.Presentation.Http;

namespace VirtualBank.Presentation.Documents;

public sealed record DocumentAclUpdateRequest(int ExpectedVersion, IReadOnlyList<DocumentAclEntry> Entries);

/// <summary>Document list/viewer, search, machine-only ingestion and metadata-only ACL administration.</summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents").RequirePrincipalContext().WithTags("Documents");
        group.MapGet("/search", async (string query, int? topK, HttpContext http, ISecureDocumentSearch search, CancellationToken ct) =>
            (await search.SearchAsync(http.GetPrincipalContext(), query, topK ?? 5, ct)).ToHttp(value => TypedResults.Ok(value)));
        group.MapGet("/", async (HttpContext http, SecureDocumentCatalog catalog, CancellationToken ct) =>
            (await catalog.ListAsync(http.GetPrincipalContext(), ct)).ToHttp(value => TypedResults.Ok(value)));
        group.MapGet("/{id}", async (string id, HttpContext http, SecureDocumentExcerpt reader, CancellationToken ct) =>
            (await reader.ReadFullAsync(http.GetPrincipalContext(), id, ct)).ToHttp(value => TypedResults.Ok(value)));
        // Original bytes for the viewer (PDF shown natively, JSON/MD/TXT rendered client-side). Inline, never sniffed.
        group.MapGet("/{id}/file", async (string id, HttpContext http, SecureDocumentExcerpt reader, CancellationToken ct) =>
            (await reader.ReadOriginalAsync(http.GetPrincipalContext(), id, ct)).ToHttp(file =>
            {
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.CacheControl = "no-store";
                return TypedResults.File(file.Content, file.ContentType, file.FileName);
            }));
        group.MapGet("/{id}/excerpt", async (string id, HttpContext http, SecureDocumentExcerpt reader, CancellationToken ct) =>
            (await reader.ReadAsync(http.GetPrincipalContext(), id, ct)).ToHttp(value => TypedResults.Ok(value)));
        group.MapPost("/ingest", async (HttpContext http, IngestDocumentsHandler ingest, CancellationToken ct) =>
            (await ingest.HandleAsync(new IngestDocumentsCommand(http.GetPrincipalContext()), ct)).ToHttp(value => TypedResults.Ok(value)));

        // Separate prefix so the ACL index can never be confused with a document ID route.
        endpoints.MapGroup("/api/acl").RequirePrincipalContext().WithTags("Documents")
            .MapGet("/documents", async (HttpContext http, DocumentAclAdministration acl, CancellationToken ct) =>
                (await acl.ListAsync(http.GetPrincipalContext(), ct)).ToHttp(value => TypedResults.Ok(value)));
        group.MapGet("/{id}/acl", async (string id, HttpContext http, DocumentAclAdministration acl, CancellationToken ct) =>
            (await acl.ReadAsync(http.GetPrincipalContext(), id, ct)).ToHttp(value => TypedResults.Ok(value)));
        group.MapGet("/{id}/acl/effective", async (string id, HttpContext http, DocumentAclAdministration acl, CancellationToken ct) =>
            (await acl.ReadEffectiveAsync(http.GetPrincipalContext(), id, ct)).ToHttp(value => TypedResults.Ok(value)));
        group.MapPut("/{id}/acl", async (string id, DocumentAclUpdateRequest request, HttpContext http,
            DocumentAclAdministration acl, CancellationToken ct) =>
            (await acl.UpdateAsync(http.GetPrincipalContext(), id, request.ExpectedVersion, request.Entries, ct))
                .ToHttp(value => TypedResults.Ok(value)));
        group.MapGet("/{id}/acl/history", async (string id, HttpContext http, DocumentAclAdministration acl, CancellationToken ct) =>
            (await acl.HistoryAsync(http.GetPrincipalContext(), id, ct)).ToHttp(value => TypedResults.Ok(value)));
        return endpoints;
    }
}
