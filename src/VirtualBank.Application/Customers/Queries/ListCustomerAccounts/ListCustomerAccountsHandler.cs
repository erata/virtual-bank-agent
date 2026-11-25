using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Customers.Abstractions;

namespace VirtualBank.Application.Customers.Queries.ListCustomerAccounts;

public sealed record ListCustomerAccountsQuery(PrincipalContext Principal, string CustomerId);

/// <summary>Query: accounts of a customer the caller may read; otherwise NotFound.</summary>
public sealed class ListCustomerAccountsHandler(CustomerReader reader, ICustomerRepository repository)
{
    public async Task<UseCaseResult<IReadOnlyList<AccountView>>> HandleAsync(ListCustomerAccountsQuery query, CancellationToken cancellationToken)
    {
        if (!CustomerReader.HasAnyReadPath(query.Principal))
        {
            return UseCaseResult<IReadOnlyList<AccountView>>.Forbidden(noticeCode: null);
        }

        if (await reader.FindReadableAsync(query.Principal, query.CustomerId, cancellationToken) is null)
        {
            return UseCaseResult<IReadOnlyList<AccountView>>.NotFound();
        }

        var accounts = await repository.ListAccountsAsync(query.Principal, query.CustomerId, cancellationToken);
        return UseCaseResult<IReadOnlyList<AccountView>>.Success(accounts.Select(AccountView.From).ToList());
    }
}
