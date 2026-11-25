using VirtualBank.Infrastructure.Documents;
using VirtualBank.Specifications;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Documents;

public sealed class PdfPageExtractionTests
{
    [Fact]
    public async Task Pdf_manifest_documents_retain_real_source_page_numbers()
    {
        var source = new FileSystemDocumentSource(new DocumentRagOptions
        {
            SampleDirectory = Path.Combine(RepositoryPaths.Root, "sample-docs"),
        });
        var parsed = await source.ReadManifestAsync(CancellationToken.None);
        var pdf = parsed.Where(d => d.SourcePath.EndsWith(".pdf", StringComparison.Ordinal)).ToArray();
        // The corpus grew from 3 to 8 PDFs (README: 8 PDF incl. five three-page ones); count from the manifest
        // so the test checks "every PDF is parsed", not a stale constant.
        var manifestPdfCount = System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.Root, "sample-docs", "manifest.json")))
            .RootElement.EnumerateArray()
            .Count(d => d.GetProperty("path").GetString()!.EndsWith(".pdf", StringComparison.Ordinal));
        Assert.True(manifestPdfCount >= 3, "The sample corpus must keep PDF fixtures.");
        Assert.Equal(manifestPdfCount, pdf.Length);
        Assert.All(pdf, document =>
        {
            Assert.NotNull(document.Pages);
            Assert.NotEmpty(document.Pages!);
            Assert.All(document.Pages!, page => Assert.True(page.PageNumber >= 1));
            var numbers = document.Pages!.Select(p => p.PageNumber).ToArray();
            Assert.Equal(numbers.Length, numbers.Distinct().Count());
        });
        // Real source pages, not "everything is page 1": the multi-page fixtures must surface later pages.
        Assert.Contains(pdf, d => d.Pages!.Any(p => p.PageNumber > 1));
    }
}
