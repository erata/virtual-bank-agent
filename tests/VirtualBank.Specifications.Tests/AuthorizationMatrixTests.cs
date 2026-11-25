using VirtualBank.Application.Assistant.Authorization;
using VirtualBank.Application.Authorization.Decisions;
using VirtualBank.Application.Documents.Authorization;
using VirtualBank.Application.Authorization.Policies;
using VirtualBank.Application.Credit.Authorization;
using VirtualBank.Application.Customers.Authorization;
using Xunit;

namespace VirtualBank.Specifications.Tests;

/// <summary>
/// Executes tests/specifications/authorization-matrix.yaml. Expected outcomes live only in the YAML file.
/// <list type="bullet">
/// <item>Active cases run against the policies the running services actually register.</item>
/// <item>Planned cases (modules not built yet) run against their design policies, and must additionally be
/// denied with NO_POLICY by the running API, so a green matrix never claims enforcement that does not exist.</item>
/// </list>
/// </summary>
public sealed class AuthorizationMatrixTests
{
    /// <summary>Policy sets of the modules, exactly as the composition root registers them in the API.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<IResourcePolicy>> RunningServices { get; } =
        new Dictionary<string, IReadOnlyList<IResourcePolicy>>(StringComparer.Ordinal)
        {
            ["api (customers + credit + assistant)"] = [.. CustomerPolicies.Create(), .. CreditPolicies.Create(), .. AssistantPolicies.Create(), .. DocumentPolicies.Create()],
        };

    private static readonly PersonaTokens Personas = PersonaTokens.Load(RepositoryPaths.Realm);

    private static readonly FixtureResources Resources = FixtureResources.Load(RepositoryPaths.Fixtures, Personas.SubjectIds);

    private static readonly MatrixEvaluator Running = Evaluator(RunningServices.Values.SelectMany(p => p));

    private static readonly MatrixEvaluator Design = Evaluator(PlannedPolicies.Create());

    private static readonly List<MatrixCase> Cases = MatrixLoader.Load().Cases;

    private static readonly Dictionary<string, MatrixCase> CasesById = Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);

    private static MatrixEvaluator Evaluator(IEnumerable<IResourcePolicy> policies) => new(Personas, policies, Resources.Resolve);

    // Case IDs (strings) keep theory data serializable and give readable test names.
    public static IEnumerable<object[]> ActiveCaseIds() => Cases.Where(c => !c.IsPlanned).Select(c => new object[] { c.Id });

    public static IEnumerable<object[]> PlannedCaseIds() => Cases.Where(c => c.IsPlanned).Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(ActiveCaseIds))]
    public void Active_case_matches_specification(string caseId)
    {
        var outcome = Running.Evaluate(CasesById[caseId]);
        Assert.True(outcome.Passed, outcome.Message);
    }

    [Theory]
    [MemberData(nameof(PlannedCaseIds))]
    public void Planned_case_matches_its_design(string caseId)
    {
        var outcome = Design.Evaluate(CasesById[caseId]);
        Assert.True(outcome.Passed, outcome.Message);
    }

    [Theory]
    [MemberData(nameof(PlannedCaseIds))]
    public void Planned_case_is_denied_by_every_running_service(string caseId)
    {
        foreach (var (service, policies) in RunningServices)
        {
            var decision = Evaluator(policies).Decide(CasesById[caseId]);
            Assert.False(decision.IsAllowed, $"{caseId}: {service} allows a capability whose module does not exist yet.");
            Assert.Equal(ReasonCodes.NoPolicy, decision.ReasonCode);
        }
    }

    [Fact]
    public void Planned_cases_name_a_module_and_a_target_milestone()
    {
        Assert.Empty(Cases.Where(c => c.IsPlanned && (string.IsNullOrEmpty(c.Module) || string.IsNullOrEmpty(c.Target))).Select(c => c.Id));
        Assert.Empty(Cases.Where(c => c.Status is not ("active" or "planned")).Select(c => c.Id));
    }

    [Fact]
    public void Matrix_case_ids_are_unique()
    {
        Assert.Empty(Cases.GroupBy(c => c.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [Fact]
    public void Matrix_covers_every_persona()
    {
        var covered = Cases.Select(c => c.Principal).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(Personas.SubjectIds.Keys.Except(covered));
    }

    [Fact]
    public void Policy_ids_are_unique_across_running_and_planned_policies()
    {
        var all = RunningServices.Values.SelectMany(p => p).Concat(PlannedPolicies.Create());
        Assert.Empty(all.GroupBy(p => p.PolicyId, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));
    }
}
