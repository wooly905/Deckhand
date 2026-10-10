using Deckhand.Agent.Tests.Fakes;
using Deckhand.Agent.Undo;

namespace Deckhand.Agent.Tests.Undo;

public class UndoJournalTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entries_are_listed_newest_first_and_survive_a_new_instance()
    {
        var folder = Directory.CreateTempSubdirectory("deckhand-undo-").FullName;
        var time = new ManualTimeProvider(Start);
        var journal = new UndoJournal(folder, time);
        journal.Record("s1", "wallpaper", "wallpaper_set", "wallpaper", "a.jpg", "b.jpg");
        time.Now = Start.AddMinutes(1);
        journal.Record("s1", "volume", "volume_set", "volume", "30", "60");

        var entries = new UndoJournal(folder, time).List();

        Assert.Equal(["volume_set", "wallpaper_set"], entries.Select(e => e.Tool));
    }

    [Fact]
    public void Entries_older_than_7_days_are_removed()
    {
        var folder = Directory.CreateTempSubdirectory("deckhand-undo-").FullName;
        var time = new ManualTimeProvider(Start);
        var journal = new UndoJournal(folder, time);
        journal.Record("s1", "wallpaper", "wallpaper_set", "wallpaper", "a.jpg", "b.jpg");

        time.Now = Start.AddDays(8);

        Assert.Empty(journal.List());
        Assert.DoesNotContain(File.ReadAllLines(Path.Combine(folder, "journal.jsonl")), l => l.Length > 0);
    }

    [Fact]
    public void Only_the_newest_entry_for_a_target_is_newest()
    {
        var folder = Directory.CreateTempSubdirectory("deckhand-undo-").FullName;
        var time = new ManualTimeProvider(Start);
        var journal = new UndoJournal(folder, time);
        var first = journal.Record("s1", "wallpaper", "wallpaper_set", "wallpaper", "a.jpg", "b.jpg");
        time.Now = Start.AddMinutes(1);
        var second = journal.Record("s2", "wallpaper", "wallpaper_set", "wallpaper", "b.jpg", "c.jpg");

        Assert.False(journal.IsNewest(first));
        Assert.True(journal.IsNewest(second));
    }
}
