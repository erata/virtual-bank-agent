using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;
using VirtualBank.Model.Credit;

namespace VirtualBank.Application.Credit.Authorization;

/// <summary>Policies and notice rules of the credit context. Mirrored by the Credit RLS policies in credit_db.</summary>
public static class CreditPolicies
{
    public static IReadOnlyList<IResourcePolicy> Create() =>
        [new CreditApplicationReadPolicy(), new CreditNoteWritePolicy(), new CreditApprovePolicy()];

    public static IReadOnlyList<NoticeRule> NoticeRules { get; } =
    [
        new(KnownPermissions.CreditRead, ReasonCodes.MissingPermission, "CREDIT_READ_REQUIRED"),
        new(KnownPermissions.CreditRead, ReasonCodes.ResourceNotAssigned, "CREDIT_APPLICATION_NOT_ASSIGNED"),
        new(KnownPermissions.CreditNoteWrite, ReasonCodes.MissingPermission, "CREDIT_NOTE_PERMISSION_REQUIRED"),
        new(KnownPermissions.CreditNoteWrite, ReasonCodes.ResourceNotAssigned, "CREDIT_APPLICATION_NOT_ASSIGNED"),
        new(KnownPermissions.CreditApprove, ReasonCodes.MissingPermission, "CREDIT_APPROVE_REQUIRED"),
        new(KnownPermissions.CreditApprove, ReasonCodes.ApprovalLimitExceeded, "CREDIT_APPROVAL_LIMIT_EXCEEDED"),
        new(KnownPermissions.CreditApprove, ReasonCodes.FourEyesViolation, "CREDIT_FOUR_EYES_REQUIRED"),
        new(KnownPermissions.CreditApprove, ReasonCodes.InvalidResourceState, "CREDIT_APPLICATION_NOT_APPROVABLE"),
    ];
}

/// <summary>
/// Analysts read applications assigned to them; approvers read submitted applications and those they approved;
/// bank customers (credit.request) read only applications they prepared themselves.
/// </summary>
public sealed class CreditApplicationReadPolicy : IResourcePolicy
{
    public string PolicyId => "credit.application.read.v1";

    public string ResourceType => CreditApplicationResource.ResourceType;

    public Permission Action => KnownPermissions.CreditRead;

    public DisclosureMode Disclosure => DisclosureMode.PermissionHint;

    public PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource)
    {
        if (resource is not CreditApplicationResource { Application: var application })
        {
            return PolicyResult.Deny(ReasonCodes.ResourceTypeMismatch);
        }

        var analyst = principal.HasPermission(KnownPermissions.CreditRead);
        var approver = principal.HasPermission(KnownPermissions.CreditApprove);

        if (analyst && application.IsAssignedTo(principal.SubjectId))
        {
            return PolicyResult.Allow();
        }

        if (approver && (application.Status == CreditApplicationStatus.Submitted ||
                         string.Equals(application.ApprovedBy, principal.SubjectId, StringComparison.Ordinal)))
        {
            return PolicyResult.Allow();
        }

        var requester = principal.HasPermission(KnownPermissions.CreditRequest);
        if (requester && string.Equals(application.PreparedBy, principal.SubjectId, StringComparison.Ordinal))
        {
            return PolicyResult.Allow();
        }

        return analyst || approver || requester
            ? PolicyResult.Deny(ReasonCodes.ResourceNotAssigned)
            : PolicyResult.Deny(ReasonCodes.MissingPermission);
    }
}

/// <summary>Notes may be written by holders of credit.note.write on applications assigned to them.</summary>
public sealed class CreditNoteWritePolicy : IResourcePolicy
{
    public string PolicyId => "credit.note.write.v1";

    public string ResourceType => CreditApplicationResource.ResourceType;

    public Permission Action => KnownPermissions.CreditNoteWrite;

    public DisclosureMode Disclosure => DisclosureMode.PermissionHint;

    public PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource)
    {
        if (resource is not CreditApplicationResource { Application: var application })
        {
            return PolicyResult.Deny(ReasonCodes.ResourceTypeMismatch);
        }

        if (!principal.HasPermission(KnownPermissions.CreditNoteWrite))
        {
            return PolicyResult.Deny(ReasonCodes.MissingPermission);
        }

        return application.IsAssignedTo(principal.SubjectId) ? PolicyResult.Allow() : PolicyResult.Deny(ReasonCodes.ResourceNotAssigned);
    }
}

/// <summary>
/// Approval authority: the permission plus the domain approval rules (<see cref="CreditApplication.CheckApproval"/>).
/// The rules are defined once in the domain and translated into reason codes here.
/// Explicit human confirmation is a separate workflow step (M6).
/// </summary>
public sealed class CreditApprovePolicy : IResourcePolicy
{
    public string PolicyId => "credit.application.approve.v1";

    public string ResourceType => CreditApplicationResource.ResourceType;

    public Permission Action => KnownPermissions.CreditApprove;

    public DisclosureMode Disclosure => DisclosureMode.PermissionHint;

    public PolicyResult Evaluate(PrincipalContext principal, ResourceDescriptor resource)
    {
        if (resource is not CreditApplicationResource { Application: var application })
        {
            return PolicyResult.Deny(ReasonCodes.ResourceTypeMismatch);
        }

        if (!principal.HasPermission(KnownPermissions.CreditApprove))
        {
            return PolicyResult.Deny(ReasonCodes.MissingPermission);
        }

        // The approval limit attribute is defined in EUR.
        var limit = principal.ApprovalLimit is { } value ? Money.Eur(value) : (Money?)null;
        return application.CheckApproval(principal.SubjectId, limit) switch
        {
            ApprovalCheck.Allowed => PolicyResult.Allow(),
            ApprovalCheck.NotSubmitted => PolicyResult.Deny(ReasonCodes.InvalidResourceState),
            ApprovalCheck.FourEyesViolation => PolicyResult.Deny(ReasonCodes.FourEyesViolation),
            _ => PolicyResult.Deny(ReasonCodes.ApprovalLimitExceeded),
        };
    }
}
