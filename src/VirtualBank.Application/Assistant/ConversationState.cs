namespace VirtualBank.Application.Assistant;

/// <summary>Only owner-scoped conversation messages; tool outputs are never persisted.</summary>
public sealed record ConversationState(string Id, IReadOnlyList<string> PriorUserMessages, IReadOnlyList<string>? PriorAssistantMessages = null);
