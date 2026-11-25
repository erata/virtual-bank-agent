using VirtualBank.Application.Assistant;
using VirtualBank.Infrastructure.Assistant;
using VirtualBank.Infrastructure.Assistant.SemanticKernel;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Assistant;

/// <summary>Endpoint rules for local/native Ollama and the per-turn reply-language instruction.</summary>
public sealed class AssistantRuntimeConfigurationTests
{
    [Theory]
    [InlineData("http://ollama:11434/v1/")]               // Compose service (default)
    [InlineData("http://host.docker.internal:11434/v1/")] // native Windows/macOS Ollama seen from a container
    [InlineData("http://localhost:11434/v1/")]            // API running on the host
    public void Local_ollama_endpoints_may_use_plain_http(string endpoint) =>
        new AssistantOptions { Provider = AssistantProvider.Ollama, Endpoint = new Uri(endpoint) }.Validate();

    [Theory]
    [InlineData(AssistantProvider.OpenRouter, "http://host.docker.internal:11434/v1/")]
    [InlineData(AssistantProvider.OpenAI, "http://ollama:11434/v1/")]
    [InlineData(AssistantProvider.Ollama, "http://models.example.com/v1/")]
    public void Plain_http_is_rejected_outside_the_local_ollama_allow_list(AssistantProvider provider, string endpoint) =>
        Assert.Throws<InvalidOperationException>(() =>
            new AssistantOptions { Provider = provider, Endpoint = new Uri(endpoint), ApiKey = "test-key-not-real" }.Validate());

    [Fact]
    public void Offline_does_not_validate_an_endpoint_it_never_uses() =>
        new AssistantOptions { Provider = AssistantProvider.Offline, Endpoint = new Uri("http://ollama:11434/v1/") }.Validate();

    [Theory]
    [InlineData(AssistantLanguage.Turkish, "Turkish")]
    [InlineData(AssistantLanguage.German, "German")]
    [InlineData(AssistantLanguage.English, "English")]
    public void Recognised_language_becomes_an_explicit_instruction(AssistantLanguage language, string expected) =>
        Assert.Contains(expected, SemanticKernelAssistantRuntime.ReplyLanguageInstruction(language), StringComparison.Ordinal);

    [Fact]
    public void Unrecognised_language_adds_no_instruction() =>
        Assert.Null(SemanticKernelAssistantRuntime.ReplyLanguageInstruction(null));

    [Fact]
    public void Prior_questions_are_closed_so_only_the_newest_message_is_pending()
    {
        var turn = new AssistantTurn("system", "müşterilerimin toplam bakiyelerini göster", [], 4,
            ["Yetkilerimi açıkla", "müşterilerimi listele"], AssistantLanguage.Turkish);

        var roles = SemanticKernelAssistantRuntime.BuildHistory(turn)
            .Select(m => m.Role.Label).ToArray();

        Assert.Equal(["system", "user", "assistant", "user", "assistant", "user", "system"], roles);
        var history = SemanticKernelAssistantRuntime.BuildHistory(turn);
        Assert.Equal(turn.UserMessage, history[^2].Content);
        // Prior answers are a fixed placeholder, never replayed model text or data.
        Assert.All(history.Where(m => m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant),
            m => Assert.Equal(SemanticKernelAssistantRuntime.PriorAnswerPlaceholder, m.Content));
    }
}
