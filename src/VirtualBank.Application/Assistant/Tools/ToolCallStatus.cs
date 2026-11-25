namespace VirtualBank.Application.Assistant.Tools;

public enum ToolCallStatus
{
    Succeeded,

    /// <summary>Does not exist or is not visible to the caller. Deliberately indistinguishable, so no notice is emitted.</summary>
    NotFound,

    Denied,
    Invalid,
    Failed,
}

/// <summary>Outcome of the policy gate or of the use case behind a tool.</summary>
public sealed record ToolOutcome(ToolCallStatus Status, object? Data = null, string? NoticeCode = null)
{
    public static ToolOutcome Denied(string noticeCode) => new(ToolCallStatus.Denied, NoticeCode: noticeCode);

    public static ToolOutcome Invalid(string noticeCode) => new(ToolCallStatus.Invalid, NoticeCode: noticeCode);
}
