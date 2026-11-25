using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualBank.Infrastructure.Persistence.EntityFramework;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework.Migrations;

[DbContext(typeof(DocumentsDbContext))]
[Migration("20261124010000_SecureDocumentsVectorUsage")]
public sealed class SecureDocumentsVectorUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // InitialDocumentsSchema revokes ALL on schema public from PUBLIC. pgvector lives in public, so the
        // read role could resolve the vector TYPE (bound by OID) but not the <=> operator, and every search
        // failed with 42883. USAGE only: lookup of extension objects. No CREATE, no table rights, RLS unchanged.
        migrationBuilder.Sql("GRANT USAGE ON SCHEMA public TO document_api;");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("REVOKE USAGE ON SCHEMA public FROM document_api;");
}
