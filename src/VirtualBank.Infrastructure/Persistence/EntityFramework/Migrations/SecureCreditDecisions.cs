using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

/// <summary>
/// Rejection as a second credit decision, decision notes, and read access to one's own decisions.
/// SQL only (policies and grants); the EF model is unchanged.
/// </summary>
[DbContext(typeof(CreditDbContext))]
[Migration("20261125010000_SecureCreditDecisions")]
public sealed class SecureCreditDecisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
-- Rejection. Its USING is deliberately IDENTICAL to applications_approve: permissive policies are OR-ed, so a
-- wider USING here would let an over-limit approver reach the APPROVED branch of the other WITH CHECK.
CREATE POLICY applications_reject ON credit.credit_applications
    FOR UPDATE TO credit_approval
    USING (
        authz.has_permission('credit.approve')
        AND status = 'SUBMITTED'
        AND amount <= coalesce(authz.approval_limit(), 0)
        AND prepared_by <> coalesce(authz.principal_id(), ''))
    WITH CHECK (status = 'REJECTED' AND approved_by IS NULL);

-- The decider writes the reason/comment as a note in the same transaction (before the state change, while the
-- application is still visible to the decider).
CREATE POLICY notes_insert_decision ON credit.credit_notes
    FOR INSERT TO credit_approval
    WITH CHECK (
        authz.has_permission('credit.approve')
        AND author_id = authz.principal_id()
        AND EXISTS (SELECT 1 FROM credit.credit_applications a WHERE a.id = credit_notes.application_id));
GRANT INSERT ON credit.credit_notes TO credit_approval;

-- The decision log is readable by the person who decided (for "my decisions"); nobody else, nothing else.
CREATE POLICY approval_requests_read_own ON credit.approval_requests
    FOR SELECT TO credit_api
    USING (requested_by = authz.principal_id());
GRANT SELECT ON credit.approval_requests TO credit_api;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
