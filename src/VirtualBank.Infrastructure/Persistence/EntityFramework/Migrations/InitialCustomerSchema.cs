using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

[DbContext(typeof(CustomerDbContext))]
[Migration("20261123010000_InitialCustomerSchema")]
public sealed class InitialCustomerSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("customer");
        migrationBuilder.EnsureSchema("authz");
        // Entity configuration is authoritative for relational DDL; SQL here is only for RLS/functions/grants.
        migrationBuilder.Sql("""
CREATE FUNCTION authz.principal_id() RETURNS text
    LANGUAGE sql STABLE PARALLEL SAFE
    AS $$ SELECT nullif(current_setting('app.principal_id', true), '') $$;

CREATE FUNCTION authz.branch() RETURNS text
    LANGUAGE sql STABLE PARALLEL SAFE
    AS $$ SELECT nullif(current_setting('app.branch', true), '') $$;

CREATE FUNCTION authz.has_permission(permission text) RETURNS boolean
    LANGUAGE sql STABLE PARALLEL SAFE
    AS $$
        SELECT coalesce(
            permission = ANY (string_to_array(nullif(current_setting('app.permissions', true), ''), ',')),
            false)
    $$;


""");
        migrationBuilder.CreateTable(
            name: "customers", schema: "customer",
            columns: table => new
            {
            id = table.Column<string>(type: "text", nullable: false),
            branch = table.Column<string>(type: "text", nullable: false),
            first_name = table.Column<string>(type: "text", nullable: false),
            last_name = table.Column<string>(type: "text", nullable: false),
            email = table.Column<string>(type: "text", nullable: false),
            phone = table.Column<string>(type: "text", nullable: false),
            iban = table.Column<string>(type: "text", nullable: false),
            date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
            segment = table.Column<string>(type: "text", nullable: false),
            identity_subject_id = table.Column<string?>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_customers", x => x.id);
                table.CheckConstraint("customers_id_format", "id ~ '^C-[0-9]{4}$'");
                table.CheckConstraint("customers_segment_allowed", "segment IN ('private','business')");
            });
        migrationBuilder.CreateTable(
            name: "accounts", schema: "customer",
            columns: table => new
            {
            id = table.Column<string>(type: "text", nullable: false),
            customer_id = table.Column<string>(type: "text", nullable: false),
            type = table.Column<string>(type: "text", nullable: false),
            currency = table.Column<string>(type: "character(3)", nullable: false),
            balance = table.Column<decimal>(type: "numeric(14,2)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_accounts", x => x.id);
                table.ForeignKey("FK_customer_accounts_customer_customers", x => x.customer_id, "customers", "id", "customer", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("accounts_currency_iso", "currency ~ '^[A-Z]{3}$'");
            });
        migrationBuilder.CreateTable(
            name: "credit_assignments", schema: "customer",
            columns: table => new
            {
            customer_id = table.Column<string>(type: "text", nullable: false),
            analyst_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_credit_assignments", x => new { x.customer_id, x.analyst_id });
                table.ForeignKey("FK_customer_credit_assignments_customer_customers", x => x.customer_id, "customers", "id", "customer", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "scheduled_payments", schema: "customer",
            columns: table => new
            {
            id = table.Column<string>(type: "text", nullable: false),
            customer_id = table.Column<string>(type: "text", nullable: false),
            description = table.Column<string>(type: "text", nullable: false),
            amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
            currency = table.Column<string>(type: "character(3)", nullable: false),
            due_date = table.Column<DateOnly>(type: "date", nullable: false),
            status = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_scheduled_payments", x => x.id);
                table.ForeignKey("FK_customer_scheduled_payments_customer_customers", x => x.customer_id, "customers", "id", "customer", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("scheduled_payments_status_check", "status IN ('planned','completed','cancelled')");
            });
        migrationBuilder.CreateTable(
            name: "account_transactions", schema: "customer",
            columns: table => new
            {
            id = table.Column<string>(type: "text", nullable: false),
            customer_id = table.Column<string>(type: "text", nullable: false),
            description = table.Column<string>(type: "text", nullable: false),
            amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
            currency = table.Column<string>(type: "character(3)", nullable: false),
            booked_on = table.Column<DateOnly>(type: "date", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_account_transactions", x => x.id);
                table.ForeignKey("FK_customer_account_transactions_customer_customers", x => x.customer_id, "customers", "id", "customer", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "standing_orders", schema: "customer",
            columns: table => new
            {
            id = table.Column<string>(type: "text", nullable: false),
            customer_id = table.Column<string>(type: "text", nullable: false),
            description = table.Column<string>(type: "text", nullable: false),
            amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
            currency = table.Column<string>(type: "character(3)", nullable: false),
            frequency = table.Column<string>(type: "text", nullable: false),
            next_due_date = table.Column<DateOnly>(type: "date", nullable: false),
            active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_standing_orders", x => x.id);
                table.ForeignKey("FK_customer_standing_orders_customer_customers", x => x.customer_id, "customers", "id", "customer", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("customers_identity_subject_unique", "customers", "identity_subject_id", schema: "customer", unique: true);
        migrationBuilder.CreateIndex("accounts_customer_id_idx", "accounts", "customer_id", schema: "customer");
        migrationBuilder.CreateIndex("credit_assignments_analyst_idx", "credit_assignments", schema: "customer", columns: new[] { "analyst_id", "customer_id" });
        migrationBuilder.CreateIndex("scheduled_payments_customer_due", "scheduled_payments", schema: "customer", columns: new[] { "customer_id", "due_date" });
        migrationBuilder.CreateIndex("account_transactions_customer_date", "account_transactions", schema: "customer", columns: new[] { "customer_id", "booked_on" });
        migrationBuilder.CreateIndex("standing_orders_customer_due", "standing_orders", schema: "customer", columns: new[] { "customer_id", "next_due_date" });

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Development-only rollback. Never drop production schema without an explicit migration review.
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS customer CASCADE;");
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS authz CASCADE;");
    }
}
