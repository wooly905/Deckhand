using Microsoft.Extensions.AI;

namespace WindowsAxon.Agent.Approval;

/// <summary>Decides for one policy-driven tool, for example app_launch or script_run.</summary>
public interface IApprovalRule
{
    bool AppliesTo(string toolName);

    ApprovalDecision Decide(FunctionCallContent call);
}
