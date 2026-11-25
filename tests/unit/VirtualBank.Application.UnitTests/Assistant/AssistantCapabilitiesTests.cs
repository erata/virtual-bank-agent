using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.UnitTests.Support;
using Xunit;

namespace VirtualBank.Application.UnitTests.Assistant;

public sealed class AssistantCapabilitiesTests
{
    [Fact]
    public void Platform_admin_only_gets_permission_inspection_not_bank_data_suggestions()
    {
        var harness = new AssistantHarness();
        var result = AssistantCapabilitiesBuilder.Build(Personas.Get("lena.admin"),
            harness.Catalog, harness.Limits, "ollama", "tr");
        Assert.Contains(result.Suggestions, x => x.Tool == "describe_my_access");
        Assert.DoesNotContain(result.AvailableTools, x => x is "list_customers" or "list_credit_applications" or "summarize_customer_balances");
        Assert.DoesNotContain(result.Suggestions, x => x.Tool == "summarize_customer_balances");
    }

    [Fact]
    public void Advisor_sees_customer_tools_but_not_credit_read_or_self_service()
    {
        var harness = new AssistantHarness();
        var result = AssistantCapabilitiesBuilder.Build(Personas.Get("anna.berater"),
            harness.Catalog, harness.Limits, "ollama", "tr");
        Assert.Contains(result.Suggestions, x => x.Tool == "list_customers");
        Assert.DoesNotContain(result.AvailableTools, x => x == "list_credit_applications" || x.StartsWith("get_my_", StringComparison.Ordinal));
    }

    [Fact]
    public void Suggestions_remain_permission_scoped_even_in_explain_mode()
    {
        var harness = new AssistantHarness(new AssistantLimits { ToolVisibility = ToolVisibilityMode.Explain });
        var result = AssistantCapabilitiesBuilder.Build(Personas.Get("lena.admin"),
            harness.Catalog, harness.Limits, "ollama", "en");
        Assert.DoesNotContain(result.AvailableTools, x => x is "list_customers" or "get_credit_application");
    }
}
