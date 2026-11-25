using System.Text.RegularExpressions;

namespace VirtualBank.Model.Customers;

/// <summary>Customer identifiers have the form C-dddd (e.g. C-1002).</summary>
public static partial class CustomerId
{
    [GeneratedRegex("^C-[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);
}
