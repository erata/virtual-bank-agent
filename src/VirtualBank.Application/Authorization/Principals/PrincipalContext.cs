using System.Collections.Frozen;
using VirtualBank.Application.Authorization.Permissions;

namespace VirtualBank.Application.Authorization.Principals;

/// <summary>
/// Trusted, server-side view of the authenticated caller. Built only from a validated token
/// by <see cref="PrincipalResolver"/>; never from client-supplied request data.
/// </summary>
public sealed record PrincipalContext
{
    public required string SubjectId { get; init; }

    public required string Username { get; init; }

    public required PrincipalType Type { get; init; }

    public string? ClientId { get; init; }

    public FrozenSet<string> Roles { get; init; } = FrozenSet<string>.Empty;

    /// <summary>Group memberships including all ancestor groups.</summary>
    public FrozenSet<GroupPath> Groups { get; init; } = FrozenSet<GroupPath>.Empty;

    public FrozenSet<Permission> Permissions { get; init; } = FrozenSet<Permission>.Empty;

    public string? Branch { get; init; }

    public string? Department { get; init; }

    public Clearance Clearance { get; init; } = Clearance.Public;

    public decimal? ApprovalLimit { get; init; }

    public bool IsActive { get; init; }

    public bool HasPermission(Permission permission) => IsActive && Permissions.Contains(permission);

    public bool IsMemberOf(GroupPath group) => Groups.Contains(group);
}
