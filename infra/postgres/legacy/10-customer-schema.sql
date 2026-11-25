-- Customer service schema with PostgreSQL Row-Level Security.
--
-- Trusted request context (set by the Customer API inside an explicit transaction,
-- transaction-local via set_config(..., true), derived only from a validated JWT):
--   app.principal_id  stable Keycloak subject ID
--   app.branch        branch attribute of the principal, empty if none
--   app.permissions   comma-separated canonical permission IDs resolved server-side
--
-- Every policy requires BOTH the business permission AND the row condition.
-- Missing or empty context matches no policy, so protected tables fail closed.

\connect customer_db

SET ROLE customer_owner;

CREATE SCHEMA authz;
CREATE SCHEMA customer;

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

CREATE TABLE customer.customers (
    id             text PRIMARY KEY CHECK (id ~ '^C-[0-9]{4}$'),
    branch         text NOT NULL,
    first_name     text NOT NULL,
    last_name      text NOT NULL,
    email          text NOT NULL,
    phone          text NOT NULL,
    iban           text NOT NULL,
    date_of_birth  date NOT NULL,
    segment        text NOT NULL CHECK (segment IN ('private', 'business')),
    identity_subject_id text UNIQUE
);

CREATE TABLE customer.accounts (
    id           text PRIMARY KEY,
    customer_id  text NOT NULL REFERENCES customer.customers (id),
    type         text NOT NULL,
    currency     char(3) NOT NULL,
    balance      numeric(14, 2) NOT NULL
);
CREATE INDEX accounts_customer_id_idx ON customer.accounts (customer_id);

-- Read-only authorization projection of credit assignments.
-- The Credit domain is authoritative; this table is a derived copy (seeded in M1,
-- synchronized in M4). Staleness risk is documented in docs/threat-model.md.
CREATE TABLE customer.credit_assignments (
    customer_id  text NOT NULL REFERENCES customer.customers (id),
    analyst_id   text NOT NULL,
    PRIMARY KEY (customer_id, analyst_id)
);
CREATE INDEX credit_assignments_analyst_idx ON customer.credit_assignments (analyst_id, customer_id);

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

RESET ROLE;

-- Least privilege for the runtime role: read-only, no DDL, no RLS bypass.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA authz, customer TO customer_api;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA authz TO customer_api;
GRANT SELECT ON customer.customers, customer.accounts, customer.credit_assignments TO customer_api;
