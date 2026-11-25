using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

[DbContext(typeof(CreditDbContext))]
[Migration("20261123021000_SecureCreditSchema")]
public sealed class SecureCreditSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL native policies, FORCE RLS, and minimum privileges follow the EF schema+seed migration.
        migrationBuilder.Sql("""
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
""");
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
