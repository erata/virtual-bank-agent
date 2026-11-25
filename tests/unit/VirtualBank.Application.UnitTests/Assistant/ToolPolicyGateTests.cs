using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.UnitTests.Support;
using Xunit;

namespace VirtualBank.Application.UnitTests.Assistant;

public sealed class ToolPolicyGateTests
{
    private static readonly AssistantToolCatalog Catalog = new AssistantHarness().Catalog;
    private static readonly ToolPolicyGate Gate = new(new AssistantLimits(), AssistantHarness.Notices);

    private static GateResult Evaluate(string persona, string tool, params (string Key, string? Value)[] arguments) =>
        Gate.Evaluate(Personas.Get(persona), Catalog.Find(tool), arguments.ToDictionary(a => a.Key, a => a.Value), 0, 0);

    [Fact]
    public void Undeclared_arguments_are_dropped_before_the_use_case_runs()
    {
        var result = Evaluate("anna.berater", "get_customer", ("customer_id", " C-1002 "), ("as_user", "lena.admin"));

        Assert.True(result.IsAllowed);
        Assert.Equal("C-1002", result.Arguments!.Get("customer_id"));
        Assert.Null(result.Arguments.Find("as_user"));
    }

    [Fact]
    public void Budget_is_checked_before_anything_else()
    {
        var result = Gate.Evaluate(Personas.Get("lena.admin"), null, new Dictionary<string, string?>(), callsSoFar: 6, writesSoFar: 0);

        Assert.Equal(AssistantNoticeCodes.ToolCallLimitReached, result.Rejection!.NoticeCode);
    }

    [Fact]
    public void Permission_is_checked_before_arguments_so_denials_do_not_depend_on_input()
    {
        var result = Evaluate("anna.berater", "get_credit_application", ("application_id", "not-an-id"));

        Assert.Equal(ToolCallStatus.Denied, result.Rejection!.Status);
        Assert.Equal("CREDIT_READ_REQUIRED", result.Rejection.NoticeCode);
    }

    [Fact]
    public void Model_initiated_note_writes_require_confirmation_regardless_of_text()
    {
        var result = Evaluate("mehmet.kredit", "add_credit_note", ("application_id", "K-100"), ("text", new string('x', 4001)));

        Assert.Equal(AssistantNoticeCodes.HumanConfirmationRequired, result.Rejection!.NoticeCode);
    }
}
