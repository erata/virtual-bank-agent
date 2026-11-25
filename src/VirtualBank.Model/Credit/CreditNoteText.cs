namespace VirtualBank.Model.Credit;

/// <summary>Validated note text: trimmed, 1 to <see cref="MaxLength"/> characters.</summary>
public sealed record CreditNoteText
{
    public const int MaxLength = 4000;

    private CreditNoteText(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? raw, out CreditNoteText? text)
    {
        var trimmed = raw?.Trim();
        text = string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength ? null : new CreditNoteText(trimmed);
        return text is not null;
    }

    public override string ToString() => Value;
}

public sealed record CreditNote(long Id, CreditApplicationId ApplicationId, string AuthorId, DateTimeOffset CreatedAt);
