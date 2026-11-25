using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Common;
using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Documents.Authorization;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Model.Documents;
using VirtualBank.Application.UnitTests.Support;
using Xunit;

namespace VirtualBank.Application.UnitTests.Documents;

public sealed class DocumentSecurityTests
{
    private sealed class CountingEmbedding : IEmbeddingProvider
    {
        public int Calls { get; private set; }
        public string ModelId => "bge-m3";
        public int Dimensions => 1024;
        public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new float[1024]);
        }
    }

    private sealed class CountingRepository : IDocumentSearchRepository
    {
        public int Calls { get; private set; }
        public IReadOnlyList<DocumentSearchCandidate> Candidates { get; set; } = [];
        public Task<IReadOnlyList<DocumentSearchCandidate>> SearchAsync(PrincipalContext principal, float[] vector,
            string model, int k, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Candidates);
        }
    }

    private static SecureDocumentSearch CreateSearch(IEmbeddingProvider embeddings, IDocumentSearchRepository repo,
        IAuthorizationAudit? audit = null) => new(embeddings, repo,
        new ResourceAuthorizationService(DocumentPolicies.Create()), audit ?? new NoopAudit());

    private sealed class NoopAudit : IAuthorizationAudit
    {
        public void Record(VirtualBank.Application.Authorization.Principals.PrincipalContext principal,
            VirtualBank.Application.Authorization.Decisions.AuthorizationDecision decision) { }
        public void RecordPolicyRlsMismatch(PrincipalContext principal,
            VirtualBank.Application.Authorization.Decisions.AuthorizationDecision decision) { }
    }

    [Fact]
    public async Task Low_similarity_document_chunks_are_excluded_even_if_acl_allows_them()
    {
        var repo = new CountingRepository();
        var resource = new DocumentResource("DOC-101", 0, null,
            [new DocumentAclEntry("public", "*", "allow")]);
        repo.Candidates = [
            new DocumentSearchCandidate(new DocumentSearchHit("DOC-101", "Unrelated", "1", "unrelated text", 0.21, 1), resource),
            new DocumentSearchCandidate(new DocumentSearchHit("DOC-101", "Relevant", "2", "matching policy", 0.78, 2), resource),
        ];
        var search = new SecureDocumentSearch(new CountingEmbedding(), repo,
            new ResourceAuthorizationService(DocumentPolicies.Create()), new NoopAudit(),
            new DocumentSearchQualityOptions(0.5));
        var result = await search.SearchAsync(Personas.Get("anna.berater"), "policy", 5, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Equal("matching policy", Assert.Single(result.Value!).Content);
    }

    [Fact]
    public async Task Principal_without_document_read_never_reaches_embedding_or_database()
    {
        var embeddings = new CountingEmbedding();
        var repo = new CountingRepository();
        var result = await CreateSearch(embeddings, repo)
            .SearchAsync(Personas.Get("lena.admin"), "HR salaries", 5, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.Equal(0, embeddings.Calls);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task Input_and_topk_are_validated_before_embedding()
    {
        var embeddings = new CountingEmbedding();
        var repo = new CountingRepository();
        var service = CreateSearch(embeddings, repo);
        var principal = Personas.Get("anna.berater");
        Assert.Equal(UseCaseStatus.Invalid, (await service.SearchAsync(principal, "", 5, CancellationToken.None)).Status);
        Assert.Equal(UseCaseStatus.Invalid, (await service.SearchAsync(principal, "policy", 500, CancellationToken.None)).Status);
        Assert.Equal(0, embeddings.Calls);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public void Chunker_is_deterministic_and_handles_unicode()
    {
        var text = string.Join(" ", Enumerable.Repeat("Kreditrichtlinie Benutzer kullanıcı", 120));
        var chunks = DocumentChunker.Split(text, 400, 50).ToArray();
        Assert.True(chunks.Length > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 400));
        Assert.Equal(chunks, DocumentChunker.Split(text, 400, 50).ToArray());
    }

    [Fact]
    public void Bank_customer_can_read_public_documents_but_not_internal_credit_or_hr()
    {
        var policy = new DocumentReadPolicy();
        var customer = Personas.Get("deniz.customer");
        var publicFaq = new DocumentResource("DOC-110", 0, null,
            [new DocumentAclEntry("public", "*", "allow")]);
        var internalPolicy = new DocumentResource("DOC-107", 1, null,
            [new DocumentAclEntry("role", "bank-customer", "allow")]);
        var creditPolicy = new DocumentResource("DOC-105", 2, null,
            [new DocumentAclEntry("group", "/Credit", "allow")]);
        Assert.True(policy.Evaluate(customer, publicFaq).IsAllowed);
        Assert.False(policy.Evaluate(customer, internalPolicy).IsAllowed);
        Assert.False(policy.Evaluate(customer, creditPolicy).IsAllowed);
    }

    [Fact]
    public async Task Bank_customer_public_only_search_is_independently_filtered_after_repository()
    {
        var repo = new CountingRepository();
        var publicResource = new DocumentResource("DOC-101", 0, null,
            [new DocumentAclEntry("public", "*", "allow")]);
        var internalResource = new DocumentResource("DOC-107", 1, null,
            [new DocumentAclEntry("role", "bank-customer", "allow")]);
        repo.Candidates = [
            new DocumentSearchCandidate(new DocumentSearchHit("DOC-101", "Public", "1", "Public account information", .9, 1), publicResource),
            new DocumentSearchCandidate(new DocumentSearchHit("DOC-107", "Private", "2", "Internal information", .95, 1), internalResource),
        ];
        var result = await CreateSearch(new CountingEmbedding(), repo)
            .SearchAsync(Personas.Get("deniz.customer"), "account information", 5, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Equal("DOC-101", Assert.Single(result.Value!).DocumentId);
    }

    [Fact]
    public void HR_classification_and_acl_both_required()
    {
        var policy = new DocumentReadPolicy();
        var alice = Personas.Get("anna.berater");
        var sophie = Personas.Get("sophie.hr");
        var salary = new DocumentResource("DOC-106", 3, null,
            [new DocumentAclEntry("role", "hr-specialist", "allow")]);
        Assert.False(policy.Evaluate(alice, salary).IsAllowed);
        Assert.True(policy.Evaluate(sophie, salary).IsAllowed);
    }

    [Fact]
    public void Explicit_deny_overrides_an_actual_matching_public_allow()
    {
        var anna = Personas.Get("anna.berater");
        var policy = new DocumentReadPolicy();
        var resource = new DocumentResource("DOC-101", 0, null,
            [new DocumentAclEntry("public", "*", "allow"),
             new DocumentAclEntry("user", anna.SubjectId, "deny")]);
        Assert.False(policy.Evaluate(anna, resource).IsAllowed);
        Assert.True(policy.Evaluate(Personas.Get("mehmet.kredit"), resource).IsAllowed);
    }

    [Fact]
    public void Branch_scope_applies_to_application_document_policy()
    {
        var resource = new DocumentResource("DOC-104", 1, "STR",
            [new DocumentAclEntry("role", "customer-advisor", "allow"),
             new DocumentAclEntry("role", "credit-analyst", "allow")]);
        var policy = new DocumentReadPolicy();
        Assert.True(policy.Evaluate(Personas.Get("anna.berater"), resource).IsAllowed);
        Assert.False(policy.Evaluate(Personas.Get("mehmet.kredit"), resource).IsAllowed);
    }

    [Fact]
    public async Task Application_authorization_rejects_candidates_even_when_repository_returns_them()
    {
        var repo = new CountingRepository();
        var spy = new RecordingAudit();
        var unauthorized = new DocumentResource("DOC-106", 3, null,
            [new DocumentAclEntry("role", "hr-specialist", "allow")]);
        repo.Candidates = [new DocumentSearchCandidate(
            new DocumentSearchHit("DOC-106", "Restricted", "Salary", "secret text", .99, 1), unauthorized)];
        var result = await CreateSearch(new CountingEmbedding(), repo, spy)
            .SearchAsync(Personas.Get("anna.berater"), "salary", 5, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
        Assert.Single(spy.Mismatches);
    }
}
