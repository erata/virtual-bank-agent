using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;

namespace VirtualBank.Application.Authorization.Policies;

/// <summary>Policy for capabilities whose only condition is holding a specific permission.</summary>
public sealed class PermissionPolicy(
    string policyId,
    string resourceType,
    Permission action,
    DisclosureMode disclosure) : IResourcePolicy
{
    public string PolicyId { get; } = policyId;

    public string ResourceType { get; } = resourceType;

    public Permission Action { get; } = action;

    public DisclosureMode Disclosure { get; } = disclosure;

    public PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource) =>
        principal.HasPermission(Action) ? PolicyResult.Allow() : PolicyResult.Deny(ReasonCodes.MissingPermission);
}
