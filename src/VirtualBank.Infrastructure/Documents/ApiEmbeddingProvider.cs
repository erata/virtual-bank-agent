using System.Net.Http.Json;
using System.Text.Json;
using VirtualBank.Application.Documents;

namespace VirtualBank.Infrastructure.Documents;

/// <summary>OpenRouter / OpenAI embeddings; real HTTP inference (not chat completions).</summary>
public sealed class ApiEmbeddingProvider(HttpClient http, DocumentRagOptions options) : IEmbeddingProvider
{
    public string ModelId => options.ModelId;
    public int Dimensions => options.Dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        // Both OpenAI and OpenRouter implement the embeddings endpoint.
        using var response = await http.PostAsJsonAsync("embeddings",
            new { model = options.Model, input = text, dimensions = Dimensions }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var data = json.RootElement.GetProperty("data");
        if (data.GetArrayLength() != 1)
            throw new InvalidOperationException("Embedding service returned an unexpected number of vectors.");
        var values = data[0].GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if (values.Length != Dimensions || values.Any(x => !float.IsFinite(x)))
            throw new InvalidOperationException($"Invalid embedding vector: expected {Dimensions} finite values.");
        return values;
    }
}
