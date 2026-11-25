using VirtualBank.Application.Assistant;
using System.Collections.Concurrent;
using VirtualBank.Application.Assistant.Abstractions;

namespace VirtualBank.Infrastructure.Assistant;

/// <summary>Development-only bounded conversation store. Stores user text, never tool results, keys or protected chunks.</summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private sealed class Entry(string owner)
    {
        public string Owner { get; } = owner;
        public DateTimeOffset Touched { get; set; } = DateTimeOffset.UtcNow;
        public List<string> UserMessages { get; } = [];
        public List<string> AssistantMessages { get; } = [];
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _mutex = new();

    public Task<ConversationState?> LoadAsync(string ownerSubject, string? requestedId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_mutex)
        {
            foreach (var (id, entry) in _entries)
                if (entry.Touched < DateTimeOffset.UtcNow.AddHours(-1)) _entries.TryRemove(id, out _);
            if (string.IsNullOrWhiteSpace(requestedId))
            {
                if (_entries.Count >= 1000) return Task.FromResult<ConversationState?>(null);
                var id = Guid.NewGuid().ToString("N");
                _entries[id] = new Entry(ownerSubject);
                return Task.FromResult<ConversationState?>(new ConversationState(id, []));
            }
            if (!_entries.TryGetValue(requestedId, out var state) || state.Owner != ownerSubject)
                return Task.FromResult<ConversationState?>(null);
            state.Touched = DateTimeOffset.UtcNow;
            return Task.FromResult<ConversationState?>(new ConversationState(requestedId, state.UserMessages.TakeLast(8).ToArray(),
                state.AssistantMessages.TakeLast(8).ToArray()));
        }
    }

    public Task AppendTurnAsync(string ownerSubject, string conversationId, string userMessage, string? assistantAnswer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_mutex)
        {
            if (!_entries.TryGetValue(conversationId, out var state) || state.Owner != ownerSubject)
                throw new InvalidOperationException("Conversation does not belong to the current principal.");
            state.UserMessages.Add(userMessage);
            state.AssistantMessages.Add(assistantAnswer ?? string.Empty);
            if (state.UserMessages.Count > 10) state.UserMessages.RemoveAt(0);
            if (state.AssistantMessages.Count > 10) state.AssistantMessages.RemoveAt(0);
            state.Touched = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }
}
