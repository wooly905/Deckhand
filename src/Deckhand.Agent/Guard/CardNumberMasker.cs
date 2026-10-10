using System.Text.RegularExpressions;

namespace Deckhand.Agent.Guard;

/// <summary>Removes payment card numbers from text. Deckhand does not handle cardholder data.</summary>
public static partial class CardNumberMasker
{
    public const string Replacement = "[card number removed]";

    public static string Mask(string text) =>
        Candidate().Replace(text, match =>
        {
            var digits = match.Value.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
            return digits.Length is >= 13 and <= 19 && IsLuhnValid(digits) ? Replacement : match.Value;
        });

    private static bool IsLuhnValid(int[] digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }

    // Digits with single optional spaces or dashes between them, not touching other digits.
    [GeneratedRegex(@"(?<![\d-])\d(?:[ -]?\d){12,18}(?![\d])")]
    private static partial Regex Candidate();
}
