using System.Globalization;
using VirtualBank.Model.Common;

namespace VirtualBank.Model.Credit;

/// <summary>Non-negative amount in an ISO 4217 currency. Amounts in different currencies are never compared.</summary>
public readonly record struct Money
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Of(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new DomainRuleViolationException("An amount cannot be negative.");
        }

        var code = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            throw new DomainRuleViolationException($"'{currency}' is not a three-letter currency code.");
        }

        return new Money(amount, code);
    }

    public static Money Eur(decimal amount) => Of(amount, "EUR");

    public bool IsAtMost(Money limit) =>
        Currency == limit.Currency
            ? Amount <= limit.Amount
            : throw new DomainRuleViolationException($"Cannot compare {Currency} with {limit.Currency}.");

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");
}
