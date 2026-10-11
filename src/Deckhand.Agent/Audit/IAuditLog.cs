
namespace Deckhand.Agent.Audit;

public interface IAuditLog
{
    void Write(AuditEntry entry);
}
