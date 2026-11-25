using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;

namespace VirtualBank.Application.Authorization;

/// <summary>Answers: can this principal perform this action on this resource under these conditions?</summary>
public interface IResourceAuthorizationService
{
    Task<AuthorizationDecision> AuthorizeAsync(
        PrincipalContext principal,
        ResourceDescriptor resource,
        Permission action,
        CancellationToken cancellationToken);
}
