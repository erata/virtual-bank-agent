using VirtualBank.Model.Credit;

namespace VirtualBank.Application.Credit;

/// <summary>Read model returned by the queries. Preparer and assignment IDs stay internal.</summary>
public sealed record CreditApplicationView(
    string Id, string CustomerId, decimal Amount, string Currency, CreditApplicationStatus Status, string Purpose, int Version)
{
    public static CreditApplicationView From(CreditApplication a) =>
        new(a.Id.Value, a.CustomerId, a.Amount.Amount, a.Amount.Currency, a.Status, a.Purpose, a.Version);
}

/// <summary>A note as stored (author is a subject id); mapped to CreditNoteView before it leaves the use case.</summary>
public sealed record StoredCreditNote(long Id, string AuthorId, string Body, DateTimeOffset CreatedAt);
