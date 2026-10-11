using Deckhand.Agent.Approval;
using Deckhand.Agent.Audit;
using Deckhand.Agent.Guard;
using Deckhand.Agent.Selection;
using Deckhand.Agent.Steps;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent;

/// <summary>Everything that belongs to one conversation.</summary>
public sealed class DeckhandSession
{
    public required AIAgent Agent
    {
        get; init;
    }

    public required AgentSession Session
    {
        get; init;
    }

    public required ToolSelectionState Selection
    {
        get; init;
    }

    public required StepEvents Steps
    {
        get; init;
    }

    public required CallTracker Calls
    {
        get; init;
    }

    public required ApprovalLedger Ledger
    {
        get; init;
    }

    public required ApprovalPolicy Policy
    {
        get; init;
    }

    public required IReadOnlyList<AITool> Tools
    {
        get; init;
    }
}
