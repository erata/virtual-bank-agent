#!/usr/bin/env bash
set -euo pipefail
# Requires a REAL indexed pgvector database (make ai-index). Expectations mirror sample-docs/manifest.json.
psql -X -v ON_ERROR_STOP=1 -d documents_db <<'SQL'
-- Each case uses the exact runtime role, verified-principal-style context and actual indexed chunks.
BEGIN;
SET LOCAL ROLE document_api;
DO $$ BEGIN
    IF (SELECT count(*) FROM docs.chunks) <> 0 THEN
        RAISE EXCEPTION 'Document RLS fail-open without principal context';
    END IF;
END $$;
-- Anna, Stuttgart customer advisor, internal clearance: sees public and Stuttgart-only document.
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000004',true),
       set_config('app.permissions','document.read',true),
       set_config('app.clearance','1',true),
       set_config('app.roles','["customer-advisor"]',true),
       set_config('app.groups','["/Customer/Stuttgart","/Customer"]',true),
       set_config('app.branch','STR',true);
DO $$ BEGIN
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-101') = 0 THEN
        RAISE EXCEPTION 'Anna must see public DOC-101';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-104') = 0 THEN
        RAISE EXCEPTION 'Anna must see branch-specific DOC-104';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-105') <> 0 THEN
        RAISE EXCEPTION 'Anna must not see confidential credit DOC-105';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-106') <> 0 THEN
        RAISE EXCEPTION 'Anna must not see HR salary DOC-106';
    END IF;
    IF docs.readable_acl('DOC-106') IS NOT NULL THEN
        RAISE EXCEPTION 'RLS-denied ACL metadata is visible to document_api';
    END IF;
    IF docs.readable_acl('DOC-101') IS NULL THEN
        RAISE EXCEPTION 'Authorized reader must receive allow-checked ACL snapshot';
    END IF;
END $$;
-- Mehmet: credit analyst, no branch, confidential, /Credit/Analysis expands to /Credit.
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000005',true),
       set_config('app.clearance','2',true),
       set_config('app.roles','["credit-analyst"]',true),
       set_config('app.groups','["/Credit/Analysis","/Credit"]',true),
       set_config('app.branch','',true);
DO $$ BEGIN
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-105') = 0 THEN
        RAISE EXCEPTION 'Mehmet must see allowed credit document';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-104') <> 0 THEN
        RAISE EXCEPTION 'Mehmet must not bypass Stuttgart branch restriction';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-106') <> 0 THEN
        RAISE EXCEPTION 'Mehmet must not see HR salary bands';
    END IF;
END $$;
-- Sophie: restricted clearance and hr-specialist role, both mandatory.
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000007',true),
       set_config('app.clearance','3',true),
       set_config('app.roles','["hr-specialist"]',true),
       set_config('app.groups','["/HR"]',true);
DO $$ BEGIN
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-106') = 0 OR
       (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-114') = 0 THEN
        RAISE EXCEPTION 'Sophie must see both HR documents (clearance + role)';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-105') <> 0 THEN
        RAISE EXCEPTION 'Sophie must not see confidential credit document';
    END IF;
END $$;
-- Oliver: an actual operations persona with document.read and classification 2.
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000010',true),
       set_config('app.clearance','2',true),
       set_config('app.roles','["operations-operator"]',true),
       set_config('app.groups','["/IT/Operations","/IT"]',true);
DO $$ BEGIN
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-109') = 0 OR
       (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-115') = 0 THEN
        RAISE EXCEPTION 'Oliver must see operations documents';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-106') <> 0 THEN
        RAISE EXCEPTION 'Oliver must not see restricted HR documents';
    END IF;
END $$;
-- Deniz: subject-bound bank-customer may read public documents, never internal/confidential.
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000011',true),
       set_config('app.permissions','document.read,customer.read.self,assistant.use',true),
       set_config('app.clearance','0',true),
       set_config('app.roles','["bank-customer"]',true),
       set_config('app.groups','[]',true),
       set_config('app.branch','MUC',true);
DO $$ BEGIN
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-101') = 0 OR
       (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-117') = 0 THEN
        RAISE EXCEPTION 'Deniz must see public account and current fee documents';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-104') <> 0 OR
       (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-105') <> 0 OR
       (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-106') <> 0 THEN
        RAISE EXCEPTION 'Bank customer must not see internal, credit, or HR documents';
    END IF;
END $$;
-- Lena: even public docs require document.read, admin role does not imply business data access.
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000001',true),
       set_config('app.permissions','system.configure,operations.read',true),
       set_config('app.clearance','3',true),
       set_config('app.roles','["platform-admin"]',true);
DO $$ BEGIN
    IF (SELECT count(*) FROM docs.chunks) <> 0 THEN
        RAISE EXCEPTION 'Platform admin must not bypass missing document.read';
    END IF;
END $$;
ROLLBACK;

-- Real explicit deny test: Anna first matches a public ALLOW on DOC-101, then also a user DENY.
-- Any denial caused by a missing allow would be a false-positive test: prove initial access first.
BEGIN;
INSERT INTO docs.acls(document_id,subject_kind,subject_value,effect)
VALUES ('DOC-101','user','00000000-0000-4000-8000-000000000004','deny');
SET LOCAL ROLE document_api;
SELECT set_config('app.principal_id','00000000-0000-4000-8000-000000000004',true),
       set_config('app.permissions','document.read',true),
       set_config('app.clearance','1',true),
       set_config('app.roles','["customer-advisor"]',true),
       set_config('app.groups','["/Customer/Stuttgart","/Customer"]',true),
       set_config('app.branch','STR',true);
DO $$ BEGIN
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-101') <> 0 THEN
        RAISE EXCEPTION 'Explicit user deny must override matching public allow';
    END IF;
    IF (SELECT count(*) FROM docs.chunks WHERE document_id='DOC-102') = 0 THEN
        RAISE EXCEPTION 'Explicit deny for DOC-101 must not deny unrelated public DOC-102';
    END IF;
END $$;
ROLLBACK;
SQL
printf 'Document RLS: 6 personas, positive access, branch, metadata, explicit deny over allow passed.\n'
