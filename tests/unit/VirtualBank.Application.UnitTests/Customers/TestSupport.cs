using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Application.Customers.Authorization;
using VirtualBank.Application.Customers.Queries.GetCustomer;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.ListCustomers;
using VirtualBank.Model.Customers;
using VirtualBank.Application.UnitTests.Support;

namespace VirtualBank.Application.UnitTests.Customers;

/// <summary>
/// Returns whatever it is given, ignoring the principal. This simulates a database whose RLS policy is broken or
/// missing, so the tests show what the application layer alone guarantees.
/// </summary>
internal sealed class UnfilteredCustomerRepository(params Customer[] customers) : ICustomerRepository
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<Customer>> ListAsync(PrincipalContext principal, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<Customer>>(customers);
    }

    public Task<Customer?> FindAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(customers.FirstOrDefault(c => c.Id == customerId));
    }

    public Task<IReadOnlyList<Account>> ListAccountsAsync(PrincipalContext principal, string customerId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<Account>>([new Account("A-1", "checking", "EUR", 100.50m)]);
    }
}

/// <summary>Wires the customer use cases exactly like the composition root, with test doubles for the ports.</summary>
internal sealed class CustomerUseCases
{
    public CustomerUseCases(params Customer[] customers)
    {
        Repository = new UnfilteredCustomerRepository(customers);
    }

    public UnfilteredCustomerRepository Repository { get; }

    public RecordingAudit Audit { get; } = new();

    /// <summary>The customer module's policies only.</summary>
    public static ResourceAuthorizationService PolicyEngine { get; } = new(CustomerPolicies.Create());

    private CustomerReader Reader => new(Repository, PolicyEngine, Audit);

    public ListCustomersHandler List => new(Reader);

    public GetCustomerHandler Get => new(Reader, PolicyEngine, Audit);

    public ListCustomerAccountsHandler Accounts => new(Reader, Repository);

    public static Customer Customer(string id, string branch, params string[] assignedAnalysts) =>
        new(id, branch, "First", "Last", "private", $"{id}@example.test", "+49 0", "DE00" + id, new DateOnly(1980, 1, 1),
            assignedAnalysts.ToHashSet(StringComparer.Ordinal));
}
