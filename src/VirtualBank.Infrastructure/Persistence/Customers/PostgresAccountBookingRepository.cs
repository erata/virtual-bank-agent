using System.Data.Common;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Customers.Abstractions;

namespace VirtualBank.Infrastructure.Persistence.Customers;

/// <summary>
/// Books all lines in ONE transaction under the caller's RLS context. Debits first, so an overdraft fails before any
/// credit is written. RLS decides which accounts are bookable (own accounts vs. branch cash desk); the CHECK
/// constraint accounts_balance_not_negative refuses overdrafts. On any failure nothing is committed.
/// </summary>
public sealed class PostgresAccountBookingRepository(CustomerDatabase database) : IAccountBookingRepository
{
    private const string CheckViolation = "23514";
    private const string InsufficientPrivilege = "42501";

    public async Task<BookingResult> BookAsync(PrincipalContext principal, string customerId, IReadOnlyList<BookingLine> lines,
        CancellationToken cancellationToken, BeneficiaryCredit? beneficiary = null)
    {
        if (lines.Count == 0) return BookingResult.Failed(BookingStatus.NotFound);
        await using var session = await AuthorizedDbSession.OpenAsync(database.DataSource, principal, cancellationToken);
        var balances = new List<AccountBalance>();
        var transactions = new List<string>();
        try
        {
            foreach (var line in lines.OrderBy(l => l.Amount))
            {
                string currency;
                decimal balance;
                await using (var update = session.CreateCommand("""
                        UPDATE customer.accounts SET balance = balance + @amount
                        WHERE id = @account AND customer_id = @customer
                        RETURNING trim(currency), balance
                        """).AddParameter("amount", line.Amount).AddParameter("account", line.AccountId).AddParameter("customer", customerId))
                await using (var row = await update.ExecuteReaderAsync(cancellationToken))
                {
                    // No row: the account does not exist for this customer or RLS does not let this caller book it.
                    if (!await row.ReadAsync(cancellationToken)) return BookingResult.Failed(BookingStatus.NotFound);
                    currency = row.GetString(0);
                    balance = row.GetDecimal(1);
                }

                var transactionId = $"T-{customerId[2..]}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
                await using (var insert = session.CreateCommand("""
                        INSERT INTO customer.account_transactions (id, customer_id, description, amount, currency, booked_on)
                        VALUES (@id, @customer, @description, @amount, @currency, CURRENT_DATE)
                        """).AddParameter("id", transactionId).AddParameter("customer", customerId)
                        .AddParameter("description", $"{line.AccountId} · {line.Description}")
                        .AddParameter("amount", line.Amount).AddParameter("currency", currency))
                {
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
                balances.Add(new AccountBalance(line.AccountId, currency, balance));
                transactions.Add(transactionId);
            }

            // Internal transfer: credit the beneficiary in the same transaction (the function verifies the debit above).
            var internalCredit = false;
            if (beneficiary is not null)
            {
                await using var credit = session.CreateCommand(
                        "SELECT customer.credit_internal_beneficiary(@iban, @amount, @currency, @description)")
                    .AddParameter("iban", beneficiary.Iban).AddParameter("amount", beneficiary.Amount)
                    .AddParameter("currency", beneficiary.Currency).AddParameter("description", beneficiary.Description);
                internalCredit = await credit.ExecuteScalarAsync(cancellationToken) is string;
            }

            await session.CommitAsync(cancellationToken);
            return new BookingResult(BookingStatus.Booked, balances, transactions, internalCredit);
        }
        catch (DbException ex) when (ex.SqlState == CheckViolation)
        {
            return BookingResult.Failed(BookingStatus.InsufficientFunds); // balance would go below zero; rolled back
        }
        catch (DbException ex) when (ex.SqlState == "P0002")
        {
            return BookingResult.Failed(BookingStatus.BeneficiaryHasNoAccountInCurrency); // rolled back, payer not debited
        }
        catch (DbException ex) when (ex.SqlState == InsufficientPrivilege)
        {
            return BookingResult.Failed(BookingStatus.NotFound); // RLS WITH CHECK refused the transaction row
        }
    }
}
