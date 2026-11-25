using System.Text.Json;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Documents.Authorization;
using VirtualBank.Infrastructure.Persistence;
using VirtualBank.Model.Documents;

namespace VirtualBank.Infrastructure.Documents;

/// <summary>Document metadata under the reader role's RLS session. No chunk content is selected.</summary>
public sealed class PostgresDocumentCatalogRepository(DocumentReaderDatabase database) : IDocumentCatalogRepository
{
    public const int MaxDocuments = 100;

    public async Task<IReadOnlyList<DocumentListCandidate>> ListAsync(PrincipalContext principal, CancellationToken ct)
    {
        await using var session = await DocumentDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var command = session.CreateCommand("""
            SELECT d.id, d.title, d.language, d.classification, d.branch, docs.readable_acl(d.id)::text,
                   d.source_path, d.updated_at
            FROM docs.documents d
            ORDER BY d.classification, d.id
            LIMIT @limit
            """).AddParameter("limit", MaxDocuments);
        await using var rows = await command.ExecuteReaderAsync(ct);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var result = new List<DocumentListCandidate>();
        while (await rows.ReadAsync(ct))
        {
            // Missing ACL metadata is a security failure, not an empty ACL or an implicit grant.
            if (rows.IsDBNull(5)) throw new InvalidOperationException("Authorized ACL metadata is unavailable.");
            var acl = JsonSerializer.Deserialize<List<DocumentAclEntry>>(rows.GetString(5), options)
                ?? throw new InvalidOperationException("Authorized ACL metadata could not be parsed.");
            var id = rows.GetString(0);
            var classification = rows.GetInt16(3);
            result.Add(new DocumentListCandidate(
                new DocumentListItem(id, rows.GetString(1), rows.GetString(2), (Clearance)classification,
                    Path.GetFileName(rows.GetString(6)), SecureDocumentExcerpt.FormatOf(rows.GetString(6)),
                    rows.GetFieldValue<DateTimeOffset>(7)),
                new DocumentResource(id, classification, rows.IsDBNull(4) ? null : rows.GetString(4), acl)));
        }
        return result;
    }
}
