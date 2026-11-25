using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Customers.Queries.SummarizeCustomerBalances;
using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Infrastructure.Assistant.Offline;
using Xunit;
using static VirtualBank.Application.UnitTests.Assistant.ScriptedRuntime;

namespace VirtualBank.Application.UnitTests.Assistant;

public sealed class BalanceAssistantTests
{
    [Fact]
    public async Task Wrong_tool_cannot_turn_customer_names_into_a_fabricated_money_answer()
    {
        var reply = (await new AssistantHarness().AskAsync("anna.berater",
            new ScriptedRuntime("These customers own 7 million euros", Call("list_customers")),
            "show my customer total money")).Value!;

        Assert.DoesNotContain("7 million", reply.Answer);
        Assert.Contains(AssistantNoticeCodes.NoVerifiedFinancialData, reply.NoticeCodes);
        Assert.Empty(reply.Tables!);
    }

    [Fact]
    public async Task Correct_tool_uses_only_visible_accounts_and_has_an_auditable_table()
    {
        var reply = (await new AssistantHarness().AskAsync("anna.berater",
            new ScriptedRuntime("FAKE balance EUR 999999", Call("summarize_customer_balances")),
            "show my customer total money")).Value!;

        Assert.DoesNotContain("999999", reply.Answer);
        Assert.Contains("301.50 EUR", reply.Answer);
        Assert.Contains("not physical cash", reply.Answer);
        var table = Assert.Single(reply.Tables!);
        Assert.Equal("summarize_customer_balances", table.SourceTool);
        Assert.Equal(3, table.Rows.Count); // only 3 Stuttgart customers
        Assert.DoesNotContain(table.Rows, row => row[0] == "C-5001" || row[0] == "C-5002");
        Assert.Equal(new[] { "C-1002", "C-1003", "C-1004" }, table.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task Model_may_not_substitute_all_customers_when_one_id_was_requested()
    {
        var reply = (await new AssistantHarness().AskAsync("anna.berater",
            new ScriptedRuntime("The balance is 999999", Call("summarize_customer_balances")),
            "show balance for C-1002")).Value!;
        Assert.Empty(reply.Tables!);
        Assert.DoesNotContain("999999", reply.Answer);
        Assert.Equal(ToolCallStatus.Invalid, Assert.Single(reply.ToolCalls).Status);
        Assert.Contains(AssistantNoticeCodes.NoVerifiedFinancialData, reply.NoticeCodes);
    }

    [Fact]
    public async Task Model_may_not_substitute_an_unrequested_customer_name()
    {
        var reply = (await new AssistantHarness().AskAsync("anna.berater",
            new ScriptedRuntime("Oliver owns 999999", Call("summarize_customer_balances", ("customer_name", "Oliver"))),
            "Jana'nın bakiyesini göster")).Value!;
        Assert.Empty(reply.Tables!);
        Assert.DoesNotContain("999999", reply.Answer);
        Assert.Contains(AssistantNoticeCodes.NoVerifiedFinancialData, reply.NoticeCodes);
    }

    [Fact]
    public async Task A_foreign_customer_balance_is_not_disclosed_even_if_model_calls_tool()
    {
        var reply = (await new AssistantHarness().AskAsync("anna.berater",
            new ScriptedRuntime("C-5001 owns a fortune",
                Call("summarize_customer_balances", ("customer_id", "C-5001"))),
            "show balance of C-5001")).Value!;

        Assert.Empty(reply.Tables!);
        Assert.DoesNotContain("fortune", reply.Answer);
        Assert.Contains("not found or not visible", reply.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Currency_amounts_are_displayed_separately_and_never_forged_into_one_number()
    {
        var overview = new CustomerBalanceOverviewView([],
            [new CurrencyBalanceTotal("EUR", 100m, 1), new CurrencyBalanceTotal("USD", 200m, 1)],
            1, false);
        var answer = AssistantBalanceFormatter.Format(overview, AssistantLanguage.English);

        Assert.Contains("100.00 EUR", answer);
        Assert.Contains("200.00 USD", answer);
        Assert.Contains("Different currencies were not added", answer);
        Assert.DoesNotContain("300.00", answer);
    }

    [Fact]
    public async Task Offline_aliases_are_consistent_and_result_in_verified_finance_data()
    {
        foreach (var question in new[] { "show my customer total money", "musterilerimin toplam bakiyesini göster",
                     "Gesamtsaldo meiner Kunden" })
        {
            var reply = (await new AssistantHarness().AskAsync("anna.berater", new OfflineAssistantRuntime(), question)).Value!;
            Assert.Contains(reply.ToolCalls, r => r.Tool == "summarize_customer_balances" && r.Status == ToolCallStatus.Succeeded);
            Assert.Single(reply.Tables!);
        }
    }

    [Fact]
    public async Task A_unique_visible_name_can_be_resolved_without_any_cross_customer_read()
    {
        var reply = (await new AssistantHarness().AskAsync("anna.berater", new OfflineAssistantRuntime(),
            "Jana müşterimin bakiyesini göster")).Value!;
        Assert.Contains(reply.ToolCalls, r => r.Tool == "summarize_customer_balances" && r.Status == ToolCallStatus.Succeeded);
        Assert.Equal("C-1002", Assert.Single(Assert.Single(reply.Tables!).Rows)[0]);
    }
}
