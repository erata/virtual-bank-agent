using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;

namespace VirtualBank.Application.Customers.Queries.ListCustomers;

public sealed record ListCustomersQuery(PrincipalContext Principal);

/// <summary>Query: customers the caller may read (branch path or assignment path).</summary>
public sealed class ListCustomersHandler(CustomerReader reader)
{
    public async Task<UseCaseResult<IReadOnlyList<CustomerSummaryView>>> HandleAsync(ListCustomersQuery query, CancellationToken cancellationToken)
    {
        if (!CustomerReader.HasAnyReadPath(query.Principal))
        {
            return UseCaseResult<IReadOnlyList<CustomerSummaryView>>.Forbidden(noticeCode: null);
        }

        var customers = await reader.ListReadableAsync(query.Principal, cancellationToken);
        return UseCaseResult<IReadOnlyList<CustomerSummaryView>>.Success(customers.Select(CustomerSummaryView.From).ToList());
    }
}
