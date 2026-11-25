using VirtualBank.Model.Documents;

namespace VirtualBank.Application.Documents;

/// <summary>Ingestion files are defined by server-owned manifest metadata, not user-supplied paths.</summary>
public sealed record ParsedPage(int PageNumber, string Text);

public sealed record ParsedDocument(string Id, string Title, string SourcePath, string Language,
    int Classification, string? Branch, IReadOnlyList<DocumentAclEntry> InitialAcl, string Text,
    IReadOnlyList<ParsedPage>? Pages = null);

public interface IDocumentSourceReader
{
    Task<IReadOnlyList<ParsedDocument>> ReadManifestAsync(CancellationToken cancellationToken);
}

public sealed record IngestionSummary(int Total, int Updated, int Unchanged, int Chunks);

public static class DocumentChunker
{
    /// <summary>Overlapping text windows preserve local context; independent of source format.</summary>
    public static IEnumerable<string> Split(string text, int maxChars = 1000, int overlap = 120)
    {
        if (maxChars is < 200 or > 4000 || overlap < 0 || overlap >= maxChars / 2)
            throw new ArgumentOutOfRangeException(nameof(maxChars));
        var clean = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) yield break;
        var start = 0;
        while (start < clean.Length)
        {
            var end = Math.Min(start + maxChars, clean.Length);
            if (end < clean.Length)
            {
                var boundary = clean.LastIndexOf(' ', end - 1, end - start);
                if (boundary > start + maxChars / 2) end = boundary;
            }
            yield return clean[start..end];
            if (end == clean.Length) break;
            start = Math.Max(start + 1, end - overlap);
        }
    }
}
