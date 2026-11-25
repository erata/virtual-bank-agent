-- PostgreSQL Row-Level Security regression tests.
-- Run as the bootstrap superuser against a database initialized with infra/postgres/init.
-- Each test switches to the runtime role inside a transaction and sets the trusted
-- context transaction-locally, exactly like the APIs do.
--
--   psql -v ON_ERROR_STOP=1 -f tests/database/rls-tests.sql
--
-- Covered invariants: I5 (fail closed), I14 (no RLS bypass), I22 (assigned-customer scope),
-- I27 (no context leakage across requests).

\set ON_ERROR_STOP on
\set QUIET on
\pset tuples_only on

\set anna   '00000000-0000-4000-8000-000000000004'
\set mehmet '00000000-0000-4000-8000-000000000005'
\set max    '00000000-0000-4000-8000-000000000006'

------------------------------------------------------------------------------
\connect customer_db
------------------------------------------------------------------------------

CREATE FUNCTION pg_temp.expect(actual text, expected text, test_name text) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    IF actual IS DISTINCT FROM expected THEN
        RAISE EXCEPTION 'FAIL % : expected [%] but was [%]', test_name, expected, actual;
    END IF;
    RAISE NOTICE 'PASS %', test_name;
END $$;

-- I22 / branch path: Anna sees exactly the Stuttgart customers.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id', :'anna', true), set_config('app.branch', 'STR', true),
       set_config('app.permissions', 'customer.read,document.read', true) \gset ctx_
SELECT pg_temp.expect((SELECT string_agg(id, ',' ORDER BY id) FROM customer.customers),
                      'C-1002,C-1003,C-1004', 'CUSTOMER_BRANCH_PATH_ANNA');
-- "API filter omitted": an unrestricted query still cannot reach Munich rows.
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers WHERE branch = 'MUC'), '0', 'CUSTOMER_CROSS_BRANCH_DENIED');
SELECT pg_temp.expect((SELECT string_agg(id, ',' ORDER BY id) FROM customer.accounts),
                      'A-10021,A-10022,A-10023,A-10024,A-10031,A-10032,A-10033,A-10041', 'ACCOUNTS_FOLLOW_VISIBLE_CUSTOMERS_ANNA');
SELECT pg_temp.expect((SELECT string_agg(DISTINCT currency::text, ',' ORDER BY currency::text) FROM customer.accounts),
                      'EUR,TRY,USD', 'ANNA_MULTI_CURRENCY_VISIBLE');
ROLLBACK;

-- I22 / assignment path: Mehmet sees only customers linked to his assigned applications.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id', :'mehmet', true), set_config('app.branch', '', true),
       set_config('app.permissions', 'customer.read.assigned,credit.read,credit.note.write,document.read', true) \gset ctx_
SELECT pg_temp.expect((SELECT string_agg(id, ',' ORDER BY id) FROM customer.customers),
                      'C-1002,C-5002', 'CUSTOMER_ASSIGNMENT_PATH_MEHMET');
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers WHERE id = 'C-5001'), '0', 'CUSTOMER_UNASSIGNED_DENIED');
SELECT pg_temp.expect((SELECT string_agg(id, ',' ORDER BY id) FROM customer.accounts),
                      'A-10021,A-10022,A-10023,A-10024,A-50021', 'ACCOUNTS_FOLLOW_VISIBLE_CUSTOMERS_MEHMET');
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.credit_assignments), '2', 'PROJECTION_OWN_ROWS_ONLY');
ROLLBACK;

-- Permission-aware RLS: a matching branch without the permission grants nothing.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id', :'anna', true), set_config('app.branch', 'STR', true),
       set_config('app.permissions', 'document.read', true) \gset ctx_
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers), '0', 'BRANCH_WITHOUT_PERMISSION_DENIED');
ROLLBACK;

-- Permission-aware RLS: existing assignment rows without the permission grant nothing.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id', :'mehmet', true), set_config('app.branch', '', true),
       set_config('app.permissions', 'customer.read', true) \gset ctx_
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers), '0', 'ASSIGNMENT_WITHOUT_PERMISSION_DENIED');
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.credit_assignments), '0', 'PROJECTION_WITHOUT_PERMISSION_DENIED');
ROLLBACK;

-- The permission alone does not let a principal choose another principal's assignment rows.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id', :'anna', true), set_config('app.branch', '', true),
       set_config('app.permissions', 'customer.read.assigned', true) \gset ctx_
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers), '0', 'ASSIGNMENT_PATH_FOREIGN_PRINCIPAL_DENIED');
ROLLBACK;

-- I5: no context at all fails closed.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers), '0', 'NO_CONTEXT_FAILS_CLOSED');
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.accounts), '0', 'NO_CONTEXT_ACCOUNTS_FAIL_CLOSED');
ROLLBACK;

-- I27: transaction-local context does not survive into the next transaction on a reused connection.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id', :'anna', true), set_config('app.branch', 'STR', true),
       set_config('app.permissions', 'customer.read', true) \gset ctx_
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers), '3', 'CONTEXT_ACTIVE_IN_OWN_TRANSACTION');
COMMIT;
BEGIN;
SET LOCAL ROLE customer_api;
SELECT pg_temp.expect(coalesce(current_setting('app.permissions', true), ''), '', 'CONTEXT_CLEARED_AFTER_COMMIT');
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers), '0', 'NO_LEAK_INTO_NEXT_TRANSACTION');
ROLLBACK;

-- Runtime role cannot modify data.
BEGIN;
SET LOCAL ROLE customer_api;
SELECT set_config('app.principal_id', :'anna', true), set_config('app.branch', 'STR', true),
       set_config('app.permissions', 'customer.read', true) \gset ctx_
DO $$
BEGIN
    UPDATE customer.customers SET branch = 'MUC' WHERE id = 'C-1002';
    RAISE EXCEPTION 'FAIL RUNTIME_ROLE_READ_ONLY : update succeeded';
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'PASS RUNTIME_ROLE_READ_ONLY';
END $$;
ROLLBACK;

-- I14: FORCE ROW LEVEL SECURITY applies to the table owner as well.
BEGIN;
SET LOCAL ROLE customer_owner;
SELECT pg_temp.expect((SELECT count(*)::text FROM customer.customers), '0', 'OWNER_SUBJECT_TO_FORCED_RLS');
ROLLBACK;

-- I14: runtime roles are neither superusers nor allowed to bypass RLS.
SELECT pg_temp.expect(
    (SELECT string_agg(rolname || ':' || rolsuper || ':' || rolbypassrls, ',' ORDER BY rolname)
     FROM pg_roles WHERE rolname IN ('credit_api', 'credit_approval', 'credit_owner', 'customer_api', 'customer_owner')),
    'credit_api:false:false,credit_approval:false:false,credit_owner:false:false,customer_api:false:false,customer_owner:false:false',
    'RUNTIME_ROLES_CANNOT_BYPASS_RLS');

SELECT pg_temp.expect(
    (SELECT string_agg(relname || ':' || relrowsecurity || ':' || relforcerowsecurity, ',' ORDER BY relname)
     FROM pg_class WHERE relnamespace = 'customer'::regnamespace AND relkind = 'r'),
    'account_transactions:true:true,accounts:true:true,credit_assignments:true:true,customers:true:true,scheduled_payments:true:true,standing_orders:true:true',
    'ALL_CUSTOMER_TABLES_FORCE_RLS');

------------------------------------------------------------------------------
\connect credit_db
------------------------------------------------------------------------------

CREATE FUNCTION pg_temp.expect(actual text, expected text, test_name text) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    IF actual IS DISTINCT FROM expected THEN
        RAISE EXCEPTION 'FAIL % : expected [%] but was [%]', test_name, expected, actual;
    END IF;
    RAISE NOTICE 'PASS %', test_name;
END $$;

-- Analyst reads assigned applications only.
BEGIN;
SET LOCAL ROLE credit_api;
SELECT set_config('app.principal_id', :'mehmet', true),
       set_config('app.permissions', 'customer.read.assigned,credit.read,credit.note.write,document.read', true) \gset ctx_
SELECT pg_temp.expect((SELECT string_agg(id, ',' ORDER BY id) FROM credit.credit_applications),
                      'K-100,K-101,K-102', 'CREDIT_ANALYST_READS_ASSIGNED');
-- Analysts may add notes to visible applications, only as themselves.
INSERT INTO credit.credit_notes (application_id, author_id, body) VALUES ('K-100', :'mehmet', 'Income documents verified.');
SELECT pg_temp.expect((SELECT count(*)::text FROM credit.credit_notes), '1', 'CREDIT_NOTE_INSERT_ALLOWED');
SELECT set_config('app.spoofed_author', :'max', true) \gset ctx_
DO $$
BEGIN
    INSERT INTO credit.credit_notes (application_id, author_id, body)
    VALUES ('K-100', current_setting('app.spoofed_author'), 'Spoofed author');
    RAISE EXCEPTION 'FAIL CREDIT_NOTE_AUTHOR_SPOOF_DENIED : insert succeeded';
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'PASS CREDIT_NOTE_AUTHOR_SPOOF_DENIED';
END $$;
-- The Credit API runtime role has no UPDATE privilege on applications at all (least privilege before M6).
DO $$
BEGIN
    UPDATE credit.credit_applications SET version = version + 1 WHERE id = 'K-100';
    RAISE EXCEPTION 'FAIL CREDIT_API_ROLE_CANNOT_UPDATE : update succeeded';
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'PASS CREDIT_API_ROLE_CANNOT_UPDATE';
END $$;
ROLLBACK;

-- Approval executor role (M6) with approver context (permission + approval limit): the three seeded cases.
BEGIN;
SET LOCAL ROLE credit_approval;
SELECT set_config('app.principal_id', :'max', true), set_config('app.approval_limit', '50000', true),
       set_config('app.permissions', 'credit.read,credit.approve,document.read', true) \gset ctx_
SELECT pg_temp.expect((SELECT string_agg(id, ',' ORDER BY id) FROM credit.credit_applications),
                      'K-100,K-101,K-102', 'APPROVER_READS_SUBMITTED_ONLY');
WITH u AS (
        UPDATE credit.credit_applications SET status = 'APPROVED', approved_by = :'max', approved_at = now(), version = version + 1
        WHERE id = 'K-101' RETURNING 1)
SELECT pg_temp.expect(count(*)::text, '0', 'APPROVAL_OVER_LIMIT_DENIED') FROM u;
WITH u AS (
        UPDATE credit.credit_applications SET status = 'APPROVED', approved_by = :'max', approved_at = now(), version = version + 1
        WHERE id = 'K-102' RETURNING 1)
SELECT pg_temp.expect(count(*)::text, '0', 'APPROVAL_FOUR_EYES_DENIED') FROM u;
SELECT set_config('app.spoofed_approver', :'mehmet', true) \gset ctx_
DO $$
BEGIN
    UPDATE credit.credit_applications
    SET status = 'APPROVED', approved_by = current_setting('app.spoofed_approver'), approved_at = now()
    WHERE id = 'K-100';
    RAISE EXCEPTION 'FAIL APPROVAL_FORGED_APPROVER_DENIED : update succeeded';
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'PASS APPROVAL_FORGED_APPROVER_DENIED';
END $$;
WITH u AS (
        UPDATE credit.credit_applications SET status = 'APPROVED', approved_by = :'max', approved_at = now(), version = version + 1
        WHERE id = 'K-100' AND version = 1 RETURNING 1)
SELECT pg_temp.expect(count(*)::text, '1', 'APPROVAL_WITHIN_LIMIT_ALLOWED') FROM u;
-- Optimistic concurrency: a second approval with the stale version changes nothing.
WITH u AS (
        UPDATE credit.credit_applications SET status = 'APPROVED', approved_by = :'max', approved_at = now(), version = version + 1
        WHERE id = 'K-100' AND version = 1 RETURNING 1)
SELECT pg_temp.expect(count(*)::text, '0', 'APPROVAL_STALE_VERSION_NO_EFFECT') FROM u;
ROLLBACK;

-- Domain invariants mirrored as CHECK constraints (checked as the owner, i.e. independent of RLS and grants).
BEGIN;
DO $$
BEGIN
    INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by, approved_by)
    VALUES ('K-990', 'C-1002', 10, 'EUR', 'APPROVED', 'Test', 'someone', 'approver');
    RAISE EXCEPTION 'FAIL APPROVED_REQUIRES_TIMESTAMP : insert succeeded';
EXCEPTION WHEN check_violation THEN
    RAISE NOTICE 'PASS APPROVED_REQUIRES_TIMESTAMP';
END $$;
DO $$
BEGIN
    INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by, version)
    VALUES ('K-991', 'C-1002', 10, 'EUR', 'DRAFT', 'Test', 'someone', 0);
    RAISE EXCEPTION 'FAIL VERSION_MUST_BE_POSITIVE : insert succeeded';
EXCEPTION WHEN check_violation THEN
    RAISE NOTICE 'PASS VERSION_MUST_BE_POSITIVE';
END $$;
DO $$
BEGIN
    INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by)
    VALUES ('K-992', 'C-1002', 0, 'EUR', 'DRAFT', 'Test', 'someone');
    RAISE EXCEPTION 'FAIL CREDIT_AMOUNT_MUST_BE_POSITIVE : insert succeeded';
EXCEPTION WHEN check_violation THEN
    RAISE NOTICE 'PASS CREDIT_AMOUNT_MUST_BE_POSITIVE';
END $$;
DO $$
BEGIN
    INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by, approved_by, approved_at)
    VALUES ('K-993', 'C-1002', 10, 'EUR', 'APPROVED', 'Test', 'someone', '   ', now());
    RAISE EXCEPTION 'FAIL BLANK_APPROVER_REJECTED : insert succeeded';
EXCEPTION WHEN check_violation THEN
    RAISE NOTICE 'PASS BLANK_APPROVER_REJECTED';
END $$;
DO $$
BEGIN
    INSERT INTO credit.credit_applications (id, customer_id, amount, currency, status, purpose, prepared_by)
    VALUES ('K-994', 'C-1002', 10, 'EUR', 'PAUSED', 'Test', 'someone');
    RAISE EXCEPTION 'FAIL UNKNOWN_STATUS_REJECTED : insert succeeded';
EXCEPTION WHEN check_violation THEN
    RAISE NOTICE 'PASS UNKNOWN_STATUS_REJECTED';
END $$;
ROLLBACK;

-- Approver without an approval limit in context fails closed.
BEGIN;
SET LOCAL ROLE credit_approval;
SELECT set_config('app.principal_id', :'max', true),
       set_config('app.permissions', 'credit.read,credit.approve', true) \gset ctx_
WITH u AS (
        UPDATE credit.credit_applications SET status = 'APPROVED', approved_by = :'max', approved_at = now()
        WHERE id = 'K-100' RETURNING 1)
SELECT pg_temp.expect(count(*)::text, '0', 'APPROVAL_WITHOUT_LIMIT_FAILS_CLOSED') FROM u;
ROLLBACK;

-- Even the approval role cannot approve on behalf of an analyst without credit.approve.
BEGIN;
SET LOCAL ROLE credit_approval;
SELECT set_config('app.principal_id', :'mehmet', true), set_config('app.approval_limit', '50000', true),
       set_config('app.permissions', 'customer.read.assigned,credit.read,credit.note.write,document.read', true) \gset ctx_
WITH u AS (
        UPDATE credit.credit_applications SET status = 'APPROVED', approved_by = :'mehmet', approved_at = now()
        WHERE id = 'K-100' RETURNING 1)
SELECT pg_temp.expect(count(*)::text, '0', 'ANALYST_CANNOT_APPROVE') FROM u;
ROLLBACK;

-- Customer advisor has no credit permissions.
BEGIN;
SET LOCAL ROLE credit_api;
SELECT set_config('app.principal_id', :'anna', true),
       set_config('app.permissions', 'customer.read,document.read', true) \gset ctx_
SELECT pg_temp.expect((SELECT count(*)::text FROM credit.credit_applications), '0', 'ADVISOR_NO_CREDIT_ACCESS');
ROLLBACK;

BEGIN;
SET LOCAL ROLE credit_api;
SELECT pg_temp.expect((SELECT count(*)::text FROM credit.credit_applications), '0', 'CREDIT_NO_CONTEXT_FAILS_CLOSED');
ROLLBACK;

-- I14: FORCE ROW LEVEL SECURITY applies to the credit_db owner as well.
BEGIN;
SET LOCAL ROLE credit_owner;
SELECT pg_temp.expect((SELECT count(*)::text FROM credit.credit_applications), '0', 'CREDIT_OWNER_SUBJECT_TO_FORCED_RLS');
ROLLBACK;

SELECT pg_temp.expect(
    (SELECT string_agg(relname || ':' || relrowsecurity || ':' || relforcerowsecurity, ',' ORDER BY relname)
     FROM pg_class WHERE relnamespace = 'credit'::regnamespace AND relkind = 'r'),
    'approval_requests:true:true,credit_applications:true:true,credit_notes:true:true',
    'ALL_CREDIT_TABLES_FORCE_RLS');

\echo 'All RLS tests passed.'
