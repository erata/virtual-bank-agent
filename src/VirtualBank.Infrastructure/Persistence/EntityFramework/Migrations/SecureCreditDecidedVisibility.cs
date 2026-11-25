using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

/// <summary>
/// PostgreSQL also checks the NEW row of an UPDATE against the SELECT policies. A REJECTED row (no approved_by) was
/// not visible to the deciding approver, so every rejection failed. The executor role may now see the rows it has
/// decided, via its own entry in the decision log (written in the same transaction, before the UPDATE).
/// Only credit_approval gets this; the API role's visibility is unchanged.
/// </summary>
[DbContext(typeof(CreditDbContext))]
[Migration("20261125030000_SecureCreditDecidedVisibility")]
public sealed class SecureCreditDecidedVisibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE POLICY applications_read_decided ON credit.credit_applications
    FOR SELECT TO credit_approval
    USING (
        authz.has_permission('credit.approve')
        AND EXISTS (SELECT 1 FROM credit.approval_requests r
                    WHERE r.application_id = credit_applications.id
                      AND r.requested_by = authz.principal_id()));
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
