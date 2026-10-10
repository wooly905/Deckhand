using Deckhand.Agent.Tools;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent.Approval;

public enum ApprovalOutcome
{
    AutoApprove,
    Ask,
}

public sealed record ApprovalDecision(ApprovalOutcome Outcome, bool AllowForConversation, string? Note);

/// <summary>Decides for one policy-driven tool, for example app_launch or script_run.</summary>
public interface IApprovalRule
{
    bool AppliesTo(string toolName);

    ApprovalDecision Decide(FunctionCallContent call);
}

/// <summary>The approval gate in code. A prompt, a skill, or an observation cannot change it.</summary>
public sealed class ApprovalPolicy(ToolCatalog catalog, IEnumerable<IApprovalRule> rules)
{
    private static readonly ApprovalDecision AskEachTime = new(ApprovalOutcome.Ask, false, null);

    private readonly IReadOnlyList<IApprovalRule> _rules = rules.ToList();
    private readonly HashSet<string> _allowed = [];

    public ApprovalDecision Decide(FunctionCallContent call)
    {
        var entry = catalog.Find(call.Name);
        if (entry is null)
        {
            return AskEachTime;
        }

        var decision = _rules.FirstOrDefault(r => r.AppliesTo(call.Name))?.Decide(call)
            ?? new ApprovalDecision(ApprovalOutcome.Ask, entry.Risk == RiskLevel.Change, null);

        return decision is { Outcome: ApprovalOutcome.Ask, AllowForConversation: true } && _allowed.Contains(call.Name)
            ? decision with { Outcome = ApprovalOutcome.AutoApprove }
            : decision;
    }

    /// <summary>The user selected "Allow for this conversation". Has no effect for tools that ask each time.</summary>
    public void AllowForConversation(string toolName) => _allowed.Add(toolName);

    public void Reset() => _allowed.Clear();

    /// <summary>The catalog risk of a tool, for the audit log; null for an unknown tool.</summary>
    public RiskLevel? RiskOf(string toolName) => catalog.Find(toolName)?.Risk;
}

/// <summary>With a remote endpoint, reading files, pages, or windows sends the content out of the computer.</summary>
public sealed class SensitiveReadRule(bool endpointIsRemote, string endpointHost) : IApprovalRule
{
    private static readonly HashSet<string> Tools = ["fs_read_text", "browser_read", "ui_read_text"];

    public bool AppliesTo(string toolName) => Tools.Contains(toolName);

    public ApprovalDecision Decide(FunctionCallContent call) =>
        endpointIsRemote
            ? new ApprovalDecision(ApprovalOutcome.Ask, true, $"The content will be sent to {endpointHost}.")
            : new ApprovalDecision(ApprovalOutcome.AutoApprove, false, null);
}
