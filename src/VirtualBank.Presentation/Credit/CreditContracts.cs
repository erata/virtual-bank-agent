using VirtualBank.Application.Credit;
using VirtualBank.Model.Credit;

namespace VirtualBank.Presentation.Credit;

/// <summary>HTTP contract for an application. Preparer and assignment IDs stay internal.</summary>
public sealed record CreditApplicationResponse(
    string Id, string CustomerId, decimal Amount, string Currency, string Status, string Purpose, int Version)
{
    public static CreditApplicationResponse From(CreditApplicationView v) =>
        new(v.Id, v.CustomerId, v.Amount, v.Currency, v.Status.ToString().ToUpperInvariant(), v.Purpose, v.Version);
}

public sealed record AddCreditNoteRequest(string? Text);

public sealed record CreditNoteResponse(long Id, string ApplicationId, DateTimeOffset CreatedAt)
{
    public static CreditNoteResponse From(CreditNote n) => new(n.Id, n.ApplicationId.Value, n.CreatedAt);
}
