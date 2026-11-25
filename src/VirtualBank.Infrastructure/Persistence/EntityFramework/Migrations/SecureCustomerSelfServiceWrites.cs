using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

/// <summary>
/// Self-service writes on the caller's OWN rows only: plan a payment, cancel a planned one, pause/resume a standing
/// order. Ownership is the authoritative customer row bound to the token subject; column grants keep everything
/// else read-only. SQL only (policies and grants); the EF model is unchanged.
/// </summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261125020000_SecureCustomerSelfServiceWrites")]
public sealed class SecureCustomerSelfServiceWrites : Migration
{
    private const string Owner = """
        EXISTS (SELECT 1 FROM customer.customers c
                WHERE c.id = customer_id AND c.identity_subject_id = authz.principal_id())
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
CREATE POLICY scheduled_self_insert ON customer.scheduled_payments
    FOR INSERT TO customer_api
    WITH CHECK (
        authz.has_permission('customer.payment.write')
        AND status = 'planned' AND amount > 0 AND amount <= 10000 AND due_date >= CURRENT_DATE
        AND {Owner});

CREATE POLICY scheduled_self_cancel ON customer.scheduled_payments
    FOR UPDATE TO customer_api
    USING (authz.has_permission('customer.payment.write') AND status = 'planned' AND {Owner})
    WITH CHECK (status = 'cancelled' AND {Owner});

CREATE POLICY standing_self_toggle ON customer.standing_orders
    FOR UPDATE TO customer_api
    USING (authz.has_permission('customer.payment.write') AND {Owner})
    WITH CHECK ({Owner});

GRANT INSERT ON customer.scheduled_payments TO customer_api;
GRANT UPDATE (status) ON customer.scheduled_payments TO customer_api;
GRANT UPDATE (active) ON customer.standing_orders TO customer_api;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
