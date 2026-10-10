using Deckhand.Agent.Approval;
using Deckhand.Agent.Audit;
using Deckhand.Agent.Guard;
using Deckhand.Agent.Selection;
using Deckhand.Agent.Settings;
using Deckhand.Agent.Skills;
using Deckhand.Agent.Steps;
using Deckhand.Agent.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent;

public sealed record AgentDependencies(
    ChatSettings Chat,
    IChatClient Model,
    ToolCatalog Catalog,
    SkillCatalog Skills,
    IReadOnlyList<string> SkillRoots,
    IAuditLog Audit,
    IReadOnlyList<IApprovalRule> ApprovalRules,
    IStepLimitPrompter StepLimitPrompter,
    bool DocsAvailable);

/// <summary>Everything that belongs to one conversation.</summary>
public sealed class DeckhandSession
{
    public required AIAgent Agent { get; init; }
    public required AgentSession Session { get; init; }
    public required ToolSelectionState Selection { get; init; }
    public required StepEvents Steps { get; init; }
    public required CallTracker Calls { get; init; }
    public required ApprovalLedger Ledger { get; init; }
    public required ApprovalPolicy Policy { get; init; }
    public required IReadOnlyList<AITool> Tools { get; init; }
}

public sealed class AgentFactory(AgentDependencies dependencies)
{
    public async Task<DeckhandSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var d = dependencies;
        var groups = d.Chat.ToolSelection == ToolSelectionMode.Groups;
        var selection = new ToolSelectionState(d.Catalog, d.Chat.MaxActiveTools) { AllToolsActive = !groups };
        var steps = new StepEvents();
        var calls = new CallTracker();
        var ledger = new ApprovalLedger();

        var builder = d.Model.AsBuilder()
            .UseFunctionInvocation(configure: f =>
            {
                f.MaximumIterationsPerRequest = 35; // backstop; ToolScopeChatClient owns the step limit
                f.MaximumConsecutiveErrorsPerRequest = 2;
                f.IncludeDetailedErrors = true;
            })
            .Use(inner => new ToolScopeChatClient(inner, selection, d.Skills, d.StepLimitPrompter, scopeTools: groups));
        if (d.Chat.StepMode == StepMode.Structured)
        {
            builder.Use(inner => new ReActStepChatClient(inner, d.Chat.ArgsSchema, new TranscriptWriter(d.Chat.ContextWindow), steps));
        }

        var tools = d.Catalog.BuildAgentTools(entry => new GuardedAIFunction(entry, selection, calls, ledger, d.Audit)).ToList();
        if (groups)
        {
            tools.Add(new EnableToolsFunction(d.Catalog, selection));
        }

        var skillsProvider = new AgentSkillsProvider(
            new CachingAgentSkillsSource(new DeduplicatingAgentSkillsSource(
                new AgentFileSkillsSource(d.SkillRoots, options: new AgentFileSkillsSourceOptions { AllowedScriptExtensions = [] }))),
            options: new AgentSkillsProviderOptions
            {
                DisableLoadSkillApproval = true,
                DisableReadSkillResourceApproval = true,
                SkillsInstructionPrompt = Prompts.SkillsInstruction,
            },
            ownsSource: true);

        var agent = builder.Build().AsAIAgent(new ChatClientAgentOptions
        {
            Name = "Deckhand",
            ChatOptions = new ChatOptions
            {
                Instructions = Prompts.System(d.DocsAvailable),
                Temperature = 0.1f,
                Tools = tools,
            },
            AIContextProviders = [skillsProvider],
            UseProvidedChatClientAsIs = true,
        });

        return new DeckhandSession
        {
            Agent = agent,
            Session = await agent.CreateSessionAsync(cancellationToken: cancellationToken),
            Selection = selection,
            Steps = steps,
            Calls = calls,
            Ledger = ledger,
            Policy = new ApprovalPolicy(d.Catalog, d.ApprovalRules),
            Tools = tools,
        };
    }
}
