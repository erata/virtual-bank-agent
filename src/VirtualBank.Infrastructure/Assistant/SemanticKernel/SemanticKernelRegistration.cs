using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;

namespace VirtualBank.Infrastructure.Assistant.SemanticKernel;

internal static class SemanticKernelRegistration
{
    /// <summary>
    /// OpenRouter speaks the OpenAI chat-completions protocol, so the Semantic Kernel OpenAI connector is used with a
    /// custom endpoint. Only the chat service is a singleton; kernels are created per turn.
    /// </summary>
    public static IServiceCollection AddSemanticKernelRuntime(this IServiceCollection services, AssistantOptions options)
    {
        services.AddSingleton<IChatCompletionService>(sp =>
        {
            var isOllama = options.Provider == VirtualBank.Infrastructure.Assistant.AssistantProvider.Ollama;
            var clientOptions = new OpenAIClientOptions { Endpoint = options.Endpoint, NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
            // A local CPU model that timed out will time out again; SDK retries only multiply the wait
            // and queue a duplicate prompt behind Ollama's single runner.
            if (isOllama) clientOptions.RetryPolicy = new ClientRetryPolicy(maxRetries: 0);
            var client = new OpenAIClient(
                new ApiKeyCredential(isOllama ? "ollama-local" : options.ApiKey!),
                clientOptions);
            return new OpenAIChatCompletionService(options.Model, client, sp.GetService<ILoggerFactory>());
        });
        return services;
    }
}
