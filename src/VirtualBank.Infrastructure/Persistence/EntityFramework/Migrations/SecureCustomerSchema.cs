using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

[DbContext(typeof(CustomerDbContext))]
[Migration("20261123011000_SecureCustomerSchema")]
public sealed class SecureCustomerSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL native policies, FORCE RLS, and minimum privileges follow the EF schema+seed migration.
        migrationBuilder.Sql("""
ALTER TABLE customer.customers          ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.customers          FORCE  ROW LEVEL SECURITY;
ALTER TABLE customer.accounts           ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.accounts           FORCE  ROW LEVEL SECURITY;
ALTER TABLE customer.credit_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.credit_assignments FORCE  ROW LEVEL SECURITY;

-- Path 0: self-service customer sees exactly the row bound to their validated JWT subject.
-- Role/permission AND subject match required, and FORCE RLS prohibits runtime bypass.
CREATE POLICY customers_read_own ON customer.customers
    FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read.self')
       AND identity_subject_id IS NOT NULL
       AND identity_subject_id = authz.principal_id());

-- Path 1: branch advisors see customers of their own branch.
CREATE POLICY customers_read_by_branch ON customer.customers
    FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read') AND branch = authz.branch());

-- Path 2: credit analysts see customers linked to applications assigned to them.
CREATE POLICY customers_read_by_assignment ON customer.customers
    FOR SELECT TO customer_api
    USING (
        authz.has_permission('customer.read.assigned')
        AND EXISTS (
            SELECT 1
            FROM customer.credit_assignments a
            WHERE a.customer_id = customers.id
              AND a.analyst_id = authz.principal_id()));

-- Analysts may see only their own assignment rows (used by path 2).
CREATE POLICY credit_assignments_read_own ON customer.credit_assignments
    FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read.assigned') AND analyst_id = authz.principal_id());

-- Accounts are visible only if the owning customer is visible (customer policies apply in the subquery).
CREATE POLICY accounts_read_via_customer ON customer.accounts
    FOR SELECT TO customer_api
    USING (EXISTS (SELECT 1 FROM customer.customers c WHERE c.id = accounts.customer_id));



-- Least privilege for the runtime role: read-only, no DDL, no RLS bypass.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA authz, customer TO customer_api;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA authz TO customer_api;
GRANT SELECT ON customer.customers, customer.accounts, customer.credit_assignments TO customer_api;
ALTER TABLE customer.scheduled_payments ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.scheduled_payments FORCE ROW LEVEL SECURITY;
ALTER TABLE customer.account_transactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.account_transactions FORCE ROW LEVEL SECURITY;
ALTER TABLE customer.standing_orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer.standing_orders FORCE ROW LEVEL SECURITY;

-- Never rely on a customer ID supplied by the browser/model; require bank-customer permission
-- and the owner mapping on the authoritative Customer row.
DROP POLICY IF EXISTS scheduled_self_read ON customer.scheduled_payments;
CREATE POLICY scheduled_self_read ON customer.scheduled_payments FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read.self') AND EXISTS
      (SELECT 1 FROM customer.customers c WHERE c.id = scheduled_payments.customer_id
       AND c.identity_subject_id = authz.principal_id()));
DROP POLICY IF EXISTS transaction_self_read ON customer.account_transactions;
CREATE POLICY transaction_self_read ON customer.account_transactions FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read.self') AND EXISTS
      (SELECT 1 FROM customer.customers c WHERE c.id = account_transactions.customer_id
       AND c.identity_subject_id = authz.principal_id()));
DROP POLICY IF EXISTS standing_self_read ON customer.standing_orders;
CREATE POLICY standing_self_read ON customer.standing_orders FOR SELECT TO customer_api
    USING (authz.has_permission('customer.read.self') AND EXISTS
      (SELECT 1 FROM customer.customers c WHERE c.id = standing_orders.customer_id
       AND c.identity_subject_id = authz.principal_id()));

GRANT SELECT ON customer.scheduled_payments, customer.account_transactions, customer.standing_orders TO customer_api;
""");
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
