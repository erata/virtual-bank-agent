using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using VirtualBank.Application.Documents;
using VirtualBank.Model.Documents;

namespace VirtualBank.Infrastructure.Documents;

/// <summary>Manifest-limited file ingestion. Server owns paths and initial ACLs; reindex never resets canonical DB ACL.</summary>
public sealed class FileSystemDocumentSource(DocumentRagOptions options) : IDocumentSourceReader
{
    private sealed record ManifestItem(string Id, string Title, string Path, string Language,
        int Classification, string? Branch, List<DocumentAclEntry> Acl);

    public async Task<IReadOnlyList<ParsedDocument>> ReadManifestAsync(CancellationToken ct)
    {
        var root = Path.GetFullPath(options.SampleDirectory);
        var manifest = Path.Combine(root, "manifest.json");
        await using var fs = File.OpenRead(manifest);
        var items = await JsonSerializer.DeserializeAsync<List<ManifestItem>>(fs,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }, ct)
            ?? throw new InvalidOperationException("The document manifest is empty.");
        var parsed = new List<ParsedDocument>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(item.Id, @"^DOC-[0-9]{3}$") || !seen.Add(item.Id))
                throw new InvalidOperationException("Duplicate or invalid document ID in manifest.");
            if (item.Classification is < 0 or > 3 || item.Language is not ("en" or "de" or "tr") ||
                item.Acl is null || item.Acl.Count == 0)
                throw new InvalidOperationException("Invalid manifest classification, language, or ACL.");
            foreach (var acl in item.Acl)
                if (acl.SubjectKind is not ("public" or "user" or "group" or "role") ||
                    acl.Effect is not ("allow" or "deny") || string.IsNullOrWhiteSpace(acl.SubjectValue))
                    throw new InvalidOperationException("Invalid manifest ACL entry.");
            var fullPath = Path.GetFullPath(Path.Combine(root, item.Path));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A document path escapes the configured source directory.");
            IReadOnlyList<ParsedPage>? pages = null;
            string text;
            if (Path.GetExtension(fullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                // Preserve source page numbers for citations, including for multi-page PDFs.
                using var pdf = PdfDocument.Open(fullPath);
                pages = pdf.GetPages()
                    .Select(page => new ParsedPage(page.Number, ContentOrderTextExtractor.GetText(page)))
                    .Where(page => !string.IsNullOrWhiteSpace(page.Text)).ToArray();
                text = string.Join("\n", pages.Select(page => page.Text));
            }
            else
            {
                text = await ReadTextAsync(fullPath, ct);
            }
            if (text.Length < 20 || text.Length > 2_000_000)
                throw new InvalidOperationException("Document text length is outside accepted limits.");
            parsed.Add(new ParsedDocument(item.Id, item.Title, item.Path, item.Language,
                item.Classification, item.Branch, item.Acl, text, pages));
        }
        return parsed;
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken ct)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".txt":
            case ".md": return await File.ReadAllTextAsync(path, ct);
            case ".json":
                using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct)))
                    return json.RootElement.ToString();
            case ".docx":
                using (var file = File.OpenRead(path))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("DOCX has no document XML.");
                    await using var entryStream = entry.Open();
                    var xml = await XDocument.LoadAsync(entryStream, LoadOptions.None, ct);
                    XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    return string.Join("\n", xml.Descendants(word + "p")
                        .Select(p => string.Join("", p.Descendants(word + "t").Select(t => t.Value))));
                }
            default: throw new NotSupportedException("Unsupported document format.");
        }
    }
}
