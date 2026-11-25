namespace VirtualBank.Model.Credit;

/// <summary>Outcome of the credit approval business rules for one approver.</summary>
public enum ApprovalCheck
{
    Allowed,
    NotSubmitted,
    FourEyesViolation,
    LimitExceeded,
}
