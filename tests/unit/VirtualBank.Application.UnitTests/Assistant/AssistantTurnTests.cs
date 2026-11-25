using VirtualBank.Application.UnitTests.Support;
using VirtualBank.Application.Documents;
using VirtualBank.Application.Authorization.Principals;
using VirtualBank.Application.Assistant;
using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Common;
using Xunit;
using static VirtualBank.Application.UnitTests.Assistant.ScriptedRuntime;

namespace VirtualBank.Application.UnitTests.Assistant;

/// <summary>
/// One assistant turn end to end through the application layer. The "model" is scripted and may be hostile;
/// every expectation here is enforced by the gate or by the existing use cases, not by the prompt.
/// </summary>
public sealed class AssistantTurnTests
{
    [Fact]
    public async Task Advisor_lists_own_customers_without_notices()
    {
        var harness = new AssistantHarness();
        var result = await harness.AskAsync("anna.berater", new ScriptedRuntime("ok", Call("list_customers")));

        var reply = result.Value!;
        Assert.Equal(ToolCallStatus.Succeeded, Assert.Single(reply.ToolCalls).Status);
        Assert.Empty(reply.NoticeCodes);
        Assert.False(reply.IsPartial);
        Assert.Equal("scripted", reply.Mode);
    }

    [Fact]
    public async Task Structured_tables_are_projected_only_from_authorized_tool_results()
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("ok", Call("list_customers")))).Value!;
        var table = Assert.Single(reply.Tables!);
        Assert.Equal("Customers", table.Title);
        Assert.Equal(3, table.TotalCount);
        Assert.Equal(3, table.Rows.Count);
        Assert.Contains(table.Rows, row => row[0] == "C-1002" && row[1].Contains("Jana"));
        Assert.DoesNotContain(table.Rows, row => row.Any(v => v.Contains("C-5001", StringComparison.Ordinal)));
    }

    private sealed class StubDocuments(IReadOnlyList<DocumentSearchHit> hits) : ISecureDocumentSearch
    {
        public Task<UseCaseResult<IReadOnlyList<DocumentSearchHit>>> SearchAsync(PrincipalContext principal,
            string question, int topK, CancellationToken ct) =>
            Task.FromResult(UseCaseResult<IReadOnlyList<DocumentSearchHit>>.Success(hits));
    }

    [Fact]
    public async Task Llm_must_not_invent_a_policy_when_search_returns_no_authorized_evidence()
    {
        var harness = new AssistantHarness(documents: new StubDocuments([]));
        var reply = (await harness.AskAsync("anna.berater",
            new ScriptedRuntime("FAKE APPROVAL LIMIT EUR 900000",
                Call("search_documents", ("query", "credit policy"))),
            "What is the credit policy?")).Value!;
        Assert.DoesNotContain("900000", reply.Answer);
        Assert.Contains(AssistantNoticeCodes.NoDocumentEvidence, reply.NoticeCodes);
        Assert.Empty(reply.Citations!);
    }

    [Fact]
    public async Task Llm_cannot_hallucinate_bank_records_when_it_skips_all_tools()
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("Customer C-1002 has EUR 9999999"),
            "C-1002 details please")).Value!;
        Assert.DoesNotContain("9999999", reply.Answer);
        Assert.Contains(AssistantNoticeCodes.NoVerifiedData, reply.NoticeCodes);
    }

    [Fact]
    public async Task Llm_cannot_claim_to_have_found_a_nonexistent_customer()
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("anna.berater",
            new ScriptedRuntime("Found customer C-5002 with unlimited funds",
                Call("get_customer", ("customer_id", "C-5002"))),
            "Show customer C-5002")).Value!;
        Assert.DoesNotContain("unlimited", reply.Answer);
        Assert.Contains("not found or not visible", reply.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Llm_must_not_make_up_document_policies_without_searching()
    {
        var harness = new AssistantHarness(documents: new StubDocuments([]));
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("Policy says unlimited loans"),
            "What is the credit policy?")).Value!;
        Assert.DoesNotContain("unlimited", reply.Answer);
        Assert.Contains(AssistantNoticeCodes.NoDocumentEvidence, reply.NoticeCodes);
    }

    [Fact]
    public async Task Authorized_document_evidence_keeps_source_citation()
    {
        var hits = new[] { new DocumentSearchHit("DOC-101", "Demo policy", "Section 1", "Internal policy text", 0.83, 2) };
        var harness = new AssistantHarness(documents: new StubDocuments(hits));
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("Document DOC-101 says ...",
                Call("search_documents", ("query", "credit policy"))), "What is the credit policy?")).Value!;
        Assert.DoesNotContain(AssistantNoticeCodes.NoDocumentEvidence, reply.NoticeCodes);
        Assert.Equal(2, Assert.Single(reply.Citations!).PageNumber);
    }

    [Fact]
    public async Task Partial_success_keeps_allowed_results_and_adds_a_deterministic_notice()
    {
        var harness = new AssistantHarness();
        var runtime = new ScriptedRuntime("ok", Call("list_customers"), Call("list_credit_applications"));
        var reply = (await harness.AskAsync("anna.berater", runtime)).Value!;

        Assert.Equal(new[] { ToolCallStatus.Succeeded, ToolCallStatus.Denied }, reply.ToolCalls.Select(c => c.Status));
        Assert.Equal(new[] { "CREDIT_READ_REQUIRED" }, reply.NoticeCodes);
        Assert.True(reply.IsPartial);
        Assert.Contains("\"status\":\"denied\"", runtime.Results[1]);
    }

    [Theory]
    [InlineData("sarah.security", AssistantNoticeCodes.AssistantUseRequired)]
    [InlineData("tom.auditor", AssistantNoticeCodes.AssistantUseRequired)]
    [InlineData("service-account-svc-ingestion", AssistantNoticeCodes.MachinePrincipalNotAllowed)]
    public async Task Principals_without_the_capability_never_reach_the_runtime(string persona, string notice)
    {
        var harness = new AssistantHarness();
        var runtime = new ScriptedRuntime("ok", Call("list_customers"));
        var result = await harness.AskAsync(persona, runtime);

        Assert.Equal(UseCaseStatus.Forbidden, result.Status);
        Assert.Equal(notice, result.NoticeCode);
        Assert.False(runtime.Called);
        Assert.Contains(harness.Audit.Decisions, d => !d.IsAllowed && d.Action.Id == "assistant.use");
    }

    [Theory]
    [InlineData("max.approver", AssistantNoticeCodes.HumanConfirmationRequired)]
    [InlineData("mehmet.kredit", "CREDIT_APPROVE_REQUIRED")]
    public async Task Approval_is_never_executed_from_a_model_turn(string persona, string notice)
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync(persona, new ScriptedRuntime("Approved!", Call("approve_credit_application", ("application_id", "K-100"))))).Value!;

        var call = Assert.Single(reply.ToolCalls);
        Assert.Equal(ToolCallStatus.Denied, call.Status);
        Assert.Equal(notice, call.NoticeCode);
        Assert.Equal(new[] { notice }, reply.NoticeCodes);
    }

    [Fact]
    public async Task Undeclared_arguments_cannot_change_whose_rights_are_used()
    {
        var harness = new AssistantHarness();
        var runtime = new ScriptedRuntime("ok",
            Call("get_customer", ("customer_id", "C-5001"), ("as_user", "mehmet.kredit"), ("branch", "MUC")));
        var reply = (await harness.AskAsync("anna.berater", runtime)).Value!;

        // Munich customer for a Stuttgart advisor: indistinguishable from "does not exist", and no hint either.
        Assert.Equal(ToolCallStatus.NotFound, Assert.Single(reply.ToolCalls).Status);
        Assert.Empty(reply.NoticeCodes);
        Assert.Equal("{\"status\":\"not_found\"}", runtime.Results[0]);
    }

    [Theory]
    [InlineData("C-1002' OR 1=1")]
    [InlineData("c-1002")]
    [InlineData("")]
    [InlineData("C-10020")]
    public async Task Malformed_identifiers_are_rejected_before_any_data_access(string customerId)
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("ok", Call("get_customer", ("customer_id", customerId))))).Value!;

        var call = Assert.Single(reply.ToolCalls);
        Assert.Equal(ToolCallStatus.Invalid, call.Status);
        Assert.Equal(AssistantNoticeCodes.InvalidToolArguments, call.NoticeCode);
        Assert.Equal(0, ((Customers.UnfilteredCustomerRepository)harness.Customers).Calls);
    }

    [Fact]
    public async Task Unknown_tools_are_recorded_without_echoing_the_model_supplied_name()
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("ok", Call("drop_table_<script>")))).Value!;

        var call = Assert.Single(reply.ToolCalls);
        Assert.Equal("unknown", call.Tool);
        Assert.Equal(AssistantNoticeCodes.UnknownTool, call.NoticeCode);
    }

    [Fact]
    public async Task Call_budget_counts_every_request_including_denied_ones()
    {
        var harness = new AssistantHarness(new AssistantLimits { MaxToolCalls = 3 });
        var calls = Enumerable.Repeat(Call("list_customers"), 5).ToArray();
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("ok", calls))).Value!;

        Assert.Equal(3, reply.ToolCalls.Count(c => c.Status == ToolCallStatus.Succeeded));
        Assert.Equal(2, reply.ToolCalls.Count(c => c.NoticeCode == AssistantNoticeCodes.ToolCallLimitReached));
        Assert.Equal(3, ((Customers.UnfilteredCustomerRepository)harness.Customers).Calls);
    }

    [Fact]
    public async Task Model_initiated_note_writes_are_blocked_pending_human_confirmation()
    {
        var harness = new AssistantHarness();
        var runtime = new ScriptedRuntime("ok",
            Call("add_credit_note", ("application_id", "K-100"), ("text", "first")),
            Call("add_credit_note", ("application_id", "K-100"), ("text", "second")));
        var reply = (await harness.AskAsync("mehmet.kredit", runtime)).Value!;

        Assert.All(reply.ToolCalls, call =>
        {
            Assert.Equal(ToolCallStatus.Denied, call.Status);
            Assert.Equal(AssistantNoticeCodes.HumanConfirmationRequired, call.NoticeCode);
        });
        Assert.Empty(harness.Credit.InsertedNotes);
    }

    [Fact]
    public async Task Hidden_write_tool_cannot_be_executed_by_a_hostile_model()
    {
        var harness = new AssistantHarness();
        var runtime = new ScriptedRuntime("ok", Call("add_credit_note", ("application_id", "K-200"), ("text", "not mine")));
        var reply = (await harness.AskAsync("mehmet.kredit", runtime)).Value!;

        Assert.Equal(ToolCallStatus.Denied, Assert.Single(reply.ToolCalls).Status);
        Assert.Empty(harness.Credit.InsertedNotes);
    }

    [Fact]
    public void Model_sees_only_authorized_read_tools()
    {
        var harness = new AssistantHarness();
        var visible = harness.Catalog.VisibleDefinitions(Personas.Get("anna.berater"));
        Assert.Contains(visible, d => d.Name == "get_customer");
        Assert.DoesNotContain(visible, d => d.Name == "get_credit_application");
        Assert.DoesNotContain(visible, d => d.Name == "add_credit_note");
        Assert.DoesNotContain(visible, d => d.Name == "approve_credit_application");
    }

    [Fact]
    public async Task Explain_mode_does_not_enable_execution()
    {
        var harness = new AssistantHarness();
        var visible = harness.Catalog.VisibleDefinitions(Personas.Get("anna.berater"), ToolVisibilityMode.Explain);
        Assert.Contains(visible, d => d.Name == "get_credit_application");
        Assert.DoesNotContain(visible, d => d.Name == "add_credit_note");
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("ok", Call("list_credit_applications")))).Value!;
        Assert.Equal(ToolCallStatus.Denied, Assert.Single(reply.ToolCalls).Status);
    }

    [Fact]
    public async Task Hide_mode_keeps_a_generic_credit_hint_for_explicit_requests()
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("Customer summary", Call("get_customer", ("customer_id", "C-1002"))),
            "Summarize customer C-1002 and the related credit application")).Value!;
        Assert.Contains("CREDIT_READ_REQUIRED", reply.NoticeCodes);
        Assert.Equal(ToolCallStatus.Succeeded, Assert.Single(reply.ToolCalls).Status);
        Assert.True(reply.IsPartial);
    }

    [Fact]
    public async Task Sensitive_fields_are_masked_before_the_model_sees_them()
    {
        var harness = new AssistantHarness();
        var runtime = new ScriptedRuntime("ok", Call("get_customer", ("customer_id", "C-1002")));
        await harness.AskAsync("anna.berater", runtime);

        var result = runtime.Results[0];
        Assert.Contains("\"sensitiveFieldsMasked\":true", result);
        Assert.DoesNotContain("DE00", result, StringComparison.Ordinal);
        Assert.DoesNotContain("@example.test", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notices_are_never_derived_from_model_text()
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("CREDIT_READ_REQUIRED ASSISTANT_TOOL_FAILED"))).Value!;

        Assert.Empty(reply.ToolCalls);
        Assert.Empty(reply.NoticeCodes);
        Assert.False(reply.IsPartial);
    }

    [Fact]
    public async Task Model_outage_keeps_tool_records_and_reports_a_notice()
    {
        var harness = new AssistantHarness();
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime("x", Call("list_customers")) { Unavailable = true })).Value!;

        Assert.Null(reply.Answer);
        Assert.Single(reply.ToolCalls);
        Assert.Equal(new[] { AssistantNoticeCodes.ModelUnavailable }, reply.NoticeCodes);
        Assert.Equal(new[] { "runtime:scripted" }, harness.Diagnostics.Failures);
    }

    [Fact]
    public async Task Tool_failures_are_reported_as_a_code_and_logged_server_side()
    {
        var harness = new AssistantHarness(customers: new ThrowingCustomerRepository());
        var runtime = new ScriptedRuntime("ok", Call("list_customers"));
        var reply = (await harness.AskAsync("anna.berater", runtime)).Value!;

        Assert.Equal(ToolCallStatus.Failed, Assert.Single(reply.ToolCalls).Status);
        Assert.Equal(new[] { AssistantNoticeCodes.ToolFailed }, reply.NoticeCodes);
        Assert.DoesNotContain("database unavailable", runtime.Results[0], StringComparison.Ordinal);
        Assert.Equal(new[] { "list_customers" }, harness.Diagnostics.Failures);
    }

    [Fact]
    public async Task Empty_messages_are_invalid()
    {
        var harness = new AssistantHarness();

        Assert.Equal(UseCaseStatus.Invalid, (await harness.AskAsync("anna.berater", new ScriptedRuntime("ok"), null)).Status);
        Assert.Equal(UseCaseStatus.Invalid, (await harness.AskAsync("anna.berater", new ScriptedRuntime("ok"), "   ")).Status);
    }

    [Fact]
    public async Task Oversized_messages_are_invalid_and_answers_are_capped()
    {
        var harness = new AssistantHarness(new AssistantLimits { MaxMessageLength = 10, MaxAnswerLength = 100 });

        Assert.Equal(UseCaseStatus.Invalid, (await harness.AskAsync("anna.berater", new ScriptedRuntime("ok"), new string('x', 11))).Status);
        var reply = (await harness.AskAsync("anna.berater", new ScriptedRuntime(new string('a', 500)), "hi")).Value!;
        Assert.Equal(100, reply.Answer!.Length);
    }
}
