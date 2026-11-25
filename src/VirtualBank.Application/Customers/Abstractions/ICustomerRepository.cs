using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Model.Customers;

namespace VirtualBank.Application.Customers.Abstractions;

/// <summary>
/// Port to customer persistence. Every call runs under the caller's database authorization context (RLS);
/// the repository never makes authorization decisions.
/// </summary>
public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> ListAsync(PrincipalContext principal, CancellationToken cancellationToken);

    Task<Customer?> FindAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Account>> ListAccountsAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken);
}
