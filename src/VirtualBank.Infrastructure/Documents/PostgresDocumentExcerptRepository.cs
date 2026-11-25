using System.Text.Json;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Documents.Authorization;
using VirtualBank.Infrastructure.Persistence;
using VirtualBank.Model.Documents;

namespace VirtualBank.Infrastructure.Documents;

/// <summary>Exact ID lookup, no vector similarity required; all result chunks/ACL protected by PostgreSQL RLS.</summary>
public sealed class PostgresDocumentExcerptRepository(DocumentReaderDatabase database) : IDocumentExcerptRepository
{
    public async Task<DocumentExcerptCandidate?> ReadAsync(PrincipalContext principal, string documentId, CancellationToken ct,
        int maxChunks = 6, int maxCharsPerChunk = 900)
    {
        await using var session = await DocumentDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var command = session.CreateCommand("""
            SELECT d.id, d.title, d.classification, d.branch, docs.readable_acl(d.id)::text,
                   left(c.content, @chars), c.page_number, d.source_path, d.language, d.updated_at
            FROM docs.documents d
            JOIN docs.chunks c ON c.document_id=d.id
            WHERE d.id=@id ORDER BY c.chunk_index LIMIT @limit
            """).AddParameter("id",documentId).AddParameter("limit", maxChunks).AddParameter("chars", maxCharsPerChunk);
        await using var rows = await command.ExecuteReaderAsync(ct);
        var sections = new List<DocumentExcerptSection>();
        string? title = null;
        int classification = 0;
        string? branch = null;
        List<DocumentAclEntry>? acl = null;
        DocumentSourceInfo? source = null;
        while (await rows.ReadAsync(ct))
        {
            if (rows.IsDBNull(4)) throw new InvalidOperationException("Missing authorized ACL metadata.");
            if (title is null)
            {
                title = rows.GetString(1);
                classification = rows.GetInt16(2);
                branch = rows.IsDBNull(3) ? null : rows.GetString(3);
                acl = JsonSerializer.Deserialize<List<DocumentAclEntry>>(rows.GetString(4),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("Malformed ACL metadata.");
                source = new DocumentSourceInfo(rows.GetString(7), rows.GetString(8),
                    rows.GetFieldValue<DateTimeOffset>(9));
            }
            sections.Add(new DocumentExcerptSection(rows.GetString(5), rows.IsDBNull(6) ? null : rows.GetInt32(6)));
        }
        return title is null || acl is null ? null : new DocumentExcerptCandidate(
            new DocumentExcerpt(documentId, title, sections),
            new DocumentResource(documentId, classification, branch, acl), source);
    }
}
