using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Documents.Authorization;

namespace VirtualBank.Application.Documents;

/// <summary>Metadata of one readable document: never content, never the ACL itself.</summary>
public sealed record DocumentListItem(string DocumentId, string Title, string Language, Clearance Classification,
    string? FileName = null, string? Format = null, DateTimeOffset? UpdatedAt = null);
public sealed record DocumentListCandidate(DocumentListItem Item, DocumentResource Resource);

public interface IDocumentCatalogRepository
{
    /// <summary>Documents visible under PostgreSQL RLS for this principal (bounded).</summary>
    Task<IReadOnlyList<DocumentListCandidate>> ListAsync(PrincipalContext principal, CancellationToken ct);
}

/// <summary>
/// "Which documents can I read?" A semantic search cannot answer a listing request, so it gets its own use case.
/// Same two layers as search and excerpt: DB RLS filters first, then the application ACL policy rechecks each row.
/// </summary>
public sealed class SecureDocumentCatalog(IDocumentCatalogRepository repository,
    IResourceAuthorizationService authorization, IAuthorizationAudit audit)
{
    public async Task<UseCaseResult<IReadOnlyList<DocumentListItem>>> ListAsync(PrincipalContext principal, CancellationToken ct)
    {
        if (!principal.HasPermission(KnownPermissions.DocumentRead) || principal.Type == PrincipalType.Machine)
            return UseCaseResult<IReadOnlyList<DocumentListItem>>.Forbidden(null);

        var visible = new List<DocumentListItem>();
        foreach (var candidate in await repository.ListAsync(principal, ct))
        {
            var decision = await authorization.AuthorizeAsync(principal, candidate.Resource, KnownPermissions.DocumentRead, ct);
            if (decision.IsAllowed)
            {
                visible.Add(candidate.Item);
                continue;
            }
            // RLS returned a row the policy rejects: drop it and record the disagreement.
            audit.Record(principal, decision);
            audit.RecordPolicyRlsMismatch(principal, decision);
        }
        return UseCaseResult<IReadOnlyList<DocumentListItem>>.Success(visible);
    }
}
