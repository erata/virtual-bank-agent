using VirtualBank.Application.Assistant;

namespace VirtualBank.Application.Assistant.Abstractions;

/// <summary>
/// Port for the conversational runtime (Semantic Kernel with an LLM, or the deterministic offline runtime).
/// Implementations orchestrate the conversation; they make no authorization decisions.
/// </summary>
public interface IAssistantRuntime
{
    /// <summary>Short identifier reported to the client, e.g. "openrouter" or "offline".</summary>
    string Mode { get; }

    /// <summary>Returns the final answer text. Throws <see cref="AssistantRuntimeUnavailableException"/> when the model cannot be reached.</summary>
    Task<string> CompleteAsync(AssistantTurn turn, IToolInvoker tools, CancellationToken cancellationToken);
}

/// <summary>Server-side diagnostics for failures that are reported to the client only as notice codes.</summary>
public interface IAssistantDiagnostics
{
    void ToolFailed(string tool, Exception exception);

    void RuntimeUnavailable(string mode, Exception exception);
}
