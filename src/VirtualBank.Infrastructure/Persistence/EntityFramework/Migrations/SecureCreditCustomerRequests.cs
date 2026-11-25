using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

/// <summary>
/// Bank customers apply for credit themselves (credit.request). An application they create is SUBMITTED, prepared by
/// them (so four-eyes holds automatically), unassigned and undecided; approvers then decide it as usual. Customers
/// read only applications they prepared. Numbers come from a sequence via a SECURITY DEFINER function, because RLS
/// hides other applications and "max + 1" would collide. SQL only.
/// </summary>
[DbContext(typeof(CreditDbContext))]
[Migration("20261125050000_SecureCreditCustomerRequests")]
public sealed class SecureCreditCustomerRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE SEQUENCE credit.application_number_seq START 300 MINVALUE 300 MAXVALUE 999 NO CYCLE;

CREATE FUNCTION credit.next_application_id() RETURNS text
    LANGUAGE sql VOLATILE SECURITY DEFINER
    SET search_path = pg_catalog, credit
    AS $$ SELECT 'K-' || lpad(nextval('credit.application_number_seq')::text, 3, '0') $$;
REVOKE ALL ON FUNCTION credit.next_application_id() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION credit.next_application_id() TO credit_api;

CREATE POLICY applications_customer_request ON credit.credit_applications
    FOR INSERT TO credit_api
    WITH CHECK (
        authz.has_permission('credit.request')
        AND prepared_by = authz.principal_id()
        AND status = 'SUBMITTED' AND approved_by IS NULL AND approved_at IS NULL
        AND assigned_analyst_id IS NULL AND version = 1
        AND amount > 0 AND amount <= 250000);

CREATE POLICY applications_read_own_request ON credit.credit_applications
    FOR SELECT TO credit_api
    USING (authz.has_permission('credit.request') AND prepared_by = authz.principal_id());

GRANT INSERT ON credit.credit_applications TO credit_api;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Security migrations must never be rolled back automatically.");
}
