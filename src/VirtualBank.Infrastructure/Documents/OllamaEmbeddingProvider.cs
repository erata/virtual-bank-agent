using System.Net.Http.Json;
using System.Text.Json;
using VirtualBank.Application.Documents;

namespace VirtualBank.Infrastructure.Documents;

/// <summary>Real multilingual 1024-dimensional embeddings via Ollama /api/embed.</summary>
public sealed class OllamaEmbeddingProvider(HttpClient http, DocumentRagOptions options) : IEmbeddingProvider
{
    public string ModelId => options.ModelId;
    public int Dimensions => options.Dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("api/embed", new { model = options.Model, input = text }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var embeddings = json.RootElement.GetProperty("embeddings");
        if (embeddings.GetArrayLength() != 1) throw new InvalidOperationException("Expected one embedding per input.");
        var values = embeddings[0].EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != Dimensions || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException($"Ollama model {options.Model} returned invalid embeddings: expected {Dimensions} finite values.");
        return values;
    }
}

public enum DocumentEmbeddingProvider { OpenRouter, OpenAI, Ollama }

/// <summary>Independent embedding provider: chat and retrieval can use different models and even different services.</summary>
public sealed record DocumentRagOptions
{
    public DocumentEmbeddingProvider Provider { get; init; } = DocumentEmbeddingProvider.Ollama;
    public string Model { get; init; } = "bge-m3";
    public string Endpoint { get; init; } = "http://ollama:11434/";
    public int Dimensions { get; init; } = 1024;
    public string? ApiKey { get; init; }
    public string ModelId => $"{Provider}:{Model}:{Dimensions}";
    public string SampleDirectory { get; init; } = "/sample-docs";
    public double MinimumCosineSimilarity { get; init; } = 0.45;

    public void Validate()
    {
        new DocumentSearchQualityOptions(MinimumCosineSimilarity).Validate();
        if (Dimensions != 1024)
            throw new InvalidOperationException("Documents:Dimensions must be 1024 for the current vector(1024) schema. A different dimension needs an EF migration and a complete reindex.");
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var url))
            throw new InvalidOperationException("Invalid embedding endpoint.");
        if (Provider == DocumentEmbeddingProvider.Ollama)
        {
            if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Invalid Ollama endpoint.");
            // Both BGE-M3 and Qwen3-Embedding 0.6B natively produce 1024-dimensional vectors.
            // Do not accept arbitrary models here: the database column is vector(1024).
            if (Model is not ("bge-m3" or "qwen3-embedding:0.6b"))
                throw new InvalidOperationException("This vector(1024) demo supports Ollama bge-m3 or qwen3-embedding:0.6b. Any other model requires dimension verification and a deliberate schema/index migration.");
        }
        else
        {
            if (url.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Cloud embeddings must use HTTPS.");
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException("Selected document embedding provider requires its API key.");
            var expected = Provider == DocumentEmbeddingProvider.OpenRouter
                ? "openai/text-embedding-3-small" : "text-embedding-3-small";
            if (!string.Equals(Model, expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected {expected} for 1024-dimensional embeddings; change both the model and indexing configuration together.");
        }
    }
}
