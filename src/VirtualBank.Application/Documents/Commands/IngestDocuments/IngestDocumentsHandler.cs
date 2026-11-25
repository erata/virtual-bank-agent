using VirtualBank.Application.Documents;
using System.Security.Cryptography;
using System.Text;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Model.Documents;

namespace VirtualBank.Application.Documents.Commands.IngestDocuments;

public sealed record IngestDocumentsCommand(PrincipalContext Principal);

/// <summary>Real embeddings, deterministic chunk IDs, idempotent updates and one-time ACL import.</summary>
public sealed class IngestDocumentsHandler(IDocumentSourceReader source, IEmbeddingProvider embeddings,
    IDocumentIngestionRepository repository)
{
    public async Task<UseCaseResult<IngestionSummary>> HandleAsync(IngestDocumentsCommand command, CancellationToken ct)
    {
        return await ExecuteAsync(command.Principal, ct);
    }

    private async Task<UseCaseResult<IngestionSummary>> ExecuteAsync(PrincipalContext principal, CancellationToken ct)
    {
        if (!principal.IsActive || principal.Type != PrincipalType.Machine ||
            !string.Equals(principal.ClientId, "svc-ingestion", StringComparison.Ordinal) ||
            !principal.HasPermission(KnownPermissions.DocumentIngest))
            return UseCaseResult<IngestionSummary>.Forbidden(null);

        var inputs = await source.ReadManifestAsync(ct);
        var updated = 0;
        var chunks = 0;
        foreach (var parsed in inputs)
        {
            ct.ThrowIfCancellationRequested();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parsed.Text)));
            var record = new BankDocument(parsed.Id, parsed.Title, parsed.SourcePath, parsed.Language,
                parsed.Classification, parsed.Branch, hash, embeddings.ModelId, 1, 1);
            // Do NOT call the embedding service when content and security-relevant metadata are unchanged.
            if (await repository.HasCurrentDocumentAsync(record, ct)) continue;
            var pieces = parsed.Pages is { Count: > 0 }
                ? parsed.Pages.SelectMany(page => DocumentChunker.Split(page.Text)
                    .Select(text => (Text: text, Page: (int?)page.PageNumber))).ToArray()
                : DocumentChunker.Split(parsed.Text).Select(text => (Text: text, Page: (int?)null)).ToArray();
            var entries = new List<DocumentChunk>(pieces.Length);
            for (var i = 0; i < pieces.Length; i++)
            {
                entries.Add(new DocumentChunk($"{parsed.Id}-{i:D4}", parsed.Id, i, pieces[i].Text, pieces[i].Page,
                    await embeddings.EmbedAsync(pieces[i].Text, ct)));
            }
            if (await repository.UpsertAsync(new DocumentIngestionEntry(record, parsed.InitialAcl, entries), ct))
                updated++;
            chunks += entries.Count;
        }
        return UseCaseResult<IngestionSummary>.Success(new IngestionSummary(inputs.Count, updated,
            inputs.Count - updated, chunks));
    }
}

