using Deckhand.Agent.Tests.Fakes;
using Microsoft.Agents.AI;

namespace Deckhand.Agent.Tests;

/// <summary>Behaviors of MAF and MEAI that the Deckhand design depends on.</summary>
public class MafContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AIAgent BuildAgent(IChatClient inner, IList<AITool> tools, AIContextProvider[]? providers = null)
    {
        IChatClient chat = inner.AsBuilder().UseFunctionInvocation().Build();
        return chat.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "Test",
            ChatOptions = new ChatOptions { Tools = tools },
            AIContextProviders = providers ?? [],
            UseProvidedChatClientAsIs = true,
        });
    }

    private sealed class RecordingFunction(AIFunction inner) : DelegatingAIFunction(inner)
    {
        public int Calls
        {
            get; private set;
        }

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
                                                              CancellationToken cancellationToken)
        {
            Calls++;
            return base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }

    [Fact] // C1
    public async Task Function_invoking_client_runs_a_delegating_wrapper_and_sends_the_result_back()
    {
        var wrapped = new RecordingFunction(AIFunctionFactory.Create(() => "pong", "net_ping"));
        var inner = new ScriptedChatClient().ReplyCall("net_ping").Reply("done");
        var agent = BuildAgent(inner, [wrapped]);

        var response = await agent.RunAsync("ping it",
                                            await agent.CreateSessionAsync(cancellationToken: Ct),
                                            cancellationToken: Ct);

        Assert.Equal("done", response.Text);
        Assert.Equal(1, wrapped.Calls);
        var result = inner.Calls[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Equal("pong", result.Result?.ToString());
    }

    [Fact] // C2
    public async Task Approval_required_function_returns_a_request_and_runs_after_approval()
    {
        var wrapped = new RecordingFunction(AIFunctionFactory.Create(() => "set", "wallpaper_set"));
        var inner = new ScriptedChatClient().ReplyCall("wallpaper_set").Reply("done");
        var agent = BuildAgent(inner, [new ApprovalRequiredAIFunction(wrapped)]);
        var session = await agent.CreateSessionAsync(cancellationToken: Ct);

        var first = await agent.RunAsync("set it", session, cancellationToken: Ct);
        var request = first.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().Single();
        Assert.Equal(0, wrapped.Calls);

        var second = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]),
                                          session,
                                          cancellationToken: Ct);

        Assert.Equal(1, wrapped.Calls);
        Assert.Equal("done", second.Text);
    }

    [Fact] // C3
    public async Task Additional_properties_of_input_messages_survive_in_session_history()
    {
        var inner = new ScriptedChatClient().Reply("a").Reply("b");
        var agent = BuildAgent(inner, []);
        var session = await agent.CreateSessionAsync(cancellationToken: Ct);
        var first = new ChatMessage(ChatRole.User, "first") { AdditionalProperties = new() { ["deckhand.request"] = true } };

        await agent.RunAsync(first, session, cancellationToken: Ct);
        await agent.RunAsync("second", session, cancellationToken: Ct);

        var seen = inner.Calls[1].Messages.First(m => m.Text == "first");
        Assert.True(seen.AdditionalProperties?.ContainsKey("deckhand.request"));
    }

    [Fact] // C4
    public async Task Skills_provider_adds_load_skill_and_lists_the_skill()
    {
        var root = Directory.CreateTempSubdirectory("deckhand-skills-");
        var skillDir = Directory.CreateDirectory(Path.Combine(root.FullName, "demo-skill"));
        File.WriteAllText(Path.Combine(skillDir.FullName, "SKILL.md"),
                          "---\nname: demo-skill\ndescription: Demo skill for tests.\nmetadata:\n  tools: \"net_ping\"\n---\n# Steps\n1. Run net_ping.\n");
        var source = new AgentFileSkillsSource([root.FullName],
                                               options: new AgentFileSkillsSourceOptions { AllowedScriptExtensions = [] });
        var provider = new AgentSkillsProvider(source,
                                               options: new AgentSkillsProviderOptions
                                               {
                                                   DisableLoadSkillApproval = true,
                                                   DisableReadSkillResourceApproval = true
                                               },
                                               ownsSource: true);
        var inner = new ScriptedChatClient().Reply("ok");
        var agent = BuildAgent(inner, [], [provider]);

        await agent.RunAsync("hi", await agent.CreateSessionAsync(cancellationToken: Ct), cancellationToken: Ct);

        var (messages, options) = inner.Calls[0];
        Assert.Contains("load_skill", options!.Tools!.Select(t => t.Name));
        var allText = options.Instructions + "\n" + string.Join("\n", messages.Select(m => m.Text));
        Assert.Contains("demo-skill", allText);
    }
}
