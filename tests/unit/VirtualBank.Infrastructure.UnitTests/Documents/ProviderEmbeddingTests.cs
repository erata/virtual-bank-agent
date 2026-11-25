using System.Net;
using System.Text;
using VirtualBank.Infrastructure.Documents;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Documents;

public sealed class ProviderEmbeddingTests
{
    private sealed class ReplyHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData(DocumentEmbeddingProvider.OpenRouter, "openai/text-embedding-3-small", "https://openrouter.ai/api/v1/")]
    [InlineData(DocumentEmbeddingProvider.OpenAI, "text-embedding-3-small", "https://api.openai.com/v1/")]
    public async Task Cloud_provider_submits_real_embedding_request_with_model_and_1024_dimensions(
        DocumentEmbeddingProvider provider, string model, string endpoint)
    {
        var options = new DocumentRagOptions { Provider=provider, Model=model, Endpoint=endpoint,
            Dimensions=1024, ApiKey="fake-test-key" };
        options.Validate();
        var handler = new ReplyHandler("{\"data\":[{\"embedding\":[" + string.Join(",",Enumerable.Repeat("0.0125",1024)) + "]}]}");
        var http = new HttpClient(handler) { BaseAddress = new Uri(endpoint) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fake-test-key");
        var adapter = new ApiEmbeddingProvider(http, options);
        var vec = await adapter.EmbedAsync("Kreditpolitikası", CancellationToken.None);
        Assert.Equal(1024, vec.Length);
        Assert.EndsWith("/embeddings", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"dimensions\":1024", handler.Body);
        Assert.Contains(model, handler.Body);
        Assert.Contains(provider.ToString(), adapter.ModelId);
    }

    [Fact]
    public void Switching_embedding_provider_changes_vector_space_identifier()
    {
        var cloud = new DocumentRagOptions { Provider=DocumentEmbeddingProvider.OpenRouter,
            Model="openai/text-embedding-3-small",Endpoint="https://openrouter.ai/api/v1/",ApiKey="fake-test-key" };
        var local = new DocumentRagOptions { Provider=DocumentEmbeddingProvider.Ollama,
            Model="bge-m3", Endpoint="http://ollama:11434/" };
        Assert.NotEqual(cloud.ModelId, local.ModelId);
        Assert.Equal(1024, cloud.Dimensions);
        Assert.Equal(1024, local.Dimensions);
    }

    [Fact]
    public void Cloud_requires_key_and_correct_dimensions()
    {
        Assert.Throws<InvalidOperationException>(() => new DocumentRagOptions {
            Provider=DocumentEmbeddingProvider.OpenRouter, Model="openai/text-embedding-3-small",
            Endpoint="https://openrouter.ai/api/v1/"
        }.Validate());
        Assert.Throws<InvalidOperationException>(()=>new DocumentRagOptions { Dimensions=1536 }.Validate());
    }
    [Fact]
    public void Default_embedding_provider_is_local_BgeM3_without_an_api_key()
    {
        var options = new DocumentRagOptions();
        options.Validate();
        Assert.Equal(DocumentEmbeddingProvider.Ollama, options.Provider);
        Assert.Equal("bge-m3", options.Model);
        Assert.Equal(1024, options.Dimensions);
    }

    [Fact]
    public async Task Local_BgeM3_embedding_uses_real_Ollama_endpoint_and_returns_1024_dimensions()
    {
        var options = new DocumentRagOptions();
        var fake = new ReplyHandler("{\"embeddings\":[[" + string.Join(",", Enumerable.Repeat("0.01", 1024)) + "]]}");
        var http = new HttpClient(fake) { BaseAddress=new Uri(options.Endpoint) };
        var adapter = new OllamaEmbeddingProvider(http, options);
        var vector = await adapter.EmbedAsync("Kredibilgisi", CancellationToken.None);
        Assert.Equal(1024, vector.Length);
        Assert.EndsWith("/api/embed", fake.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("bge-m3", fake.Body);
    }

    [Fact]
    public void Ollama_embedding_models_must_match_vector_dimension_contract()
    {
        new DocumentRagOptions { Model="bge-m3" }.Validate();
        Assert.Throws<InvalidOperationException>(() => new DocumentRagOptions { Model="nomic-embed-text" }.Validate());
    }

}
