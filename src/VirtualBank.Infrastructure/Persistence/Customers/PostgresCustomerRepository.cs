using System.Data.Common;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Model.Customers;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Infrastructure.Persistence.Customers;

/// <summary>
/// Reads customers inside an <see cref="AuthorizedDbSession"/>. Queries contain no authorization
/// predicates on purpose: visibility comes from RLS, and the handlers re-check every row with the
/// application policy (defense in depth).
/// </summary>
public sealed class PostgresCustomerRepository(CustomerDatabase database) : ICustomerRepository
{
    internal const string SelectCustomers = """
        SELECT c.id, c.branch, c.first_name, c.last_name, c.segment, c.email, c.phone, c.iban, c.date_of_birth,
               coalesce(array_agg(a.analyst_id) FILTER (WHERE a.analyst_id IS NOT NULL), '{}') AS analyst_ids, c.identity_subject_id
        FROM customer.customers c
        LEFT JOIN customer.credit_assignments a ON a.customer_id = c.id
        """;

    internal const string ListCustomersSql = SelectCustomers + " GROUP BY c.id ORDER BY c.id";

    internal const string FindCustomerSql = SelectCustomers + " WHERE c.id = @customer_id GROUP BY c.id";

    internal const string ListAccountsSql = """
        SELECT id, type, currency, balance
        FROM customer.accounts
        WHERE customer_id = @customer_id
        ORDER BY id
        """;

    public async Task<IReadOnlyList<Customer>> ListAsync(PrincipalContext principal, CancellationToken cancellationToken)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand(ListCustomersSql);
        return await ReadCustomersAsync(command, cancellationToken);
    }

    public async Task<Customer?> FindAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand(FindCustomerSql).AddParameter("customer_id", customerId);
        return (await ReadCustomersAsync(command, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        await using var command = session.CreateCommand(ListAccountsSql).AddParameter("customer_id", customerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new List<Account>();
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new Account(reader.GetString(0), reader.GetString(1), reader.GetString(2).Trim(), reader.GetDecimal(3)));
        }

        return accounts;
    }

    private static async Task<IReadOnlyList<Customer>> ReadCustomersAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var customers = new List<Customer>();
        while (await reader.ReadAsync(cancellationToken))
        {
            customers.Add(new Customer(
                Id: reader.GetString(0),
                Branch: reader.GetString(1),
                FirstName: reader.GetString(2),
                LastName: reader.GetString(3),
                Segment: reader.GetString(4),
                Email: reader.GetString(5),
                Phone: reader.GetString(6),
                Iban: reader.GetString(7),
                DateOfBirth: reader.GetFieldValue<DateOnly>(8),
                AssignedAnalystIds: reader.GetFieldValue<string[]>(9).ToHashSet(StringComparer.Ordinal),
                OwnerSubjectId: reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return customers;
    }
}
