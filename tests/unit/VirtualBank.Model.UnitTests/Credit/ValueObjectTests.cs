using VirtualBank.Model.Common;
using VirtualBank.Model.Credit;
using Xunit;

namespace VirtualBank.Model.UnitTests.Credit;

public sealed class ValueObjectTests
{
    [Fact]
    public void Money_rejects_negative_amounts()
    {
        Assert.Throws<DomainRuleViolationException>(() => Money.Eur(-1m));
    }

    [Fact]
    public void Money_normalizes_the_currency_code()
    {
        Assert.Equal("EUR", Money.Of(5m, " eur ").Currency);
    }

    [Fact]
    public void Money_in_different_currencies_is_not_comparable()
    {
        Assert.Throws<DomainRuleViolationException>(() => Money.Eur(1m).IsAtMost(Money.Of(1m, "USD")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_note_text_is_rejected(string raw)
    {
        Assert.False(CreditNoteText.TryCreate(raw, out _));
    }

    [Fact]
    public void Note_text_is_trimmed_and_bounded()
    {
        Assert.True(CreditNoteText.TryCreate("  ok  ", out var text));
        Assert.Equal("ok", text!.Value);
        Assert.False(CreditNoteText.TryCreate(new string('x', CreditNoteText.MaxLength + 1), out _));
        Assert.True(CreditNoteText.TryCreate(new string('x', CreditNoteText.MaxLength), out _));
    }
}
