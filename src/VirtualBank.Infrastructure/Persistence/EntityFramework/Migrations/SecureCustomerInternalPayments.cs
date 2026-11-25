using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

/// <summary>
/// Transfers between customers of this bank: the beneficiary's account is credited in the SAME transaction as the
/// payer's debit. The payer cannot see the beneficiary's rows (RLS, correctly), so crediting goes through one narrow
/// SECURITY DEFINER function that
///   * only credits (never debits) a positive amount up to 10,000,
///   * requires customer.payment.write and a payer row bound to the token subject,
///   * refuses the payer's own IBAN,
///   * requires that the payer's DEBIT of exactly this amount was already written in the current transaction
///     (so money is never created: no debit, no credit),
///   * returns NULL for IBANs that are not customers of this bank (external transfer: debit only).
/// The function runs as customer_owner; FORCE RLS applies to the owner too, hence owner-only policies below.
/// </summary>
[DbContext(typeof(CustomerDbContext))]
[Migration("20261125060000_SecureCustomerInternalPayments")]
public sealed class SecureCustomerInternalPayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE POLICY customers_owner_payments ON customer.customers FOR SELECT TO customer_owner USING (true);
CREATE POLICY accounts_owner_payments_read ON customer.accounts FOR SELECT TO customer_owner USING (true);
CREATE POLICY accounts_owner_payments_credit ON customer.accounts FOR UPDATE TO customer_owner USING (true) WITH CHECK (true);
CREATE POLICY transactions_owner_payments_read ON customer.account_transactions FOR SELECT TO customer_owner USING (true);
CREATE POLICY transactions_owner_payments_insert ON customer.account_transactions FOR INSERT TO customer_owner WITH CHECK (true);

CREATE FUNCTION customer.credit_internal_beneficiary(p_iban text, p_amount numeric, p_currency text, p_description text)
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

    SELECT c.id INTO v_payer FROM customer.customers c WHERE c.identity_subject_id = authz.principal_id();
    IF v_payer IS NULL THEN
        RAISE EXCEPTION 'no customer for caller' USING ERRCODE = '42501';
    END IF;

    SELECT c.id INTO v_beneficiary FROM customer.customers c WHERE upper(replace(c.iban, ' ', '')) = upper(replace(p_iban, ' ', ''));
    IF v_beneficiary IS NULL THEN
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
    RETURN v_beneficiary;
END;
$$;
REVOKE ALL ON FUNCTION customer.credit_internal_beneficiary(text, numeric, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION customer.credit_internal_beneficiary(text, numeric, text, text) TO customer_api;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
