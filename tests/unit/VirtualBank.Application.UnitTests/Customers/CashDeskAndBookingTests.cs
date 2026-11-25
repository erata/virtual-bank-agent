using VirtualBank.Application.Authorization;
using VirtualBank.Application.Common;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Application.Customers.Authorization;
using VirtualBank.Application.Customers.Commands.CashDesk;
using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Model.Customers;
using Xunit;

namespace VirtualBank.Application.UnitTests.Customers;

/// <summary>Branch cash desk: advisors book cash only for customers of their own branch, never overdrawing.</summary>
public sealed class CashDeskAndBookingTests
{
    private sealed class Bookings : IAccountBookingRepository
    {
        public List<BookingLine> Booked { get; } = [];

        public Task<BookingResult> BookAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext principal, string customerId,
            IReadOnlyList<BookingLine> lines, CancellationToken ct, BeneficiaryCredit? beneficiary = null)
        {
            Booked.AddRange(lines);
            return Task.FromResult(new BookingResult(BookingStatus.Booked,
                [new AccountBalance(lines[0].AccountId, "EUR", 100.50m + lines[0].Amount)], ["T-1"]));
        }
    }

    private static Customer Customer(string id, string branch) =>
        new(id, branch, "Test", "Customer", "private", "t@example.test", "+49 0", "DE00" + id, new DateOnly(1990, 1, 1),
            new HashSet<string>(), null);

    private static (CashDesk Desk, ActionConfirmation Confirmation, Bookings Bookings) Create()
    {
        // Stuttgart customer C-1002 (the advisor's branch) and Munich customer C-5001; the repository ignores RLS on purpose.
        var db = new UnfilteredCustomerRepository(Customer("C-1002", "STR"), Customer("C-5001", "MUC"));
        var reader = new CustomerReader(db, new ResourceAuthorizationService(CustomerPolicies.Create()), new RecordingAudit());
        var store = new InMemoryPendingActionStore(TimeProvider.System);
        var bookings = new Bookings();
        var desk = new CashDesk(reader, db, bookings, new PendingActionFactory(store, TimeProvider.System));
        var confirmation = new ActionConfirmation(store,
            [new CashDeskAction(desk, CashDesk.DepositKind), new CashDeskAction(desk, CashDesk.WithdrawalKind)], TimeProvider.System);
        return (desk, confirmation, bookings);
    }

    [Fact]
    public async Task Advisor_deposits_cash_for_a_branch_customer_only_after_confirming()
    {
        var (desk, confirmation, bookings) = Create();
        var anna = Personas.Get("anna.berater");

        var card = await desk.PrepareAsync(anna, "C-1002", "A-1", 250m, deposit: true, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, card.Status);
        Assert.Contains(card.Value!.Facts, f => f.Key == "balanceAfter" && f.Value == "350.50 EUR");
        Assert.Empty(bookings.Booked);

        var done = await confirmation.ConfirmAsync(anna, card.Value.Id, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, done.Status);
        Assert.Equal(250m, Assert.Single(bookings.Booked).Amount);
    }

    [Fact]
    public async Task Customers_of_another_branch_are_not_found_at_the_cash_desk()
    {
        var (desk, _, _) = Create();
        var result = await desk.PrepareAsync(Personas.Get("anna.berater"), "C-5001", "A-1", 10m, deposit: true, CancellationToken.None);
        Assert.Equal(UseCaseStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Withdrawal_cannot_exceed_the_balance()
    {
        var (desk, _, _) = Create();
        var result = await desk.PrepareAsync(Personas.Get("anna.berater"), "C-1002", "A-1", 100.51m, deposit: false, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Invalid, result.Status);
    }

    [Theory]
    [InlineData("mehmet.kredit")]   // credit analyst: no cash desk
    [InlineData("deniz.customer")]  // customers cannot book cash on themselves
    [InlineData("lena.admin")]
    public async Task Only_advisors_with_account_cash_use_the_cash_desk(string persona)
    {
        var (desk, _, _) = Create();
        var result = await desk.PrepareAsync(Personas.Get(persona), "C-1002", "A-1", 10m, deposit: true, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
    }
}
