using System.Collections.Frozen;
using System.Diagnostics;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;

namespace VirtualBank.Application.Authorization;

/// <summary>
/// Deny-by-default policy dispatcher. Exactly one policy may exist per (resource type, action);
/// a missing policy denies with <see cref="ReasonCodes.NoPolicy"/>.
/// </summary>
public sealed class ResourceAuthorizationService : IResourceAuthorizationService
{
    private const string DefaultPolicyId = "default-deny";

    private readonly FrozenDictionary<(string ResourceType, Permission Action), IResourcePolicy> _policies;

    public ResourceAuthorizationService(IEnumerable<IResourcePolicy> policies)
    {
        var map = new Dictionary<(string, Permission), IResourcePolicy>();
        foreach (var policy in policies)
        {
            if (!map.TryAdd((policy.ResourceType, policy.Action), policy))
            {
                throw new InvalidOperationException(
                    $"Duplicate policy for resource type '{policy.ResourceType}' and action '{policy.Action}'.");
            }
        }

        _policies = map.ToFrozenDictionary();
    }

    public Task<AuthorizationDecision> AuthorizeAsync(
        PrincipalContext principal,
        ResourceDescriptor resource,
        Permission action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Authorize(principal, resource, action));
    }

    public AuthorizationDecision Authorize(PrincipalContext principal, ResourceDescriptor resource, Permission action)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resource);

        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        if (!_policies.TryGetValue((resource.Type, action), out var policy))
        {
            return Decision(false, ReasonCodes.NoPolicy, DefaultPolicyId, DisclosureMode.None);
        }

        if (!principal.IsActive)
        {
            return Decision(false, ReasonCodes.PrincipalInactive, policy.PolicyId, DisclosureMode.Generic);
        }

        var result = policy.Evaluate(principal, resource);
        return Decision(result.IsAllowed, result.ReasonCode, policy.PolicyId, policy.Disclosure);

        AuthorizationDecision Decision(bool allowed, string reason, string policyId, DisclosureMode disclosure) => new()
        {
            IsAllowed = allowed,
            ReasonCode = reason,
            Action = action,
            ResourceType = resource.Type,
            ResourceId = resource.Id,
            PolicyId = policyId,
            CorrelationId = correlationId,
            Disclosure = disclosure,
        };
    }
}
