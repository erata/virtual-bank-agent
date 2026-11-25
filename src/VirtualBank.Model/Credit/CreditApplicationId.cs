using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using VirtualBank.Model.Common;

namespace VirtualBank.Model.Credit;

/// <summary>
/// Strongly typed identifier of a credit application (form K-ddd, e.g. K-100). A sealed record rather than
/// a struct so that no invalid default value can exist; the only way in is <see cref="TryParse"/> or <see cref="Parse"/>.
/// </summary>
public sealed partial record CreditApplicationId
{
    private CreditApplicationId(string value) => Value = value;

    public string Value { get; }

    [GeneratedRegex("^K-[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryParse(string? value, [NotNullWhen(true)] out CreditApplicationId? id)
    {
        id = value is not null && Pattern().IsMatch(value) ? new CreditApplicationId(value) : null;
        return id is not null;
    }

    public static CreditApplicationId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new DomainRuleViolationException($"'{value}' is not a valid credit application id.");

    public override string ToString() => Value;
}
