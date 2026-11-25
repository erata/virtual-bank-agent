-- Run after the additive self-service migration; use an initialized, disposable demo DB.
\set ON_ERROR_STOP on
\connect customer_db
CREATE FUNCTION pg_temp.expect_equal(actual text, expected text, name text) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
 IF actual IS DISTINCT FROM expected THEN RAISE EXCEPTION '%: expected %, got %',name,expected,actual; END IF;
 RAISE NOTICE 'PASS %', name;
END $$;

BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000011',true);
SELECT set_config('app.branch','MUC',true);
SELECT set_config('app.permissions','customer.read.self,customer.read-sensitive',true);
SELECT pg_temp.expect_equal((SELECT string_agg(id,',' ORDER BY id) FROM customer.customers), 'C-2001', 'DENIZ_ONLY_OWN_CUSTOMER');
SELECT pg_temp.expect_equal((SELECT string_agg(id,',' ORDER BY id) FROM customer.accounts), 'A-20011,A-20012,A-20013,A-20014', 'DENIZ_ONLY_OWN_ACCOUNTS');
SELECT pg_temp.expect_equal((SELECT string_agg(id,',' ORDER BY id) FROM customer.scheduled_payments), 'P-2001-01,P-2001-02,P-2001-03,P-2001-04,P-2001-05', 'DENIZ_ONLY_OWN_PAYMENTS');
SELECT pg_temp.expect_equal((SELECT count(*)::text FROM customer.account_transactions), '5', 'DENIZ_ONLY_OWN_TRANSACTIONS');
SELECT pg_temp.expect_equal((SELECT count(*)::text FROM customer.standing_orders), '3', 'DENIZ_ONLY_OWN_STANDING');
ROLLBACK;

BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000012',true);
SELECT set_config('app.branch','MUC',true);
SELECT set_config('app.permissions','customer.read.self,customer.read-sensitive',true);
SELECT pg_temp.expect_equal((SELECT string_agg(id,',' ORDER BY id) FROM customer.customers), 'C-5001', 'THOMAS_ONLY_OWN_CUSTOMER');
SELECT pg_temp.expect_equal((SELECT string_agg(id,',' ORDER BY id) FROM customer.accounts), 'A-50011,A-50012,A-50013', 'THOMAS_ONLY_OWN_ACCOUNT');
SELECT pg_temp.expect_equal((SELECT string_agg(id,',' ORDER BY id) FROM customer.scheduled_payments), 'P-5001-01', 'THOMAS_ONLY_OWN_PAYMENTS');
ROLLBACK;

BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000011',true);
SELECT set_config('app.permissions','assistant.use',true);
SELECT pg_temp.expect_equal((SELECT count(*)::text FROM customer.customers), '0', 'NO_SELF_PERMISSION_NO_CUSTOMER');
SELECT pg_temp.expect_equal((SELECT count(*)::text FROM customer.scheduled_payments), '0', 'NO_SELF_PERMISSION_NO_PAYMENT');
ROLLBACK;

BEGIN;
SET LOCAL ROLE customer_api;
SELECT pg_temp.expect_equal((SELECT count(*)::text FROM customer.customers), '0', 'NO_CONTEXT_NO_CUSTOMER');
SELECT pg_temp.expect_equal((SELECT count(*)::text FROM customer.standing_orders), '0', 'NO_CONTEXT_NO_STANDING');
ROLLBACK;

-- New personas are subject-bound; Meryem cannot read Lucas' USD/EUR/TRY account rows.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000013',true);
SELECT set_config('app.branch','MUC',true);
SELECT set_config('app.permissions','customer.read.self,customer.read-sensitive',true);
SELECT pg_temp.expect_equal((SELECT string_agg(id,',' ORDER BY id) FROM customer.customers), 'C-2002', 'MERYEM_OWN_ID_ONLY');
SELECT pg_temp.expect_equal((SELECT string_agg(currency::text,',' ORDER BY currency::text) FROM customer.accounts), 'EUR,TRY,USD', 'MERYEM_THREE_CURRENCIES_ONLY');
SELECT pg_temp.expect_equal((SELECT count(*)::text FROM customer.accounts WHERE customer_id='C-2003'), '0', 'MERYEM_CANNOT_READ_LUCAS_ACCOUNTS');
ROLLBACK;
