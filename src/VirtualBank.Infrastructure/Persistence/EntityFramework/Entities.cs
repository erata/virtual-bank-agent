using Pgvector;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework;

// EF persistence models: the Domain (VirtualBank.Model) stays free of ORM concerns.
public sealed class CustomerRow
{
    public string Id { get; set; } = "";
    public string Branch { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Iban { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public string Segment { get; set; } = "";
    public string? IdentitySubjectId { get; set; }
    public ICollection<AccountRow> Accounts { get; } = new List<AccountRow>();
    public ICollection<CreditAssignmentRow> CreditAssignments { get; } = new List<CreditAssignmentRow>();
    public ICollection<ScheduledPaymentRow> ScheduledPayments { get; } = new List<ScheduledPaymentRow>();
    public ICollection<AccountTransactionRow> Transactions { get; } = new List<AccountTransactionRow>();
    public ICollection<StandingOrderRow> StandingOrders { get; } = new List<StandingOrderRow>();
}
public sealed class AccountRow
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Balance { get; set; }
    public CustomerRow Customer { get; set; } = null!;
}
public sealed class CreditAssignmentRow
{
    public string CustomerId { get; set; } = "";
    public string AnalystId { get; set; } = "";
    public CustomerRow Customer { get; set; } = null!;
}
public sealed class ScheduledPaymentRow
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = "";
    public CustomerRow Customer { get; set; } = null!;
}
public sealed class AccountTransactionRow
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateOnly BookedOn { get; set; }
    public CustomerRow Customer { get; set; } = null!;
}
public sealed class StandingOrderRow
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Frequency { get; set; } = "";
    public DateOnly NextDueDate { get; set; }
    public bool Active { get; set; } = true;
    public CustomerRow Customer { get; set; } = null!;
}
public sealed class CreditApplicationRow
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string PreparedBy { get; set; } = "";
    public string? AssignedAnalystId { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public int Version { get; set; } = 1;
    public ICollection<CreditNoteRow> Notes { get; } = new List<CreditNoteRow>();
    public ICollection<ApprovalRequestRow> ApprovalRequests { get; } = new List<ApprovalRequestRow>();
}
public sealed class CreditNoteRow
{
    public long Id { get; set; }
    public string ApplicationId { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public CreditApplicationRow Application { get; set; } = null!;
}
public sealed class ApprovalRequestRow
{
    public Guid IdempotencyKey { get; set; }
    public string ApplicationId { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public string Outcome { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public CreditApplicationRow Application { get; set; } = null!;
}
public sealed class DocumentRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Language { get; set; } = "";
    public short Classification { get; set; }
    public string? Branch { get; set; }
    public string ContentHash { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
    public int ContentVersion { get; set; } = 1;
    public int AclVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<DocumentChunkRow> Chunks { get; } = new List<DocumentChunkRow>();
    public ICollection<DocumentAclRow> Acls { get; } = new List<DocumentAclRow>();
    public ICollection<DocumentAclHistoryRow> AclHistory { get; } = new List<DocumentAclHistoryRow>();
}
public sealed class DocumentChunkRow
{
    public string Id { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = "";
    public int? PageNumber { get; set; }
    public Vector Embedding { get; set; } = null!;
    public DocumentRow Document { get; set; } = null!;
}
public sealed class DocumentAclRow
{
    public string DocumentId { get; set; } = "";
    public string SubjectKind { get; set; } = "";
    public string SubjectValue { get; set; } = "";
    public string Effect { get; set; } = "";
    public DocumentRow Document { get; set; } = null!;
}
public sealed class DocumentAclHistoryRow
{
    public long Id { get; set; }
    public string DocumentId { get; set; } = "";
    public int Version { get; set; }
    public string ChangedBy { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }
    public string ChangesJson { get; set; } = "{}";
    public DocumentRow Document { get; set; } = null!;
}
