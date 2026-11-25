using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Abstractions;
using VirtualBank.Infrastructure.Assistant;
using VirtualBank.Infrastructure.Assistant.Offline;
using VirtualBank.Infrastructure.Assistant.SemanticKernel;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Assistant;

public sealed class AssistantOptionsTests
{
    private static ServiceCollection Register(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddAssistantRuntime(configuration);
        return services;
    }

    private static Type? RuntimeType(ServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(IAssistantRuntime)).ImplementationType;

    [Fact]
    public void Default_is_real_local_ollama_semantic_kernel_runtime()
    {
        var services = Register();

        Assert.Equal(typeof(SemanticKernelAssistantRuntime), RuntimeType(services));
        var options = services.BuildServiceProvider().GetRequiredService<AssistantOptions>();
        Assert.Equal(AssistantProvider.Ollama, options.Provider);
        Assert.Equal("qwen3:4b-instruct", options.Model);
        Assert.Equal(6, services.BuildServiceProvider().GetRequiredService<AssistantLimits>().MaxToolCalls);
    }

    [Fact]
    public void Offline_requires_explicit_configuration()
    {
        var services = Register(("Assistant:Provider", "Offline"));
        Assert.Equal(typeof(OfflineAssistantRuntime), RuntimeType(services));
    }

    [Fact]
    public void OpenRouter_without_a_key_fails_at_startup() =>
        Assert.Throws<InvalidOperationException>(() => Register(("Assistant:Provider", "OpenRouter")));

    [Fact]
    public void OpenRouter_key_can_come_from_the_environment_variable_name()
    {
        var services = Register(("Assistant:Provider", "OpenRouter"), ("OPENROUTER_API_KEY", "test-key-not-real"));

        Assert.Equal(typeof(SemanticKernelAssistantRuntime), RuntimeType(services));
    }

    [Fact]
    public void Plain_http_endpoints_are_rejected_unless_loopback()
    {
        Assert.Throws<InvalidOperationException>(() => Register(
            ("Assistant:Provider", "OpenRouter"), ("Assistant:ApiKey", "k"), ("Assistant:Endpoint", "http://models.example.test/v1")));

        Register(("Assistant:Provider", "OpenRouter"), ("Assistant:ApiKey", "k"), ("Assistant:Endpoint", "http://127.0.0.1:11434/v1"));
    }

    [Fact]
    public void Direct_OpenAI_uses_a_separate_key()
    {
        Assert.Throws<InvalidOperationException>(() => Register(("Assistant:Provider", "OpenAI")));
        var services = Register(("Assistant:Provider", "OpenAI"),
            ("Assistant:Model", "gpt-4o"), ("OPENAI_API_KEY", "fake-test-key"));
        Assert.Equal(typeof(SemanticKernelAssistantRuntime), RuntimeType(services));
    }

    [Fact]
    public void Limits_are_bound_and_validated()
    {
        var services = Register(("Assistant:Limits:MaxToolCalls", "2"), ("OPENROUTER_API_KEY", "test-key-not-real"));
        Assert.Equal(2, services.BuildServiceProvider().GetRequiredService<AssistantLimits>().MaxToolCalls);

        Assert.Throws<InvalidOperationException>(() => Register(("Assistant:Limits:MaxToolCalls", "500"),
            ("OPENROUTER_API_KEY", "test-key-not-real")));
    }
}
