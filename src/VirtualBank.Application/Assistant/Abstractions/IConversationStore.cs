using VirtualBank.Application.Assistant;

namespace VirtualBank.Application.Assistant.Abstractions;

/// <summary>Never caches tool results or protected documents; current authorization applies to every new turn.</summary>
public interface IConversationStore
{
    Task<ConversationState?> LoadAsync(string ownerSubject, string? requestedId, CancellationToken ct);
    Task AppendTurnAsync(string ownerSubject, string conversationId, string userMessage, string? assistantAnswer, CancellationToken ct);
}
