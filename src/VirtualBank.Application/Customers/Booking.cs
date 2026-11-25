namespace VirtualBank.Application.Customers;

/// <summary>One booking line: a signed amount on one account (negative = debit) with a transaction text.</summary>
public sealed record BookingLine(string AccountId, decimal Amount, string Description);

public enum BookingStatus
{
    Booked,
    /// <summary>A debit would take the balance below zero (database CHECK); nothing was booked.</summary>
    InsufficientFunds,
    /// <summary>An account is not visible/bookable for this caller (RLS) or does not exist; nothing was booked.</summary>
    NotFound,
    /// <summary>The beneficiary is a customer of this bank but has no account in that currency; nothing was booked.</summary>
    BeneficiaryHasNoAccountInCurrency,
}

public sealed record AccountBalance(string AccountId, string Currency, decimal Balance);

/// <summary>
/// Optional credit to a beneficiary IBAN in the same transaction. If the IBAN belongs to a customer of this bank,
/// their account in that currency is credited (internal transfer); otherwise nothing is credited (external transfer).
/// </summary>
public sealed record BeneficiaryCredit(string Iban, decimal Amount, string Currency, string Description);

public sealed record BookingResult(BookingStatus Status, IReadOnlyList<AccountBalance> Balances, IReadOnlyList<string> TransactionIds,
    bool CreditedInternally = false)
{
    public static BookingResult Failed(BookingStatus status) => new(status, [], []);
}
