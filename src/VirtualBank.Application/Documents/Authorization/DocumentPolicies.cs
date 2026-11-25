using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;
using VirtualBank.Model.Documents;

namespace VirtualBank.Application.Documents.Authorization;

/// <summary>Metadata comes from the document repository, never from an HTTP/model-supplied ACL.</summary>
public sealed record DocumentResource(string DocumentId, int Classification, string? Branch,
    IReadOnlyList<DocumentAclEntry> Entries) : ResourceDescriptor("document", DocumentId);

/// <summary>Independent application-level authorization, in addition to database RLS.</summary>
public sealed class DocumentReadPolicy : IResourcePolicy
{
    public string PolicyId => "document.read.v1";
    public string ResourceType => "document";
    public Permission Action => KnownPermissions.DocumentRead;
    public DisclosureMode Disclosure => DisclosureMode.None;

    public PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource)
    {
        if (!principal.HasPermission(Action) || principal.Type == PrincipalType.Machine)
            return PolicyResult.Deny(ReasonCodes.MissingPermission);
        if (resource is not DocumentResource document)
            return PolicyResult.Deny(ReasonCodes.ResourceTypeMismatch);
        if (document.Classification is < 0 or > 3 || document.Classification > (int)principal.Clearance)
            return PolicyResult.Deny(ReasonCodes.InsufficientClearance);
        if (document.Branch is not null && !string.Equals(document.Branch, principal.Branch, StringComparison.Ordinal))
            return PolicyResult.Deny(ReasonCodes.BranchMismatch);
        if (document.Entries.Any(entry => entry.Effect == "deny" && Matches(entry, principal)))
            return PolicyResult.Deny(ReasonCodes.DocumentAclDenied);
        return document.Entries.Any(entry => entry.Effect == "allow" && Matches(entry, principal))
            ? PolicyResult.Allow()
            : PolicyResult.Deny(ReasonCodes.DocumentAclDenied);
    }

    private static bool Matches(DocumentAclEntry acl, PrincipalContext principal) => acl.SubjectKind switch
    {
        "public" => acl.SubjectValue == "*",
        "user" => string.Equals(acl.SubjectValue, principal.SubjectId, StringComparison.Ordinal),
        "role" => principal.Roles.Contains(acl.SubjectValue),
        // PrincipalContext.Groups is expanded from verified Keycloak paths, segment-aware.
        "group" => GroupPath.TryParse(acl.SubjectValue, out var group) && principal.IsMemberOf(group!),
        _ => false,
    };
}

public static class DocumentPolicies
{
    public static IReadOnlyList<IResourcePolicy> Create() => [
        new DocumentReadPolicy(),
        new PermissionPolicy("document.acl.manage.v1", "document", KnownPermissions.DocumentAclManage, DisclosureMode.Generic),
        new PermissionPolicy("document.ingest.v1", "document", KnownPermissions.DocumentIngest, DisclosureMode.Generic),
    ];
}
