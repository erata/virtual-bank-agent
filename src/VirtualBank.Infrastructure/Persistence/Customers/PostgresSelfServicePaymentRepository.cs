using System.Data.Common;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Customers.Abstractions;

namespace VirtualBank.Infrastructure.Persistence.Customers;

/// <summary>Own identity is determined by transaction-local, trusted JWT subject; SQL accepts no customer ID.</summary>
public sealed class PostgresSelfServicePaymentRepository(CustomerDatabase database) : ISelfServicePaymentRepository
{
    public async Task<IReadOnlyList<ScheduledPaymentView>> UpcomingAsync(PrincipalContext principal, int days, CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var cmd = session.CreateCommand("""
            SELECT id, customer_id, description, amount, trim(currency), due_date, status
            FROM customer.scheduled_payments
            WHERE due_date >= CURRENT_DATE AND due_date <= CURRENT_DATE + @days AND status = 'planned'
            ORDER BY due_date, id
            """).AddParameter("days", days);
        await using var data = await cmd.ExecuteReaderAsync(ct);
        var result = new List<ScheduledPaymentView>();
        while (await data.ReadAsync(ct)) result.Add(new(data.GetString(0),data.GetString(1),data.GetString(2),data.GetDecimal(3),
            data.GetString(4),data.GetFieldValue<DateOnly>(5),data.GetString(6)));
        return result;
    }

    public async Task<IReadOnlyList<BookedTransactionView>> RecentAsync(PrincipalContext principal, int days, CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var cmd = session.CreateCommand("""
            SELECT id, customer_id, description, amount, trim(currency), booked_on
            FROM customer.account_transactions
            WHERE booked_on >= CURRENT_DATE - @days AND booked_on <= CURRENT_DATE
            ORDER BY booked_on DESC, id
            """).AddParameter("days", days);
        await using var data = await cmd.ExecuteReaderAsync(ct);
        var result = new List<BookedTransactionView>();
        while (await data.ReadAsync(ct)) result.Add(new(data.GetString(0),data.GetString(1),data.GetString(2),data.GetDecimal(3),
            data.GetString(4),data.GetFieldValue<DateOnly>(5)));
        return result;
    }

    public async Task<IReadOnlyList<StandingOrderView>> StandingOrdersAsync(PrincipalContext principal, CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var cmd = session.CreateCommand("""
            SELECT id, customer_id, description, amount, trim(currency), frequency, next_due_date, active
            FROM customer.standing_orders ORDER BY active DESC, next_due_date, id
            """);
        await using var data = await cmd.ExecuteReaderAsync(ct);
        var result = new List<StandingOrderView>();
        while (await data.ReadAsync(ct)) result.Add(new(data.GetString(0),data.GetString(1),data.GetString(2),data.GetDecimal(3),
            data.GetString(4),data.GetString(5),data.GetFieldValue<DateOnly>(6),data.GetBoolean(7)));
        return result;
    }

    public async Task<ScheduledPaymentView?> FindPlannedPaymentAsync(PrincipalContext principal, string paymentId, CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var cmd = session.CreateCommand("""
            SELECT id, customer_id, description, amount, trim(currency), due_date, status
            FROM customer.scheduled_payments
            WHERE id = @id AND status = 'planned' AND due_date >= CURRENT_DATE
            """).AddParameter("id", paymentId);
        await using var data = await cmd.ExecuteReaderAsync(ct);
        return await data.ReadAsync(ct)
            ? new ScheduledPaymentView(data.GetString(0), data.GetString(1), data.GetString(2), data.GetDecimal(3),
                data.GetString(4), data.GetFieldValue<DateOnly>(5), data.GetString(6))
            : null;
    }

    public async Task<bool> CreatePlannedPaymentAsync(PrincipalContext principal, string customerId, string id, string description,
        decimal amount, string currency, DateOnly dueDate, CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var cmd = session.CreateCommand("""
            INSERT INTO customer.scheduled_payments (id, customer_id, description, amount, currency, due_date, status)
            VALUES (@id, @customer, @description, @amount, @currency, @due, 'planned')
            """).AddParameter("id", id).AddParameter("customer", customerId).AddParameter("description", description)
            .AddParameter("amount", amount).AddParameter("currency", currency).AddParameter("due", dueDate);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
            await session.CommitAsync(ct);
            return true;
        }
        catch (DbException ex) when (ex.SqlState == "42501")
        {
            return false; // RLS WITH CHECK: not the caller's customer row or permission missing
        }
    }

    public async Task<bool> CancelPlannedPaymentAsync(PrincipalContext principal, string paymentId, CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var cmd = session.CreateCommand("""
            UPDATE customer.scheduled_payments SET status = 'cancelled'
            WHERE id = @id AND status = 'planned' AND due_date >= CURRENT_DATE
            """).AddParameter("id", paymentId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 1) await session.CommitAsync(ct);
        return rows == 1;
    }

    public async Task<bool> SetStandingOrderActiveAsync(PrincipalContext principal, string orderId, bool active, CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var cmd = session.CreateCommand(
            "UPDATE customer.standing_orders SET active = @active WHERE id = @id AND active <> @active")
            .AddParameter("id", orderId).AddParameter("active", active);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 1) await session.CommitAsync(ct);
        return rows == 1;
    }

    public async Task<bool> CreateStandingOrderAsync(PrincipalContext principal, string customerId, string id, string description,
        decimal amount, string currency, string frequency, DateOnly nextDueDate, CancellationToken ct)
    {
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, ct);
        await using var cmd = session.CreateCommand("""
            INSERT INTO customer.standing_orders (id, customer_id, description, amount, currency, frequency, next_due_date, active)
            VALUES (@id, @customer, @description, @amount, @currency, @frequency, @next, true)
            """).AddParameter("id", id).AddParameter("customer", customerId).AddParameter("description", description)
            .AddParameter("amount", amount).AddParameter("currency", currency).AddParameter("frequency", frequency)
            .AddParameter("next", nextDueDate);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
            await session.CommitAsync(ct);
            return true;
        }
        catch (DbException ex) when (ex.SqlState == "42501")
        {
            return false;
        }
    }
}
