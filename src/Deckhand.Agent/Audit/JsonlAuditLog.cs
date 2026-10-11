using System.Text.Json;
using Deckhand.Agent.Guard;

namespace Deckhand.Agent.Audit;

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
            Result = entry.Result is null ? null : CardNumberMasker.Mask(entry.Result),
            Error = entry.Error is null ? null : CardNumberMasker.Mask(entry.Error),
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
