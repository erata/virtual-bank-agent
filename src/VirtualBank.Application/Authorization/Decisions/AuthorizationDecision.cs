using VirtualBank.Application.Authorization.Permissions;

namespace VirtualBank.Application.Authorization.Decisions;

/// <summary>Structured, auditable result of an authorization check.</summary>
public sealed record AuthorizationDecision
{
    public required bool IsAllowed { get; init; }

    public required string ReasonCode { get; init; }

    public required Permission Action { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceId { get; init; }

    public required string PolicyId { get; init; }

    public required string CorrelationId { get; init; }

    public required DisclosureMode Disclosure { get; init; }
}
