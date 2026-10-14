using WindowsAxon.Agent.Approval;
using Microsoft.Extensions.AI;

namespace WindowsAxon.Agent;

/// <summary>Shows the approval card. The desktop app implements it in WinUI 3.</summary>
public interface IApprovalPrompter
{
    Task<ApprovalAnswer> AskAsync(FunctionCallContent call,
                                  ApprovalDecision decision,
                                  CancellationToken cancellationToken);
}
