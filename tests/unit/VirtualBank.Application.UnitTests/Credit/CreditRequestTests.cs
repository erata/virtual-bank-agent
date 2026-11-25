using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Credit.Abstractions;
using VirtualBank.Application.Credit.Commands.RequestCredit;
using VirtualBank.Application.Credit.Queries.ListCreditApplications;
using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Model.Credit;
using Xunit;

namespace VirtualBank.Application.UnitTests.Credit;

/// <summary>Customers apply for credit themselves; they see only their own applications; approvers decide.</summary>
public sealed class CreditRequestTests
{
    private sealed class Directory(string? customerId) : ICreditApplicantDirectory
    {
        public Task<string?> FindOwnCustomerIdAsync(PrincipalContext principal, CancellationToken ct) => Task.FromResult(customerId);
    }

    private static CreditRequests Requests(CreditUseCases useCases, InMemoryPendingActionStore store) =>
        new(new Directory("C-2001"), useCases.Repository, new PendingActionFactory(store, TimeProvider.System));

    [Fact]
    public async Task Customer_request_becomes_a_submitted_application_visible_to_them_and_to_approvers()
    {
        var useCases = new CreditUseCases(CreditUseCases.NewApplication("K-100", CreditApplicationStatus.Submitted, null));
        var store = new InMemoryPendingActionStore(TimeProvider.System);
        var requests = Requests(useCases, store);
        var confirmation = new ActionConfirmation(store, [new CreditRequestAction(requests)], TimeProvider.System);
        var deniz = Personas.Get("deniz.customer");

        var card = await requests.PrepareAsync(deniz, 15_000m, 60, "Car purchase", CancellationToken.None);
        var done = await confirmation.ConfirmAsync(deniz, card.Value!.Id, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, done.Status);
        var id = done.Value!.ResourceId;

        // The customer sees exactly their own request, not K-100 of another customer.
        var mine = await useCases.List.HandleAsync(new ListCreditApplicationsQuery(deniz), CancellationToken.None);
        Assert.Equal([id], mine.Value!.Select(a => a.Id));
        // Another customer does not see it.
        Assert.Empty((await useCases.List.HandleAsync(new ListCreditApplicationsQuery(Personas.Get("thomas.customer")), CancellationToken.None)).Value!);
        // Approvers see it (SUBMITTED); four-eyes holds because the customer prepared it.
        Assert.Contains((await useCases.List.HandleAsync(new ListCreditApplicationsQuery(Personas.Get("max.approver")), CancellationToken.None)).Value!,
            a => a.Id == id);
    }

    [Theory]
    [InlineData(100, 60, "amount")]
    [InlineData(300_000, 60, "amount")]
    [InlineData(5_000, 2, "termMonths")]
    public async Task Out_of_range_requests_are_rejected_before_a_card_exists(decimal amount, int term, string field)
    {
        var requests = Requests(new CreditUseCases(), new InMemoryPendingActionStore(TimeProvider.System));
        var result = await requests.PrepareAsync(Personas.Get("deniz.customer"), amount, term, "Renovation", CancellationToken.None);
        Assert.Equal(UseCaseStatus.Invalid, result.Status);
        Assert.Contains(field, result.Errors!.Keys);
    }

    [Theory]
    [InlineData("anna.berater")]
    [InlineData("mehmet.kredit")]
    public async Task Staff_cannot_apply_as_a_customer(string persona)
    {
        var requests = Requests(new CreditUseCases(), new InMemoryPendingActionStore(TimeProvider.System));
        var result = await requests.PrepareAsync(Personas.Get(persona), 5_000m, 24, "Test", CancellationToken.None);
        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
    }
}
