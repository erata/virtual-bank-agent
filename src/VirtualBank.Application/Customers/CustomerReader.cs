using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Application.Customers.Authorization;
using VirtualBank.Model.Customers;

namespace VirtualBank.Application.Customers;

/// <summary>
/// The rule every customer query shares: rows come from the repository under RLS and are re-authorized with the
/// application policy (defense in depth). A row RLS returned but the policy denies is treated as invisible and
/// audited as a mismatch. Hidden, unknown and malformed ids are indistinguishable.
/// </summary>
public sealed class CustomerReader(ICustomerRepository repository, IResourceAuthorizationService authorization, IAuthorizationAudit audit)
{
    public static bool HasAnyReadPath(PrincipalContext principal) =>
        principal.HasPermission(KnownPermissions.CustomerRead) || principal.HasPermission(KnownPermissions.CustomerReadAssigned) ||
        principal.HasPermission(KnownPermissions.CustomerReadSelf);

    public async Task<IReadOnlyList<Customer>> ListReadableAsync(PrincipalContext principal, CancellationToken cancellationToken)
    {
        var readable = new List<Customer>();
        foreach (var customer in await repository.ListAsync(principal, cancellationToken))
        {
            if (await IsReadableAsync(principal, customer, cancellationToken))
            {
                readable.Add(customer);
            }
        }

        return readable;
    }

    public async Task<Customer?> FindReadableAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken)
    {
        if (!CustomerId.IsValid(customerId))
        {
            return null;
        }

        var customer = await repository.FindAsync(principal, customerId, cancellationToken);
        return customer is not null && await IsReadableAsync(principal, customer, cancellationToken) ? customer : null;
    }

    private async Task<bool> IsReadableAsync(PrincipalContext principal, Customer customer, CancellationToken cancellationToken)
    {
        var decision = await authorization.AuthorizeAsync(principal, CustomerResource.From(customer), KnownPermissions.CustomerRead, cancellationToken);
        if (decision.IsAllowed)
        {
            audit.Record(principal, decision);
            return true;
        }

        audit.RecordPolicyRlsMismatch(principal, decision);
        return false;
    }
}
