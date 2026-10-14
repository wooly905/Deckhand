using System.Text.Json;

namespace WindowsAxon.Agent.Undo;

public sealed record UndoEntry(string Id,
                               DateTimeOffset Time,
                               string Session,
                               string Kind,
                               string Tool,
                               string Target,
                               string? PreviousValue,
                               string? NewValue);

/// <summary>Global record of the previous value of each setting that WindowsAxon changed. Kept 7 days.</summary>
public sealed class UndoJournal(string folder, TimeProvider time)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly Lock _lock = new();

    private string FilePath => Path.Combine(folder, "journal.jsonl");

    public UndoEntry Record(string session,
                            string kind,
                            string tool,
                            string target,
                            string? previousValue,
                            string? newValue)
    {
        var entry = new UndoEntry(Guid.NewGuid().ToString("N")[..8],
                                  time.GetUtcNow(),
                                  session,
                                  kind,
                                  tool,
                                  target,
                                  previousValue,
                                  newValue);
        lock (_lock)
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(FilePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }

        return entry;
    }

    public IReadOnlyList<UndoEntry> List()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var all = File.ReadAllLines(FilePath)
                .Where(l => l.Length > 0)
                .Select(l => JsonSerializer.Deserialize<UndoEntry>(l)!)
                .ToList();
            var cutoff = time.GetUtcNow() - Retention;
            var kept = all.Where(e => e.Time >= cutoff).ToList();
            if (kept.Count != all.Count)
            {
                File.WriteAllLines(FilePath, kept.Select(e => JsonSerializer.Serialize(e)));
            }

            // Reverse first so that entries with the same time keep the later-written one first (stable sort).
            return Enumerable.Reverse(kept).OrderByDescending(e => e.Time).ToList();
        }
    }

    public UndoEntry? Find(string id) => List().FirstOrDefault(e => e.Id == id);

    public bool IsNewest(UndoEntry entry) => List()
        .First(e => e.Kind == entry.Kind
            && SameTarget(e, entry))
        .Id == entry.Id;

    private static bool SameTarget(UndoEntry left, UndoEntry right) => left.Kind == "registry"
        ? JsonSerializer.Deserialize<string[]>(left.Target)!.SequenceEqual(JsonSerializer.Deserialize<string[]>(right.Target)!,
                                                                           StringComparer.OrdinalIgnoreCase)
        : left.Target == right.Target;
}
