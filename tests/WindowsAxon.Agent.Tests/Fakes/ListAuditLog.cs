using WindowsAxon.Agent.Audit;

namespace WindowsAxon.Agent.Tests.Fakes;

public sealed class ListAuditLog : IAuditLog
{
    public List<AuditEntry> Entries { get; } = [];

    public void Write(AuditEntry entry) => Entries.Add(entry);
}
