using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

/// <summary>
/// Narrows the customer_owner policies added by <see cref="SecureCustomerInternalPayments"/>. With USING (true) the
/// table owner saw every row without any trusted context, which broke invariant I14 (FORCE RLS applies to the owner:
/// no context, no rows). The policies now apply only while <c>customer.credit_internal_beneficiary</c> runs: the
/// function sets the transaction-local flag <c>app.internal_credit</c> after its permission checks and clears it before
/// returning (a function-level SET clause would need a superuser for a custom parameter). Only roles that can act as customer_owner are affected; the runtime
/// role customer_api is not a member of customer_owner, so setting the flag itself gains it nothing.
/// </summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261125070000_SecureOwnerPaymentPolicyScope")]
public sealed class SecureOwnerPaymentPolicyScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER POLICY customers_owner_payments ON customer.customers
    USING (current_setting('app.internal_credit', true) = 'on');
ALTER POLICY accounts_owner_payments_read ON customer.accounts
    USING (current_setting('app.internal_credit', true) = 'on');
ALTER POLICY accounts_owner_payments_credit ON customer.accounts
    USING (current_setting('app.internal_credit', true) = 'on')
    WITH CHECK (current_setting('app.internal_credit', true) = 'on');
ALTER POLICY transactions_owner_payments_read ON customer.account_transactions
    USING (current_setting('app.internal_credit', true) = 'on');
ALTER POLICY transactions_owner_payments_insert ON customer.account_transactions
    WITH CHECK (current_setting('app.internal_credit', true) = 'on');

CREATE OR REPLACE FUNCTION customer.credit_internal_beneficiary(p_iban text, p_amount numeric, p_currency text, p_description text)
RETURNS text
LANGUAGE plpgsql VOLATILE SECURITY DEFINER
SET search_path = pg_catalog, customer, authz
AS $$
DECLARE
    v_payer text;
    v_beneficiary text;
    v_account text;
BEGIN
    IF NOT authz.has_permission('customer.payment.write') OR authz.principal_id() IS NULL THEN
        RAISE EXCEPTION 'internal credit not permitted' USING ERRCODE = '42501';
    END IF;
    IF p_amount IS NULL OR p_amount <= 0 OR p_amount > 10000 THEN
        RAISE EXCEPTION 'amount out of range' USING ERRCODE = '22023';
    END IF;

    -- Opens the owner-only policies for the statements below; cleared again before every RETURN.
    -- Errors abort the whole transaction, so the flag cannot outlive a failed call either.
    PERFORM set_config('app.internal_credit', 'on', true);

    SELECT c.id INTO v_payer FROM customer.customers c WHERE c.identity_subject_id = authz.principal_id();
    IF v_payer IS NULL THEN
        RAISE EXCEPTION 'no customer for caller' USING ERRCODE = '42501';
    END IF;

    SELECT c.id INTO v_beneficiary FROM customer.customers c WHERE upper(replace(c.iban, ' ', '')) = upper(replace(p_iban, ' ', ''));
    IF v_beneficiary IS NULL THEN
        PERFORM set_config('app.internal_credit', '', true);
        RETURN NULL; -- not a customer of this bank: external transfer, nothing to credit here
    END IF;
    IF v_beneficiary = v_payer THEN
        RAISE EXCEPTION 'own IBAN' USING ERRCODE = '22023';
    END IF;

    -- Conservation of money: the payer's matching debit must already exist in THIS transaction.
    IF NOT EXISTS (
        SELECT 1 FROM customer.account_transactions t
        WHERE t.customer_id = v_payer AND t.amount = -p_amount AND trim(t.currency) = p_currency
          AND t.xmin::text = (pg_current_xact_id()::text::bigint % 4294967296)::text) THEN
        RAISE EXCEPTION 'no matching debit in this transaction' USING ERRCODE = '42501';
    END IF;

    SELECT a.id INTO v_account FROM customer.accounts a
    WHERE a.customer_id = v_beneficiary AND trim(a.currency) = p_currency
    ORDER BY (a.type = 'checking') DESC, a.id
    LIMIT 1
    FOR UPDATE;
    IF v_account IS NULL THEN
        RAISE EXCEPTION 'beneficiary has no account in this currency' USING ERRCODE = 'P0002';
    END IF;

    UPDATE customer.accounts SET balance = balance + p_amount WHERE id = v_account;
    INSERT INTO customer.account_transactions (id, customer_id, description, amount, currency, booked_on)
    VALUES ('T-' || substr(v_beneficiary, 3) || '-' || upper(substr(md5(random()::text || clock_timestamp()::text), 1, 8)),
            v_beneficiary, v_account || ' · ' || p_description, p_amount, p_currency, CURRENT_DATE);
    PERFORM set_config('app.internal_credit', '', true);
    RETURN v_beneficiary;
END;
$$;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
