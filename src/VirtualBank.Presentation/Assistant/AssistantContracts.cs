using VirtualBank.Application.Common.Actions;
using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Documents;

namespace VirtualBank.Presentation.Assistant;

public sealed record AssistantChatRequest(string? Message, string? ConversationId = null);

public sealed record ToolCallResponse(int Sequence, string Tool, string Status, string? NoticeCode);

/// <summary>
/// HTTP contract of one assistant turn. <c>answer</c> is model narrative; <c>toolCalls</c>, <c>notices</c> and
/// <c>partial</c> are computed by the application and are what clients should rely on.
/// </summary>
public sealed record AssistantChatResponse(
    string? Answer, string Mode, bool Partial, IReadOnlyList<ToolCallResponse> ToolCalls, IReadOnlyList<string> Notices,
    IReadOnlyList<DocumentCitation>? Citations = null,
    string? ConversationId = null,
    IReadOnlyList<AssistantDataTable>? Tables = null,
    IReadOnlyList<PendingActionView>? Actions = null)
{
    public static AssistantChatResponse From(AssistantReply reply) => new(
        reply.Answer,
        reply.Mode,
        reply.IsPartial,
        reply.ToolCalls.Select(c => new ToolCallResponse(c.Sequence, c.Tool, StatusName(c.Status), c.NoticeCode)).ToList(),
        reply.NoticeCodes, reply.Citations, reply.ConversationId, reply.Tables, reply.Actions);

    private static string StatusName(ToolCallStatus status) => status switch
    {
        ToolCallStatus.Succeeded => "SUCCEEDED",
        ToolCallStatus.NotFound => "NOT_FOUND",
        ToolCallStatus.Denied => "DENIED",
        ToolCallStatus.Invalid => "INVALID",
        _ => "FAILED",
    };
}
