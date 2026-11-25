using System.Globalization;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Customers.Abstractions;

namespace VirtualBank.Application.Customers.Commands.CashDesk;

/// <summary>
/// Branch cash desk: an advisor books a cash deposit or withdrawal on an account of a customer of their OWN branch
/// (the customer hands over / receives cash at the counter). Always a confirmation card; the database again requires
/// account.cash + customer.read + same branch, and refuses a withdrawal that would overdraw the account.
/// </summary>
public sealed class CashDesk(CustomerReader customers, ICustomerRepository repository, IAccountBookingRepository bookings,
    PendingActionFactory actions)
{
    public const string DepositKind = "cash.deposit";
    public const string WithdrawalKind = "cash.withdrawal";
    public const decimal MaxAmount = 10_000m;

    private static bool CanUse(PrincipalContext principal) =>
        principal.HasPermission(KnownPermissions.AccountCash) && principal.HasPermission(KnownPermissions.CustomerRead) &&
        principal.Type != PrincipalType.Machine;

    public async Task<UseCaseResult<PendingActionView>> PrepareAsync(PrincipalContext principal, string customerId, string accountId,
        decimal amount, bool deposit, CancellationToken ct)
    {
        if (!CanUse(principal)) return UseCaseResult<PendingActionView>.Forbidden(null);
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
            return UseCaseResult<PendingActionView>.Invalid("amount", $"Enter an amount between 0.01 and {MaxAmount:N0} with at most two decimals.");
        var customer = await customers.FindReadableAsync(principal, customerId, ct);
        if (customer is null) return UseCaseResult<PendingActionView>.NotFound();
        var account = (await repository.ListAccountsAsync(principal, customer.Id, ct)).FirstOrDefault(a => a.Id == accountId);
        if (account is null) return UseCaseResult<PendingActionView>.NotFound();
        if (!deposit && amount > account.Balance)
            return UseCaseResult<PendingActionView>.Invalid("amount", "The withdrawal exceeds the account balance.");

        var money = Format(amount, account.Currency);
        var action = actions.Create(principal, deposit ? DepositKind : WithdrawalKind,
            new Dictionary<string, string>
            {
                ["customerId"] = customer.Id,
                ["accountId"] = account.Id,
                ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
            },
            [
                new("customer", $"{customer.FirstName} {customer.LastName} ({customer.Id})"),
                new("account", $"{account.Id} ({account.Type})"),
                new("amount", money),
                new("balanceBefore", Format(account.Balance, account.Currency)),
                new("balanceAfter", Format(deposit ? account.Balance + amount : account.Balance - amount, account.Currency)),
            ]);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    internal async Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct)
    {
        if (!CanUse(principal)) return UseCaseResult<ActionOutcome>.Forbidden(null);
        var a = action.Arguments;
        // Re-check visibility with the CURRENT token: the advisor's branch may have changed meanwhile.
        if (await customers.FindReadableAsync(principal, a["customerId"], ct) is null) return UseCaseResult<ActionOutcome>.NotFound();
        var deposit = action.Kind == DepositKind;
        var amount = decimal.Parse(a["amount"], CultureInfo.InvariantCulture);
        var text = deposit ? "Cash deposit at the branch" : "Cash withdrawal at the branch";
        var result = await bookings.BookAsync(principal, a["customerId"], [new BookingLine(a["accountId"], deposit ? amount : -amount, text)], ct);
        return result.Status switch
        {
            BookingStatus.Booked => UseCaseResult<ActionOutcome>.Success(new ActionOutcome(action.Kind, "BOOKED", a["accountId"],
                [new("transaction", result.TransactionIds[0]), new("balanceAfter", Format(result.Balances[0].Balance, result.Balances[0].Currency))])),
            BookingStatus.InsufficientFunds => UseCaseResult<ActionOutcome>.Invalid("amount", "The withdrawal exceeds the account balance."),
            _ => UseCaseResult<ActionOutcome>.NotFound(),
        };
    }

    public static string Format(decimal amount, string currency) => amount.ToString("N2", CultureInfo.InvariantCulture) + " " + currency;
}

/// <summary>Confirms cash desk cards.</summary>
public sealed class CashDeskAction(CashDesk desk, string kind) : IConfirmableAction
{
    public string Kind => kind;

    public Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct) =>
        desk.ExecuteAsync(principal, action, ct);
}
