namespace VirtualBank.Application.Assistant.Tools;

/// <summary>How the gate validates a parameter value before any use case runs.</summary>
public enum ToolParameterKind
{
    /// <summary>A business identifier such as C-1002 or K-100 (validated against a pattern).</summary>
    Identifier,

    /// <summary>Free text with a maximum length.</summary>
    Text,
}

/// <summary>A tool parameter. Identifiers carry an anchored <paramref name="Pattern"/>; text carries a maximum length.</summary>
public sealed record ToolParameter(
    string Name, string Description, ToolParameterKind Kind, string? Pattern = null, bool Required = true, int MaxLength = 64);

/// <summary>
/// Risk tier of a tool. Reads go through the use case; low-risk writes are rate-limited per turn;
/// actions requiring human confirmation are never executed from a model turn.
/// </summary>
public enum ToolRisk
{
    Read,
    LowRiskWrite,
    RequiresHumanConfirmation,

    /// <summary>
    /// Changes nothing: returns a confirmation card for a write. Only the same user can execute the card, outside
    /// the model turn (POST /api/actions/{id}/confirm), so the model can propose but never perform a change.
    /// </summary>
    PreparesConfirmation,
}

/// <summary>Provider-neutral tool schema. Runtimes translate it (e.g. into Semantic Kernel functions).</summary>
public sealed record ToolDefinition(string Name, string Description, IReadOnlyList<ToolParameter> Parameters, ToolRisk Risk);
