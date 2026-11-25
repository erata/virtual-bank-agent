using VirtualBank.Application.Credit;
using System.Data.Common;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Credit.Abstractions;
using VirtualBank.Model.Credit;

namespace VirtualBank.Infrastructure.Persistence.Credit;

/// <summary>
/// PostgreSQL adapter for <see cref="ICreditApplicationRepository"/>. Every statement runs inside an
/// <see cref="AuthorizedDbSession"/>, so credit_db RLS filters rows for the calling principal.
/// </summary>
public sealed class PostgresCreditApplicationRepository(CreditDatabase database) : ICreditApplicationRepository
{
    private const string InsufficientPrivilege = "42501";

    internal const string SelectApplications = """
        SELECT id, customer_id, amount, currency, status, purpose, prepared_by, assigned_analyst_id, approved_by, approved_at, version
        FROM credit.credit_applications
        """;

    internal const string ListApplicationsSql = SelectApplications + " ORDER BY id";

    internal const string FindApplicationSql = SelectApplications + " WHERE id = @application_id";

    internal const string InsertNoteSql = """
        INSERT INTO credit.credit_notes (application_id, author_id, body)
        VALUES (@application_id, @author_id, @body)
        RETURNING id, created_at
        """;

    public async Task<IReadOnlyList<CreditApplication>> ListAsync(PrincipalContext principal, CancellationToken cancellationToken)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand(ListApplicationsSql);
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<CreditApplication?> FindAsync(PrincipalContext principal, CreditApplicationId applicationId, CancellationToken cancellationToken)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand(FindApplicationSql).AddParameter("application_id", applicationId.Value);
        return (await ReadAsync(command, cancellationToken)).SingleOrDefault();
    }

    public async Task<CreditNote?> AddNoteAsync(
        PrincipalContext principal, CreditApplicationId applicationId, CreditNoteText text, CancellationToken cancellationToken)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand(InsertNoteSql)
            .AddParameter("application_id", applicationId.Value)
            .AddParameter("author_id", principal.SubjectId)
            .AddParameter("body", text.Value);
        try
        {
            CreditNote note;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                note = new CreditNote(reader.GetInt64(0), applicationId, principal.SubjectId, reader.GetFieldValue<DateTimeOffset>(1));
            }

            await session.CommitAsync(cancellationToken);
            return note;
        }
        catch (DbException ex) when (ex.SqlState == InsufficientPrivilege)
        {
            // RLS WITH CHECK rejected the row: treated like an invisible application.
            return null;
        }
    }

    public async Task<string?> CreateRequestAsync(PrincipalContext principal, string customerId, decimal amount, string currency,
        string purpose, CancellationToken cancellationToken)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand("""
            INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by, version)
            VALUES (credit.next_application_id(), @customer, @amount, @currency, 'SUBMITTED', @purpose, @by, 1)
            RETURNING id
            """).AddParameter("customer", customerId).AddParameter("amount", amount).AddParameter("currency", currency)
            .AddParameter("purpose", purpose).AddParameter("by", principal.SubjectId);
        try
        {
            var id = (string?)await command.ExecuteScalarAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return id;
        }
        catch (DbException ex) when (ex.SqlState == InsufficientPrivilege)
        {
            return null; // RLS WITH CHECK refused it
        }
    }

    public async Task<IReadOnlyList<StoredCreditNote>> ListNotesAsync(
        PrincipalContext principal, CreditApplicationId applicationId, CancellationToken cancellationToken)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand("""
            SELECT id, author_id, body, created_at FROM credit.credit_notes
            WHERE application_id = @application_id ORDER BY created_at, id LIMIT 200
            """).AddParameter("application_id", applicationId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var notes = new List<StoredCreditNote>();
        while (await reader.ReadAsync(cancellationToken))
            notes.Add(new StoredCreditNote(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        return notes;
    }

    internal static async Task<IReadOnlyList<CreditApplication>> ReadAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applications = new List<CreditApplication>();
        while (await reader.ReadAsync(cancellationToken))
        {
            applications.Add(CreditApplication.Rehydrate(
                id: CreditApplicationId.Parse(reader.GetString(0)),
                customerId: reader.GetString(1),
                amount: Money.Of(reader.GetDecimal(2), reader.GetString(3)),
                status: Enum.Parse<CreditApplicationStatus>(reader.GetString(4), ignoreCase: true),
                purpose: reader.GetString(5),
                preparedBy: reader.GetString(6),
                assignedAnalystId: reader.IsDBNull(7) ? null : reader.GetString(7),
                approvedBy: reader.IsDBNull(8) ? null : reader.GetString(8),
                approvedAt: reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                version: reader.GetInt32(10)));
        }

        return applications;
    }
}
