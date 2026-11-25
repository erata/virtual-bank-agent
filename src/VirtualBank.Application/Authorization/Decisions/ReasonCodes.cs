namespace VirtualBank.Application.Authorization.Decisions;

/// <summary>
/// Stable, language-independent internal reason codes. They are written to audit records and
/// mapped to user-facing notice codes by <see cref="Notices.NoticeCatalog"/>; they are never shown verbatim.
/// </summary>
public static class ReasonCodes
{
    public const string Allowed = "ALLOWED";
    public const string NoPolicy = "NO_POLICY";
    public const string PrincipalInactive = "PRINCIPAL_INACTIVE";
    public const string MissingPermission = "MISSING_PERMISSION";
    public const string BranchMismatch = "BRANCH_MISMATCH";
    public const string ResourceNotAssigned = "RESOURCE_NOT_ASSIGNED";
    public const string InvalidResourceState = "INVALID_RESOURCE_STATE";
    public const string ApprovalLimitExceeded = "APPROVAL_LIMIT_EXCEEDED";
    public const string FourEyesViolation = "FOUR_EYES_VIOLATION";
    public const string ResourceTypeMismatch = "RESOURCE_TYPE_MISMATCH";
    public const string DocumentAclDenied = "DOCUMENT_ACL_DENIED";
    public const string InsufficientClearance = "INSUFFICIENT_CLEARANCE";
    public const string AclStale = "ACL_STALE";
    public const string TokenAudienceInvalid = "TOKEN_AUDIENCE_INVALID";
    public const string PrincipalTypeNotAllowed = "PRINCIPAL_TYPE_NOT_ALLOWED";
}
