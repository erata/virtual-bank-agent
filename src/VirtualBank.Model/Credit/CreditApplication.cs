using VirtualBank.Model.Common;

namespace VirtualBank.Model.Credit;

/// <summary>
/// Aggregate root of the credit context. Owns the approval rules (state, four-eyes, approval limit) and the
/// only allowed state transition to <see cref="CreditApplicationStatus.Approved"/>. The authorization policy
/// and the database UPDATE policy enforce the same rules; this class is their single business definition.
/// </summary>
public sealed class CreditApplication
{
    private CreditApplication(
        CreditApplicationId id, string customerId, Money amount, CreditApplicationStatus status, string purpose,
        string preparedBy, string? assignedAnalystId, string? approvedBy, DateTimeOffset? approvedAt, int version)
    {
        Id = id;
        CustomerId = customerId;
        Amount = amount;
        Status = status;
        Purpose = purpose;
        PreparedBy = preparedBy;
        AssignedAnalystId = assignedAnalystId;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        Version = version;
    }

    public CreditApplicationId Id { get; }

    public string CustomerId { get; }

    public Money Amount { get; }

    public CreditApplicationStatus Status { get; private set; }

    public string Purpose { get; }

    public string PreparedBy { get; }

    public string? AssignedAnalystId { get; }

    public string? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Optimistic-concurrency version; incremented by every state change.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Restores an application from persistence, validating its invariants. The same rules exist as CHECK
    /// constraints on credit.credit_applications, so the domain and the database accept the same states.
    /// </summary>
    public static CreditApplication Rehydrate(
        CreditApplicationId id, string customerId, Money amount, CreditApplicationStatus status, string purpose,
        string preparedBy, string? assignedAnalystId, string? approvedBy = null, DateTimeOffset? approvedAt = null, int version = 1)
    {
        ArgumentNullException.ThrowIfNull(id);
        Require(Enum.IsDefined(status), "Unknown credit application status.");
        Require(!string.IsNullOrWhiteSpace(customerId), "An application belongs to a customer.");
        Require(!string.IsNullOrWhiteSpace(purpose), "An application has a purpose.");
        Require(!string.IsNullOrWhiteSpace(preparedBy), "An application has a preparer.");

        // A Money value may be zero in general; a credit application amount may not (CHECK amount > 0).
        Require(amount.Amount > 0, "A credit application amount must be positive.");

        // Approved <=> approver recorded <=> approval timestamp recorded.
        var approved = status == CreditApplicationStatus.Approved;
        Require(approved == !string.IsNullOrWhiteSpace(approvedBy), "An application identifies its approver exactly when it is approved.");
        Require(approved || approvedBy is null, "An unapproved application has no approver, not even a blank one.");
        Require(approved == (approvedAt is not null), "An application has an approval timestamp exactly when it is approved.");

        Require(version >= 1, "The application version must be positive.");

        return new CreditApplication(id, customerId, amount, status, purpose, preparedBy, assignedAnalystId, approvedBy, approvedAt, version);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainRuleViolationException(message);
        }
    }

    public bool IsAssignedTo(string analystId) => string.Equals(AssignedAnalystId, analystId, StringComparison.Ordinal);

    /// <summary>
    /// Evaluates the approval rules in a fixed order: state, four-eyes, limit. A missing limit means
    /// no approval authority (fail closed).
    /// </summary>
    public ApprovalCheck CheckApproval(string approverId, Money? approvalLimit)
    {
        if (Status != CreditApplicationStatus.Submitted)
        {
            return ApprovalCheck.NotSubmitted;
        }

        if (string.Equals(PreparedBy, approverId, StringComparison.Ordinal))
        {
            return ApprovalCheck.FourEyesViolation;
        }

        return approvalLimit is { } limit && limit.Currency == Amount.Currency && Amount.IsAtMost(limit)
            ? ApprovalCheck.Allowed
            : ApprovalCheck.LimitExceeded;
    }

    /// <summary>
    /// The only transition to Approved. Used by the M6 approval executor after explicit human confirmation;
    /// callers must authorize first, but the rules are re-checked here regardless.
    /// </summary>
    public void Approve(string approverId, Money? approvalLimit, DateTimeOffset approvedAt)
    {
        Require(!string.IsNullOrWhiteSpace(approverId), "An approval identifies its approver.");
        var check = CheckApproval(approverId, approvalLimit);
        if (check != ApprovalCheck.Allowed)
        {
            throw new DomainRuleViolationException($"Approval rejected by business rules: {check}.");
        }

        Status = CreditApplicationStatus.Approved;
        ApprovedBy = approverId;
        ApprovedAt = approvedAt;
        Version++;
    }

    /// <summary>
    /// The only transition to Rejected. A rejection is a credit decision too, so it needs the same authority as an
    /// approval (submitted state, four-eyes, within the decider's limit). The decider is recorded in the decision
    /// log (credit.approval_requests), not in approved_by, which by invariant only exists for approved applications.
    /// </summary>
    public void Reject(string deciderId, Money? decisionLimit)
    {
        Require(!string.IsNullOrWhiteSpace(deciderId), "A rejection identifies its decider.");
        var check = CheckApproval(deciderId, decisionLimit);
        if (check != ApprovalCheck.Allowed)
        {
            throw new DomainRuleViolationException($"Rejection refused by business rules: {check}.");
        }

        Status = CreditApplicationStatus.Rejected;
        Version++;
    }
}
