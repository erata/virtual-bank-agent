using VirtualBank.Application.Customers;

namespace VirtualBank.Presentation.Customers;

/// <summary>HTTP contracts of the customer endpoints (stable JSON shape, decoupled from the application views).</summary>
public sealed record CustomerSummaryResponse(string Id, string Branch, string FirstName, string LastName, string Segment)
{
    public static CustomerSummaryResponse From(CustomerSummaryView v) => new(v.Id, v.Branch, v.FirstName, v.LastName, v.Segment);
}

public sealed record CustomerDetailResponse(
    string Id, string Branch, string FirstName, string LastName, string Segment,
    string? Email, string? Phone, string? Iban, DateOnly? DateOfBirth, bool SensitiveFieldsMasked)
{
    public static CustomerDetailResponse From(CustomerDetailView v) =>
        new(v.Id, v.Branch, v.FirstName, v.LastName, v.Segment, v.Email, v.Phone, v.Iban, v.DateOfBirth, v.SensitiveFieldsMasked);
}

public sealed record AccountResponse(string Id, string Type, string Currency, decimal Balance)
{
    public static AccountResponse From(AccountView v) => new(v.Id, v.Type, v.Currency, v.Balance);
}
