using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;

namespace VirtualBank.Application.Assistant.Authorization;

/// <summary>The assistant as a capability resource ("assistant:chat").</summary>
public static class AssistantResource
{
    public const string ResourceType = "assistant";

    public static NamedResource Chat { get; } = new(ResourceType, "chat");
}

/// <summary>Policies and notice rules of the assistant module.</summary>
public static class AssistantPolicies
{
    public static IReadOnlyList<IResourcePolicy> Create() => [new AssistantUsePolicy()];

    public static IReadOnlyList<NoticeRule> NoticeRules { get; } =
    [
        new(KnownPermissions.AssistantUse, ReasonCodes.MissingPermission, AssistantNoticeCodes.AssistantUseRequired),
        new(KnownPermissions.AssistantUse, ReasonCodes.PrincipalTypeNotAllowed, AssistantNoticeCodes.MachinePrincipalNotAllowed),
    ];
}

/// <summary>
/// Using the assistant requires <c>assistant.use</c> and a human principal. Service accounts are denied even if a
/// role mapping error granted them the permission: an agent acting for a machine has no user whose rights it can bound.
/// </summary>
public sealed class AssistantUsePolicy : IResourcePolicy
{
    public string PolicyId => "assistant.use.v1";

    public string ResourceType => AssistantResource.ResourceType;

    public Permission Action => KnownPermissions.AssistantUse;

    public DisclosureMode Disclosure => DisclosureMode.PermissionHint;

    public PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource)
    {
        if (principal.Type == PrincipalType.Machine)
        {
            return PolicyResult.Deny(ReasonCodes.PrincipalTypeNotAllowed);
        }

        return principal.HasPermission(Action) ? PolicyResult.Allow() : PolicyResult.Deny(ReasonCodes.MissingPermission);
    }
}
