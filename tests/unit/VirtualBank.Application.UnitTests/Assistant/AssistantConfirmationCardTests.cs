using VirtualBank.Application.Assistant.Tools;
using VirtualBank.Application.Common;
using VirtualBank.Application.Credit.Commands.AddCreditNote;
using VirtualBank.Application.Credit.Commands.ApproveCreditApplication;
using VirtualBank.Application.UnitTests.Support;
using Xunit;
using static VirtualBank.Application.UnitTests.Assistant.ScriptedRuntime;

namespace VirtualBank.Application.UnitTests.Assistant;

/// <summary>With confirmation cards wired: the model can PROPOSE a write, only the user's own confirmation executes it.</summary>
public sealed class AssistantConfirmationCardTests
{
    [Fact]
    public async Task A_note_proposed_by_the_model_is_a_card_and_nothing_is_written_until_the_user_confirms()
    {
        var harness = new AssistantHarness(confirmationCards: true);
        var runtime = new ScriptedRuntime("Please confirm the card below.",
            Call("add_credit_note", ("application_id", "K-100"), ("text", "Income verified.")));

        var reply = (await harness.AskAsync("mehmet.kredit", runtime)).Value!;

        Assert.Equal(ToolCallStatus.Succeeded, Assert.Single(reply.ToolCalls).Status);
        var card = Assert.Single(reply.Actions!);
        Assert.Equal(CreditNoteCards.Kind, card.Kind);
        Assert.Contains(card.Facts, f => f.Key == "note" && f.Value == "Income verified.");
        Assert.Empty(harness.Credit.InsertedNotes); // the model turn wrote nothing

        var confirmed = await harness.Confirmation!.ConfirmAsync(Personas.Get("mehmet.kredit"), card.Id, CancellationToken.None);
        Assert.Equal(UseCaseStatus.Succeeded, confirmed.Status);
        Assert.Equal(["Income verified."], harness.Credit.InsertedNotes);
    }

    [Fact]
    public async Task A_hostile_model_cannot_prepare_a_card_for_an_application_the_user_may_not_write()
    {
        var harness = new AssistantHarness(confirmationCards: true);
        var runtime = new ScriptedRuntime("ok", Call("add_credit_note", ("application_id", "K-200"), ("text", "not mine")));

        var reply = (await harness.AskAsync("mehmet.kredit", runtime)).Value!;

        Assert.NotEqual(ToolCallStatus.Succeeded, Assert.Single(reply.ToolCalls).Status);
        Assert.Empty(reply.Actions ?? []);
    }

    [Fact]
    public async Task Approval_tool_prepares_a_card_and_is_offered_only_to_approvers()
    {
        var harness = new AssistantHarness(confirmationCards: true);
        Assert.Contains(harness.Catalog.VisibleDefinitions(Personas.Get("max.approver")), d => d.Name == "approve_credit_application");
        Assert.DoesNotContain(harness.Catalog.VisibleDefinitions(Personas.Get("mehmet.kredit")), d => d.Name == "approve_credit_application");
        Assert.DoesNotContain(harness.Catalog.VisibleDefinitions(Personas.Get("anna.berater")), d => d.Name == "add_credit_note");

        var reply = (await harness.AskAsync("max.approver",
            new ScriptedRuntime("Review the card.", Call("approve_credit_application", ("application_id", "K-100"))), "approve K-100")).Value!;

        Assert.Equal(CreditApproval.ApproveKind, Assert.Single(reply.Actions!).Kind);
        Assert.DoesNotContain(reply.ToolCalls, c => c.Status == ToolCallStatus.Denied);
    }
}
