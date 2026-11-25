using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Customers.Abstractions;
using VirtualBank.Application.Customers.Queries.GetMyBanking;

namespace VirtualBank.Application.Customers.Commands.SelfService;

/// <summary>A transfer order as entered by the customer. There is no customer ID: the token decides whose it is.</summary>
public sealed record TransferRequest(string? BeneficiaryName, string? Iban, decimal Amount, string? Currency,
    DateOnly? ExecutionDate, string? Reference);

/// <summary>A new standing order. Frequency is "weekly" or "monthly".</summary>
public sealed record StandingOrderRequest(string? BeneficiaryName, string? Iban, decimal Amount, string? Currency,
    string? Frequency, DateOnly? FirstDate, string? Reference);

/// <summary>
/// Self-service payments on the customer's OWN accounts: SEPA transfer (booked immediately when dated today,
/// otherwise planned), transfer between own accounts, new standing order, cancel a planned payment, pause/resume
/// a standing order. Each is prepared as a confirmation card (nothing changes) and executed only after the customer
/// confirms; the database (RLS + CHECK balance >= 0) again requires customer.payment.write, ownership via the token
/// subject and sufficient funds.
/// </summary>
public sealed class MyPaymentOrders(GetMyBankingHandler banking, ISelfServicePaymentRepository payments,
    IAccountBookingRepository bookings, PendingActionFactory actions, TimeProvider clock)
{
    public const string TransferKind = "self.transfer";
    public const string InternalTransferKind = "self.internal-transfer";
    public const string CreateStandingOrderKind = "self.standing-order.create";
    public const string CancelPaymentKind = "self.payment.cancel";
    public const string PauseStandingOrderKind = "self.standing-order.pause";
    public const string ResumeStandingOrderKind = "self.standing-order.resume";
    public const decimal MaxTransferAmount = 10_000m;
    public const int MaxDaysAhead = 90;

    private static readonly Regex Name = new(@"^[\p{L}\p{M}0-9 .,'&()/-]{2,70}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Reference = new(@"^[\p{L}\p{M}0-9 .,:'/()+-]{0,140}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static bool CanWrite(PrincipalContext principal) =>
        principal.HasPermission(KnownPermissions.CustomerPaymentWrite) && principal.HasPermission(KnownPermissions.CustomerReadSelf) &&
        principal.Type != PrincipalType.Machine;

    public async Task<UseCaseResult<PendingActionView>> PrepareTransferAsync(PrincipalContext principal, TransferRequest request, CancellationToken ct)
    {
        if (!CanWrite(principal)) return UseCaseResult<PendingActionView>.Forbidden(null);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var name = request.BeneficiaryName?.Trim() ?? "";
        var iban = NormalizeIban(request.Iban);
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? "";
        var date = request.ExecutionDate ?? today;
        var reference = request.Reference?.Trim() ?? "";

        if (!Name.IsMatch(name)) return UseCaseResult<PendingActionView>.Invalid("beneficiaryName", "Enter the beneficiary's name (2-70 characters).");
        if (!IsValidIban(iban)) return UseCaseResult<PendingActionView>.Invalid("iban", "The IBAN is not valid (checksum).");
        if (request.Amount <= 0 || request.Amount > MaxTransferAmount || decimal.Round(request.Amount, 2) != request.Amount)
            return UseCaseResult<PendingActionView>.Invalid("amount", $"Enter an amount between 0.01 and {MaxTransferAmount:N0} with at most two decimals.");
        if (date < today || date > today.AddDays(MaxDaysAhead))
            return UseCaseResult<PendingActionView>.Invalid("executionDate", $"Choose a date from today up to {MaxDaysAhead} days ahead.");
        if (!Reference.IsMatch(reference)) return UseCaseResult<PendingActionView>.Invalid("reference", "The reference may have at most 140 plain characters.");

        var profile = await banking.ProfileAsync(principal, ct);
        if (profile.Status != UseCaseStatus.Succeeded) return UseCaseResult<PendingActionView>.NotFound();
        if (string.Equals(iban, NormalizeIban(profile.Value!.Iban), StringComparison.Ordinal))
            return UseCaseResult<PendingActionView>.Invalid("iban", "This is your own account. Choose a different beneficiary.");
        var accounts = (await banking.AccountsAsync(principal, ct)).Value ?? [];
        var inCurrency = accounts.Where(a => a.Currency == currency).ToList();
        if (inCurrency.Count == 0) return UseCaseResult<PendingActionView>.Invalid("currency", "You have no account in this currency.");
        var immediate = date == today;
        // Dated today = booked at once: needs cover now. Future = planned order, checked when it is due.
        var account = immediate ? inCurrency.FirstOrDefault(a => a.Balance >= request.Amount) : inCurrency[0];
        if (account is null) return UseCaseResult<PendingActionView>.Invalid("amount", "Insufficient funds on your account in this currency.");

        var amount = request.Amount.ToString("N2", CultureInfo.InvariantCulture) + " " + currency;
        var action = actions.Create(principal, TransferKind,
            new Dictionary<string, string>
            {
                ["customerId"] = profile.Value.Id,
                ["accountId"] = account.Id,
                ["beneficiaryName"] = name,
                ["iban"] = iban,
                ["amount"] = request.Amount.ToString(CultureInfo.InvariantCulture),
                ["currency"] = currency,
                ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["reference"] = reference,
            },
            [
                new("fromAccount", $"{account.Id} ({currency})"),
                new("beneficiary", name),
                new("iban", MaskIban(iban)),
                new("amount", amount),
                new("executionDate", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new("reference", reference.Length == 0 ? "—" : reference),
                new("mode", immediate ? "immediate" : "planned"),
            ]);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    /// <summary>Between two of the customer's own accounts in the same currency; booked immediately.</summary>
    public async Task<UseCaseResult<PendingActionView>> PrepareInternalTransferAsync(PrincipalContext principal, string fromAccountId,
        string toAccountId, decimal amount, CancellationToken ct)
    {
        if (!CanWrite(principal)) return UseCaseResult<PendingActionView>.Forbidden(null);
        if (amount <= 0 || amount > MaxTransferAmount || decimal.Round(amount, 2) != amount)
            return UseCaseResult<PendingActionView>.Invalid("amount", $"Enter an amount between 0.01 and {MaxTransferAmount:N0} with at most two decimals.");
        var profile = await banking.ProfileAsync(principal, ct);
        if (profile.Status != UseCaseStatus.Succeeded) return UseCaseResult<PendingActionView>.NotFound();
        var accounts = (await banking.AccountsAsync(principal, ct)).Value ?? [];
        var from = accounts.FirstOrDefault(a => a.Id == fromAccountId);
        var to = accounts.FirstOrDefault(a => a.Id == toAccountId);
        if (from is null || to is null) return UseCaseResult<PendingActionView>.NotFound();
        if (from.Id == to.Id) return UseCaseResult<PendingActionView>.Invalid("toAccountId", "Choose two different accounts.");
        if (from.Currency != to.Currency)
            return UseCaseResult<PendingActionView>.Invalid("toAccountId", "Both accounts must have the same currency (no currency conversion).");
        if (from.Balance < amount) return UseCaseResult<PendingActionView>.Invalid("amount", "Insufficient funds on the source account.");
        var action = actions.Create(principal, InternalTransferKind,
            new Dictionary<string, string>
            {
                ["customerId"] = profile.Value!.Id, ["from"] = from.Id, ["to"] = to.Id,
                ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
            },
            [
                new("fromAccount", $"{from.Id} ({from.Type}) · {Money(from.Balance, from.Currency)}"),
                new("toAccount", $"{to.Id} ({to.Type}) · {Money(to.Balance, to.Currency)}"),
                new("amount", Money(amount, from.Currency)),
                new("mode", "immediate"),
            ]);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    /// <summary>A new standing order from one of the customer's accounts in the given currency.</summary>
    public async Task<UseCaseResult<PendingActionView>> PrepareCreateStandingOrderAsync(PrincipalContext principal, StandingOrderRequest request,
        CancellationToken ct)
    {
        if (!CanWrite(principal)) return UseCaseResult<PendingActionView>.Forbidden(null);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var name = request.BeneficiaryName?.Trim() ?? "";
        var iban = NormalizeIban(request.Iban);
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? "";
        var frequency = request.Frequency?.Trim().ToLowerInvariant() ?? "";
        var first = request.FirstDate ?? today.AddDays(1);
        var reference = request.Reference?.Trim() ?? "";
        if (!Name.IsMatch(name)) return UseCaseResult<PendingActionView>.Invalid("beneficiaryName", "Enter the beneficiary's name (2-70 characters).");
        if (!IsValidIban(iban)) return UseCaseResult<PendingActionView>.Invalid("iban", "The IBAN is not valid (checksum).");
        if (request.Amount <= 0 || request.Amount > MaxTransferAmount || decimal.Round(request.Amount, 2) != request.Amount)
            return UseCaseResult<PendingActionView>.Invalid("amount", $"Enter an amount between 0.01 and {MaxTransferAmount:N0} with at most two decimals.");
        if (frequency is not ("weekly" or "monthly")) return UseCaseResult<PendingActionView>.Invalid("frequency", "Choose weekly or monthly.");
        if (first < today || first > today.AddDays(MaxDaysAhead))
            return UseCaseResult<PendingActionView>.Invalid("firstDate", $"Choose a first date from today up to {MaxDaysAhead} days ahead.");
        if (!Reference.IsMatch(reference)) return UseCaseResult<PendingActionView>.Invalid("reference", "The reference may have at most 140 plain characters.");
        var profile = await banking.ProfileAsync(principal, ct);
        if (profile.Status != UseCaseStatus.Succeeded) return UseCaseResult<PendingActionView>.NotFound();
        var account = ((await banking.AccountsAsync(principal, ct)).Value ?? []).FirstOrDefault(a => a.Currency == currency);
        if (account is null) return UseCaseResult<PendingActionView>.Invalid("currency", "You have no account in this currency.");
        var action = actions.Create(principal, CreateStandingOrderKind,
            new Dictionary<string, string>
            {
                ["customerId"] = profile.Value!.Id, ["beneficiaryName"] = name, ["iban"] = iban,
                ["amount"] = request.Amount.ToString(CultureInfo.InvariantCulture), ["currency"] = currency,
                ["frequency"] = frequency, ["first"] = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["reference"] = reference,
            },
            [
                new("fromAccount", $"{account.Id} ({currency})"),
                new("beneficiary", name),
                new("iban", MaskIban(iban)),
                new("amount", Money(request.Amount, currency)),
                new("frequency", frequency),
                new("nextDate", first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new("reference", reference.Length == 0 ? "—" : reference),
            ]);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    private static string Money(decimal amount, string currency) => amount.ToString("N2", CultureInfo.InvariantCulture) + " " + currency;

    public async Task<UseCaseResult<PendingActionView>> PrepareCancelPaymentAsync(PrincipalContext principal, string paymentId, CancellationToken ct)
    {
        if (!CanWrite(principal)) return UseCaseResult<PendingActionView>.Forbidden(null);
        var payment = await payments.FindPlannedPaymentAsync(principal, paymentId, ct);
        if (payment is null) return UseCaseResult<PendingActionView>.NotFound();
        var action = actions.Create(principal, CancelPaymentKind,
            new Dictionary<string, string> { ["paymentId"] = payment.Id },
            [
                new("payment", payment.Id),
                new("description", payment.Description),
                new("amount", payment.Amount.ToString("N2", CultureInfo.InvariantCulture) + " " + payment.Currency),
                new("executionDate", payment.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ]);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    public async Task<UseCaseResult<PendingActionView>> PrepareStandingOrderAsync(PrincipalContext principal, string orderId, bool active, CancellationToken ct)
    {
        if (!CanWrite(principal)) return UseCaseResult<PendingActionView>.Forbidden(null);
        var order = (await payments.StandingOrdersAsync(principal, ct)).FirstOrDefault(o => o.Id == orderId);
        if (order is null) return UseCaseResult<PendingActionView>.NotFound();
        if (order.Active == active)
            return UseCaseResult<PendingActionView>.Invalid("active", active ? "This standing order is already active." : "This standing order is already paused.");
        var action = actions.Create(principal, active ? ResumeStandingOrderKind : PauseStandingOrderKind,
            new Dictionary<string, string> { ["orderId"] = order.Id },
            [
                new("standingOrder", order.Id),
                new("description", order.Description),
                new("amount", order.Amount.ToString("N2", CultureInfo.InvariantCulture) + " " + order.Currency),
                new("frequency", order.Frequency),
                new("nextDate", order.NextDueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ]);
        return UseCaseResult<PendingActionView>.Success(PendingActionView.From(action));
    }

    internal async Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct)
    {
        if (!CanWrite(principal)) return UseCaseResult<ActionOutcome>.Forbidden(null);
        var a = action.Arguments;
        switch (action.Kind)
        {
            case TransferKind:
            {
                // Re-check ownership now: the customer row must still belong to this token subject.
                var profile = await banking.ProfileAsync(principal, ct);
                if (profile.Status != UseCaseStatus.Succeeded || profile.Value!.Id != a["customerId"])
                    return UseCaseResult<ActionOutcome>.NotFound();
                var description = $"Transfer to {a["beneficiaryName"]} ({MaskIban(a["iban"])})" +
                    (a["reference"].Length > 0 ? $" – {a["reference"]}" : "");
                var amount = decimal.Parse(a["amount"], CultureInfo.InvariantCulture);
                var due = DateOnly.ParseExact(a["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (due <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                {
                    // Dated today: debit now. The CHECK (balance >= 0) refuses it if the cover is gone meanwhile.
                    // If the IBAN belongs to a customer of this bank, their account is credited in the same transaction.
                    var payer = await banking.ProfileAsync(principal, ct);
                    var payerName = payer.Value is { } me ? $"{me.FirstName} {me.LastName}" : a["customerId"];
                    var credit = new BeneficiaryCredit(a["iban"], amount, a["currency"],
                        $"Transfer from {payerName}" + (a["reference"].Length > 0 ? $" – {a["reference"]}" : ""));
                    var booked = await bookings.BookAsync(principal, a["customerId"], [new BookingLine(a["accountId"], -amount, description)], ct, credit);
                    return Booked(action.Kind, booked, a["accountId"]);
                }
                var id = $"P-{a["customerId"][2..]}-T{action.Id.ToString("N")[..6].ToUpperInvariant()}";
                var created = await payments.CreatePlannedPaymentAsync(principal, a["customerId"], id, description,
                    decimal.Parse(a["amount"], CultureInfo.InvariantCulture), a["currency"],
                    DateOnly.ParseExact(a["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture), ct);
                return created
                    ? UseCaseResult<ActionOutcome>.Success(new ActionOutcome(action.Kind, "PLANNED", id, [new("payment", id)]))
                    : UseCaseResult<ActionOutcome>.Forbidden(null);
            }
            case InternalTransferKind:
            {
                var amount = decimal.Parse(a["amount"], CultureInfo.InvariantCulture);
                var booked = await bookings.BookAsync(principal, a["customerId"],
                    [new BookingLine(a["from"], -amount, $"Transfer to own account {a["to"]}"),
                     new BookingLine(a["to"], amount, $"Transfer from own account {a["from"]}")], ct);
                return Booked(action.Kind, booked, a["from"]);
            }
            case CreateStandingOrderKind:
            {
                var profile = await banking.ProfileAsync(principal, ct);
                if (profile.Status != UseCaseStatus.Succeeded || profile.Value!.Id != a["customerId"])
                    return UseCaseResult<ActionOutcome>.NotFound();
                var id = $"SO-{a["customerId"][2..]}-N{action.Id.ToString("N")[..5].ToUpperInvariant()}";
                var description = $"{a["beneficiaryName"]} ({MaskIban(a["iban"])})" + (a["reference"].Length > 0 ? $" – {a["reference"]}" : "");
                var created = await payments.CreateStandingOrderAsync(principal, a["customerId"], id, description,
                    decimal.Parse(a["amount"], CultureInfo.InvariantCulture), a["currency"], a["frequency"],
                    DateOnly.ParseExact(a["first"], "yyyy-MM-dd", CultureInfo.InvariantCulture), ct);
                return created
                    ? UseCaseResult<ActionOutcome>.Success(new ActionOutcome(action.Kind, "ACTIVE", id, [new("standingOrder", id)]))
                    : UseCaseResult<ActionOutcome>.Forbidden(null);
            }
            case CancelPaymentKind:
                return await payments.CancelPlannedPaymentAsync(principal, a["paymentId"], ct)
                    ? UseCaseResult<ActionOutcome>.Success(new ActionOutcome(action.Kind, "CANCELLED", a["paymentId"], []))
                    : UseCaseResult<ActionOutcome>.NotFound();
            case PauseStandingOrderKind or ResumeStandingOrderKind:
            {
                var active = action.Kind == ResumeStandingOrderKind;
                return await payments.SetStandingOrderActiveAsync(principal, a["orderId"], active, ct)
                    ? UseCaseResult<ActionOutcome>.Success(new ActionOutcome(action.Kind, active ? "ACTIVE" : "PAUSED", a["orderId"], []))
                    : UseCaseResult<ActionOutcome>.NotFound();
            }
            default:
                return UseCaseResult<ActionOutcome>.NotFound();
        }
    }

    private static UseCaseResult<ActionOutcome> Booked(string kind, BookingResult result, string accountId) => result.Status switch
    {
        BookingStatus.Booked => UseCaseResult<ActionOutcome>.Success(new ActionOutcome(kind, "BOOKED", result.TransactionIds[0],
            [.. result.Balances.Select(b => new ActionFact("balanceAfter", $"{b.AccountId}: {Money(b.Balance, b.Currency)}")),
             .. kind == TransferKind ? [new ActionFact("beneficiaryBank", result.CreditedInternally ? "internal" : "external")] : Array.Empty<ActionFact>()])),
        BookingStatus.BeneficiaryHasNoAccountInCurrency =>
            UseCaseResult<ActionOutcome>.Invalid("currency", "The beneficiary has no account in this currency; nothing was booked."),
        BookingStatus.InsufficientFunds => UseCaseResult<ActionOutcome>.Invalid("amount", "Insufficient funds; nothing was booked."),
        _ => UseCaseResult<ActionOutcome>.NotFound(),
    };

    public static string NormalizeIban(string? iban) =>
        new string((iban ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    /// <summary>ISO 13616: 15-34 characters, country code, check digits, mod 97 = 1.</summary>
    public static bool IsValidIban(string iban)
    {
        if (!Regex.IsMatch(iban, "^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
            return false;
        var rearranged = iban[4..] + iban[..4];
        var digits = string.Concat(rearranged.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString()));
        return BigInteger.Parse(digits, CultureInfo.InvariantCulture) % 97 == 1;
    }

    public static string MaskIban(string iban) => iban.Length <= 8 ? iban : iban[..4] + " •••• " + iban[^4..];
}

/// <summary>Confirms the four self-service card kinds.</summary>
public sealed class MyPaymentOrderAction(MyPaymentOrders orders, string kind) : IConfirmableAction
{
    public string Kind => kind;

    public Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct) =>
        orders.ExecuteAsync(principal, action, ct);
}
