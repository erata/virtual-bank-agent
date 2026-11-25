using VirtualBank.Application.Common;

namespace VirtualBank.Application.Assistant.Tools;

/// <summary>Translates use-case outcomes into tool outcomes, keeping the notice code decided by the use case.</summary>
public static class ToolResults
{
    public static ToolOutcome From<T>(UseCaseResult<T> result, Func<T, object>? project = null) => result.Status switch
    {
        UseCaseStatus.Succeeded => new ToolOutcome(ToolCallStatus.Succeeded, project is null ? result.Value : project(result.Value!)),
        UseCaseStatus.NotFound => new ToolOutcome(ToolCallStatus.NotFound),
        UseCaseStatus.Forbidden => ToolOutcome.Denied(result.NoticeCode ?? AssistantNoticeCodes.ToolNotPermitted),
        // The model gets the application's own validation messages (e.g. "The IBAN is not valid"), so it can tell the
        // user exactly what to correct instead of a generic failure. These are fixed texts, never user data.
        UseCaseStatus.Invalid => new ToolOutcome(ToolCallStatus.Invalid, new { invalid = result.Errors }, AssistantNoticeCodes.InvalidToolArguments),
        _ => throw new InvalidOperationException($"Unmapped use-case status {result.Status}."),
    };
}
