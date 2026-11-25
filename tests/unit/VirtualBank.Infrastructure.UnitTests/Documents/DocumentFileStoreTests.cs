using System.Xml.Linq;
using VirtualBank.Infrastructure.Documents;
using VirtualBank.Specifications;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Documents;

/// <summary>Original-file access for the document viewer: format allow-list, root confinement, DOCX structure.</summary>
public sealed class DocumentFileStoreTests
{
    private static readonly FileSystemDocumentFileStore Store = new(new DocumentRagOptions
    {
        SampleDirectory = Path.Combine(RepositoryPaths.Root, "sample-docs"),
    });

    [Theory]
    [InlineData("public/fees.pdf", "application/pdf")]
    [InlineData("public/fee-faq-2026.json", "application/json; charset=utf-8")]
    [InlineData("credit/approval-policy-2026-current.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("operations/runbook.md", "text/markdown; charset=utf-8")]
    public async Task Allow_listed_originals_are_served_with_an_exact_content_type(string path, string type)
    {
        var file = await Store.ReadAsync(path, CancellationToken.None);

        Assert.NotNull(file);
        Assert.Equal(type, file.ContentType);
        Assert.Equal(Path.GetFileName(path), file.FileName);
        Assert.NotEmpty(file.Content);
    }

    [Theory]
    [InlineData("../compose.yaml")]
    [InlineData("../../etc/passwd")]
    [InlineData("manifest.json")]          // inside the root and .json, but it is the ACL configuration
    [InlineData("public/does-not-exist.pdf")]
    public async Task Paths_outside_the_source_or_unknown_files_are_never_served(string path) =>
        Assert.Null(await Store.ReadAsync(path, CancellationToken.None));

    [Fact]
    public async Task Docx_keeps_title_and_headings_as_structure()
    {
        var blocks = await Store.ReadBlocksAsync("credit/approval-policy-2026-current.docx", CancellationToken.None);

        Assert.NotNull(blocks);
        Assert.Equal("title", blocks[0].Kind);
        Assert.Contains(blocks, b => b.Kind == "heading" && b.Level == 2);
        Assert.Contains(blocks, b => b.Kind == "paragraph");
    }

    [Fact]
    public void Docx_tables_and_lists_become_plain_text_blocks()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = new XElement(w + "body",
            new XElement(w + "p", new XElement(w + "pPr", new XElement(w + "numPr")),
                new XElement(w + "r", new XElement(w + "t", "first item"))),
            new XElement(w + "tbl",
                new XElement(w + "tr",
                    new XElement(w + "tc", new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "Grade")))),
                    new XElement(w + "tc", new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", "<b>Band</b>")))))));

        var blocks = FileSystemDocumentFileStore.DocxBlocks(body).ToArray();

        Assert.Equal("listItem", blocks[0].Kind);
        Assert.Equal("table", blocks[1].Kind);
        // Text is carried verbatim as data; the UI renders it by interpolation, so markup never becomes HTML.
        Assert.Equal(["Grade", "<b>Band</b>"], blocks[1].Rows![0]);
    }

    [Fact]
    public async Task Pdf_has_no_structured_blocks_because_the_browser_shows_the_original()
    {
        Assert.Null(await Store.ReadBlocksAsync("public/fees.pdf", CancellationToken.None));
    }
}
