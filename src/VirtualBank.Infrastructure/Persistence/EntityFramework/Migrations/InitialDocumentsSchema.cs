using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

[DbContext(typeof(DocumentsDbContext))]
[Migration("20261123030000_InitialDocumentsSchema")]
public sealed class InitialDocumentsSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("docs");
        // Entity configuration is authoritative for relational DDL; SQL here is only for RLS/functions/grants.
        migrationBuilder.Sql("""
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


""");
        migrationBuilder.CreateTable(
            name: "documents", schema: "docs",
            columns: table => new
            {
            id = table.Column<string>(type: "text", nullable: false),
            title = table.Column<string>(type: "text", nullable: false),
            source_path = table.Column<string>(type: "text", nullable: false),
            language = table.Column<string>(type: "text", nullable: false),
            classification = table.Column<short>(type: "smallint", nullable: false),
            branch = table.Column<string?>(type: "text", nullable: true),
            content_hash = table.Column<string>(type: "text", nullable: false),
            embedding_model = table.Column<string>(type: "text", nullable: false),
            content_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
            acl_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
            updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_docs_documents", x => x.id);
                table.CheckConstraint("docs_id_format", "id ~ '^DOC-[0-9]{3}$'");
                table.CheckConstraint("docs_title_nonempty", "length(trim(title)) > 0");
                table.CheckConstraint("docs_language", "language IN ('en','de','tr')");
                table.CheckConstraint("docs_classification", "classification BETWEEN 0 AND 3");
                table.CheckConstraint("docs_content_version", "content_version > 0");
                table.CheckConstraint("docs_acl_version", "acl_version > 0");
            });
        migrationBuilder.CreateTable(
            name: "chunks", schema: "docs",
            columns: table => new
            {
            id = table.Column<string>(type: "text", nullable: false),
            document_id = table.Column<string>(type: "text", nullable: false),
            chunk_index = table.Column<int>(type: "integer", nullable: false),
            content = table.Column<string>(type: "text", nullable: false),
            page_number = table.Column<int?>(type: "integer", nullable: true),
            embedding = table.Column<Pgvector.Vector>(type: "vector(1024)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_docs_chunks", x => x.id);
                table.ForeignKey("FK_docs_chunks_docs_documents", x => x.document_id, "documents", "id", "docs", onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("docs_chunk_index", "chunk_index >= 0");
                table.CheckConstraint("docs_chunk_nonempty", "length(content) > 0");
            });
        migrationBuilder.CreateTable(
            name: "acls", schema: "docs",
            columns: table => new
            {
            document_id = table.Column<string>(type: "text", nullable: false),
            subject_kind = table.Column<string>(type: "text", nullable: false),
            subject_value = table.Column<string>(type: "text", nullable: false),
            effect = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_docs_acls", x => new { x.document_id, x.subject_kind, x.subject_value, x.effect });
                table.ForeignKey("FK_docs_acls_docs_documents", x => x.document_id, "documents", "id", "docs", onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("docs_acl_kind", "subject_kind IN ('public','user','group','role')");
                table.CheckConstraint("docs_acl_effect", "effect IN ('allow','deny')");
            });
        migrationBuilder.CreateTable(
            name: "acl_history", schema: "docs",
            columns: table => new
            {
            id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
            document_id = table.Column<string>(type: "text", nullable: false),
            version = table.Column<int>(type: "integer", nullable: false),
            changed_by = table.Column<string>(type: "text", nullable: false),
            changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
            changes_json = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_docs_acl_history", x => x.id);
                table.ForeignKey("FK_docs_acl_history_docs_documents", x => x.document_id, "documents", "id", "docs", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_chunks_document_id_chunk_index", "chunks", schema: "docs", columns: new[] { "document_id", "chunk_index" }, unique: true);
        migrationBuilder.CreateIndex("IX_acls_document_id", "acls", "document_id", schema: "docs");
        migrationBuilder.CreateIndex("IX_acl_history_document_id", "acl_history", "document_id", schema: "docs");
        migrationBuilder.CreateIndex("chunks_embedding_hnsw", "chunks", "embedding", schema: "docs")
            .Annotation("Npgsql:IndexMethod", "hnsw")
            .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        migrationBuilder.Sql("""
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
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Development-only rollback. Never drop production schema without an explicit migration review.
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS docs CASCADE;");
    }
}
