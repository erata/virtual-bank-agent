using VirtualBank.Application.Assistant;
using Xunit;

namespace VirtualBank.Application.UnitTests.Assistant;

/// <summary>"Send money" turns may end with a clarifying question; read questions keep the no-guessing guard.</summary>
public sealed class PaymentInstructionTests
{
    [Theory]
    [InlineData("elif yilmaz a bin euro gönder", true)]
    [InlineData("DE06100100100000005003 iban adresine 1000 euro gönder", true)]
    [InlineData("Überweise 50 EUR an Elif", true)]
    [InlineData("send 20 euro to my landlord", true)]
    [InlineData("60 ay vadeli kredi başvurusu yapmak istiyorum", true)]
    [InlineData("ödemelerimi göster", false)]
    [InlineData("show my upcoming payments", false)]
    [InlineData("Zeige meine Kreditanträge", false)]
    public void Only_instructions_count_as_payment_instructions(string message, bool expected) =>
        Assert.Equal(expected, NaturalLanguageIntent.IsPaymentInstruction(NaturalLanguageIntent.Normalize(message)));

    [Fact]
    public async Task A_clarifying_question_for_a_payment_is_not_replaced_by_the_no_data_guard()
    {
        var runtime = new ScriptedRuntime("Bitte nennen Sie die IBAN des Empfängers.");
        var reply = (await new AssistantHarness().AskAsync("deniz.customer", runtime, "elif yilmaz a bin euro gönder")).Value!;

        Assert.Equal("Bitte nennen Sie die IBAN des Empfängers.", reply.Answer);
        Assert.DoesNotContain(AssistantNoticeCodes.NoVerifiedData, reply.NoticeCodes);
    }

    [Fact]
    public async Task A_read_question_without_data_still_gets_the_no_guessing_answer()
    {
        var runtime = new ScriptedRuntime("You paid 300 EUR rent yesterday."); // invented, no tool call
        var reply = (await new AssistantHarness().AskAsync("deniz.customer", runtime, "show my upcoming payments")).Value!;

        Assert.DoesNotContain("300 EUR", reply.Answer);
        Assert.Contains(AssistantNoticeCodes.NoVerifiedData, reply.NoticeCodes);
    }
}
