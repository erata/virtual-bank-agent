-- Credit service schema with PostgreSQL Row-Level Security.
--
-- Trusted request context (transaction-local, derived from a validated JWT by the Credit API):
--   app.principal_id    stable Keycloak subject ID
--   app.permissions     comma-separated canonical permission IDs
--   app.approval_limit  numeric approval limit of the principal, empty if none
--
-- The approval UPDATE policy re-checks permission, state, approval limit and the
-- four-eyes rule in the database. The application checks the same rules first;
-- the database is the second, independent line of defense.
--
-- Least privilege: only the credit_approval role may UPDATE applications. The Credit API
-- runtime role (credit_api) has no UPDATE privilege at all until the M6 approval workflow exists.

\connect credit_db

SET ROLE credit_owner;

CREATE SCHEMA authz;
CREATE SCHEMA credit;

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

CREATE TABLE credit.credit_applications (
    id                   text PRIMARY KEY CHECK (id ~ '^K-[0-9]{3}$'),
    customer_id          text NOT NULL,
    amount               numeric(14, 2) NOT NULL CHECK (amount > 0),
    currency             char(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
    status               text NOT NULL CHECK (status IN ('DRAFT', 'SUBMITTED', 'APPROVED', 'REJECTED')),
    purpose              text NOT NULL CHECK (btrim(purpose) <> ''),
    prepared_by          text NOT NULL CHECK (btrim(prepared_by) <> ''),
    assigned_analyst_id  text NULL,
    approved_by          text NULL,
    approved_at          timestamptz NULL,
    version              integer NOT NULL DEFAULT 1 CHECK (version >= 1),
    -- Same invariants as CreditApplication.Rehydrate: approved <=> approver <=> approval timestamp.
    CONSTRAINT credit_applications_approver_iff_approved CHECK (
        (status = 'APPROVED') = (approved_by IS NOT NULL AND btrim(approved_by) <> '')),
    CONSTRAINT credit_applications_no_blank_approver CHECK (approved_by IS NULL OR btrim(approved_by) <> ''),
    CONSTRAINT credit_applications_approved_at_iff_approved CHECK ((status = 'APPROVED') = (approved_at IS NOT NULL))
);
CREATE INDEX credit_applications_analyst_idx ON credit.credit_applications (assigned_analyst_id);

CREATE TABLE credit.credit_notes (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    application_id  text NOT NULL REFERENCES credit.credit_applications (id),
    author_id       text NOT NULL,
    body            text NOT NULL CHECK (length(body) BETWEEN 1 AND 4000),
    created_at      timestamptz NOT NULL DEFAULT now()
);

-- Idempotency records for approval requests (one execution per key).
CREATE TABLE credit.approval_requests (
    idempotency_key  uuid PRIMARY KEY,
    application_id   text NOT NULL REFERENCES credit.credit_applications (id),
    requested_by     text NOT NULL,
    outcome          text NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE credit.credit_applications ENABLE ROW LEVEL SECURITY;
ALTER TABLE credit.credit_applications FORCE  ROW LEVEL SECURITY;
ALTER TABLE credit.credit_notes        ENABLE ROW LEVEL SECURITY;
ALTER TABLE credit.credit_notes        FORCE  ROW LEVEL SECURITY;
ALTER TABLE credit.approval_requests   ENABLE ROW LEVEL SECURITY;
ALTER TABLE credit.approval_requests   FORCE  ROW LEVEL SECURITY;

-- Analysts read applications assigned to them.
CREATE POLICY applications_read_assigned ON credit.credit_applications
    FOR SELECT TO credit_api
    USING (authz.has_permission('credit.read') AND assigned_analyst_id = authz.principal_id());

-- Approvers read submitted applications awaiting a decision, and those they decided.
CREATE POLICY applications_read_for_approval ON credit.credit_applications
    FOR SELECT TO credit_api, credit_approval
    USING (
        authz.has_permission('credit.approve')
        AND (status = 'SUBMITTED' OR approved_by = authz.principal_id()));

-- Approval: permission, state, limit and four-eyes are enforced in the database as well.
CREATE POLICY applications_approve ON credit.credit_applications
    FOR UPDATE TO credit_approval
    USING (
        authz.has_permission('credit.approve')
        AND status = 'SUBMITTED'
        AND amount <= coalesce(authz.approval_limit(), 0)
        AND prepared_by <> coalesce(authz.principal_id(), ''))
    WITH CHECK (
        status = 'APPROVED'
        AND approved_by = authz.principal_id()
        AND prepared_by <> approved_by);

CREATE POLICY notes_read ON credit.credit_notes
    FOR SELECT TO credit_api
    USING (EXISTS (SELECT 1 FROM credit.credit_applications a WHERE a.id = credit_notes.application_id));

CREATE POLICY notes_insert ON credit.credit_notes
    FOR INSERT TO credit_api
    WITH CHECK (
        authz.has_permission('credit.note.write')
        AND author_id = authz.principal_id()
        AND EXISTS (SELECT 1 FROM credit.credit_applications a WHERE a.id = credit_notes.application_id));

CREATE POLICY approval_requests_own ON credit.approval_requests
    FOR ALL TO credit_approval
    USING (requested_by = authz.principal_id())
    WITH CHECK (requested_by = authz.principal_id() AND authz.has_permission('credit.approve'));

RESET ROLE;

REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA authz, credit TO credit_api, credit_approval;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA authz TO credit_api, credit_approval;

-- Credit API: read applications, read and add notes. No UPDATE, no approval records.
GRANT SELECT ON credit.credit_applications TO credit_api;
GRANT SELECT, INSERT ON credit.credit_notes TO credit_api;

-- Approval executor (M6): change only the approval columns and record idempotency keys.
GRANT SELECT ON credit.credit_applications TO credit_approval;
GRANT UPDATE (status, approved_by, approved_at, version) ON credit.credit_applications TO credit_approval;
GRANT SELECT, INSERT ON credit.approval_requests TO credit_approval;
