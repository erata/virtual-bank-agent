namespace VirtualBank.Application.Common;

public enum UseCaseStatus
{
    Succeeded,
    NotFound,
    Forbidden,
    Invalid,
}

/// <summary>
/// Transport-neutral outcome of a use case. The presentation layer maps it to HTTP (or any other protocol);
/// the application layer never references HTTP types.
/// </summary>
public sealed record UseCaseResult<T>
{
    private UseCaseResult(UseCaseStatus status, T? value, string? noticeCode, IReadOnlyDictionary<string, string[]>? errors)
    {
        Status = status;
        Value = value;
        NoticeCode = noticeCode;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public UseCaseStatus Status { get; }

    public T? Value { get; }

    /// <summary>Deterministic, language-independent notice code for Forbidden outcomes (may be null).</summary>
    public string? NoticeCode { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static UseCaseResult<T> Success(T value) => new(UseCaseStatus.Succeeded, value, null, null);

    /// <summary>Not visible to the caller, or does not exist. The two cases are intentionally indistinguishable.</summary>
    public static UseCaseResult<T> NotFound() => new(UseCaseStatus.NotFound, default, null, null);

    public static UseCaseResult<T> Forbidden(string? noticeCode) => new(UseCaseStatus.Forbidden, default, noticeCode, null);

    public static UseCaseResult<T> Invalid(string field, string message) =>
        new(UseCaseStatus.Invalid, default, null, new Dictionary<string, string[]> { [field] = [message] });
}
