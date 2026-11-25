using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

[DbContext(typeof(CreditDbContext))]
[Migration("20261123020000_InitialCreditSchema")]
public sealed class InitialCreditSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("credit");
        migrationBuilder.EnsureSchema("authz");
        // Entity configuration is authoritative for relational DDL; SQL here is only for RLS/functions/grants.
        migrationBuilder.Sql("""
CREATE FUNCTION authz.principal_id() RETURNS text
    LANGUAGE sql STABLE PARALLEL SAFE
    AS $$ SELECT nullif(current_setting('app.principal_id', true), '') $$;

CREATE FUNCTION authz.has_permission(permission text) RETURNS boolean
    LANGUAGE sql STABLE PARALLEL SAFE
    AS $$
        SELECT coalesce(
            permission = ANY (string_to_array(nullif(current_setting('app.permissions', true), ''), ',')),
            false)
    $$;

CREATE FUNCTION authz.approval_limit() RETURNS numeric
    LANGUAGE sql STABLE PARALLEL SAFE
    AS $$ SELECT nullif(current_setting('app.approval_limit', true), '')::numeric $$;


""");
        migrationBuilder.CreateTable(
            name: "credit_applications", schema: "credit",
            columns: table => new
            {
            id = table.Column<string>(type: "text", nullable: false),
            customer_id = table.Column<string>(type: "text", nullable: false),
            amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
            currency = table.Column<string>(type: "character(3)", nullable: false),
            status = table.Column<string>(type: "text", nullable: false),
            purpose = table.Column<string>(type: "text", nullable: false),
            prepared_by = table.Column<string>(type: "text", nullable: false),
            assigned_analyst_id = table.Column<string?>(type: "text", nullable: true),
            approved_by = table.Column<string?>(type: "text", nullable: true),
            approved_at = table.Column<DateTimeOffset?>(type: "timestamp with time zone", nullable: true),
            version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_credit_credit_applications", x => x.id);
                table.CheckConstraint("credit_id_format", "id ~ '^K-[0-9]{3}$'");
                table.CheckConstraint("credit_amount_positive", "amount > 0");
                table.CheckConstraint("credit_currency_iso", "currency ~ '^[A-Z]{3}$'");
                table.CheckConstraint("credit_status_allowed", "status IN ('DRAFT','SUBMITTED','APPROVED','REJECTED')");
                table.CheckConstraint("credit_purpose_nonempty", "btrim(purpose) <> ''");
                table.CheckConstraint("credit_preparer_nonempty", "btrim(prepared_by) <> ''");
                table.CheckConstraint("credit_version_positive", "version >= 1");
                table.CheckConstraint("credit_applications_approver_iff_approved", "(status = 'APPROVED') = (approved_by IS NOT NULL AND btrim(approved_by) <> '')");
                table.CheckConstraint("credit_applications_no_blank_approver", "approved_by IS NULL OR btrim(approved_by) <> ''");
                table.CheckConstraint("credit_applications_approved_at_iff_approved", "(status = 'APPROVED') = (approved_at IS NOT NULL)");
            });
        migrationBuilder.CreateTable(
            name: "credit_notes", schema: "credit",
            columns: table => new
            {
            id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
            application_id = table.Column<string>(type: "text", nullable: false),
            author_id = table.Column<string>(type: "text", nullable: false),
            body = table.Column<string>(type: "text", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_credit_credit_notes", x => x.id);
                table.ForeignKey("FK_credit_credit_notes_credit_credit_applications", x => x.application_id, "credit_applications", "id", "credit", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("credit_note_length", "length(body) BETWEEN 1 AND 4000");
            });
        migrationBuilder.CreateTable(
            name: "approval_requests", schema: "credit",
            columns: table => new
            {
            idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
            application_id = table.Column<string>(type: "text", nullable: false),
            requested_by = table.Column<string>(type: "text", nullable: false),
            outcome = table.Column<string>(type: "text", nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_credit_approval_requests", x => x.idempotency_key);
                table.ForeignKey("FK_credit_approval_requests_credit_credit_applications", x => x.application_id, "credit_applications", "id", "credit", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("credit_applications_analyst_idx", "credit_applications", "assigned_analyst_id", schema: "credit");
        migrationBuilder.CreateIndex("IX_credit_notes_application_id", "credit_notes", "application_id", schema: "credit");
        migrationBuilder.CreateIndex("IX_approval_requests_application_id", "approval_requests", "application_id", schema: "credit");

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Development-only rollback. Never drop production schema without an explicit migration review.
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS credit CASCADE;");
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS authz CASCADE;");
    }
}
