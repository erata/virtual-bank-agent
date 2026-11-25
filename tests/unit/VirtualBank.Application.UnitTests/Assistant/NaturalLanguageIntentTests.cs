using VirtualBank.Application.Assistant;
using Xunit;

namespace VirtualBank.Application.UnitTests.Assistant;

public sealed class NaturalLanguageIntentTests
{
    [Theory]
    [InlineData("müşterilerimi göster", "musterilerimi goster")]
    [InlineData("müsterilerimi göster", "musterilerimi goster")]
    [InlineData("başvurularımı göster", "basvurularimi goster")]
    [InlineData("Kreditrichtlinie für Benutzer", "kreditrichtlinie fur benutzer")]
    public void Unicode_keyboard_variants_normalize_identically(string input, string expected) =>
        Assert.Equal(expected, NaturalLanguageIntent.Normalize(input));

    [Theory]
    [InlineData("müşterilerimi göster")]
    [InlineData("musterilerimi goster")]
    [InlineData("müsterilerimi göster")]
    [InlineData("Zeig mir meine Kunden")]
    [InlineData("list my customers")]
    public void Customer_intents_are_recognized(string input) =>
        Assert.True(NaturalLanguageIntent.IsCustomerRequest(NaturalLanguageIntent.Normalize(input)));

    [Theory]
    [InlineData("basvurulari goster")]
    [InlineData("başvurularımı göster")]
    [InlineData("Kreditanträge")]
    [InlineData("credit applications")]
    public void Credit_intents_are_recognized(string input) =>
        Assert.True(NaturalLanguageIntent.IsCreditRequest(NaturalLanguageIntent.Normalize(input)));

    [Fact]
    public void Irrelevant_sentence_is_not_customer_or_credit_intent()
    {
        var text = NaturalLanguageIntent.Normalize("What is the weather today?");
        Assert.False(NaturalLanguageIntent.IsCustomerRequest(text));
        Assert.False(NaturalLanguageIntent.IsCreditRequest(text));
        Assert.False(NaturalLanguageIntent.IsDocumentRequest(text));
    }
}
