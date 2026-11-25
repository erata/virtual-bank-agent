using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Model.Documents;
using VirtualBank.Application.Documents.Authorization;

namespace VirtualBank.Application.Documents;

/// <summary>Model-independent embedding service; the implementation must produce real vectors.</summary>
public interface IEmbeddingProvider
{
    string ModelId { get; }
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

public interface IDocumentSearchRepository
{
    Task<IReadOnlyList<DocumentSearchCandidate>> SearchAsync(PrincipalContext principal, float[] vector, string embeddingModel,
        int topK, CancellationToken cancellationToken);
}

public sealed record DocumentSearchCandidate(DocumentSearchHit Hit, DocumentResource Resource);

public sealed record DocumentIngestionEntry(BankDocument Document,
    IReadOnlyList<DocumentAclEntry> InitialAcl, IReadOnlyList<DocumentChunk> Chunks);

public interface IDocumentIngestionRepository
{
    Task<bool> HasCurrentDocumentAsync(BankDocument document, CancellationToken cancellationToken);
    Task<bool> UpsertAsync(DocumentIngestionEntry document, CancellationToken cancellationToken);
}

public sealed record DocumentAclSnapshot(string DocumentId, string Title, int Version,
    IReadOnlyList<DocumentAclEntry> Entries, int Classification = 0, string? Branch = null);
public sealed record DocumentAclRevision(int Version, string ChangedBy, DateTimeOffset ChangedAt, string ChangesJson);

/// <summary>ACL-administration index row: metadata only (the ACL role has no content or chunk access).</summary>
public sealed record DocumentAclIndexItem(string DocumentId, string Title, int Version, int Classification, string? Branch,
    int EntryCount);

public interface IDocumentAclAdministrationRepository
{
    Task<DocumentAclSnapshot?> ReadAsync(string documentId, CancellationToken cancellationToken);
    Task<DocumentAclSnapshot?> UpdateAsync(string documentId, string changedBy, int expectedVersion,
        IReadOnlyList<DocumentAclEntry> entries, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentAclRevision>> HistoryAsync(string documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentAclIndexItem>> ListAsync(CancellationToken cancellationToken);
}
