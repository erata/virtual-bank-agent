using VirtualBank.Application.Common;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Documents.Commands.IngestDocuments;
using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Model.Documents;
using Xunit;

namespace VirtualBank.Application.UnitTests.Documents;

public sealed class DocumentIngestionTests
{
    private sealed class EmbeddingSpy : IEmbeddingProvider
    {
        public string ModelId => "bge-m3";
        public int Dimensions => 1024;
        public int Calls { get; private set; }
        public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new float[Dimensions]);
        }
    }

    private sealed class SourceStub(ParsedDocument document) : IDocumentSourceReader
    {
        public Task<IReadOnlyList<ParsedDocument>> ReadManifestAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ParsedDocument>>([document]);
    }

    private sealed class StoreStub(bool current) : IDocumentIngestionRepository
    {
        public DocumentIngestionEntry? LastEntry { get; private set; }
        public Task<bool> HasCurrentDocumentAsync(BankDocument doc, CancellationToken ct) => Task.FromResult(current);
        public Task<bool> UpsertAsync(DocumentIngestionEntry document, CancellationToken ct)
        {
            LastEntry = document;
            return Task.FromResult(true);
        }
    }

    private static ParsedDocument Source(IReadOnlyList<ParsedPage>? pages = null) =>
        new("DOC-101", "Test", "test.pdf", "de", 0, null,
            [new DocumentAclEntry("public", "*", "allow")],
            pages is null ? "A sample test document with sufficient content to index." :
            string.Join("\n", pages.Select(p => p.Text)), pages);

    [Fact]
    public async Task Unchanged_document_never_calls_embedding_server()
    {
        var embeddings = new EmbeddingSpy();
        var store = new StoreStub(current: true);
        var handler = new IngestDocumentsHandler(new SourceStub(Source()), embeddings, store);
        var result = await handler.HandleAsync(new IngestDocumentsCommand(Personas.Get("service-account-svc-ingestion")), CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Equal(1, result.Value!.Unchanged);
        Assert.Equal(0, embeddings.Calls);
        Assert.Null(store.LastEntry);
    }

    [Fact]
    public async Task Page_numbers_survive_chunking_and_insertion()
    {
        var embeddings = new EmbeddingSpy();
        var store = new StoreStub(current: false);
        var pages = new[]
        {
            new ParsedPage(1, "The first page has enough textual material for a document."),
            new ParsedPage(2, "Die zweite Seite enthält die interne Kreditrichtlinie."),
        };
        var handler = new IngestDocumentsHandler(new SourceStub(Source(pages)), embeddings, store);
        var result = await handler.HandleAsync(new IngestDocumentsCommand(Personas.Get("service-account-svc-ingestion")), CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal(new int?[] { 1, 2 }, store.LastEntry!.Chunks.Select(c => c.PageNumber));
        Assert.Equal(2, embeddings.Calls);
    }
}
