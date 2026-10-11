using System.Collections.Concurrent;

namespace Deckhand.Agent.Audit;

/// <summary>Approval decisions of one session, by tool call ID.</summary>
public sealed class ApprovalLedger
{
    private readonly ConcurrentDictionary<string, bool> _decisions = new();
    private int _unapproved;

    /// <summary>The text of the request that is running, for the audit log.</summary>
    public string? CurrentRequest
    {
        get; set;
    }

    public int UnapprovedCalls => _unapproved;

    public void Record(string callId, bool approved) => _decisions[callId] = approved;

    public bool? Find(string callId) => _decisions.TryGetValue(callId, out var approved) ? approved : null;

    public void CountUnapproved() => Interlocked.Increment(ref _unapproved);
}
