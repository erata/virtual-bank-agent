-- Creates database roles and one database per service.
-- Executed once by the PostgreSQL container entrypoint as the bootstrap superuser.
--
-- Role model (see docs/adr/0005-credit-assignment-projection-and-rls-context.md):
--   customer_owner NOLOGIN  owns the customer_db schema objects; subject to FORCE ROW LEVEL SECURITY
--   credit_owner   NOLOGIN  owns the credit_db schema objects; subject to FORCE ROW LEVEL SECURITY
--   customer_api   LOGIN    runtime role of the Customer API (no BYPASSRLS, no DDL)
--   credit_api     LOGIN    runtime role of the Credit API: reads applications, writes notes; cannot UPDATE applications
--   credit_approval LOGIN    approval executor (M6, human confirmation): the only role allowed to change application
--                            state. The API uses it solely after an explicit, single-use confirmation; RLS re-checks
--                            permission, state, limit and four-eyes on every UPDATE.
--
-- Passwords below are DEVELOPMENT-ONLY values. Override them for any shared environment.

CREATE ROLE customer_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE credit_owner   NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE customer_api LOGIN PASSWORD 'customer-api-dev-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE credit_api   LOGIN PASSWORD 'credit-api-dev-only'   NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE credit_approval LOGIN PASSWORD 'credit-approval-dev-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;

CREATE DATABASE customer_db OWNER customer_owner;
CREATE DATABASE credit_db   OWNER credit_owner;

-- Each runtime role may connect only to the database of its own service.
REVOKE CONNECT ON DATABASE customer_db FROM PUBLIC;
REVOKE CONNECT ON DATABASE credit_db   FROM PUBLIC;
GRANT  CONNECT ON DATABASE customer_db TO customer_api;
GRANT  CONNECT ON DATABASE credit_db   TO credit_api, credit_approval;

-- M3: separate document store and minimally privileged runtime identities.
CREATE ROLE document_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE document_api LOGIN PASSWORD 'document-api-dev-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE document_ingest LOGIN PASSWORD 'document-ingest-dev-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE document_acl_admin LOGIN PASSWORD 'document-acl-admin-dev-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE DATABASE documents_db OWNER document_owner;
REVOKE CONNECT ON DATABASE documents_db FROM PUBLIC;
GRANT CONNECT ON DATABASE documents_db TO document_api, document_ingest, document_acl_admin;

-- Dedicated LOGIN migration identities: SET ROLE to NOLOGIN schema owners, never API or superuser.
CREATE ROLE customer_migrator LOGIN PASSWORD 'customer-migrator-dev-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE credit_migrator LOGIN PASSWORD 'credit-migrator-dev-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
CREATE ROLE document_migrator LOGIN PASSWORD 'document-migrator-dev-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
GRANT customer_owner TO customer_migrator;
GRANT credit_owner TO credit_migrator;
GRANT document_owner TO document_migrator;
GRANT CONNECT ON DATABASE customer_db TO customer_migrator;
GRANT CONNECT ON DATABASE credit_db TO credit_migrator;
GRANT CONNECT ON DATABASE documents_db TO document_migrator;
-- pgvector extension provisioning is a privileged bootstrap operation; tables remain EF-owned.
\connect documents_db
CREATE EXTENSION IF NOT EXISTS vector;
