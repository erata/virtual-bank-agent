namespace VirtualBank.Model.Customers;

/// <summary>
/// A customer of the bank as stored in customer_db, including the analysts that have a credit application of
/// this customer assigned (assignment projection, see ADR-0005). Contact data, IBAN and date of birth are
/// sensitive: who may see them is decided by the customer policies in the Application layer.
/// </summary>
public sealed record Customer(
    string Id,
    string Branch,
    string FirstName,
    string LastName,
    string Segment,
    string Email,
    string Phone,
    string Iban,
    DateOnly DateOfBirth,
    IReadOnlySet<string> AssignedAnalystIds,
    string? OwnerSubjectId = null);

public sealed record Account(string Id, string Type, string Currency, decimal Balance);
