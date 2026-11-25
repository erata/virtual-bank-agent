using System.Text.RegularExpressions;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Documents.Authorization;

namespace VirtualBank.Application.Documents;

public sealed record DocumentExcerptSection(string Content, int? PageNumber);
public sealed record DocumentExcerpt(string DocumentId, string Title, IReadOnlyList<DocumentExcerptSection> Sections);
/// <summary>Where the original lives. Never sent to the model; used by the viewer and file download only.</summary>
public sealed record DocumentSourceInfo(string SourcePath, string Language, DateTimeOffset UpdatedAt);
public sealed record DocumentExcerptCandidate(DocumentExcerpt Excerpt, DocumentResource Resource,
    DocumentSourceInfo? Source = null);

/// <summary>One structural element of a rendered document (DOCX). Plain text only: no markup is passed on.</summary>
public sealed record DocumentBlock(string Kind, int Level, string? Text, IReadOnlyList<IReadOnlyList<string>>? Rows = null);

/// <summary>The original file bytes, served only after the same authorization as the text view.</summary>
public sealed record DocumentFile(byte[] Content, string ContentType, string FileName);

/// <summary>Reads originals from the document source (today a folder; later e.g. a Nextcloud/SharePoint connector).</summary>
public interface IDocumentFileStore
{
    Task<DocumentFile?> ReadAsync(string sourcePath, CancellationToken ct);

    /// <summary>Structured blocks for formats the browser cannot show natively (DOCX); null otherwise.</summary>
    Task<IReadOnlyList<DocumentBlock>?> ReadBlocksAsync(string sourcePath, CancellationToken ct);
}
public interface IDocumentExcerptRepository
{
    /// <summary>First chunks only (assistant excerpt). <paramref name="maxChunks"/> bounds the read.</summary>
    Task<DocumentExcerptCandidate?> ReadAsync(PrincipalContext principal, string documentId, CancellationToken ct,
        int maxChunks = 6, int maxCharsPerChunk = 900);
}

/// <summary>Whole indexed text of one document, grouped by page (null page = non-paginated source).</summary>
public sealed record DocumentPage(int? PageNumber, string Text);
public sealed record DocumentContent(string DocumentId, string Title, Clearance Classification, IReadOnlyList<DocumentPage> Pages,
    string? FileName = null, string? Format = null, string? Language = null, DateTimeOffset? UpdatedAt = null,
    IReadOnlyList<DocumentBlock>? Blocks = null);

/// <summary>Rebuilds readable text from overlapping retrieval chunks without repeating the overlap.</summary>
public static class DocumentTextAssembler
{
    private const int MaxOverlap = 400;

    public static IReadOnlyList<DocumentPage> Assemble(IEnumerable<DocumentExcerptSection> sections) =>
        sections
            .GroupBy(s => s.PageNumber)
            .OrderBy(g => g.Key ?? 0)
            .Select(g => new DocumentPage(g.Key, Merge(g.Select(s => s.Content))))
            .ToArray();

    public static string Merge(IEnumerable<string> chunks)
    {
        var text = new System.Text.StringBuilder();
        foreach (var chunk in chunks)
        {
            if (text.Length == 0) { text.Append(chunk); continue; }
            var overlap = 0;
            for (var k = Math.Min(MaxOverlap, Math.Min(chunk.Length, text.Length)); k > 0; k--)
            {
                if (EndsWith(text, chunk.AsSpan(0, k))) { overlap = k; break; }
            }
            if (overlap == 0) text.Append(' ');
            text.Append(chunk, overlap, chunk.Length - overlap);
        }
        return text.ToString();
    }

    private static bool EndsWith(System.Text.StringBuilder text, ReadOnlySpan<char> suffix)
    {
        var offset = text.Length - suffix.Length;
        for (var i = 0; i < suffix.Length; i++)
            if (text[offset + i] != suffix[i]) return false;
        return true;
    }
}

/// <summary>Document-by-ID excerpt (not original-file download). DB RLS + app ACL are both mandatory.</summary>
public sealed class SecureDocumentExcerpt(IDocumentExcerptRepository repository,
    IResourceAuthorizationService authorization, IAuthorizationAudit audit, IDocumentFileStore? files = null)
{
    public async Task<UseCaseResult<DocumentExcerpt>> ReadAsync(PrincipalContext principal,
        string documentId, CancellationToken ct)
    {
        if (!principal.HasPermission(KnownPermissions.DocumentRead) || principal.Type == PrincipalType.Machine)
            return UseCaseResult<DocumentExcerpt>.Forbidden(null);
        if (!Regex.IsMatch(documentId, @"^DOC-[0-9]{3}$", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(50)))
            return UseCaseResult<DocumentExcerpt>.NotFound();
        var candidate = await repository.ReadAsync(principal, documentId, ct);
        if (candidate is null) return UseCaseResult<DocumentExcerpt>.NotFound();
        var decision = await authorization.AuthorizeAsync(principal, candidate.Resource,
            KnownPermissions.DocumentRead, ct);
        audit.Record(principal, decision);
        if (!decision.IsAllowed)
        {
            audit.RecordPolicyRlsMismatch(principal, decision);
            return UseCaseResult<DocumentExcerpt>.NotFound();
        }
        return UseCaseResult<DocumentExcerpt>.Success(candidate.Excerpt);
    }

    /// <summary>Upper bound for one document view; the demo corpus stays far below it.</summary>
    public const int MaxFullChunks = 400;

    /// <summary>
    /// Full readable text for the document viewer. Same gates as <see cref="ReadAsync"/>: permission, DB RLS,
    /// then the application ACL policy; a policy/RLS disagreement is reported as not found and audited.
    /// </summary>
    public async Task<UseCaseResult<DocumentContent>> ReadFullAsync(PrincipalContext principal,
        string documentId, CancellationToken ct)
    {
        var (status, candidate) = await AuthorizedAsync(principal, documentId, MaxFullChunks, ct);
        if (candidate is null) return status == UseCaseStatus.Forbidden
            ? UseCaseResult<DocumentContent>.Forbidden(null) : UseCaseResult<DocumentContent>.NotFound();
        var source = candidate.Source;
        var blocks = source is not null && files is not null && FormatOf(source.SourcePath) == "docx"
            ? await files.ReadBlocksAsync(source.SourcePath, ct) : null;
        return UseCaseResult<DocumentContent>.Success(new DocumentContent(candidate.Excerpt.DocumentId,
            candidate.Excerpt.Title, (Clearance)candidate.Resource.Classification,
            DocumentTextAssembler.Assemble(candidate.Excerpt.Sections),
            source is null ? null : Path.GetFileName(source.SourcePath),
            source is null ? null : FormatOf(source.SourcePath),
            source?.Language, source?.UpdatedAt, blocks));
    }

    /// <summary>
    /// The original file (PDF, DOCX, JSON ...) for the viewer. Authorized exactly like the text view; a document
    /// that is not readable is "not found". The path comes from the database row, never from the request.
    /// </summary>
    public async Task<UseCaseResult<DocumentFile>> ReadOriginalAsync(PrincipalContext principal,
        string documentId, CancellationToken ct)
    {
        var (status, candidate) = await AuthorizedAsync(principal, documentId, 1, ct);
        if (candidate is null) return status == UseCaseStatus.Forbidden
            ? UseCaseResult<DocumentFile>.Forbidden(null) : UseCaseResult<DocumentFile>.NotFound();
        if (files is null || candidate.Source is null) return UseCaseResult<DocumentFile>.NotFound();
        var file = await files.ReadAsync(candidate.Source.SourcePath, ct);
        return file is null ? UseCaseResult<DocumentFile>.NotFound() : UseCaseResult<DocumentFile>.Success(file);
    }

    public static string FormatOf(string sourcePath) =>
        Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();

    private async Task<(UseCaseStatus Status, DocumentExcerptCandidate? Candidate)> AuthorizedAsync(
        PrincipalContext principal, string documentId, int chunks, CancellationToken ct)
    {
        if (!principal.HasPermission(KnownPermissions.DocumentRead) || principal.Type == PrincipalType.Machine)
            return (UseCaseStatus.Forbidden, null);
        if (!Regex.IsMatch(documentId, @"^DOC-[0-9]{3}$", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(50)))
            return (UseCaseStatus.NotFound, null);
        var candidate = await repository.ReadAsync(principal, documentId, ct, chunks, int.MaxValue);
        if (candidate is null) return (UseCaseStatus.NotFound, null);
        var decision = await authorization.AuthorizeAsync(principal, candidate.Resource,
            KnownPermissions.DocumentRead, ct);
        audit.Record(principal, decision);
        if (!decision.IsAllowed)
        {
            audit.RecordPolicyRlsMismatch(principal, decision);
            return (UseCaseStatus.NotFound, null);
        }
        return (UseCaseStatus.Succeeded, candidate);
    }
}
