using VirtualBank.Application.Authorization;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Authorization.Resources;
using VirtualBank.Infrastructure.Identity;

namespace VirtualBank.Specifications;

/// <summary>One row of tests/specifications/authorization-matrix.yaml.</summary>
public sealed class MatrixCase
{
    public string Id { get; set; } = string.Empty;

    public string Principal { get; set; } = string.Empty;

    public string Resource { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Expected { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public List<string> Invariants { get; set; } = [];

    /// <summary>"active" (default) or "planned" (module not implemented yet).</summary>
    public string Status { get; set; } = "active";

    /// <summary>Owning module of a planned case, e.g. "hr".</summary>
    public string? Module { get; set; }

    /// <summary>Milestone in which a planned case becomes active, e.g. "M6".</summary>
    public string? Target { get; set; }

    public bool IsPlanned => string.Equals(Status, "planned", StringComparison.Ordinal);

    public override string ToString() => Id;
}

public sealed class MatrixDocument
{
    public int Version { get; set; }

    public string Milestone { get; set; } = string.Empty;

    public List<MatrixCase> Cases { get; set; } = [];
}

public sealed record MatrixOutcome(MatrixCase Case, bool Passed, string Message);

/// <summary>
/// Evaluates matrix cases against the real authorization engine. The evaluator is context-agnostic:
/// the caller supplies the policies under test and the resolver that turns "type:id" references into
/// resource descriptors, so this library depends on no bounded context.
/// </summary>
public sealed class MatrixEvaluator
{
    private readonly PrincipalResolver _resolver;
    private readonly ResourceAuthorizationService _service;
    private readonly Func<string, ResourceDescriptor> _resolveResource;

    public MatrixEvaluator(PersonaTokens personas, IEnumerable<IResourcePolicy> policies, Func<string, ResourceDescriptor> resolveResource)
    {
        Personas = personas;
        _resolver = new PrincipalResolver(PermissionCatalog.FromFile(RepositoryPaths.Permissions), TrustedMachineClients.FromServiceSettings());
        _service = new ResourceAuthorizationService(policies);
        _resolveResource = resolveResource;
    }

    public PersonaTokens Personas { get; }

    /// <summary>Decision of the configured policies for a case, without comparing it to the expectation.</summary>
    public Application.Authorization.Decisions.AuthorizationDecision Decide(MatrixCase matrixCase)
    {
        var principal = _resolver.Resolve(Personas.For(matrixCase.Principal));
        return _service.Authorize(principal, _resolveResource(matrixCase.Resource), new Permission(matrixCase.Action));
    }

    public MatrixOutcome Evaluate(MatrixCase matrixCase)
    {
        var expectedAllow = matrixCase.Expected switch
        {
            "allow" => true,
            "deny" => false,
            _ => throw new FormatException($"Case {matrixCase.Id}: expected must be 'allow' or 'deny'."),
        };

        var decision = Decide(matrixCase);
        if (decision.IsAllowed != expectedAllow)
        {
            return new MatrixOutcome(matrixCase, false,
                $"{matrixCase.Id}: expected {matrixCase.Expected} but was {(decision.IsAllowed ? "allow" : "deny")} ({decision.ReasonCode}, {decision.PolicyId}).");
        }

        if (matrixCase.Reason is not null && !string.Equals(matrixCase.Reason, decision.ReasonCode, StringComparison.Ordinal))
        {
            return new MatrixOutcome(matrixCase, false,
                $"{matrixCase.Id}: expected reason {matrixCase.Reason} but was {decision.ReasonCode}.");
        }

        return new MatrixOutcome(matrixCase, true, $"{matrixCase.Id}: {decision.ReasonCode}");
    }
}
