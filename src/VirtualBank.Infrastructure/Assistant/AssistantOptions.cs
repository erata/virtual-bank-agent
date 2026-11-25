using VirtualBank.Application.Assistant;

namespace VirtualBank.Infrastructure.Assistant;

public enum AssistantProvider
{
    /// <summary>Deterministic, rule-based runtime. No network, no API key; used for demos and tests.</summary>
    Offline,

    /// <summary>Semantic Kernel with an OpenAI-compatible endpoint (OpenRouter by default).</summary>
    OpenRouter,

    /// <summary>Local real LLM through Ollama's OpenAI-compatible tool-calling endpoint.</summary>
    Ollama,

    /// <summary>Direct OpenAI API, distinct from OpenRouter.</summary>
    OpenAI,
}

/// <summary>
/// Configuration section <c>Assistant</c>. The API key is read from <c>Assistant:ApiKey</c> or the
/// <c>OPENROUTER_API_KEY</c> environment variable and is never logged or returned.
/// </summary>
public sealed record AssistantOptions
{
    public const string Section = "Assistant";

    /// <summary>
    /// Plain-HTTP hosts accepted for a LOCAL Ollama: the Compose service, and the Docker Desktop
    /// host alias used when Ollama runs natively on Windows/macOS (see docs/LOCAL_SETUP.md).
    /// Cloud providers always require HTTPS.
    /// </summary>
    public static readonly IReadOnlyList<string> LocalOllamaHosts = ["ollama", "host.docker.internal"];

    public AssistantProvider Provider { get; init; } = AssistantProvider.Ollama;

    public string Model { get; init; } = "qwen3:4b-instruct";

    public Uri Endpoint { get; init; } = new("http://ollama:11434/v1/");

    public string? ApiKey { get; init; }

    public int MaxOutputTokens { get; init; } = 800;

    public int TimeoutSeconds { get; init; } = 240;

    public AssistantLimits Limits { get; init; } = new();

    public void Validate()
    {
        Limits.Validate();
        if (Provider is AssistantProvider.OpenRouter or AssistantProvider.OpenAI && string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "The selected chat provider requires an API key. Set OPENROUTER_API_KEY or OPENAI_API_KEY.");
        }

        // Offline never opens a connection, so its (unused) endpoint is not validated.
        if (Provider != AssistantProvider.Offline &&
            Endpoint.Scheme != Uri.UriSchemeHttps && !Endpoint.IsLoopback &&
            !(Provider == AssistantProvider.Ollama && Endpoint.Scheme == Uri.UriSchemeHttp &&
              LocalOllamaHosts.Contains(Endpoint.Host, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Assistant:Endpoint must use HTTPS unless it is a loopback address.");
        }

        if (MaxOutputTokens is < 50 or > 8000 || TimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException("Assistant:MaxOutputTokens or Assistant:TimeoutSeconds is out of range.");
        }
    }
}
