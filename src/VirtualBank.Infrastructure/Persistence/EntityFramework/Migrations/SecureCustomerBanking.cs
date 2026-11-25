using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

/// <summary>
/// Money movement. Balances change only together with a booked transaction (one DB transaction in the API) and
/// can never go below zero (CHECK). Two booking paths, each with its own permission and row rule:
///   customer.payment.write  - the customer moves money on their OWN accounts (transfers);
///   account.cash            - a branch advisor books cash deposits/withdrawals for customers of THEIR branch.
/// Also: customers may create their own standing orders. SQL only (constraint, policies, grants).
/// </summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261125040000_SecureCustomerBanking")]
public sealed class SecureCustomerBanking : Migration
{
    private const string Own = """
        EXISTS (SELECT 1 FROM customer.customers c
                WHERE c.id = customer_id AND c.identity_subject_id = authz.principal_id())
        """;

    private const string BranchCustomer = """
        EXISTS (SELECT 1 FROM customer.customers c
                WHERE c.id = customer_id AND c.branch = authz.branch())
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
ALTER TABLE customer.accounts ADD CONSTRAINT accounts_balance_not_negative CHECK (balance >= 0);

-- Booking on the caller's own accounts (outgoing transfer, transfer between own accounts).
CREATE POLICY accounts_self_book ON customer.accounts
    FOR UPDATE TO customer_api
    USING (authz.has_permission('customer.payment.write') AND {Own})
    WITH CHECK ({Own});
CREATE POLICY transactions_self_insert ON customer.account_transactions
    FOR INSERT TO customer_api
    WITH CHECK (authz.has_permission('customer.payment.write') AND {Own});

-- Cash desk: advisors book deposits/withdrawals for customers of their own branch only.
CREATE POLICY accounts_cash_desk ON customer.accounts
    FOR UPDATE TO customer_api
    USING (authz.has_permission('account.cash') AND authz.has_permission('customer.read') AND {BranchCustomer})
    WITH CHECK ({BranchCustomer});
CREATE POLICY transactions_cash_desk_insert ON customer.account_transactions
    FOR INSERT TO customer_api
    WITH CHECK (authz.has_permission('account.cash') AND authz.has_permission('customer.read') AND {BranchCustomer});
-- ... and see those customers' bookings afterwards.
CREATE POLICY transactions_branch_read ON customer.account_transactions
    FOR SELECT TO customer_api
    USING (authz.has_permission('account.cash') AND authz.has_permission('customer.read') AND {BranchCustomer});

-- Customers create their own standing orders (bounded amount, active, future first date).
CREATE POLICY standing_self_insert ON customer.standing_orders
    FOR INSERT TO customer_api
    WITH CHECK (authz.has_permission('customer.payment.write') AND amount > 0 AND amount <= 10000
                AND active AND next_due_date >= CURRENT_DATE AND {Own});

GRANT UPDATE (balance) ON customer.accounts TO customer_api;
GRANT INSERT ON customer.account_transactions TO customer_api;
GRANT INSERT ON customer.standing_orders TO customer_api;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
