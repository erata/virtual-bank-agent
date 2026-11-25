using VirtualBank.Model.Customers;

namespace VirtualBank.Application.Customers;

public sealed record CustomerSummaryView(string Id, string Branch, string FirstName, string LastName, string Segment)
{
    public static CustomerSummaryView From(Customer c) => new(c.Id, c.Branch, c.FirstName, c.LastName, c.Segment);
}

/// <summary>
/// Customer detail. Fields covered by <c>customer.read-sensitive</c> (contact data, IBAN, date of birth) are null
/// unless the caller holds that permission; <see cref="SensitiveFieldsMasked"/> says so explicitly.
/// </summary>
public sealed record CustomerDetailView(
    string Id, string Branch, string FirstName, string LastName, string Segment,
    string? Email, string? Phone, string? Iban, DateOnly? DateOfBirth, bool SensitiveFieldsMasked)
{
    public static CustomerDetailView From(Customer c, bool includeSensitive) => new(
        c.Id, c.Branch, c.FirstName, c.LastName, c.Segment,
        includeSensitive ? c.Email : null,
        includeSensitive ? c.Phone : null,
        includeSensitive ? c.Iban : null,
        includeSensitive ? c.DateOfBirth : null,
        SensitiveFieldsMasked: !includeSensitive);
}

public sealed record AccountView(string Id, string Type, string Currency, decimal Balance)
{
    public static AccountView From(Account a) => new(a.Id, a.Type, a.Currency, a.Balance);
}
