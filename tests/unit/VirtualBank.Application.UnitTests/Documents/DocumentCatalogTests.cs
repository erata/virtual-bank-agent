using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Common;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Documents.Authorization;
using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Model.Documents;
using Xunit;

namespace VirtualBank.Application.UnitTests.Documents;

/// <summary>"Which documents can I read?" is metadata only and is filtered by the same ACL policy as search.</summary>
public sealed class DocumentCatalogTests
{
    private sealed class FakeRepository(params DocumentListCandidate[] rows) : IDocumentCatalogRepository
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<DocumentListCandidate>> ListAsync(PrincipalContext principal, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<DocumentListCandidate>>(rows);
        }
    }

    private sealed class CountingAudit : IAuthorizationAudit
    {
        public int Mismatches { get; private set; }
        public void Record(PrincipalContext principal, AuthorizationDecision decision) { }
        public void RecordPolicyRlsMismatch(PrincipalContext principal, AuthorizationDecision decision) => Mismatches++;
    }

    private static DocumentListCandidate Row(string id, Clearance level, params DocumentAclEntry[] acl) =>
        new(new DocumentListItem(id, "Title " + id, "tr", level), new DocumentResource(id, (int)level, null, acl));

    private static SecureDocumentCatalog Create(IDocumentCatalogRepository repo, IAuthorizationAudit audit) =>
        new(repo, new ResourceAuthorizationService(DocumentPolicies.Create()), audit);

    [Fact]
    public async Task Rows_that_rls_returned_but_the_acl_policy_rejects_are_dropped_and_audited()
    {
        var audit = new CountingAudit();
        var repo = new FakeRepository(
            Row("DOC-103", Clearance.Public, new DocumentAclEntry("public", "*", "allow")),
            // Strictly confidential: an advisor's clearance is too low even if a row slipped through RLS.
            Row("DOC-126", Clearance.StrictlyConfidential, new DocumentAclEntry("public", "*", "allow")));

        var result = await Create(repo, audit).ListAsync(Personas.Get("anna.berater"), CancellationToken.None);

        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Equal("DOC-103", Assert.Single(result.Value!).DocumentId);
        Assert.Equal(1, audit.Mismatches);
    }

    [Fact]
    public async Task Principal_without_document_read_never_reaches_the_database()
    {
        var repo = new FakeRepository(Row("DOC-103", Clearance.Public, new DocumentAclEntry("public", "*", "allow")));

        var result = await Create(repo, new CountingAudit()).ListAsync(Personas.Get("lena.admin"), CancellationToken.None);

        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.Equal(0, repo.Calls);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(2_000)]
    [InlineData(9_500)]
    public void Overlapping_chunks_reassemble_to_the_exact_original_text(int words)
    {
        var original = string.Join(' ', Enumerable.Range(0, words).Select(i => $"wort{i % 97}-{i}"));

        var merged = DocumentTextAssembler.Merge(DocumentChunker.Split(original));

        Assert.Equal(original, merged);
    }

    [Fact]
    public void Pages_are_kept_separate_and_in_order()
    {
        var pages = DocumentTextAssembler.Assemble([
            new DocumentExcerptSection("page one", 1),
            new DocumentExcerptSection("page two", 2)]);

        Assert.Equal([1, 2], pages.Select(p => p.PageNumber));
        Assert.Equal("page two", pages[1].Text);
    }

    private sealed class FullTextRepository(DocumentExcerptCandidate? candidate) : IDocumentExcerptRepository
    {
        public int? RequestedChunks { get; private set; }
        public Task<DocumentExcerptCandidate?> ReadAsync(PrincipalContext principal, string documentId, CancellationToken ct,
            int maxChunks = 6, int maxCharsPerChunk = 900)
        {
            RequestedChunks = maxChunks;
            return Task.FromResult(candidate);
        }
    }

    private static DocumentExcerptCandidate Doc(Clearance level) => new(
        new DocumentExcerpt("DOC-126", "Vergütung", [new DocumentExcerptSection("Gehaltsbänder …", 1)]),
        new DocumentResource("DOC-126", (int)level, null, [new DocumentAclEntry("public", "*", "allow")]));

    [Fact]
    public async Task Full_text_is_returned_only_after_the_acl_policy_allows_it()
    {
        var audit = new CountingAudit();
        var reader = new SecureDocumentExcerpt(new FullTextRepository(Doc(Clearance.StrictlyConfidential)),
            new ResourceAuthorizationService(DocumentPolicies.Create()), audit);

        var denied = await reader.ReadFullAsync(Personas.Get("anna.berater"), "DOC-126", CancellationToken.None);

        Assert.Equal(UseCaseStatus.NotFound, denied.Status); // never "forbidden": existence is not revealed
        Assert.Equal(1, audit.Mismatches);
    }

    [Fact]
    public async Task Full_text_reads_all_chunks_not_the_six_chunk_excerpt()
    {
        var repo = new FullTextRepository(Doc(Clearance.Public));
        var reader = new SecureDocumentExcerpt(repo, new ResourceAuthorizationService(DocumentPolicies.Create()), new CountingAudit());

        var result = await reader.ReadFullAsync(Personas.Get("anna.berater"), "DOC-126", CancellationToken.None);

        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Equal(SecureDocumentExcerpt.MaxFullChunks, repo.RequestedChunks);
        Assert.Equal(Clearance.Public, result.Value!.Classification);
    }

    [Fact]
    public void Listing_is_rendered_as_a_metadata_table_without_content()
    {
        IReadOnlyList<DocumentListItem> items = [new("DOC-118", "SEPA Havale ve Ödeme Rehberi 2026", "tr", Clearance.Public)];

        var table = AssistantTableProjector.Create("list_documents", items, AssistantLanguage.Turkish)!;

        Assert.Equal(["Belge No", "Başlık", "Dil", "Gizlilik"], table.Columns);
        Assert.Equal(["DOC-118", "SEPA Havale ve Ödeme Rehberi 2026", "TR", "Herkese açık"], table.Rows[0]);
    }
}
