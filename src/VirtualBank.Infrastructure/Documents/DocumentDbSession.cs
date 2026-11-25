using System.Data.Common;
using System.Text.Json;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Infrastructure.Persistence;

namespace VirtualBank.Infrastructure.Documents;

/// <summary>Transactional, authenticated, fail-closed database session for document retrieval.</summary>
internal sealed class DocumentDbSession : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private DocumentDbSession(DbConnection connection, DbTransaction transaction) { _connection = connection; _transaction = transaction; }

    public static async Task<DocumentDbSession> OpenAsync(DbDataSource source, PrincipalContext principal, CancellationToken ct)
    {
        if (!principal.IsActive) throw new InvalidOperationException("Inactive principal must not access documents.");
        var conn = await source.OpenConnectionAsync(ct);
        DbTransaction? tx = null;
        try
        {
            tx = await conn.BeginTransactionAsync(ct);
            var session = new DocumentDbSession(conn, tx);
            await using var cmd = session.CreateCommand("""
                SELECT set_config('app.principal_id', @principal, true),
                       set_config('app.permissions', @permissions, true),
                       set_config('app.branch', @branch, true),
                       set_config('app.clearance', @clearance, true),
                       set_config('app.roles', @roles, true),
                       set_config('app.groups', @groups, true)
                """);
            cmd.AddParameter("principal", principal.SubjectId);
            cmd.AddParameter("permissions", string.Join(',', principal.Permissions.Select(p => p.Id)));
            cmd.AddParameter("branch", principal.Branch ?? "");
            cmd.AddParameter("clearance", ((int)principal.Clearance).ToString(System.Globalization.CultureInfo.InvariantCulture));
            cmd.AddParameter("roles", JsonSerializer.Serialize(principal.Roles.Order(StringComparer.Ordinal)));
            cmd.AddParameter("groups", JsonSerializer.Serialize(principal.Groups.Select(g => g.Value).Order(StringComparer.Ordinal)));
            await cmd.ExecuteNonQueryAsync(ct);
            return session;
        }
        catch
        {
            if (tx is not null) await tx.DisposeAsync();
            await conn.DisposeAsync();
            throw;
        }
    }

    public DbCommand CreateCommand(string sql)
    {
        var cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = sql;
        return cmd;
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
