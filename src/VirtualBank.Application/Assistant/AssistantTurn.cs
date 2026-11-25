using VirtualBank.Application.Assistant.Tools;

namespace VirtualBank.Application.Assistant;

/// <summary>
/// Everything a runtime needs for one turn. The runtime never sees the principal.
/// <paramref name="ReplyLanguage"/> is the application's deterministic language detection for the latest message
/// (null when nothing language-specific was recognised); small local models follow an explicit per-turn
/// instruction far more reliably than the generic "answer in the user's language" system rule.
/// </summary>
public sealed record AssistantTurn(string SystemPrompt, string UserMessage, IReadOnlyList<ToolDefinition> Tools, int MaxToolCalls,
    IReadOnlyList<string>? PriorUserMessages = null, AssistantLanguage? ReplyLanguage = null);

/// <summary>The model provider is unreachable or rejected the request. The message must not contain secrets.</summary>
public sealed class AssistantRuntimeUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The only way a runtime reaches bank data. Each call passes the policy gate and an existing use case;
/// the returned string is a JSON tool result for the model. Implemented by the application (<see cref="Tools.ToolSession"/>)
/// and handed to the runtime per turn, so it is a callback contract, not an infrastructure port.
/// </summary>
public interface IToolInvoker
{
    Task<string> InvokeAsync(string toolName, IReadOnlyDictionary<string, string?> arguments, CancellationToken cancellationToken);
}
