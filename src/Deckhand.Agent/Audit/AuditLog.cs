using System.Collections.Concurrent;
using System.Text.Json;
using Deckhand.Agent.Guard;

namespace Deckhand.Agent.Audit;

public sealed record AuditEntry(
    DateTimeOffset Time,
    string Kind,
    string? Request,
    string Tool,
    string Arguments,
    string Risk,
    string? Decision,
    bool? Success,
    string? ScriptHash = null);

public interface IAuditLog
{
    void Write(AuditEntry entry);
}

/// <summary>%LOCALAPPDATA%\Deckhand\logs\audit-YYYYMMDD.jsonl. One JSON object per line. No automatic delete.</summary>
public sealed class JsonlAuditLog(string folder, bool enabled = true) : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Lock _lock = new();

    public void Write(AuditEntry entry)
    {
        if (!enabled)
        {
            return;
        }

        var masked = entry with
        {
            Request = entry.Request is null ? null : CardNumberMasker.Mask(entry.Request),
            Arguments = CardNumberMasker.Mask(entry.Arguments),
        };
        var line = JsonSerializer.Serialize(masked, JsonOptions) + Environment.NewLine;
        var path = Path.Combine(folder, $"audit-{entry.Time.ToLocalTime():yyyyMMdd}.jsonl");

        lock (_lock)
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(path, line);
        }
    }
}

/// <summary>Approval decisions of one session, by tool call ID.</summary>
public sealed class ApprovalLedger
{
    private readonly ConcurrentDictionary<string, bool> _decisions = new();
    private int _unapproved;

    /// <summary>The text of the request that is running, for the audit log.</summary>
    public string? CurrentRequest { get; set; }

    public int UnapprovedCalls => _unapproved;

    public void Record(string callId, bool approved) => _decisions[callId] = approved;

    public bool? Find(string callId) => _decisions.TryGetValue(callId, out var approved) ? approved : null;

    public void CountUnapproved() => Interlocked.Increment(ref _unapproved);
}
