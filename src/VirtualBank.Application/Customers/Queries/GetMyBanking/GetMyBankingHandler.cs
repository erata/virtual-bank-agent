using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;
using VirtualBank.Application.Customers;
using VirtualBank.Model.Customers;

namespace VirtualBank.Application.Customers.Queries.GetMyBanking;

public sealed record GetMyBankingQuery(PrincipalContext Principal);

/// <summary>Never accepts a customer ID. Identity binding comes from the validated JWT subject + authoritative DB row.</summary>
public sealed class GetMyBankingHandler(
    CustomerReader reader, ListCustomerAccountsHandler accounts,
    SummarizeCustomerBalancesHandler summaries, ISelfServicePaymentRepository payments)
{
    // CQRS query entry point, with additional read-only operations scoped to the same validated subject.
    public Task<UseCaseResult<CustomerDetailView>> HandleAsync(GetMyBankingQuery query, CancellationToken ct) =>
        ProfileAsync(query.Principal, ct);

    internal async Task<Customer?> OwnerAsync(PrincipalContext principal, CancellationToken ct)
    {
        if (!principal.HasPermission(KnownPermissions.CustomerReadSelf)) return null;
        var rows = await reader.ListReadableAsync(principal, ct);
        return rows.SingleOrDefault(c => string.Equals(c.OwnerSubjectId, principal.SubjectId, StringComparison.Ordinal));
    }

    public async Task<UseCaseResult<CustomerDetailView>> ProfileAsync(PrincipalContext principal, CancellationToken ct)
    {
        var owner = await OwnerAsync(principal, ct);
        return owner is null ? UseCaseResult<CustomerDetailView>.NotFound() :
            UseCaseResult<CustomerDetailView>.Success(CustomerDetailView.From(owner,
                principal.HasPermission(KnownPermissions.CustomerReadSensitive)));
    }

    public async Task<UseCaseResult<IReadOnlyList<AccountView>>> AccountsAsync(PrincipalContext principal, CancellationToken ct)
    {
        var owner = await OwnerAsync(principal, ct);
        return owner is null ? UseCaseResult<IReadOnlyList<AccountView>>.NotFound() :
            await accounts.HandleAsync(new ListCustomerAccountsQuery(principal, owner.Id), ct);
    }

    public async Task<UseCaseResult<CustomerBalanceOverviewView>> BalanceAsync(PrincipalContext principal, CancellationToken ct)
    {
        var owner = await OwnerAsync(principal, ct);
        return owner is null ? UseCaseResult<CustomerBalanceOverviewView>.NotFound() :
            await summaries.HandleAsync(new SummarizeCustomerBalancesQuery(principal, owner.Id), ct);
    }

    public async Task<UseCaseResult<IReadOnlyList<ScheduledPaymentView>>> UpcomingAsync(PrincipalContext principal, int days, CancellationToken ct)
    {
        if (days is < 1 or > 90) return UseCaseResult<IReadOnlyList<ScheduledPaymentView>>.Invalid("days", "Use 1-90 days.");
        var owner = await OwnerAsync(principal, ct);
        if (owner is null) return UseCaseResult<IReadOnlyList<ScheduledPaymentView>>.NotFound();
        var rows = await payments.UpcomingAsync(principal, days, ct);
        // Independent row check: fail closed if a broken RLS policy ever returns a foreign payment.
        return rows.Any(row => row.CustomerId != owner.Id)
            ? UseCaseResult<IReadOnlyList<ScheduledPaymentView>>.NotFound()
            : UseCaseResult<IReadOnlyList<ScheduledPaymentView>>.Success(rows);
    }

    public async Task<UseCaseResult<IReadOnlyList<BookedTransactionView>>> RecentAsync(PrincipalContext principal, int days, CancellationToken ct)
    {
        if (days is < 1 or > 90) return UseCaseResult<IReadOnlyList<BookedTransactionView>>.Invalid("days", "Use 1-90 days.");
        var owner = await OwnerAsync(principal, ct);
        if (owner is null) return UseCaseResult<IReadOnlyList<BookedTransactionView>>.NotFound();
        var rows = await payments.RecentAsync(principal, days, ct);
        return rows.Any(row => row.CustomerId != owner.Id)
            ? UseCaseResult<IReadOnlyList<BookedTransactionView>>.NotFound()
            : UseCaseResult<IReadOnlyList<BookedTransactionView>>.Success(rows);
    }

    public async Task<UseCaseResult<IReadOnlyList<StandingOrderView>>> StandingOrdersAsync(PrincipalContext principal, CancellationToken ct)
    {
        var owner = await OwnerAsync(principal, ct);
        if (owner is null) return UseCaseResult<IReadOnlyList<StandingOrderView>>.NotFound();
        var rows = await payments.StandingOrdersAsync(principal, ct);
        return rows.Any(row => row.CustomerId != owner.Id)
            ? UseCaseResult<IReadOnlyList<StandingOrderView>>.NotFound()
            : UseCaseResult<IReadOnlyList<StandingOrderView>>.Success(rows);
    }
}
