using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Infrastructure.Assistant.Offline;
using Xunit;

namespace VirtualBank.Application.UnitTests.Assistant;

/// <summary>The deterministic offline runtime driving the real gate and use cases, as in the demo without an API key.</summary>
public sealed class OfflineRuntimeIntegrationTests
{
    [Fact]
    public async Task German_request_for_customers_and_credit_is_partially_answered_for_an_advisor()
    {
        var reply = (await new AssistantHarness().AskAsync("anna.berater", new OfflineAssistantRuntime(),
            "Zeig mir meine Kunden und Kreditanträge")).Value!;

        Assert.Equal(new[] { "list_credit_applications", "list_customers" }, reply.ToolCalls.Select(c => c.Tool));
        Assert.Equal(new[] { ToolCallStatus.Denied, ToolCallStatus.Succeeded }, reply.ToolCalls.Select(c => c.Status));
        Assert.Equal(new[] { "CREDIT_READ_REQUIRED" }, reply.NoticeCodes);
        var customerRows = Assert.Single(reply.Tables!).Rows;
        Assert.Contains(customerRows, row => row[0] == "C-1002");
        Assert.DoesNotContain(customerRows, row => row[0] == "C-5001");
        Assert.Contains("3", reply.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approver_approval_request_ends_in_the_confirmation_notice()
    {
        var reply = (await new AssistantHarness().AskAsync("max.approver", new OfflineAssistantRuntime(), "approve K-100")).Value!;

        Assert.Equal(new[] { "ASSISTANT_HUMAN_CONFIRMATION_REQUIRED" }, reply.NoticeCodes);
        Assert.Contains("not available to you", reply.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analyst_cannot_add_a_note_without_human_confirmation()
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("mehmet.kredit", new OfflineAssistantRuntime(), "note K-100: income verified")).Value!;

        Assert.Equal(ToolCallStatus.Denied, Assert.Single(reply.ToolCalls).Status);
        Assert.Contains(AssistantNoticeCodes.HumanConfirmationRequired, reply.NoticeCodes);
        Assert.Empty(harness.Credit.InsertedNotes);
    }

    /// <summary>The persona table in the README, executed.</summary>
    [Theory]
    [InlineData("anna.berater", "show my customers and credit applications", "DENIED,SUCCEEDED", "CREDIT_READ_REQUIRED")]
    [InlineData("mehmet.kredit", "show my customers and credit applications", "SUCCEEDED,SUCCEEDED", "")]
    [InlineData("max.approver", "show my customers and credit applications", "SUCCEEDED,DENIED", "ASSISTANT_TOOL_NOT_PERMITTED")]
    [InlineData("anna.berater", "show customer C-5002", "NOT_FOUND", "")]
    [InlineData("mehmet.kredit", "show customer C-5002", "SUCCEEDED", "")]
    [InlineData("max.approver", "show customer C-5002", "DENIED", "ASSISTANT_TOOL_NOT_PERMITTED")]
    [InlineData("anna.berater", "note K-100: income verified", "DENIED", "CREDIT_NOTE_PERMISSION_REQUIRED")]
    [InlineData("max.approver", "note K-100: income verified", "DENIED", "CREDIT_NOTE_PERMISSION_REQUIRED")]
    [InlineData("anna.berater", "approve K-100", "DENIED", "CREDIT_APPROVE_REQUIRED")]
    [InlineData("mehmet.kredit", "approve K-100", "DENIED", "CREDIT_APPROVE_REQUIRED")]
    public async Task Readme_persona_table_holds(string persona, string message, string statuses, string notices)
    {
        var reply = (await new AssistantHarness().AskAsync(persona, new OfflineAssistantRuntime(), message)).Value!;

        Assert.Equal(statuses, string.Join(",", reply.ToolCalls.Select(c => Name(c.Status))));
        Assert.Equal(notices, string.Join(",", reply.NoticeCodes));
    }

    [Fact]
    public async Task Turkish_ascii_keyboard_and_umlutted_variants_return_the_same_authorized_customer_table()
    {
        var harness = new AssistantHarness();
        foreach (var question in new[] { "müşterilerimi göster", "musterilerimi goster", "müsterilerimi göster" })
        {
            var reply = (await harness.AskAsync("anna.berater", new OfflineAssistantRuntime(), question)).Value!;
            Assert.Equal(ToolCallStatus.Succeeded, Assert.Single(reply.ToolCalls).Status);
            var table = Assert.Single(reply.Tables!);
            Assert.Equal(3, table.TotalCount);
            Assert.Equal(new[] { "C-1002", "C-1003", "C-1004" }, table.Rows.Select(row => row[0]));
            Assert.DoesNotContain("C-5001", string.Join(',', table.Rows.SelectMany(r => r)));
        }
    }

    private static string Name(ToolCallStatus status) => status switch
    {
        ToolCallStatus.Succeeded => "SUCCEEDED",
        ToolCallStatus.NotFound => "NOT_FOUND",
        ToolCallStatus.Denied => "DENIED",
        ToolCallStatus.Invalid => "INVALID",
        _ => "FAILED",
    };
}
