using Deckhand.Agent.Guard;

namespace Deckhand.Agent.Tests.Guard;

public class CardNumberMaskerTests
{
    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("378282246310005")]
    public void Luhn_valid_card_numbers_are_masked(string card) =>
        Assert.Equal($"pay with {CardNumberMasker.Replacement} now", CardNumberMasker.Mask($"pay with {card} now"));

    [Theory]
    [InlineData("4111111111111112")]          // Luhn fails
    [InlineData("Order 1234567890123")]       // 13 digits, Luhn fails
    [InlineData("+1 425 555 0100")]           // 11 digits
    [InlineData("id 41111111111111111111")]   // 22 digits
    [InlineData("2026-10-10 11:31:42")]
    public void Other_numbers_are_not_masked(string text) =>
        Assert.Equal(text, CardNumberMasker.Mask(text));

    [Fact]
    public void Several_cards_in_one_text_are_all_masked()
    {
        var masked = CardNumberMasker.Mask("a 4111111111111111 b 5555555555554444 c");

        Assert.Equal($"a {CardNumberMasker.Replacement} b {CardNumberMasker.Replacement} c", masked);
    }
}
