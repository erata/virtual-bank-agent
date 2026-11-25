using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Common.Actions;

/// <summary>
/// One fact shown on a confirmation card, e.g. ("amount", "35,000.00 EUR"). Keys are stable identifiers the UI
/// translates; values are already-formatted, authorized data (plain text).
/// </summary>
public sealed record ActionFact(string Key, string Value);

/// <summary>
/// A prepared, not yet executed, state-changing operation. Preparing changes nothing. Executing requires an
/// explicit human confirmation from the SAME principal before <see cref="ExpiresAt"/>; the id is single-use.
/// </summary>
public sealed record PendingAction(
    Guid Id, string SubjectId, string Kind, IReadOnlyDictionary<string, string> Arguments,
    IReadOnlyList<ActionFact> Facts, DateTimeOffset ExpiresAt);

/// <summary>What the client renders: kind (for the title/button text), facts and expiry. Never the arguments' internals.</summary>
public sealed record PendingActionView(Guid Id, string Kind, IReadOnlyList<ActionFact> Facts, DateTimeOffset ExpiresAt)
{
    public static PendingActionView From(PendingAction action) => new(action.Id, action.Kind, action.Facts, action.ExpiresAt);
}

/// <summary>Result of executing a confirmed action: a stable outcome code and the affected resource.</summary>
public sealed record ActionOutcome(string Kind, string Outcome, string ResourceId, IReadOnlyList<ActionFact> Facts);

public interface IPendingActionStore
{
    void Put(PendingAction action);

    /// <summary>Removes and returns the action (single use). A second call for the same id returns null.</summary>
    PendingAction? Take(Guid id);
}

/// <summary>
/// Executes one kind of confirmed action. Implementations MUST re-authorize with the current principal and data:
/// permissions, limits and resource state may have changed since the action was prepared.
/// </summary>
public interface IConfirmableAction
{
    string Kind { get; }

    Task<UseCaseResult<ActionOutcome>> ExecuteAsync(PrincipalContext principal, PendingAction action, CancellationToken ct);
}

/// <summary>Creates pending actions with a fixed lifetime.</summary>
public sealed class PendingActionFactory(IPendingActionStore store, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public PendingAction Create(PrincipalContext principal, string kind, IReadOnlyDictionary<string, string> arguments,
        IReadOnlyList<ActionFact> facts)
    {
        var action = new PendingAction(Guid.NewGuid(), principal.SubjectId, kind, arguments, facts, clock.GetUtcNow() + Lifetime);
        store.Put(action);
        return action;
    }
}

/// <summary>
/// The single confirmation entry point for every high-impact operation (credit decisions, payment orders ...).
/// Someone else's, expired, replayed or unknown ids are all reported as "not found".
/// </summary>
public sealed class ActionConfirmation(IPendingActionStore store, IEnumerable<IConfirmableAction> handlers, TimeProvider clock)
{
    private readonly Dictionary<string, IConfirmableAction> _handlers =
        handlers.ToDictionary(h => h.Kind, StringComparer.Ordinal);

    public async Task<UseCaseResult<ActionOutcome>> ConfirmAsync(PrincipalContext principal, Guid id, CancellationToken ct)
    {
        if (!principal.IsActive || principal.Type == PrincipalType.Machine) return UseCaseResult<ActionOutcome>.Forbidden(null);
        var action = store.Take(id);
        if (action is not null && action.SubjectId != principal.SubjectId)
        {
            store.Put(action); // someone else's card: leave it usable for its owner, reveal nothing
            return UseCaseResult<ActionOutcome>.NotFound();
        }
        if (action is null || action.ExpiresAt <= clock.GetUtcNow() || !_handlers.TryGetValue(action.Kind, out var handler))
            return UseCaseResult<ActionOutcome>.NotFound();
        return await handler.ExecuteAsync(principal, action, ct);
    }

    /// <summary>"Cancel" on the card: drops the prepared action. Only its owner can; nothing else happens.</summary>
    public UseCaseResult<bool> Discard(PrincipalContext principal, Guid id)
    {
        var action = store.Take(id);
        if (action is not null && action.SubjectId != principal.SubjectId)
        {
            store.Put(action); // not yours: put it back untouched
            return UseCaseResult<bool>.NotFound();
        }
        return action is null ? UseCaseResult<bool>.NotFound() : UseCaseResult<bool>.Success(true);
    }
}

/// <summary>Process-local store (single API instance in this demo). Expired entries are pruned on write.</summary>
public sealed class InMemoryPendingActionStore(TimeProvider clock) : IPendingActionStore
{
    private readonly Dictionary<Guid, PendingAction> _items = [];

    public void Put(PendingAction action)
    {
        lock (_items)
        {
            var now = clock.GetUtcNow();
            foreach (var expired in _items.Where(i => i.Value.ExpiresAt <= now).Select(i => i.Key).ToArray())
                _items.Remove(expired);
            _items[action.Id] = action;
        }
    }

    public PendingAction? Take(Guid id)
    {
        lock (_items)
        {
            return _items.Remove(id, out var action) ? action : null;
        }
    }
}
