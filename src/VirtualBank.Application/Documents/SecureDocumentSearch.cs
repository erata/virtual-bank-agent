using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Documents.Authorization;

namespace VirtualBank.Application.Documents;

/// <summary>Fail closed at the application boundary; SQL RLS independently enforces per-document ACL.</summary>
public sealed class SecureDocumentSearch(IEmbeddingProvider embeddings, IDocumentSearchRepository repository,
    IResourceAuthorizationService authorization, IAuthorizationAudit audit, DocumentSearchQualityOptions? quality = null)
    : ISecureDocumentSearch
{
    public async Task<UseCaseResult<IReadOnlyList<DocumentSearchHit>>> SearchAsync(
        PrincipalContext principal, string question, int topK, CancellationToken cancellationToken)
    {
        if (!principal.HasPermission(KnownPermissions.DocumentRead) || principal.Type == PrincipalType.Machine)
        {
            return UseCaseResult<IReadOnlyList<DocumentSearchHit>>.Forbidden(null);
        }
        if (string.IsNullOrWhiteSpace(question) || question.Length > 500 || topK is < 1 or > 10)
        {
            return UseCaseResult<IReadOnlyList<DocumentSearchHit>>.Invalid("query", "Invalid document search query or limit.");
        }
        var vector = await embeddings.EmbedAsync(question.Trim(), cancellationToken);
        if (vector.Length != embeddings.Dimensions || vector.Any(x => !float.IsFinite(x)))
        {
            throw new InvalidOperationException("Embedding output is not compatible with the configured vector index.");
        }
        // Database RLS filters candidates before top-K; every candidate is then independently checked
        // by the application policy. Disagreement is fail-closed, logged without protected content.
        var candidates = await repository.SearchAsync(principal, vector, embeddings.ModelId, topK, cancellationToken);
        var allowed = new List<DocumentSearchHit>(candidates.Count);
        var threshold = quality?.MinimumCosineSimilarity ?? 0.45;
        foreach (var candidate in candidates)
        {
            if (!double.IsFinite(candidate.Hit.Score) || candidate.Hit.Score < threshold) continue;
            var decision = await authorization.AuthorizeAsync(principal, candidate.Resource,
                KnownPermissions.DocumentRead, cancellationToken);
            audit.Record(principal, decision);
            if (decision.IsAllowed) allowed.Add(candidate.Hit);
            else audit.RecordPolicyRlsMismatch(principal, decision);
        }
        return UseCaseResult<IReadOnlyList<DocumentSearchHit>>.Success(allowed);
    }
}
