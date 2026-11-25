using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework;

/// <summary>Code-first schema ownership. Runtime repositories retain RLS-aware Npgsql sessions.</summary>
public sealed class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options)
{
    public DbSet<CustomerRow> Customers => Set<CustomerRow>();
    public DbSet<AccountRow> Accounts => Set<AccountRow>();
    public DbSet<CreditAssignmentRow> CreditAssignments => Set<CreditAssignmentRow>();
    public DbSet<ScheduledPaymentRow> ScheduledPayments => Set<ScheduledPaymentRow>();
    public DbSet<AccountTransactionRow> Transactions => Set<AccountTransactionRow>();
    public DbSet<StandingOrderRow> StandingOrders => Set<StandingOrderRow>();

    protected override void OnModelCreating(ModelBuilder mb) => ConfigureModel(mb);

    internal static void ConfigureModel(ModelBuilder mb)
    {
        mb.Entity<CustomerRow>(e =>
        {
            e.ToTable("customers", "customer", t => {
                t.HasCheckConstraint("customers_id_format", "id ~ '^C-[0-9]{4}$'");
                t.HasCheckConstraint("customers_segment_allowed", "segment IN ('private','business')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Branch).HasColumnName("branch").IsRequired();
            e.Property(x => x.FirstName).HasColumnName("first_name").IsRequired();
            e.Property(x => x.LastName).HasColumnName("last_name").IsRequired();
            e.Property(x => x.Email).HasColumnName("email").IsRequired();
            e.Property(x => x.Phone).HasColumnName("phone").IsRequired();
            e.Property(x => x.Iban).HasColumnName("iban").IsRequired();
            e.Property(x => x.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date");
            e.Property(x => x.Segment).HasColumnName("segment").IsRequired();
            e.Property(x => x.IdentitySubjectId).HasColumnName("identity_subject_id");
            e.HasIndex(x => x.IdentitySubjectId).IsUnique().HasDatabaseName("customers_identity_subject_unique");
        });
        mb.Entity<AccountRow>(e =>
        {
            e.ToTable("accounts", "customer", t => t.HasCheckConstraint("accounts_currency_iso", "currency ~ '^[A-Z]{3}$'"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.Type).HasColumnName("type").IsRequired();
            e.Property(x => x.Currency).HasColumnName("currency").HasColumnType("character(3)");
            e.Property(x => x.Balance).HasColumnName("balance").HasPrecision(14, 2);
            e.HasOne(x => x.Customer).WithMany(x => x.Accounts).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CustomerId).HasDatabaseName("accounts_customer_id_idx");
        });
        mb.Entity<CreditAssignmentRow>(e =>
        {
            e.ToTable("credit_assignments", "customer");
            e.HasKey(x => new { x.CustomerId, x.AnalystId });
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.AnalystId).HasColumnName("analyst_id");
            e.HasOne(x => x.Customer).WithMany(x => x.CreditAssignments).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AnalystId, x.CustomerId }).HasDatabaseName("credit_assignments_analyst_idx");
        });
        mb.Entity<ScheduledPaymentRow>(e =>
        {
            e.ToTable("scheduled_payments", "customer", t => t.HasCheckConstraint("scheduled_payments_status_check", "status IN ('planned','completed','cancelled')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.Description).HasColumnName("description").IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(14,2);
            e.Property(x => x.Currency).HasColumnName("currency").HasColumnType("character(3)");
            e.Property(x => x.DueDate).HasColumnName("due_date");
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.HasOne(x => x.Customer).WithMany(x => x.ScheduledPayments).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CustomerId, x.DueDate }).HasDatabaseName("scheduled_payments_customer_due");
        });
        mb.Entity<AccountTransactionRow>(e =>
        {
            e.ToTable("account_transactions", "customer");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.Description).HasColumnName("description").IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(14,2);
            e.Property(x => x.Currency).HasColumnName("currency").HasColumnType("character(3)");
            e.Property(x => x.BookedOn).HasColumnName("booked_on");
            e.HasOne(x => x.Customer).WithMany(x => x.Transactions).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CustomerId, x.BookedOn }).HasDatabaseName("account_transactions_customer_date");
        });
        mb.Entity<StandingOrderRow>(e =>
        {
            e.ToTable("standing_orders", "customer");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.Description).HasColumnName("description").IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(14,2);
            e.Property(x => x.Currency).HasColumnName("currency").HasColumnType("character(3)");
            e.Property(x => x.Frequency).HasColumnName("frequency").IsRequired();
            e.Property(x => x.NextDueDate).HasColumnName("next_due_date");
            e.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true);
            e.HasOne(x => x.Customer).WithMany(x => x.StandingOrders).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CustomerId, x.NextDueDate }).HasDatabaseName("standing_orders_customer_due");
        });
    }
}

public sealed class CreditDbContext(DbContextOptions<CreditDbContext> options) : DbContext(options)
{
    public DbSet<CreditApplicationRow> CreditApplications => Set<CreditApplicationRow>();
    public DbSet<CreditNoteRow> CreditNotes => Set<CreditNoteRow>();
    public DbSet<ApprovalRequestRow> ApprovalRequests => Set<ApprovalRequestRow>();

    protected override void OnModelCreating(ModelBuilder mb) => ConfigureModel(mb);

    internal static void ConfigureModel(ModelBuilder mb)
    {
        mb.Entity<CreditApplicationRow>(e =>
        {
            e.ToTable("credit_applications", "credit", t => {
                t.HasCheckConstraint("credit_id_format", "id ~ '^K-[0-9]{3}$'");
                t.HasCheckConstraint("credit_amount_positive", "amount > 0");
                t.HasCheckConstraint("credit_currency_iso", "currency ~ '^[A-Z]{3}$'");
                t.HasCheckConstraint("credit_status_allowed", "status IN ('DRAFT','SUBMITTED','APPROVED','REJECTED')");
                t.HasCheckConstraint("credit_purpose_nonempty", "btrim(purpose) <> ''");
                t.HasCheckConstraint("credit_preparer_nonempty", "btrim(prepared_by) <> ''");
                t.HasCheckConstraint("credit_version_positive", "version >= 1");
                t.HasCheckConstraint("credit_applications_approver_iff_approved", "(status = 'APPROVED') = (approved_by IS NOT NULL AND btrim(approved_by) <> '')");
                t.HasCheckConstraint("credit_applications_no_blank_approver", "approved_by IS NULL OR btrim(approved_by) <> ''");
                t.HasCheckConstraint("credit_applications_approved_at_iff_approved", "(status = 'APPROVED') = (approved_at IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(14,2);
            e.Property(x => x.Currency).HasColumnName("currency").HasColumnType("character(3)");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Purpose).HasColumnName("purpose");
            e.Property(x => x.PreparedBy).HasColumnName("prepared_by");
            e.Property(x => x.AssignedAnalystId).HasColumnName("assigned_analyst_id");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
            e.HasIndex(x => x.AssignedAnalystId).HasDatabaseName("credit_applications_analyst_idx");
        });
        mb.Entity<CreditNoteRow>(e =>
        {
            e.ToTable("credit_notes", "credit", t => t.HasCheckConstraint("credit_note_length", "length(body) BETWEEN 1 AND 4000"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.ApplicationId).HasColumnName("application_id");
            e.Property(x => x.AuthorId).HasColumnName("author_id");
            e.Property(x => x.Body).HasColumnName("body");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne(x => x.Application).WithMany(x => x.Notes).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<ApprovalRequestRow>(e =>
        {
            e.ToTable("approval_requests", "credit");
            e.HasKey(x => x.IdempotencyKey);
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
            e.Property(x => x.ApplicationId).HasColumnName("application_id");
            e.Property(x => x.RequestedBy).HasColumnName("requested_by");
            e.Property(x => x.Outcome).HasColumnName("outcome");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne(x => x.Application).WithMany(x => x.ApprovalRequests).HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<DocumentRow> Documents => Set<DocumentRow>();
    public DbSet<DocumentChunkRow> Chunks => Set<DocumentChunkRow>();
    public DbSet<DocumentAclRow> Acls => Set<DocumentAclRow>();
    public DbSet<DocumentAclHistoryRow> AclHistory => Set<DocumentAclHistoryRow>();
    protected override void OnModelCreating(ModelBuilder mb) => ConfigureModel(mb);

    internal static void ConfigureModel(ModelBuilder mb)
    {
        mb.HasPostgresExtension("vector");
        mb.Entity<DocumentRow>(e =>
        {
            e.ToTable("documents", "docs", t => {
                t.HasCheckConstraint("docs_id_format", "id ~ '^DOC-[0-9]{3}$'");
                t.HasCheckConstraint("docs_title_nonempty", "length(trim(title)) > 0");
                t.HasCheckConstraint("docs_language", "language IN ('en','de','tr')");
                t.HasCheckConstraint("docs_classification", "classification BETWEEN 0 AND 3");
                t.HasCheckConstraint("docs_content_version", "content_version > 0");
                t.HasCheckConstraint("docs_acl_version", "acl_version > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.SourcePath).HasColumnName("source_path");
            e.Property(x => x.Language).HasColumnName("language");
            e.Property(x => x.Classification).HasColumnName("classification").HasColumnType("smallint");
            e.Property(x => x.Branch).HasColumnName("branch");
            e.Property(x => x.ContentHash).HasColumnName("content_hash");
            e.Property(x => x.EmbeddingModel).HasColumnName("embedding_model");
            e.Property(x => x.ContentVersion).HasColumnName("content_version").HasDefaultValue(1);
            e.Property(x => x.AclVersion).HasColumnName("acl_version").HasDefaultValue(1);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        });
        mb.Entity<DocumentChunkRow>(e =>
        {
            e.ToTable("chunks", "docs", t => {
                t.HasCheckConstraint("docs_chunk_index", "chunk_index >= 0");
                t.HasCheckConstraint("docs_chunk_nonempty", "length(content) > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DocumentId).HasColumnName("document_id");
            e.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            e.Property(x => x.Content).HasColumnName("content");
            e.Property(x => x.PageNumber).HasColumnName("page_number");
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1024)");
            e.HasOne(x => x.Document).WithMany(x => x.Chunks).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
            e.HasIndex(x => x.Embedding).HasDatabaseName("chunks_embedding_hnsw").HasMethod("hnsw")
                .HasOperators("vector_cosine_ops").HasStorageParameter("m", 16).HasStorageParameter("ef_construction", 64);
        });
        mb.Entity<DocumentAclRow>(e =>
        {
            e.ToTable("acls", "docs", t => {
                t.HasCheckConstraint("docs_acl_kind", "subject_kind IN ('public','user','group','role')");
                t.HasCheckConstraint("docs_acl_effect", "effect IN ('allow','deny')");
            });
            e.HasKey(x => new { x.DocumentId, x.SubjectKind, x.SubjectValue, x.Effect });
            e.Property(x => x.DocumentId).HasColumnName("document_id");
            e.Property(x => x.SubjectKind).HasColumnName("subject_kind");
            e.Property(x => x.SubjectValue).HasColumnName("subject_value");
            e.Property(x => x.Effect).HasColumnName("effect");
            e.HasOne(x => x.Document).WithMany(x => x.Acls).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<DocumentAclHistoryRow>(e =>
        {
            e.ToTable("acl_history", "docs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.DocumentId).HasColumnName("document_id");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.ChangedBy).HasColumnName("changed_by");
            e.Property(x => x.ChangedAt).HasColumnName("changed_at").HasDefaultValueSql("now()");
            e.Property(x => x.ChangesJson).HasColumnName("changes_json").HasColumnType("jsonb");
            e.HasOne(x => x.Document).WithMany(x => x.AclHistory).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
