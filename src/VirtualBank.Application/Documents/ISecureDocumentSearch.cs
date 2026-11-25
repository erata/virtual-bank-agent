using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;

namespace VirtualBank.Application.Documents;

/// <summary>Only authorized document excerpts are ever returned to the agent/model.</summary>
public interface ISecureDocumentSearch
{
    Task<UseCaseResult<IReadOnlyList<DocumentSearchHit>>> SearchAsync(
        PrincipalContext principal, string question, int topK, CancellationToken cancellationToken);
}

public sealed record DocumentSearchHit(string DocumentId, string Title, string Section,
    string Content, double Score, int? PageNumber);

public sealed record DocumentCitation(string DocumentId, string Title, string Section, int? PageNumber);
