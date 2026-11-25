using System.Text.RegularExpressions;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Notices;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Assistant.Tools;

/// <summary>Result of the gate: validated arguments, or a terminal outcome with a deterministic notice code.</summary>
public sealed record GateResult(ToolArguments? Arguments, ToolOutcome? Rejection)
{
    public bool IsAllowed => Rejection is null;

    public static GateResult Allow(ToolArguments arguments) => new(arguments, null);

    public static GateResult Reject(ToolOutcome outcome) => new(null, outcome);
}

/// <summary>
/// Checks every tool call the model requests, in a fixed order: call budget → known tool → permission →
/// risk tier → write budget → arguments. It is a coarse pre-check; the use case behind the tool still applies the
/// resource policies and the database re-applies them with row-level security.
/// </summary>
public sealed class ToolPolicyGate(AssistantLimits limits, NoticeCatalog notices)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    public GateResult Evaluate(
        PrincipalContext principal, AssistantTool? tool, IReadOnlyDictionary<string, string?> arguments, int callsSoFar, int writesSoFar)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(arguments);

        if (callsSoFar >= limits.MaxToolCalls)
        {
            return Reject(AssistantNoticeCodes.ToolCallLimitReached);
        }

        if (tool is null)
        {
            return GateResult.Reject(ToolOutcome.Invalid(AssistantNoticeCodes.UnknownTool));
        }

        if (!tool.RequiresAnyOf.Any(principal.HasPermission))
        {
            return Reject(notices.HintCodeFor(tool.PrimaryPermission, ReasonCodes.MissingPermission) ?? AssistantNoticeCodes.ToolNotPermitted);
        }

        switch (tool.Definition.Risk)
        {
            case ToolRisk.RequiresHumanConfirmation:
            case ToolRisk.LowRiskWrite:
                // Model-originated mutations require a separate authenticated human-confirmation flow.
                // The existing Credit HTTP note endpoint remains available to authorized users.
                return Reject(AssistantNoticeCodes.HumanConfirmationRequired);
            case ToolRisk.PreparesConfirmation:
                break; // returns a card only; execution needs the user's own confirmation outside this turn
        }

        return Validate(tool.Definition, arguments) is { } validated
            ? GateResult.Allow(validated)
            : GateResult.Reject(ToolOutcome.Invalid(AssistantNoticeCodes.InvalidToolArguments));
    }

    private static GateResult Reject(string noticeCode) => GateResult.Reject(ToolOutcome.Denied(noticeCode));

    /// <summary>Keeps only declared parameters; rejects missing, oversized or malformed values.</summary>
    private static ToolArguments? Validate(ToolDefinition definition, IReadOnlyDictionary<string, string?> arguments)
    {
        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in definition.Parameters)
        {
            var value = arguments.TryGetValue(parameter.Name, out var raw) ? raw?.Trim() : null;
            if (string.IsNullOrEmpty(value))
            {
                if (parameter.Required)
                {
                    return null;
                }

                continue;
            }

            if (value.Length > parameter.MaxLength)
            {
                return null;
            }

            if (parameter.Kind == ToolParameterKind.Identifier &&
                (parameter.Pattern is null || !Regex.IsMatch(value, parameter.Pattern, RegexOptions.CultureInvariant, RegexTimeout)))
            {
                return null;
            }

            accepted[parameter.Name] = value;
        }

        return new ToolArguments(accepted);
    }
}
