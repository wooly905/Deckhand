using System.Text.Json;
using Deckhand.Agent.Audit;
using Deckhand.Agent.Guard;

namespace Deckhand.Agent.Tests.Audit;

public class AuditLogTests
{
    private static AuditEntry Entry(string args) => new(
        new DateTimeOffset(2026, 10, 10, 9, 30, 0, TimeSpan.Zero).ToLocalTime(), "call", "set wallpaper",
        "wallpaper_set", args, "Change", "approved", true);

    [Fact]
    public void Entries_are_appended_as_json_lines_in_a_dated_file()
    {
        var folder = Directory.CreateTempSubdirectory("deckhand-audit-").FullName;
        var log = new JsonlAuditLog(folder);

        log.Write(Entry("""{"path":"C:\a.jpg"}"""));
        log.Write(Entry("""{"path":"C:\b.jpg"}"""));

        var file = Path.Combine(folder, $"audit-{Entry("").Time:yyyyMMdd}.jsonl");
        var lines = File.ReadAllLines(file);
        Assert.Equal(2, lines.Length);
        using var json = JsonDocument.Parse(lines[1]);
        Assert.Equal("wallpaper_set", json.RootElement.GetProperty("tool").GetString());
        Assert.Contains("b.jpg", json.RootElement.GetProperty("arguments").GetString());
        Assert.Equal("approved", json.RootElement.GetProperty("decision").GetString());
    }

    [Fact]
    public void Card_numbers_in_arguments_and_request_are_masked()
    {
        var folder = Directory.CreateTempSubdirectory("deckhand-audit-").FullName;
        var log = new JsonlAuditLog(folder);

        log.Write(Entry("""{"text":"4111 1111 1111 1111"}""") with { Request = "type 4111111111111111" });

        var text = File.ReadAllText(Directory.GetFiles(folder).Single());
        Assert.DoesNotContain("4111", text);
        Assert.Contains(CardNumberMasker.Replacement, text);
    }

    [Fact]
    public void Disabled_log_writes_nothing()
    {
        var folder = Directory.CreateTempSubdirectory("deckhand-audit-").FullName;

        new JsonlAuditLog(folder, enabled: false).Write(Entry("{}"));

        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void Ledger_records_decisions_by_call_id()
    {
        var ledger = new ApprovalLedger();

        ledger.Record("c1", approved: true);
        ledger.Record("c2", approved: false);
        ledger.CountUnapproved();

        Assert.True(ledger.Find("c1"));
        Assert.False(ledger.Find("c2"));
        Assert.Null(ledger.Find("c3"));
        Assert.Equal(1, ledger.UnapprovedCalls);
    }
}
