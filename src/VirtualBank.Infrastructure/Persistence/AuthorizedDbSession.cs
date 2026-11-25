using System.Data.Common;
using System.Globalization;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Infrastructure.Persistence;

/// <summary>
/// One database transaction carrying the trusted RLS context of one principal.
/// The context is set with <c>set_config(..., true)</c>, so it ends with the transaction and can
/// never leak to the next request that reuses the pooled connection. Disposing without
/// <see cref="CommitAsync"/> rolls back.
/// </summary>
public sealed class AuthorizedDbSession : IAsyncDisposable
{
    private const string SetContextSql = """
        SELECT set_config('app.principal_id', @principal_id, true),
               set_config('app.branch', @branch, true),
               set_config('app.permissions', @permissions, true),
               set_config('app.approval_limit', @approval_limit, true)
        """;

    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;

    private AuthorizedDbSession(DbConnection connection, DbTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public static async Task<AuthorizedDbSession> OpenAsync(DbDataSource dataSource, PrincipalContext principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(principal);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        DbTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            var session = new AuthorizedDbSession(connection, transaction);
            await using var command = session.CreateCommand(SetContextSql);
            command.AddParameter("principal_id", principal.SubjectId);
            command.AddParameter("branch", principal.Branch ?? string.Empty);
            // Permission IDs come from the server-side catalog and never contain commas.
            command.AddParameter("permissions", string.Join(',', principal.Permissions.Select(p => p.Id).Order(StringComparer.Ordinal)));
            command.AddParameter("approval_limit", principal.ApprovalLimit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return session;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            await connection.DisposeAsync();
            throw;
        }
    }

    public DbCommand CreateCommand(string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }

    public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

public static class DbCommandExtensions
{
    public static DbCommand AddParameter(this DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return command;
    }
}
