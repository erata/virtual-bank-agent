using System.Text.RegularExpressions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Model.Documents;

namespace VirtualBank.Application.Documents;

/// <summary>ACL metadata administration does not confer document content access.</summary>
public sealed record DocumentAclEffectiveView(string DocumentId, int Version,
    int Classification, string? Branch, bool InheritsParent,
    IReadOnlyList<DocumentAclEntry> AllowedEntries, IReadOnlyList<DocumentAclEntry> DeniedEntries,
    string ResolutionRules);

public sealed class DocumentAclAdministration(IDocumentAclAdministrationRepository repository)
{
    public static bool CanManage(PrincipalContext principal) => principal.IsActive &&
        principal.Type != PrincipalType.Machine && principal.HasPermission(KnownPermissions.DocumentAclManage);

    /// <summary>All documents with their ACL version, for the administration screen. Metadata only.</summary>
    public async Task<UseCaseResult<IReadOnlyList<DocumentAclIndexItem>>> ListAsync(PrincipalContext principal, CancellationToken ct) =>
        CanManage(principal)
            ? UseCaseResult<IReadOnlyList<DocumentAclIndexItem>>.Success(await repository.ListAsync(ct))
            : UseCaseResult<IReadOnlyList<DocumentAclIndexItem>>.Forbidden(null);

    public async Task<UseCaseResult<DocumentAclSnapshot>> ReadAsync(PrincipalContext principal, string documentId, CancellationToken ct)
    {
        if (!CanManage(principal)) return UseCaseResult<DocumentAclSnapshot>.Forbidden(null);
        if (!ValidDocumentId(documentId)) return UseCaseResult<DocumentAclSnapshot>.NotFound();
        var snapshot = await repository.ReadAsync(documentId, ct);
        return snapshot is null ? UseCaseResult<DocumentAclSnapshot>.NotFound() : UseCaseResult<DocumentAclSnapshot>.Success(snapshot);
    }

    public async Task<UseCaseResult<DocumentAclSnapshot>> UpdateAsync(PrincipalContext principal, string documentId,
        int expectedVersion, IReadOnlyList<DocumentAclEntry>? entries, CancellationToken ct)
    {
        if (!CanManage(principal)) return UseCaseResult<DocumentAclSnapshot>.Forbidden(null);
        if (!ValidDocumentId(documentId)) return UseCaseResult<DocumentAclSnapshot>.NotFound();
        if (expectedVersion < 1 || entries is null || entries.Count is < 1 or > 100 || entries.Any(e =>
            e.SubjectKind is not ("public" or "user" or "group" or "role") ||
            e.Effect is not ("allow" or "deny") || string.IsNullOrWhiteSpace(e.SubjectValue) ||
            e.SubjectValue.Length > 150 || (e.SubjectKind == "group" && !GroupPath.TryParse(e.SubjectValue, out _)) ||
            (e.SubjectKind == "public" && e.SubjectValue != "*")))
            return UseCaseResult<DocumentAclSnapshot>.Invalid("entries", "Invalid document ACL or version.");
        if (entries.Distinct().Count() != entries.Count)
            return UseCaseResult<DocumentAclSnapshot>.Invalid("entries", "Duplicate ACL entries.");
        var updated = await repository.UpdateAsync(documentId, principal.SubjectId, expectedVersion, entries, ct);
        return updated is null ? UseCaseResult<DocumentAclSnapshot>.Invalid("expectedVersion", "Document missing or ACL version changed.")
            : UseCaseResult<DocumentAclSnapshot>.Success(updated);
    }

    public async Task<UseCaseResult<IReadOnlyList<DocumentAclRevision>>> HistoryAsync(PrincipalContext principal, string documentId, CancellationToken ct)
    {
        if (!CanManage(principal)) return UseCaseResult<IReadOnlyList<DocumentAclRevision>>.Forbidden(null);
        if (!ValidDocumentId(documentId)) return UseCaseResult<IReadOnlyList<DocumentAclRevision>>.NotFound();
        return UseCaseResult<IReadOnlyList<DocumentAclRevision>>.Success(await repository.HistoryAsync(documentId, ct));
    }

    /// <summary>
    /// Reads canonical ACL entries plus a non-user-specific summary of evaluation requirements.
    /// Caller needs ACL metadata permission; this grants no content permission.
    /// An effective decision is always specific to a verified principal and document metadata.
    /// </summary>
    public async Task<UseCaseResult<DocumentAclEffectiveView>> ReadEffectiveAsync(
        PrincipalContext principal, string documentId, CancellationToken ct)
    {
        var result = await ReadAsync(principal, documentId, ct);
        return result.Status switch
        {
            UseCaseStatus.Forbidden => UseCaseResult<DocumentAclEffectiveView>.Forbidden(result.NoticeCode),
            UseCaseStatus.NotFound => UseCaseResult<DocumentAclEffectiveView>.NotFound(),
            _ => UseCaseResult<DocumentAclEffectiveView>.Success(new DocumentAclEffectiveView(
                result.Value!.DocumentId, result.Value!.Version,
                result.Value!.Classification, result.Value!.Branch, false,
                result.Value!.Entries.Where(e => e.Effect == "allow").ToArray(),
                result.Value!.Entries.Where(e => e.Effect == "deny").ToArray(),
                "All access requires document.read, sufficient clearance, matching branch (if any), " +
                "an allowed principal and no matching explicit deny. No folder inheritance is implemented.")),
        };
    }

    private static bool ValidDocumentId(string? id) => id is not null && Regex.IsMatch(id, @"^DOC-[0-9]{3}$", RegexOptions.CultureInvariant);
}
