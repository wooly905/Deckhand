
namespace WindowsAxon.Agent.Audit;

public interface IAuditLog
{
    void Write(AuditEntry entry);
}
