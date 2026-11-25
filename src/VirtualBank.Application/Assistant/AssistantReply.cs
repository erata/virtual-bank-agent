using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Documents;

namespace VirtualBank.Application.Assistant;

/// <summary>Trace of one tool invocation. Arguments and results are not echoed to the client.</summary>
public sealed record ToolCallRecord(int Sequence, string Tool, ToolCallStatus Status, string? NoticeCode);

/// <summary>
/// Result of one assistant turn. <see cref="Answer"/> is model text and therefore untrusted narrative;
/// <see cref="ToolCalls"/>, <see cref="NoticeCodes"/> and <see cref="IsPartial"/> are computed by application code
/// and are authoritative.
/// </summary>
public sealed record AssistantReply(
    string? Answer,
    string Mode,
    IReadOnlyList<ToolCallRecord> ToolCalls,
    IReadOnlyList<string> NoticeCodes,
    IReadOnlyList<DocumentCitation>? Citations = null,
    string? ConversationId = null,
    IReadOnlyList<AssistantDataTable>? Tables = null,
    IReadOnlyList<PendingActionView>? Actions = null)
{
    /// <summary>True when at least one requested capability was denied, failed or could not be completed.</summary>
    public bool IsPartial => NoticeCodes.Count > 0
        || ToolCalls.Any(c => c.Status is not (ToolCallStatus.Succeeded or ToolCallStatus.NotFound));
}
