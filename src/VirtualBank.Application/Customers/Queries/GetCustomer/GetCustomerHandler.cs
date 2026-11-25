using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Customers.Authorization;

namespace VirtualBank.Application.Customers.Queries.GetCustomer;

public sealed record GetCustomerQuery(PrincipalContext Principal, string CustomerId);

/// <summary>Query: one customer with field-level masking, or NotFound when it is not visible to the caller.</summary>
public sealed class GetCustomerHandler(CustomerReader reader, IResourceAuthorizationService authorization, IAuthorizationAudit audit)
{
    public async Task<UseCaseResult<CustomerDetailView>> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        if (!CustomerReader.HasAnyReadPath(query.Principal))
        {
            return UseCaseResult<CustomerDetailView>.Forbidden(noticeCode: null);
        }

        var customer = await reader.FindReadableAsync(query.Principal, query.CustomerId, cancellationToken);
        if (customer is null)
        {
            return UseCaseResult<CustomerDetailView>.NotFound();
        }

        var sensitive = await authorization.AuthorizeAsync(
            query.Principal, CustomerResource.From(customer), KnownPermissions.CustomerReadSensitive, cancellationToken);
        audit.Record(query.Principal, sensitive);
        return UseCaseResult<CustomerDetailView>.Success(CustomerDetailView.From(customer, includeSensitive: sensitive.IsAllowed));
    }
}
