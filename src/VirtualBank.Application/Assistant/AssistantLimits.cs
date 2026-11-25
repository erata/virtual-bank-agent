namespace VirtualBank.Application.Assistant;

/// <summary>Per-turn limits enforced by application code, independent of the model provider.</summary>
public sealed record AssistantLimits
{
    public int MaxMessageLength { get; init; } = 2000;

    /// <summary>Tool invocations per turn, counting denied ones, so a looping model cannot probe indefinitely.</summary>
    public int MaxToolCalls { get; init; } = 6;

    /// <summary>Write tools per turn. Writes are low-risk, append-only and re-checked by RLS, but still rate-limited.</summary>
    public int MaxWriteCalls { get; init; } = 0;

    /// <summary>Server-side selection of model-visible read-tool schemas. Execution is never delegated.</summary>
    public Tools.ToolVisibilityMode ToolVisibility { get; init; } = Tools.ToolVisibilityMode.Hide;

    /// <summary>Upper bound for the model's answer text that is passed to the client.</summary>
    public int MaxAnswerLength { get; init; } = 4000;

    public void Validate()
    {
        if (MaxMessageLength is < 1 or > 20_000 || MaxToolCalls is < 0 or > 50 || MaxWriteCalls < 0 || MaxAnswerLength < 100)
        {
            throw new InvalidOperationException("Assistant limits are out of range.");
        }
    }
}
