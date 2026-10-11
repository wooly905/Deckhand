
namespace Deckhand.Agent.Selection;

public sealed record ActivationResult(string Key, IReadOnlyList<(string Key, IReadOnlyList<string> Tools)> Removed)
{
    public string Describe()
    {
        var text = $"Enabled: {ShortName(Key)}.";
        if (Removed.Count > 0)
        {
            var parts = Removed.Select(r => $"{ShortName(r.Key)} ({string.Join(", ", r.Tools)} are no longer available)");
            text += $" Removed: {string.Join("; ", parts)}.";
        }

        return text;
    }

    internal static string ShortName(string key) => key[(key.IndexOf(':') + 1)..];
}
