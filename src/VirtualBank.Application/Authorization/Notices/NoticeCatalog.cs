using System.Collections.Frozen;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;

namespace VirtualBank.Application.Authorization.Notices;

/// <summary>A deterministic, user-facing authorization notice. Text is localized separately from the code.</summary>
public sealed record AuthorizationNotice(string Code, DisclosureMode Level);

/// <summary>Maps one (action, internal reason) pair to a user-facing notice code.</summary>
public sealed record NoticeRule(Permission Action, string ReasonCode, string NoticeCode);

/// <summary>
/// Derives notice codes from (action, reason code, disclosure mode). Each bounded context contributes its
/// own rules; the mechanism is shared. Notices are produced by application code only, never by the model.
/// Disclosure <see cref="DisclosureMode.None"/> yields no notice, so the resource's existence stays hidden.
/// </summary>
public sealed class NoticeCatalog
{
    public const string GenericPartialResult = "REQUEST_PARTIALLY_UNAVAILABLE";

    private readonly FrozenDictionary<(string Action, string Reason), string> _hints;

    public NoticeCatalog(IEnumerable<NoticeRule> rules)
    {
        var map = new Dictionary<(string, string), string>();
        foreach (var rule in rules)
        {
            if (!map.TryAdd((rule.Action.Id, rule.ReasonCode), rule.NoticeCode))
            {
                throw new InvalidOperationException($"Duplicate notice rule for '{rule.Action}' / '{rule.ReasonCode}'.");
            }
        }

        _hints = map.ToFrozenDictionary();
    }

    /// <summary>
    /// Notice code for a denial that happens before a resource is loaded (e.g. an endpoint-level permission
    /// check). Null when no specific hint is defined.
    /// </summary>
    public string? HintCodeFor(Permission action, string reasonCode) =>
        _hints.TryGetValue((action.Id, reasonCode), out var code) ? code : null;

    public AuthorizationNotice? FromDecision(AuthorizationDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.IsAllowed)
        {
            return null;
        }

        return decision.Disclosure switch
        {
            DisclosureMode.None => null,
            DisclosureMode.PermissionHint when _hints.TryGetValue((decision.Action.Id, decision.ReasonCode), out var code)
                => new AuthorizationNotice(code, DisclosureMode.PermissionHint),
            // Hint requested but no specific code defined: degrade safely to the generic notice.
            _ => new AuthorizationNotice(GenericPartialResult, DisclosureMode.Generic),
        };
    }
}
