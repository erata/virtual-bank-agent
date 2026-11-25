using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.ListCustomers;

namespace VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;

/// <summary>Per-customer account balances, NOT physical cash or net worth.</summary>
public sealed record CustomerBalanceRow(string CustomerId, string CustomerName, string Currency,
    decimal Balance, int AccountCount);

public sealed record CurrencyBalanceTotal(string Currency, decimal Balance, int AccountCount);

public sealed record CustomerBalanceOverviewView(
    IReadOnlyList<CustomerBalanceRow> Rows,
    IReadOnlyList<CurrencyBalanceTotal> TotalsByCurrency,
    int CustomerCount,
    bool IsAllAccessibleCustomers,
    string? SelectedCustomerId = null,
    string? SelectedCustomerName = null);

public sealed record SummarizeCustomerBalancesQuery(PrincipalContext Principal, string? CustomerId = null,
    string? CustomerName = null);

/// <summary>
/// Read-only aggregation over the EXISTING authorized list and account use cases. A failed/denied individual
/// account lookup aborts the result, so the client never receives a silently incomplete "total". The hard cap
/// bounds N+1 work; production scale should use an explicitly authorized grouped DB query.
/// </summary>
public sealed class SummarizeCustomerBalancesHandler(
    ListCustomersHandler listCustomers, ListCustomerAccountsHandler listAccounts)
{
    private const int MaxCustomersPerRequest = 100;

    public async Task<UseCaseResult<CustomerBalanceOverviewView>> HandleAsync(
        SummarizeCustomerBalancesQuery query, CancellationToken cancellationToken)
    {
        var visible = await listCustomers.HandleAsync(new ListCustomersQuery(query.Principal), cancellationToken);
        if (visible.Status != UseCaseStatus.Succeeded)
            return visible.Status == UseCaseStatus.Forbidden
                ? UseCaseResult<CustomerBalanceOverviewView>.Forbidden(visible.NoticeCode)
                : UseCaseResult<CustomerBalanceOverviewView>.NotFound();

        var customers = visible.Value!;
        var selected = customers.AsEnumerable();
        var byId = !string.IsNullOrWhiteSpace(query.CustomerId);
        var byName = !string.IsNullOrWhiteSpace(query.CustomerName);
        if (byId && byName)
            return UseCaseResult<CustomerBalanceOverviewView>.Invalid("customer", "Choose either customer_id or customer_name.");

        if (byId)
            selected = selected.Where(c => string.Equals(c.Id, query.CustomerId, StringComparison.Ordinal));
        else if (byName)
        {
            var name = NormalizeName(query.CustomerName!);
            selected = selected.Where(c => NormalizeName($"{c.FirstName} {c.LastName}") == name ||
                NormalizeName(c.FirstName) == name || NormalizeName(c.LastName) == name);
        }

        var accessible = selected.ToArray();
        if ((byId || byName) && accessible.Length == 0)
            return UseCaseResult<CustomerBalanceOverviewView>.NotFound();
        if (byName && accessible.Length != 1)
            return UseCaseResult<CustomerBalanceOverviewView>.Invalid("customer_name", "Ambiguous name; supply a customer ID.");
        if (accessible.Length > MaxCustomersPerRequest)
            return UseCaseResult<CustomerBalanceOverviewView>.Invalid("customers", "Too many accessible customers; select a customer ID.");

        var rows = new List<CustomerBalanceRow>();
        foreach (var customer in accessible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Separate resource authorization and RLS are invoked for EVERY customer via the original handler.
            var accountResult = await listAccounts.HandleAsync(
                new ListCustomerAccountsQuery(query.Principal, customer.Id), cancellationToken);
            if (accountResult.Status == UseCaseStatus.Forbidden)
                return UseCaseResult<CustomerBalanceOverviewView>.Forbidden(accountResult.NoticeCode);
            if (accountResult.Status != UseCaseStatus.Succeeded)
                return UseCaseResult<CustomerBalanceOverviewView>.NotFound();

            foreach (var group in accountResult.Value!.GroupBy(a => a.Currency, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new CustomerBalanceRow(customer.Id,
                    $"{customer.FirstName} {customer.LastName}", group.Key.ToUpperInvariant(),
                    group.Sum(a => a.Balance), group.Count()));
            }
        }

        var totals = rows.GroupBy(r => r.Currency, StringComparer.Ordinal)
            .Select(g => new CurrencyBalanceTotal(g.Key, g.Sum(r => r.Balance), g.Sum(r => r.AccountCount)))
            .OrderBy(t => t.Currency, StringComparer.Ordinal).ToArray();

        return UseCaseResult<CustomerBalanceOverviewView>.Success(new CustomerBalanceOverviewView(
            rows.OrderBy(r => r.CustomerId, StringComparer.Ordinal).ThenBy(r => r.Currency, StringComparer.Ordinal).ToArray(),
            totals, accessible.Length, !byId && !byName,
            byId || byName ? accessible[0].Id : null,
            byId || byName ? $"{accessible[0].FirstName} {accessible[0].LastName}" : null));
    }

    private static string NormalizeName(string value) =>
        string.Join(" ", VirtualBank.Application.Assistant.NaturalLanguageIntent.Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
