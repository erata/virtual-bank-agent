using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace VirtualBank.Infrastructure.Persistence.EntityFramework;

// Design-time metadata only: dotnet ef can scaffold a migration without running the
// Web host, contacting Keycloak, using cloud API keys, or executing the DbMigrator.
// No connection is opened when the model is inspected/scaffolded.
public sealed class CustomerDbContextFactory : IDesignTimeDbContextFactory<CustomerDbContext>
{
    public CustomerDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<CustomerDbContext>()
            .UseNpgsql("Host=localhost;Database=customer_db;Username=customer_migrator")
            .Options);
}

public sealed class CreditDbContextFactory : IDesignTimeDbContextFactory<CreditDbContext>
{
    public CreditDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<CreditDbContext>()
            .UseNpgsql("Host=localhost;Database=credit_db;Username=credit_migrator")
            .Options);
}

public sealed class DocumentsDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseNpgsql("Host=localhost;Database=documents_db;Username=document_migrator", npgsql => npgsql.UseVector())
            .Options);
}
