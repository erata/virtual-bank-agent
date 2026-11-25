using System.Data.Common;
using System.Text.Json;
using VirtualBank.Application.Documents;
using VirtualBank.Infrastructure.Persistence;
using VirtualBank.Model.Documents;

namespace VirtualBank.Infrastructure.Documents;

/// <summary>ACL-only DB identity; it cannot SELECT protected chunk content.</summary>
public sealed class PostgresDocumentAclAdministrationRepository(DocumentAclAdminDatabase database)
    : IDocumentAclAdministrationRepository
{
    public async Task<DocumentAclSnapshot?> ReadAsync(string documentId, CancellationToken ct)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(ct);
        return await ReadCoreAsync(connection, null, documentId, ct);
    }

    private static async Task<DocumentAclSnapshot?> ReadCoreAsync(DbConnection connection, DbTransaction? tx,
        string documentId, CancellationToken ct)
    {
        string? title = null;
        var version = 0;
        var classification = 0;
        string? branch = null;
        await using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = tx;
            metadata.CommandText = "SELECT title,acl_version,classification,branch FROM docs.documents WHERE id=@id";
            metadata.AddParameter("id", documentId);
            await using var row = await metadata.ExecuteReaderAsync(ct);
            if (await row.ReadAsync(ct))
            {
                title = row.GetString(0);
                version = row.GetInt32(1);
                classification = row.GetInt16(2);
                branch = row.IsDBNull(3) ? null : row.GetString(3);
            }
        }
        if (title is null) return null;
        var entries = new List<DocumentAclEntry>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = "SELECT subject_kind,subject_value,effect FROM docs.acls WHERE document_id=@id ORDER BY subject_kind,subject_value,effect";
            command.AddParameter("id", documentId);
            await using var row = await command.ExecuteReaderAsync(ct);
            while (await row.ReadAsync(ct)) entries.Add(new DocumentAclEntry(row.GetString(0), row.GetString(1), row.GetString(2)));
        }
        return new DocumentAclSnapshot(documentId, title, version, entries, classification, branch);
    }

    public async Task<DocumentAclSnapshot?> UpdateAsync(string documentId, string changedBy, int expectedVersion,
        IReadOnlyList<DocumentAclEntry> entries, CancellationToken ct)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var affected = 0;
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = "UPDATE docs.documents SET acl_version=acl_version+1,updated_at=now() WHERE id=@id AND acl_version=@version";
            update.AddParameter("id", documentId).AddParameter("version", expectedVersion);
            affected = await update.ExecuteNonQueryAsync(ct);
        }
        if (affected == 0) { await tx.RollbackAsync(ct); return null; }
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM docs.acls WHERE document_id=@id";
            delete.AddParameter("id", documentId);
            await delete.ExecuteNonQueryAsync(ct);
        }
        foreach (var entry in entries)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO docs.acls(document_id,subject_kind,subject_value,effect) VALUES(@id,@kind,@value,@effect)";
            insert.AddParameter("id", documentId).AddParameter("kind", entry.SubjectKind)
                .AddParameter("value", entry.SubjectValue).AddParameter("effect", entry.Effect);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = tx;
            history.CommandText = "INSERT INTO docs.acl_history(document_id,version,changed_by,changes_json) VALUES (@id,@version,@principal,@change::jsonb)";
            history.AddParameter("id", documentId).AddParameter("version", expectedVersion + 1)
                .AddParameter("principal", changedBy).AddParameter("change", JsonSerializer.Serialize(entries));
            await history.ExecuteNonQueryAsync(ct);
        }
        var snapshot = await ReadCoreAsync(connection, tx, documentId, ct);
        await tx.CommitAsync(ct);
        return snapshot;
    }

    /// <summary>Uses only the columns granted to document_acl_admin (id, title, acl_version, classification, branch).</summary>
    public async Task<IReadOnlyList<DocumentAclIndexItem>> ListAsync(CancellationToken ct)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id, d.title, d.acl_version, d.classification, d.branch,
                   (SELECT count(*) FROM docs.acls a WHERE a.document_id = d.id)::int
            FROM docs.documents d ORDER BY d.id LIMIT 500
            """;
        var items = new List<DocumentAclIndexItem>();
        await using var rows = await command.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
            items.Add(new DocumentAclIndexItem(rows.GetString(0), rows.GetString(1), rows.GetInt32(2), rows.GetInt16(3),
                rows.IsDBNull(4) ? null : rows.GetString(4), rows.GetInt32(5)));
        return items;
    }

    public async Task<IReadOnlyList<DocumentAclRevision>> HistoryAsync(string documentId, CancellationToken ct)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version,changed_by,changed_at,changes_json::text FROM docs.acl_history WHERE document_id=@id ORDER BY version DESC LIMIT 100";
        command.AddParameter("id", documentId);
        var history = new List<DocumentAclRevision>();
        await using var rows = await command.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
            history.Add(new DocumentAclRevision(rows.GetInt32(0), rows.GetString(1), rows.GetFieldValue<DateTimeOffset>(2), rows.GetString(3)));
        return history;
    }
}
