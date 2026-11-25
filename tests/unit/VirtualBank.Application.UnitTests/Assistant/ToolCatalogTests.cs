using System.Text.RegularExpressions;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Authorization.Permissions;
using VirtualBank.Specifications;
using Xunit;

namespace VirtualBank.Application.UnitTests.Assistant;

public sealed class ToolCatalogTests
{
    private static readonly AssistantToolCatalog Catalog = new AssistantHarness().Catalog;

    [Fact]
    public void Tool_names_are_unique_snake_case_and_stable()
    {
        Assert.Equal(
            new[] { "list_customers", "get_customer", "list_customer_accounts", "list_credit_applications",
                    "get_credit_application", "add_credit_note", "approve_credit_application",
                    "summarize_customer_balances", "describe_my_access" },
            Catalog.Definitions.Select(d => d.Name));
        Assert.All(Catalog.Definitions, d => Assert.Matches("^[a-z][a-z_]{2,40}$", d.Name));
    }

    [Fact]
    public void Every_tool_requires_a_catalogued_permission()
    {
        var known = PermissionCatalog.FromFile(RepositoryPaths.Permissions).Permissions;
        foreach (var definition in Catalog.Definitions)
        {
            var tool = Catalog.Find(definition.Name)!;
            Assert.NotEmpty(tool.RequiresAnyOf);
            Assert.All(tool.RequiresAnyOf, p => Assert.Contains(p, (IReadOnlySet<Permission>)known));
        }
    }

    [Fact]
    public void Only_note_writing_is_a_write_and_approval_needs_a_human()
    {
        Assert.Equal(new[] { "add_credit_note" }, Catalog.Definitions.Where(d => d.Risk == ToolRisk.LowRiskWrite).Select(d => d.Name));
        Assert.Equal(new[] { "approve_credit_application" },
            Catalog.Definitions.Where(d => d.Risk == ToolRisk.RequiresHumanConfirmation).Select(d => d.Name));
    }

    [Fact]
    public void Identifier_parameters_have_anchored_patterns()
    {
        var identifiers = Catalog.Definitions.SelectMany(d => d.Parameters).Where(p => p.Kind == ToolParameterKind.Identifier).ToList();

        Assert.NotEmpty(identifiers);
        Assert.All(identifiers, p =>
        {
            Assert.StartsWith("^", p.Pattern);
            Assert.EndsWith("$", p.Pattern);
            Assert.DoesNotMatch(new Regex(p.Pattern!), "C-1002\nK-100");
        });
    }
}
