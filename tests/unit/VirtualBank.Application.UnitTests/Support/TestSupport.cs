using VirtualBank.Application.Authorization.Auditing;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Infrastructure.Identity;
using VirtualBank.Specifications;

namespace VirtualBank.Application.UnitTests.Support;

/// <summary>Principals resolved from the realm personas with the same resolver and machine-client list as the API.</summary>
internal static class Personas
{
    private static readonly PersonaTokens Tokens = PersonaTokens.Load(RepositoryPaths.Realm);
    private static readonly PrincipalResolver Resolver =
        new(PermissionCatalog.FromFile(RepositoryPaths.Permissions), TrustedMachineClients.FromServiceSettings());

    public static PrincipalContext Get(string username) => Resolver.Resolve(Tokens.For(username));

    public static string SubjectOf(string username) => Tokens.SubjectIds[username];
}

internal sealed class RecordingAudit : IAuthorizationAudit
{
    public List<AuthorizationDecision> Decisions { get; } = [];

    public List<AuthorizationDecision> Mismatches { get; } = [];

    public void Record(PrincipalContext principal, AuthorizationDecision decision) => Decisions.Add(decision);

    public void RecordPolicyRlsMismatch(PrincipalContext principal, AuthorizationDecision decision) => Mismatches.Add(decision);
}
