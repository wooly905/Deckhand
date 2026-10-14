using System.Text.Json;
using WindowsAxon.Agent.Approval;
using WindowsAxon.Agent.Audit;
using WindowsAxon.Agent.Guard;
using WindowsAxon.Agent.Selection;
using WindowsAxon.Agent.Settings;
using WindowsAxon.Agent.Skills;
using WindowsAxon.Agent.Steps;
using WindowsAxon.Agent.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WindowsAxon.Agent;

public sealed record AgentDependencies(ChatSettings Chat,
                                       IChatClient Model,
                                       ToolCatalog Catalog,
                                       SkillCatalog Skills,
                                       IReadOnlyList<string> SkillRoots,
                                       IAuditLog Audit,
                                       IReadOnlyList<IApprovalRule> ApprovalRules,
                                       IStepLimitPrompter StepLimitPrompter,
                                       bool DocsAvailable);

public sealed class AgentFactory(AgentDependencies dependencies)
{
    public async Task<WindowsAxonSession> CreateSessionAsync(CancellationToken cancellationToken = default)
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
                f.MaximumIterationsPerRequest = 60; // backstop; ToolScopeChatClient owns the step limit
                f.MaximumConsecutiveErrorsPerRequest = 2;
                f.IncludeDetailedErrors = true;
                // Catalog tools log inside their guard, before results are trimmed. Include provider tools too.
                f.FunctionInvoker = (context, token) => d.Catalog.Find(context.Function.Name) is not null
                    ? context.Function.InvokeAsync(context.Arguments, token)
                    : ToolCallAudit.InvokeAsync(context, d.Audit, ledger, token);
            })
            .Use(inner => new ToolScopeChatClient(inner, selection, d.Skills, d.StepLimitPrompter, scopeTools: groups));
        if (d.Chat.StepMode == StepMode.Structured)
        {
            builder.Use(inner => new ReActStepChatClient(inner,
                                                         d.Chat.ArgsSchema,
                                                         new TranscriptWriter(d.Chat.ContextWindow),
                                                         steps));
        }
        else
        {
            builder.Use(inner => new NativeFallbackChatClient(new ToolImageChatClient(inner),
                                                               new ReActStepChatClient(inner,
                                                                                       d.Chat.ArgsSchema,
                                                                                       new TranscriptWriter(d.Chat.ContextWindow),
                                                                                       steps),
                                                               Prompts.System(d.DocsAvailable, StepMode.Structured),
                                                               response => LogNativeFallback(d.Audit, ledger, response)));
        }

        builder.Use(inner => new ImageFallbackChatClient(inner, error => LogImageFallback(d.Audit, ledger, error)));

        var tools = d.Catalog.BuildAgentTools(entry => new GuardedAIFunction(entry, selection, calls, ledger, d.Audit))
            .ToList();
        if (groups)
        {
            tools.Add(new EnableToolsFunction(d.Catalog, selection));
        }

        var skillsProvider = new AgentSkillsProvider(new CachingAgentSkillsSource(new DeduplicatingAgentSkillsSource(
                new AgentFileSkillsSource(d.SkillRoots,
                                          options: new AgentFileSkillsSourceOptions { AllowedScriptExtensions = [] }))),
                                                     options: new AgentSkillsProviderOptions
                                                     {
                                                         DisableLoadSkillApproval = true,
                                                         DisableReadSkillResourceApproval = true,
                                                         SkillsInstructionPrompt = Prompts.SkillsInstruction,
                                                     },
                                                     ownsSource: true);

        var agent = builder.Build().AsAIAgent(new ChatClientAgentOptions
        {
            Name = "WindowsAxon",
            ChatOptions = new ChatOptions
            {
                Instructions = Prompts.System(d.DocsAvailable, d.Chat.StepMode),
                Temperature = string.Equals(d.Chat.Model, "auto", StringComparison.OrdinalIgnoreCase) ? null : 0.1f,
                Tools = tools,
            },
            AIContextProviders = [skillsProvider],
            UseProvidedChatClientAsIs = true,
        });

        return new WindowsAxonSession
        {
            Agent = agent,
            Session = await agent.CreateSessionAsync(cancellationToken: cancellationToken),
            Selection = selection,
            Steps = steps,
            Calls = calls,
            Ledger = ledger,
            Policy = new ApprovalPolicy(d.Catalog, d.ApprovalRules, d.Chat.AutoApproveTools),
            Tools = tools,
        };
    }

    private static void LogImageFallback(IAuditLog audit, ApprovalLedger ledger, Exception error) =>
        audit.Write(new AuditEntry(DateTimeOffset.Now,
                                   "model_fallback",
                                   ledger.CurrentRequest,
                                   "",
                                   "{}",
                                   "None",
                                   null,
                                   null,
                                   Status: "no_images",
                                   Error: error.ToString()));

    private static void LogNativeFallback(IAuditLog audit, ApprovalLedger ledger, ChatResponse response)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            response.ModelId,
            FinishReason = response.FinishReason?.Value
        });
        var reasoning = string.Join("\n", response.Messages.SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>().Select(c => c.Text));
        audit.Write(new AuditEntry(DateTimeOffset.Now,
                                   "model_fallback",
                                   ledger.CurrentRequest,
                                   "",
                                   metadata,
                                   "None",
                                   null,
                                   null,
                                   Status: "structured",
                                   Result: reasoning,
                                   Error: "Native response contained reasoning but no answer or tool call."));
    }
}
