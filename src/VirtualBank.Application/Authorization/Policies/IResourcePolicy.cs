using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;

namespace VirtualBank.Application.Authorization.Policies;

/// <summary>Outcome of a single policy evaluation.</summary>
public readonly record struct PolicyResult(bool IsAllowed, string ReasonCode)
{
    public static PolicyResult Allow() => new(true, ReasonCodes.Allowed);

    public static PolicyResult Deny(string reasonCode) => new(false, reasonCode);
}

/// <summary>
/// Decides one action on one resource type. Every mandatory condition is conjunctive:
/// a policy allows only when all of its conditions hold.
/// </summary>
public interface IResourcePolicy
{
    string PolicyId { get; }

    string ResourceType { get; }

    Permission Action { get; }

    DisclosureMode Disclosure { get; }

    PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource);
}
