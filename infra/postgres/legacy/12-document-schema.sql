-- VirtualBank M3 document store. Every document/chunk read is protected by actual PostgreSQL RLS.
\connect documents_db
CREATE EXTENSION IF NOT EXISTS vector;
SET ROLE document_owner;
CREATE SCHEMA docs;

CREATE TABLE docs.documents (
    id text PRIMARY KEY CHECK (id ~ '^DOC-[0-9]{3}$'),
    title text NOT NULL CHECK (length(trim(title)) > 0),
    source_path text NOT NULL,
    language text NOT NULL CHECK (language IN ('en','de','tr')),
    classification smallint NOT NULL CHECK (classification BETWEEN 0 AND 3),
    branch text NULL,
    content_hash text NOT NULL,
    embedding_model text NOT NULL,
    content_version integer NOT NULL DEFAULT 1 CHECK (content_version > 0),
    acl_version integer NOT NULL DEFAULT 1 CHECK (acl_version > 0),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE docs.chunks (
    id text PRIMARY KEY,
    document_id text NOT NULL REFERENCES docs.documents(id) ON DELETE CASCADE,
    chunk_index integer NOT NULL CHECK (chunk_index >= 0),
    content text NOT NULL CHECK (length(content) > 0),
    page_number integer NULL,
    embedding vector(1024) NOT NULL,
    UNIQUE (document_id, chunk_index)
);
CREATE INDEX chunks_document_idx ON docs.chunks(document_id);
CREATE TABLE docs.acls (
    document_id text NOT NULL REFERENCES docs.documents(id) ON DELETE CASCADE,
    subject_kind text NOT NULL CHECK (subject_kind IN ('public','user','group','role')),
    subject_value text NOT NULL,
    effect text NOT NULL CHECK (effect IN ('allow','deny')),
    PRIMARY KEY(document_id, subject_kind, subject_value, effect)
);
CREATE TABLE docs.acl_history (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    document_id text NOT NULL REFERENCES docs.documents(id) ON DELETE CASCADE,
    version integer NOT NULL,
    changed_by text NOT NULL,
    changed_at timestamptz NOT NULL DEFAULT now(),
    changes_json jsonb NOT NULL
);

-- SECURITY DEFINER permits a read-only runtime role to evaluate private ACL rows without getting SELECT on them.
-- All decisions require verified transaction-local context. No table/owner/database user may bypass FORCE RLS.
CREATE FUNCTION docs.can_read_document(p_id text, p_classification int, p_branch text)
RETURNS boolean LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path = pg_catalog, docs AS $$
DECLARE
    v_user text := nullif(current_setting('app.principal_id', true), '');
    v_permissions text := current_setting('app.permissions', true);
    v_clearance text := current_setting('app.clearance', true);
    v_branch text := current_setting('app.branch', true);
    v_groups jsonb := '[]'::jsonb;
    v_roles jsonb := '[]'::jsonb;
BEGIN
    IF v_user IS NULL OR v_permissions IS NULL OR
       NOT ('document.read' = ANY (string_to_array(v_permissions, ','))) OR
       v_clearance IS NULL OR v_clearance !~ '^[0-3]$' OR
       p_classification > v_clearance::int OR
       (p_branch IS NOT NULL AND p_branch <> coalesce(v_branch, '')) THEN
        RETURN false;
    END IF;
    BEGIN
        v_groups := coalesce(nullif(current_setting('app.groups', true), '')::jsonb, '[]'::jsonb);
        v_roles := coalesce(nullif(current_setting('app.roles', true), '')::jsonb, '[]'::jsonb);
        IF jsonb_typeof(v_groups) <> 'array' OR jsonb_typeof(v_roles) <> 'array' THEN RETURN false; END IF;
    EXCEPTION WHEN others THEN
        RETURN false;
    END;
    IF EXISTS (SELECT 1 FROM docs.acls a WHERE a.document_id = p_id AND a.effect = 'deny'
        AND ((a.subject_kind='public' AND a.subject_value='*')
             OR (a.subject_kind='user' AND a.subject_value=v_user)
             OR (a.subject_kind='role' AND v_roles ? a.subject_value)
             OR (a.subject_kind='group' AND v_groups ? a.subject_value))) THEN
        RETURN false;
    END IF;
    RETURN EXISTS (SELECT 1 FROM docs.acls a WHERE a.document_id = p_id AND a.effect = 'allow'
        AND ((a.subject_kind='public' AND a.subject_value='*')
             OR (a.subject_kind='user' AND a.subject_value=v_user)
             OR (a.subject_kind='role' AND v_roles ? a.subject_value)
             OR (a.subject_kind='group' AND v_groups ? a.subject_value)));
END $$;
REVOKE ALL ON FUNCTION docs.can_read_document(text,int,text) FROM PUBLIC;

-- A SECURITY DEFINER, allow-checked projection of canonical ACL metadata for application-level
-- defense in depth. The runtime role still cannot SELECT the ACL table directly.
-- The function reads the persisted document classification/branch rather than trusting caller inputs.
CREATE FUNCTION docs.readable_acl(p_id text)
RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path = pg_catalog, docs AS $$
DECLARE
    v_class int;
    v_branch text;
BEGIN
    SELECT classification, branch INTO v_class, v_branch FROM docs.documents WHERE id=p_id;
    IF NOT FOUND OR NOT docs.can_read_document(p_id, v_class, v_branch) THEN RETURN NULL; END IF;
    RETURN (SELECT coalesce(jsonb_agg(jsonb_build_object(
        'subjectKind', subject_kind, 'subjectValue', subject_value, 'effect', effect)
        ORDER BY subject_kind, subject_value, effect), '[]'::jsonb)
      FROM docs.acls WHERE document_id=p_id);
END $$;
REVOKE ALL ON FUNCTION docs.readable_acl(text) FROM PUBLIC;

ALTER TABLE docs.documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE docs.documents FORCE ROW LEVEL SECURITY;
ALTER TABLE docs.chunks ENABLE ROW LEVEL SECURITY;
ALTER TABLE docs.chunks FORCE ROW LEVEL SECURITY;

-- Internal NOLOGIN schema owner can read metadata inside the allow-checked SECURITY DEFINER function.
-- FORCE RLS remains enabled; this policy does not apply to document_api.
CREATE POLICY document_owner_metadata ON docs.documents FOR SELECT TO document_owner USING (true);
CREATE POLICY document_api_read ON docs.documents FOR SELECT TO document_api
    USING (docs.can_read_document(id, classification, branch));
CREATE POLICY document_api_chunk_read ON docs.chunks FOR SELECT TO document_api
    USING (EXISTS (SELECT 1 FROM docs.documents d WHERE d.id=chunks.document_id));

-- Ingestion writes can only be reached through a separately authenticated machine-only API use case.
CREATE POLICY document_ingestion_all ON docs.documents TO document_ingest USING (true) WITH CHECK (true);
CREATE POLICY chunk_ingestion_all ON docs.chunks TO document_ingest USING (true) WITH CHECK (true);
-- ACL administration can see document METADATA, not chunk content; column grants enforce this.
CREATE POLICY document_admin_metadata ON docs.documents FOR SELECT TO document_acl_admin USING (true);
CREATE POLICY document_admin_version ON docs.documents FOR UPDATE TO document_acl_admin USING (true) WITH CHECK (true);

RESET ROLE;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA docs TO document_api, document_ingest, document_acl_admin;
GRANT EXECUTE ON FUNCTION docs.can_read_document(text,int,text) TO document_api;
GRANT EXECUTE ON FUNCTION docs.readable_acl(text) TO document_api;
GRANT SELECT ON docs.documents, docs.chunks TO document_api;
GRANT SELECT, INSERT, UPDATE, DELETE ON docs.documents, docs.chunks, docs.acls TO document_ingest;
GRANT SELECT(id,title,acl_version,classification,branch), UPDATE(acl_version,updated_at) ON docs.documents TO document_acl_admin;
GRANT SELECT, INSERT, DELETE ON docs.acls TO document_acl_admin;
GRANT SELECT, INSERT ON docs.acl_history TO document_acl_admin;
GRANT USAGE, SELECT ON SEQUENCE docs.acl_history_id_seq TO document_acl_admin;
-- Runtime principals have no CREATE privilege, do not own tables, and have no BYPASSRLS.
