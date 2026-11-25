using System.IO.Compression;
using System.Xml.Linq;
using VirtualBank.Application.Documents;

namespace VirtualBank.Infrastructure.Documents;

/// <summary>
/// Originals from the configured source folder. The relative path comes from the documents table (written by
/// ingestion from the server-owned manifest), is re-validated against the root, and only allow-listed formats
/// are served. A different source (Nextcloud, SharePoint) would be another implementation of the same port.
/// </summary>
public sealed class FileSystemDocumentFileStore(DocumentRagOptions options) : IDocumentFileStore
{
    public const long MaxFileBytes = 20 * 1024 * 1024;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".json"] = "application/json; charset=utf-8",
        [".md"] = "text/markdown; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
    };

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public async Task<DocumentFile?> ReadAsync(string sourcePath, CancellationToken ct)
    {
        if (Resolve(sourcePath) is not { } path || !ContentTypes.TryGetValue(Path.GetExtension(path), out var type))
            return null;
        if (new FileInfo(path).Length > MaxFileBytes) return null;
        return new DocumentFile(await File.ReadAllBytesAsync(path, ct), type, Path.GetFileName(path));
    }

    public async Task<IReadOnlyList<DocumentBlock>?> ReadBlocksAsync(string sourcePath, CancellationToken ct)
    {
        if (Resolve(sourcePath) is not { } path ||
            !Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase)) return null;
        await using var file = File.OpenRead(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return null;
        await using var stream = entry.Open();
        var xml = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        var body = xml.Root?.Element(W + "body");
        return body is null ? [] : DocxBlocks(body).ToArray();
    }

    /// <summary>Top-level paragraphs and tables in document order. Only text and structure survive.</summary>
    internal static IEnumerable<DocumentBlock> DocxBlocks(XElement body)
    {
        foreach (var element in body.Elements())
        {
            if (element.Name == W + "tbl")
            {
                var rows = element.Elements(W + "tr")
                    .Select(tr => (IReadOnlyList<string>)tr.Elements(W + "tc").Select(Text).ToArray())
                    .Where(r => r.Count > 0).ToArray();
                if (rows.Length > 0) yield return new DocumentBlock("table", 0, null, rows);
                continue;
            }
            if (element.Name != W + "p") continue;
            var text = Text(element);
            if (string.IsNullOrWhiteSpace(text)) continue;
            var style = element.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value ?? "";
            var isList = element.Element(W + "pPr")?.Element(W + "numPr") is not null;
            yield return style switch
            {
                "Title" => new DocumentBlock("title", 0, text),
                "Subtitle" => new DocumentBlock("subtitle", 0, text),
                _ when style.StartsWith("Heading", StringComparison.Ordinal) &&
                       int.TryParse(style.AsSpan("Heading".Length), out var level) =>
                    new DocumentBlock("heading", Math.Clamp(level, 1, 4), text),
                _ when isList || style.StartsWith("List", StringComparison.Ordinal) => new DocumentBlock("listItem", 0, text),
                _ => new DocumentBlock("paragraph", 0, text),
            };
        }
    }

    private static string Text(XElement element) =>
        string.Concat(element.Descendants().Select(d =>
            d.Name == W + "t" ? d.Value : d.Name == W + "tab" ? "\t" : d.Name == W + "br" ? "\n" : ""));

    private string? Resolve(string relative)
    {
        var root = Path.GetFullPath(options.SampleDirectory);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full) ||
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            return null;
        // The manifest holds the initial ACLs; it is configuration, never a readable document.
        if (string.Equals(full, Path.Combine(root, "manifest.json"), StringComparison.OrdinalIgnoreCase))
            return null;
        return full;
    }
}
