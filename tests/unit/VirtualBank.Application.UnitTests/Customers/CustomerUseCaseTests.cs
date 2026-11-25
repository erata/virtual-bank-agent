using VirtualBank.Application.Common;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Customers.Queries.GetCustomer;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.ListCustomers;
using VirtualBank.Application.UnitTests.Support;
using Xunit;
using static VirtualBank.Application.UnitTests.Customers.CustomerUseCases;

namespace VirtualBank.Application.UnitTests.Customers;

/// <summary>Customer queries against a repository that ignores RLS: what the application layer alone guarantees.</summary>
public sealed class CustomerUseCaseTests
{
    private static readonly string Mehmet = Personas.SubjectOf("mehmet.kredit");

    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Advisor_reads_own_branch_customer_with_sensitive_fields_masked()
    {
        var useCases = new CustomerUseCases(Customer("C-1002", "STR"));
        var result = await useCases.Get.HandleAsync(new GetCustomerQuery(Personas.Get("anna.berater"), "C-1002"), None);

        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.True(result.Value!.SensitiveFieldsMasked);
        Assert.Null(result.Value.Iban);
        Assert.Null(result.Value.Email);
        Assert.Null(result.Value.DateOfBirth);
    }

    [Fact]
    public async Task Row_from_other_branch_is_hidden_even_if_rls_returns_it()
    {
        var useCases = new CustomerUseCases(Customer("C-5001", "MUC"));
        var result = await useCases.Get.HandleAsync(new GetCustomerQuery(Personas.Get("anna.berater"), "C-5001"), None);

        Assert.Equal(UseCaseStatus.NotFound, result.Status);
        Assert.Single(useCases.Audit.Mismatches);
    }

    [Fact]
    public async Task List_drops_rows_the_policy_denies()
    {
        var useCases = new CustomerUseCases(Customer("C-1002", "STR"), Customer("C-5001", "MUC"), Customer("C-5002", "MUC", Mehmet));
        var result = await useCases.List.HandleAsync(new ListCustomersQuery(Personas.Get("anna.berater")), None);

        Assert.Equal(new[] { "C-1002" }, result.Value!.Select(c => c.Id));
        Assert.Equal(2, useCases.Audit.Mismatches.Count);
    }

    [Fact]
    public async Task Analyst_reads_assigned_customer_in_another_branch()
    {
        var useCases = new CustomerUseCases(Customer("C-5002", "MUC", Mehmet));
        var result = await useCases.Get.HandleAsync(new GetCustomerQuery(Personas.Get("mehmet.kredit"), "C-5002"), None);

        Assert.True(result.Value!.SensitiveFieldsMasked);
    }

    [Fact]
    public async Task Analyst_cannot_read_unassigned_customer_returned_by_a_broken_store()
    {
        var useCases = new CustomerUseCases(Customer("C-5001", "MUC"));
        var result = await useCases.Get.HandleAsync(new GetCustomerQuery(Personas.Get("mehmet.kredit"), "C-5001"), None);

        Assert.Equal(UseCaseStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Principal_without_customer_permissions_is_forbidden_before_any_query()
    {
        var useCases = new CustomerUseCases(Customer("C-1002", "STR"));
        var result = await useCases.Get.HandleAsync(new GetCustomerQuery(Personas.Get("lena.admin"), "C-1002"), None);

        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.Equal(0, useCases.Repository.Calls);
    }

    [Fact]
    public async Task Accounts_of_a_hidden_customer_are_not_found_and_not_queried()
    {
        var useCases = new CustomerUseCases(Customer("C-5001", "MUC"));
        var result = await useCases.Accounts.HandleAsync(new ListCustomerAccountsQuery(Personas.Get("anna.berater"), "C-5001"), None);

        Assert.Equal(UseCaseStatus.NotFound, result.Status);
        Assert.Equal(1, useCases.Repository.Calls); // only the visibility lookup, never the accounts query
    }

    [Fact]
    public async Task Accounts_of_a_visible_customer_include_balances()
    {
        var useCases = new CustomerUseCases(Customer("C-1002", "STR"));
        var result = await useCases.Accounts.HandleAsync(new ListCustomerAccountsQuery(Personas.Get("anna.berater"), "C-1002"), None);

        Assert.Equal(100.50m, Assert.Single(result.Value!).Balance);
    }

    [Theory]
    [InlineData("C-10021")]
    [InlineData("c-1002")]
    [InlineData("C-1002' OR '1'='1")]
    [InlineData("")]
    public async Task Malformed_customer_ids_are_not_found_without_a_query(string customerId)
    {
        var useCases = new CustomerUseCases(Customer("C-1002", "STR"));
        var result = await useCases.Get.HandleAsync(new GetCustomerQuery(Personas.Get("anna.berater"), customerId), None);

        Assert.Equal(UseCaseStatus.NotFound, result.Status);
        Assert.Equal(0, useCases.Repository.Calls);
    }

    [Fact]
    public void Sensitive_fields_are_returned_only_when_explicitly_allowed()
    {
        var customer = Customer("C-1002", "STR");
        var unmasked = CustomerDetailView.From(customer, includeSensitive: true);

        Assert.False(unmasked.SensitiveFieldsMasked);
        Assert.Equal(customer.Iban, unmasked.Iban);
    }
}
