using VirtualBank.Application.Customers.Commands.SelfService;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Common;
using VirtualBank.Application.Customers;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Application.Customers.Authorization;
using VirtualBank.Application.Customers.Queries.GetCustomer;
using VirtualBank.Application.Customers.Queries.GetMyBanking;
using VirtualBank.Application.Customers.Queries.ListCustomerAccounts;
using VirtualBank.Application.Customers.Queries.ListCustomers;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;
using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Model.Customers;
using Xunit;

namespace VirtualBank.Application.UnitTests.Customers;

public sealed class SelfServiceTests
{
    private sealed class Payments : ISelfServicePaymentRepository
    {
        public int Calls;
        public bool IncludeForeignPayment;
        public Task<IReadOnlyList<ScheduledPaymentView>> UpcomingAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, int days, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ScheduledPaymentView>>(
                IncludeForeignPayment
                    ? [new("P-foreign", "C-5001", "Rent", 100, "EUR", DateOnly.FromDateTime(DateTime.UtcNow), "planned")]
                    : [new("P-own", "C-2001", "Rent", 100, "EUR", DateOnly.FromDateTime(DateTime.UtcNow), "planned")]);
        }
        public Task<IReadOnlyList<BookedTransactionView>> RecentAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, int days, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BookedTransactionView>>([]);
        public Task<IReadOnlyList<StandingOrderView>> StandingOrdersAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<StandingOrderView>>([]);
        public List<string> Created { get; } = [];
        public Task<ScheduledPaymentView?> FindPlannedPaymentAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, string id, CancellationToken ct)
            => Task.FromResult<ScheduledPaymentView?>(null);
        public Task<bool> CreatePlannedPaymentAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, string customerId, string id,
            string description, decimal amount, string currency, DateOnly dueDate, CancellationToken ct)
        {
            Created.Add($"{customerId} {amount} {currency} {dueDate:yyyy-MM-dd} {description}");
            return Task.FromResult(true);
        }
        public Task<bool> CancelPlannedPaymentAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, string id, CancellationToken ct)
            => Task.FromResult(false);
        public Task<bool> SetStandingOrderActiveAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, string id, bool active, CancellationToken ct)
            => Task.FromResult(false);
        public Task<bool> CreateStandingOrderAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, string customerId, string id,
            string description, decimal amount, string currency, string frequency, DateOnly next, CancellationToken ct)
        {
            Created.Add($"SO {customerId} {amount} {currency} {frequency} {next:yyyy-MM-dd}");
            return Task.FromResult(true);
        }
    }

    /// <summary>Books like the database would: per-account balances, never below zero, all-or-nothing.</summary>
    private sealed class Bookings : IAccountBookingRepository
    {
        public Dictionary<string, decimal> Balances { get; } = new() { ["A-1"] = 100.50m, ["A-2"] = 0m };
        public List<string> Lines { get; } = [];
        public Task<BookingResult> BookAsync(VirtualBank.Application.Authorization.Principals.PrincipalContext p, string customerId,
            IReadOnlyList<BookingLine> lines, CancellationToken ct, BeneficiaryCredit? beneficiary = null)
        {
            var next = new Dictionary<string, decimal>(Balances);
            foreach (var l in lines)
            {
                if (!next.ContainsKey(l.AccountId)) return Task.FromResult(BookingResult.Failed(BookingStatus.NotFound));
                next[l.AccountId] += l.Amount;
                if (next[l.AccountId] < 0) return Task.FromResult(BookingResult.Failed(BookingStatus.InsufficientFunds));
            }
            foreach (var (k, v) in next) Balances[k] = v;
            Lines.AddRange(lines.Select(l => $"{l.AccountId} {l.Amount}"));
            return Task.FromResult(new BookingResult(BookingStatus.Booked,
                [.. lines.Select(l => new AccountBalance(l.AccountId, "EUR", Balances[l.AccountId]))], [.. lines.Select((_, i) => $"T-{i}")]));
        }
    }
    private static Customer Customer(string id, string ownerId) =>
        new(id, "MUC", "Test", "Customer", "private", "test@example.test", "+49 0", "DE00" + id,
            new DateOnly(1990, 1, 1), new HashSet<string>(), ownerId);

    private static (GetMyBankingHandler Handler, Payments Payments, UnfilteredCustomerRepository Db, RecordingAudit Audit) Create()
    {
        var db = new UnfilteredCustomerRepository(
            Customer("C-2001", Personas.SubjectOf("deniz.customer")),
            Customer("C-5001", Personas.SubjectOf("thomas.customer")));
        var audit = new RecordingAudit();
        var reader = new CustomerReader(db, new ResourceAuthorizationService(CustomerPolicies.Create()), audit);
        var list = new ListCustomersHandler(reader);
        var accounts = new ListCustomerAccountsHandler(reader, db);
        var payments = new Payments();
        return (new GetMyBankingHandler(reader, accounts, new SummarizeCustomerBalancesHandler(list, accounts), payments),
            payments, db, audit);
    }

    [Fact]
    public async Task Two_customers_get_only_their_own_profiles_even_when_repository_ignores_rls()
    {
        var (handler, _, _, audit) = Create();
        var deniz = await handler.ProfileAsync(Personas.Get("deniz.customer"), CancellationToken.None);
        var thomas = await handler.ProfileAsync(Personas.Get("thomas.customer"), CancellationToken.None);
        Assert.Equal("C-2001", deniz.Value!.Id);
        Assert.Equal("C-5001", thomas.Value!.Id);
        Assert.NotNull(deniz.Value.Iban);
        Assert.NotEqual(deniz.Value.Iban, thomas.Value.Iban);
        Assert.NotEmpty(audit.Mismatches); // Cross-customer rows were hidden by independent Application policy.
    }

    [Fact]
    public async Task Admin_cannot_read_own_bank_profile_or_schedule_without_self_permission()
    {
        var (handler, payments, _, _) = Create();
        var profile = await handler.ProfileAsync(Personas.Get("lena.admin"), CancellationToken.None);
        var upcoming = await handler.UpcomingAsync(Personas.Get("lena.admin"), 30, CancellationToken.None);
        Assert.Equal(UseCaseStatus.NotFound, profile.Status);
        Assert.Equal(UseCaseStatus.NotFound, upcoming.Status);
        Assert.Equal(0, payments.Calls);
    }

    [Fact]
    public async Task Read_only_payment_window_rejects_out_of_range_before_db_access()
    {
        var (handler, payments, _, _) = Create();
        var result = await handler.UpcomingAsync(Personas.Get("deniz.customer"), 365, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Invalid, result.Status);
        Assert.Equal(0, payments.Calls);
    }

    [Fact]
    public async Task Broken_payment_rls_cannot_leak_foreign_customer_payment()
    {
        var (handler, payments, _, _) = Create();
        payments.IncludeForeignPayment = true;
        var result = await handler.UpcomingAsync(Personas.Get("deniz.customer"), 30, CancellationToken.None);
        Assert.Equal(UseCaseStatus.NotFound, result.Status);
        Assert.Equal(1, payments.Calls);
    }

    [Fact]
    public async Task Personal_balance_uses_only_the_identity_bound_row()
    {
        var (handler, _, _, _) = Create();
        var result = await handler.BalanceAsync(Personas.Get("deniz.customer"), CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Equal("C-2001", Assert.Single(result.Value!.Rows).CustomerId);
        Assert.Equal(100.50m, Assert.Single(result.Value.TotalsByCurrency).Balance);
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static Bookings LastBookings = new();

    private static (MyPaymentOrders Orders, ActionConfirmation Confirmation, Payments Payments, FixedClock Clock) Orders()
    {
        var (handler, payments, _, _) = Create();
        var clock = new FixedClock();
        var store = new InMemoryPendingActionStore(clock);
        LastBookings = new Bookings();
        var orders = new MyPaymentOrders(handler, payments, LastBookings, new PendingActionFactory(store, clock), clock);
        var confirmation = new ActionConfirmation(store,
            [new MyPaymentOrderAction(orders, MyPaymentOrders.TransferKind), new MyPaymentOrderAction(orders, MyPaymentOrders.InternalTransferKind),
             new MyPaymentOrderAction(orders, MyPaymentOrders.CreateStandingOrderKind)], clock);
        return (orders, confirmation, payments, clock);
    }

    private static TransferRequest Transfer(decimal amount = 120.50m, string iban = "DE89 3704 0044 0532 0130 00") =>
        new("Hausverwaltung Müller", iban, amount, "EUR", new DateOnly(2026, 11, 28), "Miete Dezember");

    [Fact]
    public async Task Transfer_is_only_planned_after_the_same_customer_confirms_the_card()
    {
        var (orders, confirmation, payments, _) = Orders();
        var deniz = Personas.Get("deniz.customer");

        var card = await orders.PrepareTransferAsync(deniz, Transfer(), CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, card.Status);
        Assert.Empty(payments.Created); // preparing changes nothing
        Assert.Contains(card.Value!.Facts, f => f.Key == "iban" && f.Value == "DE89 •••• 3000");

        // Another customer cannot confirm Deniz's card, and that attempt does not consume it either.
        Assert.Equal(UseCaseStatus.NotFound, (await confirmation.ConfirmAsync(Personas.Get("thomas.customer"), card.Value.Id, CancellationToken.None)).Status);
        Assert.Empty(payments.Created);

        var done = await confirmation.ConfirmAsync(deniz, card.Value.Id, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, done.Status);
        Assert.Equal("C-2001 120.50 EUR 2026-11-28 Transfer to Hausverwaltung Müller (DE89 •••• 3000) – Miete Dezember", Assert.Single(payments.Created));
        // Single use: a replay finds nothing.
        Assert.Equal(UseCaseStatus.NotFound, (await confirmation.ConfirmAsync(deniz, card.Value.Id, CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData(0, "DE89370400440532013000", "amount")]
    [InlineData(10_000.01, "DE89370400440532013000", "amount")]
    [InlineData(12.345, "DE89370400440532013000", "amount")]
    [InlineData(50, "DE89370400440532013001", "iban")]   // checksum
    [InlineData(50, "DE00C-2001", "iban")]
    public async Task Invalid_transfers_are_rejected_before_a_card_exists(decimal amount, string iban, string field)
    {
        var (orders, _, payments, _) = Orders();
        var result = await orders.PrepareTransferAsync(Personas.Get("deniz.customer"), Transfer(amount, iban), CancellationToken.None);
        Assert.Equal(UseCaseStatus.Invalid, result.Status);
        Assert.Contains(field, result.Errors!.Keys);
        Assert.Empty(payments.Created);
    }

    [Theory]
    [InlineData("anna.berater")]   // staff: no self-service at all
    [InlineData("lena.admin")]
    public async Task Only_bank_customers_may_prepare_orders(string persona)
    {
        var (orders, _, _, _) = Orders();
        var result = await orders.PrepareTransferAsync(Personas.Get(persona), Transfer(), CancellationToken.None);
        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task Expired_card_cannot_be_confirmed()
    {
        var (orders, confirmation, payments, clock) = Orders();
        var deniz = Personas.Get("deniz.customer");
        var card = await orders.PrepareTransferAsync(deniz, Transfer(), CancellationToken.None);
        clock.Now += PendingActionFactory.Lifetime + TimeSpan.FromSeconds(1);
        Assert.Equal(UseCaseStatus.NotFound, (await confirmation.ConfirmAsync(deniz, card.Value!.Id, CancellationToken.None)).Status);
        Assert.Empty(payments.Created);
    }

    [Theory]
    [InlineData("DE89 3704 0044 0532 0130 00", true)]
    [InlineData("GB82WEST12345698765432", true)]
    [InlineData("TR330006100519786457841326", true)]
    [InlineData("DE89370400440532013001", false)]
    public void Iban_checksum(string iban, bool valid) =>
        Assert.Equal(valid, MyPaymentOrders.IsValidIban(MyPaymentOrders.NormalizeIban(iban)));
}
