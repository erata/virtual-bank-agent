using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;

namespace VirtualBank.Application.Assistant.Tools;

/// <summary>
/// A tool = schema + coarse permission requirement + executor. The executor always calls an existing use-case
/// handler, so resource policies, masking and row-level security apply exactly as for the HTTP API.
/// </summary>
public sealed record AssistantTool(
    ToolDefinition Definition,
    IReadOnlyList<Permission> RequiresAnyOf,
    Func<PrincipalContext, ToolArguments, CancellationToken, Task<ToolOutcome>> ExecuteAsync)
{
    public string Name => Definition.Name;

    /// <summary>Permission named in the denial hint when the caller holds none of <see cref="RequiresAnyOf"/>.</summary>
    public Permission PrimaryPermission => RequiresAnyOf[0];
}
