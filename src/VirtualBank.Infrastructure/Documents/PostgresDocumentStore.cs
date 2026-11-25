using System.Data.Common;
using System.Text.Json;
using VirtualBank.Application.Documents.Authorization;
using Pgvector;
using Npgsql;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Documents;
using VirtualBank.Infrastructure.Persistence;
using VirtualBank.Model.Documents;

namespace VirtualBank.Infrastructure.Documents;

public sealed record DocumentReaderDatabase(DbDataSource DataSource);
public sealed record DocumentWriterDatabase(DbDataSource DataSource);
public sealed record DocumentAclAdminDatabase(DbDataSource DataSource);

/// <summary>Real pgvector cosine retrieval inside one transaction-local, RLS-protected document session.</summary>
public sealed class PostgresDocumentSearchRepository(DocumentReaderDatabase database) : IDocumentSearchRepository
{
    public const string SearchSql = """
        SELECT d.id, d.title, coalesce(c.page_number::text, 'Document') AS section,
               c.content, (1 - (c.embedding <=> @embedding))::double precision AS score, c.page_number,
               d.classification, d.branch, docs.readable_acl(d.id)::text AS acl
        FROM docs.chunks c
        JOIN docs.documents d ON d.id = c.document_id
        WHERE d.embedding_model = @model
        ORDER BY c.embedding <=> @embedding
        LIMIT @topk
        """;

    public async Task<IReadOnlyList<DocumentSearchCandidate>> SearchAsync(PrincipalContext principal,
        float[] vector, string embeddingModel, int topK, CancellationToken cancellationToken)
    {
        await using var session = await DocumentDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand(SearchSql);
        command.Parameters.Add(new NpgsqlParameter("embedding", new Vector(vector)));
        command.AddParameter("model", embeddingModel);
        command.AddParameter("topk", topK);
        var hits = new List<DocumentSearchCandidate>();
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken))
        {
            // Missing ACL metadata is a security failure, not an empty ACL or an implicit grant.
            if (rows.IsDBNull(8)) throw new InvalidOperationException("Authorized ACL metadata is unavailable.");
            var acl = JsonSerializer.Deserialize<List<DocumentAclEntry>>(rows.GetString(8),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Authorized ACL metadata could not be parsed.");
            var hit = new DocumentSearchHit(rows.GetString(0), rows.GetString(1), rows.GetString(2),
                rows.GetString(3), rows.GetDouble(4), rows.IsDBNull(5) ? null : rows.GetInt32(5));
            hits.Add(new DocumentSearchCandidate(hit,
                new DocumentResource(hit.DocumentId, rows.GetInt16(6), rows.IsDBNull(7) ? null : rows.GetString(7), acl)));
        }
        return hits;
    }
}

/// <summary>Versioned content updates; initial ACLs are imported once and never reset by re-ingestion.</summary>
public sealed class PostgresDocumentIngestionRepository(DocumentWriterDatabase database) : IDocumentIngestionRepository
{
    public async Task<bool> HasCurrentDocumentAsync(BankDocument doc, CancellationToken ct)
    {
        await using var conn = await database.DataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS (SELECT 1 FROM docs.documents
              WHERE id=@id AND content_hash=@hash AND embedding_model=@model
              AND title=@title AND source_path=@path AND language=@lang
              AND classification=@classification AND branch IS NOT DISTINCT FROM @branch)
            """;
        cmd.AddParameter("id", doc.Id).AddParameter("hash", doc.ContentHash)
            .AddParameter("model", doc.EmbeddingModel).AddParameter("title", doc.Title)
            .AddParameter("path", doc.SourcePath).AddParameter("lang", doc.Language)
            .AddParameter("classification", doc.Classification).AddParameter("branch", doc.Branch);
        return (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
    }

    public async Task<bool> UpsertAsync(DocumentIngestionEntry entry, CancellationToken ct)
    {
        await using var conn = await database.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var doc = entry.Document;
        string? oldHash = null;
        string? oldModel = null;
        bool oldMetadata = false;
        await using (var existing = conn.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = """
                SELECT content_hash,embedding_model,
                       (title=@title AND source_path=@path AND language=@lang
                         AND classification=@classification AND branch IS NOT DISTINCT FROM @branch)
                FROM docs.documents WHERE id=@id FOR UPDATE
                """;
            existing.AddParameter("title", doc.Title).AddParameter("path", doc.SourcePath)
                .AddParameter("lang", doc.Language).AddParameter("classification", doc.Classification)
                .AddParameter("branch", doc.Branch);
            existing.AddParameter("id", doc.Id);
            await using var row = await existing.ExecuteReaderAsync(ct);
            if (await row.ReadAsync(ct)) { oldHash = row.GetString(0); oldModel = row.GetString(1); oldMetadata = row.GetBoolean(2); }
        }
        if (oldHash == doc.ContentHash && oldModel == doc.EmbeddingModel && oldMetadata) { await tx.CommitAsync(ct); return false; }
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO docs.documents(id,title,source_path,language,classification,branch,content_hash,embedding_model)
                VALUES (@id,@title,@path,@language,@classification,@branch,@hash,@model)
                ON CONFLICT (id) DO UPDATE SET title=EXCLUDED.title, source_path=EXCLUDED.source_path,
                  language=EXCLUDED.language, classification=EXCLUDED.classification, branch=EXCLUDED.branch,
                  content_hash=EXCLUDED.content_hash, embedding_model=EXCLUDED.embedding_model,
                  content_version=docs.documents.content_version+1, updated_at=now()
                """;
            cmd.AddParameter("id", doc.Id).AddParameter("title", doc.Title)
                .AddParameter("path", doc.SourcePath).AddParameter("language", doc.Language)
                .AddParameter("classification", doc.Classification).AddParameter("branch", doc.Branch)
                .AddParameter("hash", doc.ContentHash).AddParameter("model", doc.EmbeddingModel);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        if (oldHash is null)
        {
            foreach (var acl in entry.InitialAcl)
            {
                await using var aclCmd = conn.CreateCommand();
                aclCmd.Transaction = tx;
                aclCmd.CommandText = "INSERT INTO docs.acls(document_id,subject_kind,subject_value,effect) VALUES (@doc,@kind,@subject,@effect)";
                aclCmd.AddParameter("doc", doc.Id).AddParameter("kind", acl.SubjectKind)
                    .AddParameter("subject", acl.SubjectValue).AddParameter("effect", acl.Effect);
                await aclCmd.ExecuteNonQueryAsync(ct);
            }
        }
        await using (var delete = conn.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM docs.chunks WHERE document_id=@id";
            delete.AddParameter("id", doc.Id);
            await delete.ExecuteNonQueryAsync(ct);
        }
        foreach (var chunk in entry.Chunks)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO docs.chunks (id,document_id,chunk_index,content,page_number,embedding)
                VALUES (@id,@document,@idx,@content,@page,@vector)
                """;
            cmd.AddParameter("id", chunk.Id).AddParameter("document", chunk.DocumentId)
                .AddParameter("idx", chunk.ChunkIndex).AddParameter("content", chunk.Content)
                .AddParameter("page", chunk.PageNumber);
            cmd.Parameters.Add(new NpgsqlParameter("vector", new Vector(chunk.Embedding)));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
