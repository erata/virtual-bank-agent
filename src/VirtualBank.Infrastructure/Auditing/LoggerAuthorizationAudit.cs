using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Principals;
using Microsoft.Extensions.Logging;

namespace VirtualBank.Infrastructure.Auditing;

/// <summary>Structured audit events on the dedicated "VirtualBank.Audit" log category (M1; append-only store follows later).</summary>
public sealed class LoggerAuthorizationAudit(ILoggerFactory loggerFactory) : IAuthorizationAudit
{
    private readonly ILogger _audit = loggerFactory.CreateLogger("VirtualBank.Audit");

    public void Record(PrincipalContext principal, AuthorizationDecision decision) =>
        _audit.LogInformation(
            "AuthorizationDecision {Decision} principal={PrincipalId} action={Action} resource={ResourceType}:{ResourceId} reason={ReasonCode} policy={PolicyId} correlation={CorrelationId}",
            decision.IsAllowed ? "ALLOW" : "DENY", principal.SubjectId, decision.Action.Id, decision.ResourceType,
            decision.ResourceId, decision.ReasonCode, decision.PolicyId, decision.CorrelationId);

    public void RecordPolicyRlsMismatch(PrincipalContext principal, AuthorizationDecision decision) =>
        _audit.LogWarning(
            "PolicyRlsMismatch principal={PrincipalId} action={Action} resource={ResourceType}:{ResourceId} reason={ReasonCode} policy={PolicyId} correlation={CorrelationId}",
            principal.SubjectId, decision.Action.Id, decision.ResourceType, decision.ResourceId,
            decision.ReasonCode, decision.PolicyId, decision.CorrelationId);
}
