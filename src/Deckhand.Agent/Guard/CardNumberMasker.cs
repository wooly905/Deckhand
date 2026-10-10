using System.Text;
using System.Text.RegularExpressions;

namespace Deckhand.Agent.Guard;

/// <summary>Removes payment card numbers from text. Deckhand does not handle cardholder data.</summary>
public static partial class CardNumberMasker
{
    public const string Replacement = "[card number removed]";

    public static string Mask(string text) => Candidate().Replace(text, match => MaskCandidate(match.Value));

    // The regex is greedy, so a card number next to a short digit group (for example a CVV
    // after it, or a reference number before it) lands in one candidate that fails Luhn as a whole.
    // When that happens, try every contiguous run of digit groups of 13–19 digits, longest first.
    private static string MaskCandidate(string candidate)
    {
        var groups = DigitGroup().Matches(candidate);
        if (groups.Count == 0)
        {
            return candidate;
        }

        var result = new StringBuilder();
        var copied = 0;
        var i = 0;
        while (i < groups.Count)
        {
            var end = FindCardEnd(groups, i);
            if (end < 0)
            {
                i++;
                continue;
            }

            var start = groups[i].Index;
            result.Append(candidate, copied, start - copied).Append(Replacement);
            copied = groups[end].Index + groups[end].Length;
            i = end + 1;
        }

        return result.Append(candidate, copied, candidate.Length - copied).ToString();
    }

    // Returns the index of the last group of the longest card number starting at group `first`, or -1.
    private static int FindCardEnd(MatchCollection groups, int first)
    {
        var digitCount = 0;
        var last = first;
        while (last < groups.Count && digitCount + groups[last].Length <= 19)
        {
            digitCount += groups[last].Length;
            last++;
        }

        for (var end = last - 1; end >= first; end--)
        {
            var digits = string.Concat(Enumerable.Range(first, end - first + 1).Select(g => groups[g].Value));
            if (digits.Length >= 13 && IsLuhnValid(digits))
            {
                return end;
            }
        }

        return -1;
    }

    private static bool IsLuhnValid(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
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

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitGroup();
}
