using VirtualBank.Application.Assistant;
using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using VirtualBank.Application.Assistant.Abstractions;
using VirtualBank.Application.Assistant.Tools;

namespace VirtualBank.Infrastructure.Assistant.SemanticKernel;

/// <summary>
/// LLM runtime on Semantic Kernel. Each turn builds a fresh kernel whose only plugin is the turn's
/// <see cref="IToolInvoker"/>, translated into kernel functions; the model chooses tools with automatic function
/// calling. The kernel holds no credentials or repositories, so a function call cannot reach data except through
/// the application's gate and use cases. Temperature 0 keeps tool selection as reproducible as the provider allows.
/// </summary>
public sealed class SemanticKernelAssistantRuntime(
    IChatCompletionService chat,
    AssistantOptions options,
    ILogger<SemanticKernelAssistantRuntime>? logger = null) : IAssistantRuntime
{
    private readonly ILogger _logger = logger ?? NullLogger<SemanticKernelAssistantRuntime>.Instance;

    public const string PluginName = "bank";

    /// <summary>Deterministic text returned when the request limit stops the model loop (no model text available).</summary>
    public const string StoppedText = "The assistant stopped because the tool-call limit for this request was reached.";

    public string Mode => options.Provider switch
    {
        VirtualBank.Infrastructure.Assistant.AssistantProvider.Ollama => "ollama",
        VirtualBank.Infrastructure.Assistant.AssistantProvider.OpenAI => "openai",
        _ => "openrouter"
    };

    public async Task<string> CompleteAsync(AssistantTurn turn, IToolInvoker tools, CancellationToken cancellationToken)
    {
        var kernel = new Kernel();
        kernel.Plugins.Add(KernelPluginFactory.CreateFromFunctions(PluginName, turn.Tools.Select(t => ToFunction(t, tools))));
        var limit = new ModelRequestLimitFilter(turn.MaxToolCalls);
        var timing = new ToolTimingFilter();
        kernel.AutoFunctionInvocationFilters.Add(timing);
        kernel.AutoFunctionInvocationFilters.Add(limit);

        var history = BuildHistory(turn);

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0,
            MaxTokens = options.MaxOutputTokens,
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        };

        var total = Stopwatch.StartNew();
        var outcome = "failed";
        try
        {
            var reply = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
            outcome = limit.Terminated ? "tool_limit" : "completed";
            LogUsage(reply);
            return limit.Terminated ? StoppedText : reply.Content ?? string.Empty;
        }
        catch (HttpOperationException ex)
        {
            throw new AssistantRuntimeUnavailableException("The model provider rejected or failed the request.", ex);
        }
        catch (ClientResultException ex)
        {
            throw new AssistantRuntimeUnavailableException("The model provider rejected or failed the request.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AssistantRuntimeUnavailableException("The model provider did not answer in time.", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user pressed "Cancel" (browser aborted the request): not an outage, nothing is stored.
            outcome = "cancelled";
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new AssistantRuntimeUnavailableException("The model provider is unreachable.", ex);
        }
        finally
        {
            total.Stop();
            // Timing only: no prompt, answer, tool arguments or results are logged (they can contain customer data).
            _logger.LogInformation(
                "AssistantTurnTiming mode={Mode} model={Model} outcome={Outcome} totalMs={TotalMs} modelMs={ModelMs} toolMs={ToolMs} toolCalls={ToolCalls} tools={Tools}",
                Mode, options.Model, outcome, total.ElapsedMilliseconds,
                Math.Max(0, total.ElapsedMilliseconds - timing.ToolMilliseconds), timing.ToolMilliseconds,
                timing.Calls, string.Join(',', timing.ToolNames));
        }
    }

    /// <summary>Stands in for prior model answers, which are deliberately never replayed as evidence.</summary>
    internal const string PriorAnswerPlaceholder =
        "[Answered earlier. That answer and its tool results are intentionally omitted and are not evidence " +
        "for the current request. Respond only to the newest user message.]";

    /// <summary>
    /// Prior questions of the same user are kept for resolving references ("their total", "and C-1002?").
    /// Each is closed by an assistant placeholder: consecutive unanswered user messages made models answer
    /// every earlier question again (e.g. describe_my_access on a balance request).
    /// </summary>
    internal static ChatHistory BuildHistory(AssistantTurn turn)
    {
        var history = new ChatHistory(turn.SystemPrompt);
        if (turn.PriorUserMessages is not null)
            foreach (var prior in turn.PriorUserMessages)
            {
                history.AddUserMessage(prior);
                history.AddAssistantMessage(PriorAnswerPlaceholder);
            }
        history.AddUserMessage(turn.UserMessage);
        if (ReplyLanguageInstruction(turn.ReplyLanguage) is { } languageInstruction)
            history.AddSystemMessage(languageInstruction);
        return history;
    }

    /// <summary>Explicit per-turn reply language; null keeps only the generic system-prompt rule.</summary>
    internal static string? ReplyLanguageInstruction(AssistantLanguage? language) => language switch
    {
        AssistantLanguage.Turkish => "Reply language: Turkish. Write the entire final answer in Turkish (Türkçe), even though tool results are in English.",
        AssistantLanguage.German => "Reply language: German. Write the entire final answer in German (Deutsch), even though tool results are in English.",
        AssistantLanguage.English => "Reply language: English. Write the entire final answer in English.",
        _ => null,
    };

    private void LogUsage(ChatMessageContent reply)
    {
        // The OpenAI connector exposes token usage in metadata; providers that omit it are simply not logged.
        if (reply.Metadata is not null && reply.Metadata.TryGetValue("Usage", out var usage) &&
            usage is OpenAI.Chat.ChatTokenUsage tokens)
            _logger.LogInformation("AssistantTokenUsage mode={Mode} model={Model} inputTokens={Input} outputTokens={Output} (final round)",
                Mode, options.Model, tokens.InputTokenCount, tokens.OutputTokenCount);
    }

    /// <summary>
    /// One kernel function per tool. Parameters are declared as strings from the provider-neutral schema; the
    /// function body only forwards name and raw argument strings to the invoker, which validates them.
    /// </summary>
    internal static KernelFunction ToFunction(ToolDefinition definition, IToolInvoker tools)
    {
        Func<KernelArguments, CancellationToken, Task<string>> invoke = (arguments, ct) =>
            tools.InvokeAsync(
                definition.Name,
                arguments.ToDictionary(a => a.Key, a => a.Value?.ToString(), StringComparer.Ordinal),
                ct);

        return KernelFunctionFactory.CreateFromMethod(
            invoke,
            functionName: definition.Name,
            description: definition.Description,
            parameters: definition.Parameters.Select(p => new KernelParameterMetadata(p.Name)
            {
                Description = p.Description,
                IsRequired = p.Required,
                ParameterType = typeof(string),
            }).ToList(),
            returnParameter: new KernelReturnParameterMetadata { Description = "JSON tool result.", ParameterType = typeof(string) });
    }
}

/// <summary>Measures time spent inside tool calls so model latency can be separated from use-case latency.</summary>
internal sealed class ToolTimingFilter : IAutoFunctionInvocationFilter
{
    private long _ticks;
    private readonly List<string> _names = [];

    public long ToolMilliseconds => (long)TimeSpan.FromTicks(Interlocked.Read(ref _ticks)).TotalMilliseconds;
    public int Calls { get { lock (_names) return _names.Count; } }
    public IReadOnlyList<string> ToolNames { get { lock (_names) return _names.ToArray(); } }

    public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
    {
        var started = Stopwatch.GetTimestamp();
        try { await next(context); }
        finally
        {
            Interlocked.Add(ref _ticks, Stopwatch.GetElapsedTime(started).Ticks);
            lock (_names) _names.Add(context.Function.Name);
        }
    }
}

/// <summary>
/// Hard stop for the model loop. The application's gate already denies calls beyond the budget; this filter
/// additionally ends the conversation loop once the model keeps requesting tools after the budget is spent.
/// </summary>
internal sealed class ModelRequestLimitFilter(int maxToolCalls) : IAutoFunctionInvocationFilter
{
    private int _invocations;

    public bool Terminated { get; private set; }

    public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
    {
        await next(context);

        // One extra round lets the model see the "limit reached" result and still produce a final answer.
        if (Interlocked.Increment(ref _invocations) > maxToolCalls + 1)
        {
            Terminated = true;
            context.Terminate = true;
        }
    }
}
