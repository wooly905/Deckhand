using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowsAxon.Agent.Guard;

/// <summary>Removes payment card numbers from text. WindowsAxon does not handle cardholder data.</summary>
public static partial class CardNumberMasker
{
    public const string Replacement = "[card number removed]";

    public static string Mask(string text) => Candidate().Replace(text, match => MaskCandidate(match.Value));

    // The regex is greedy, so a card number next to another digit group (for example a CVV or an
    // expiry date after it, or a quantity before it) lands in one candidate that fails Luhn as a whole.
    // Every contiguous run of 13–19 digits that starts and ends on a group boundary is checked, and the
    // union of all Luhn-valid runs is masked. Masking the union errs toward removing a neighboring
    // group rather than leaving part of a card number visible when a shorter run passes Luhn by chance.
    private static string MaskCandidate(string candidate)
    {
        var groups = DigitGroup().Matches(candidate);
        var spans = new List<(int Start, int End)>();
        for (var first = 0; first < groups.Count; first++)
        {
            var digitCount = 0;
            var digits = new StringBuilder();
            for (var last = first; last < groups.Count; last++)
            {
                digitCount += groups[last].Length;
                if (digitCount > 19)
                {
                    break;
                }

                digits.Append(groups[last].Value);
                if (digitCount >= 13 && IsLuhnValid(digits.ToString()))
                {
                    AddSpan(spans, groups[first].Index, groups[last].Index + groups[last].Length);
                }
            }
        }

        if (spans.Count == 0)
        {
            return candidate;
        }

        var result = new StringBuilder();
        var copied = 0;
        foreach (var (start, end) in spans)
        {
            result.Append(candidate, copied, start - copied).Append(Replacement);
            copied = end;
        }

        return result.Append(candidate, copied, candidate.Length - copied).ToString();
    }

    // Spans arrive ordered by start, so a new span can only overlap the last one.
    private static void AddSpan(List<(int Start, int End)> spans, int start, int end)
    {
        if (spans.Count > 0 && start < spans[^1].End)
        {
            spans[^1] = (spans[^1].Start, Math.Max(spans[^1].End, end));
            return;
        }

        spans.Add((start, end));
    }

    private static bool IsLuhnValid(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            // \d also matches non-ASCII decimal digits, for example full-width digits from an IME.
            var digit = CharUnicodeInfo.GetDecimalDigitValue(digits[digits.Length - 1 - i]);
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

    // A run of at least 13 digits with single optional spaces or dashes between them, not touching
    // other digits (a dash counts as touching only after a digit, so "ref-4111..." is still found).
    // There is no upper bound: card numbers inside longer runs are found by MaskCandidate.
    [GeneratedRegex(@"(?<!\d)(?<!\d-)\d(?:[ -]?\d){12,}(?![\d])")]
    private static partial Regex Candidate();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitGroup();
}
