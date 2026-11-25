using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using VirtualBank.Infrastructure.Persistence.EntityFramework;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Persistence;

public sealed class EfModelTests
{
    [Fact]
    public void Customer_accounts_are_mapped_with_precision_fk_and_index()
    {
        var ctx=new CustomerDbContext(new DbContextOptionsBuilder<CustomerDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=none").Options);
        var entity=ctx.Model.FindEntityType(typeof(AccountRow))!;
        Assert.Equal("accounts",entity.GetTableName());
        Assert.Equal("customer",entity.GetSchema());
        Assert.Single(entity.GetForeignKeys());
        Assert.Contains(entity.GetIndexes(),i=>i.GetDatabaseName()=="accounts_customer_id_idx");
    }

    [Fact]
    public void Document_vector_has_1024_dimensions_and_cosine_index()
    {
        var ctx=new DocumentsDbContext(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseNpgsql("Host=localhost;Database=not_used;Username=none",o=>o.UseVector()).Options);
        var entity=ctx.Model.FindEntityType(typeof(DocumentChunkRow))!;
        var vector=entity.FindProperty(nameof(DocumentChunkRow.Embedding))!;
        Assert.Equal("vector(1024)",vector.GetColumnType());
        Assert.Contains(entity.GetIndexes(),i=>i.GetDatabaseName()=="chunks_embedding_hnsw");
    }
}
