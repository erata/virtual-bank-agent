namespace VirtualBank.Model.Documents;

/// <summary>Document metadata. Document contents are stored only in secured chunks.</summary>
public sealed record BankDocument(
    string Id, string Title, string SourcePath, string Language,
    int Classification, string? Branch, string ContentHash,
    string EmbeddingModel, int ContentVersion, int AclVersion);

public sealed record DocumentAclEntry(string SubjectKind, string SubjectValue, string Effect);

public sealed record DocumentChunk(string Id, string DocumentId, int ChunkIndex, string Content, int? PageNumber, float[] Embedding);
