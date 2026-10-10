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

    [Theory]
    [InlineData("pay 4111111111111111 123 ok", "pay {0} 123 ok")]
    [InlineData("ref 12 4111111111111111", "ref 12 {0}")]
    [InlineData("card 4111 1111 1111 1111 123 exp 12-28", "card {0} 123 exp 12-28")]
    [InlineData("1234 4111111111111111 5678", "1234 {0} 5678")]
    [InlineData("4111-1111-1111-1111-12", "{0}-12")]
    [InlineData("4111 1111 1111 1111 123", "{0} 123")]
    [InlineData("4111111111111111 123", "{0} 123")]
    [InlineData("4111 1111 1111 1111 12/28", "{0} 12/28")]
    [InlineData("qty 2 4111 1111 1111 1111", "qty 2 {0}")]
    [InlineData("qty 2 5555 5555 5555 4444 cvv 737", "qty 2 {0} cvv 737")]
    [InlineData("1000 4111 1111 1111 1111", "1000 {0}")]          // more than 19 digits in the whole run
    [InlineData("4111 1111 1111 1111 2028 737", "{0} 2028 737")]
    public void Card_numbers_next_to_short_digit_groups_are_masked(string text, string expected) =>
        Assert.Equal(string.Format(expected, CardNumberMasker.Replacement), CardNumberMasker.Mask(text));

    [Theory]
    [InlineData("6 4111 1111 1111 1111")]     // "6 4111 1111 1111" also passes Luhn
    [InlineData("14 4111 1111 1111 1111")]
    [InlineData("48 5555 5555 5555 4444 99")]
    public void No_card_digits_survive_when_a_shorter_run_also_passes_luhn(string text) =>
        Assert.DoesNotMatch(@"\d{4}", CardNumberMasker.Mask(text));
}
