using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Abstractions;
using VirtualBank.Infrastructure.Assistant.Offline;
using Xunit;

namespace VirtualBank.Infrastructure.UnitTests.Assistant;

public sealed class OfflineAssistantRuntimeTests
{
    private sealed class RecordingInvoker(string result) : IToolInvoker
    {
        public List<(string Tool, IReadOnlyDictionary<string, string?> Arguments)> Calls { get; } = [];

        public Task<string> InvokeAsync(string toolName, IReadOnlyDictionary<string, string?> arguments, CancellationToken cancellationToken)
        {
            Calls.Add((toolName, arguments));
            return Task.FromResult(result);
        }
    }

    private static string[] Tools(string message) => OfflineAssistantRuntime.Plan(message).Select(p => p.Tool).ToArray();

    [Theory]
    [InlineData("müşterilerimi göster")]
    [InlineData("musterilerimi goster")]
    [InlineData("musterilerimi listele")]
    [InlineData("müsterilerimi göster")]
    [InlineData("list my customers")]
    [InlineData("Zeig mir meine Kunden")]
    [InlineData("müşterilerimi listele")]
    public void Customer_lists_are_recognised_in_three_languages(string message) =>
        Assert.Equal(new[] { "list_customers" }, Tools(message));

    [Theory]
    [InlineData("show credit applications")]
    [InlineData("Welche Kreditanträge habe ich?")]
    [InlineData("kredi başvurularını göster")]
    [InlineData("basvurulari goster")]
    [InlineData("basvurularimi listele")]
    public void Credit_lists_are_recognised_in_three_languages(string message) =>
        Assert.Equal(new[] { "list_credit_applications" }, Tools(message));

    [Theory]
    [InlineData("show my customer total money")]
    [InlineData("show my customers balance")]
    [InlineData("müşterilerimin toplam parasını göster")]
    [InlineData("musterilerimin bakiyelerini listele")]
    [InlineData("Gesamtsaldo meiner Kunden")]
    [InlineData("Kontostände meiner Kunden")]
    public void Financial_requests_never_collapse_to_a_customer_name_list(string question) =>
        Assert.Equal(new[] { "summarize_customer_balances" }, Tools(question));

    [Theory]
    [InlineData("C-1002 müşterimin nakitini göster")]
    [InlineData("show C-1002 account balance")]
    [InlineData("Zeig den Kontostand von C-1002")]
    public void Specific_customer_balance_is_not_a_customer_detail(string question)
    {
        var item = Assert.Single(OfflineAssistantRuntime.Plan(question));
        Assert.Equal("summarize_customer_balances", item.Tool);
        Assert.Equal("C-1002", item.Arguments["customer_id"]);
    }

    [Fact]
    public void Singular_unnamed_balance_request_never_aggregates_all_customers() =>
        Assert.Empty(OfflineAssistantRuntime.Plan("müşterimin bakiyesini göster"));

    [Fact]
    public void Password_synonyms_do_not_route_to_bank_customer_or_RAG_tools()
    {
        Assert.Empty(Tools("Kennwort ändern"));
        Assert.Empty(Tools("password reset"));
        Assert.Empty(Tools("şifremi değiştir"));
    }

    [Fact]
    public void Policy_questions_use_document_search_in_offline_mode()
    {
        Assert.Contains("search_documents", Tools("Welche Kreditrichtlinie gilt?"));
    }

    [Fact]
    public void Identifiers_route_to_the_specific_tools_and_are_normalised()
    {
        var plan = OfflineAssistantRuntime.Plan("show k-100 and customer c-1002 accounts");

        Assert.Equal(new[] { "get_credit_application", "list_customer_accounts" }, plan.Select(p => p.Tool));
        Assert.Equal("K-100", plan[0].Arguments["application_id"]);
        Assert.Equal("C-1002", plan[1].Arguments["customer_id"]);
    }

    [Theory]
    [InlineData("approve K-100")]
    [InlineData("Bitte K-100 genehmigen")]
    [InlineData("K-100 başvurusunu onayla")]
    public void Approval_requests_are_routed_to_the_gated_tool_only(string message) =>
        Assert.Equal(new[] { "approve_credit_application" }, Tools(message));

    [Fact]
    public void Notes_take_the_text_after_the_colon()
    {
        var (tool, arguments) = Assert.Single(OfflineAssistantRuntime.Plan("Notiz zu K-100: Einkommen geprüft"));

        Assert.Equal("add_credit_note", tool);
        Assert.Equal("Einkommen geprüft", arguments["text"]);
    }

    [Fact]
    public async Task Unrecognised_messages_get_help_without_any_tool_call()
    {
        var invoker = new RecordingInvoker("{}");
        var answer = await new OfflineAssistantRuntime().CompleteAsync(Turn("hello"), invoker, CancellationToken.None);

        Assert.Equal(OfflineAssistantRuntime.HelpText, answer);
        Assert.Empty(invoker.Calls);
    }

    [Theory]
    [InlineData("{\"status\":\"denied\",\"notice\":\"CREDIT_READ_REQUIRED\"}", "not available to you")]
    [InlineData("{\"status\":\"not_found\"}", "not found or not visible to you")]
    [InlineData("{\"status\":\"succeeded\",\"data\":[]}", "No accessible records")]
    [InlineData("{\"status\":\"succeeded\",\"data\":{\"id\":\"K-100\",\"email\":null}}", "id: K-100")]
    public async Task Answers_are_rendered_from_tool_results_only(string result, string expected)
    {
        var answer = await new OfflineAssistantRuntime().CompleteAsync(Turn("show K-100"), new RecordingInvoker(result), CancellationToken.None);

        Assert.Contains(expected, answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", answer, StringComparison.Ordinal);
    }

    private static AssistantTurn Turn(string message) => new(AssistantPrompt.System, message, [], 6);
}
