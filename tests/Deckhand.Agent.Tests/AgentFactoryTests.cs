using Deckhand.Agent.Approval;
using Deckhand.Agent.Selection;
using Deckhand.Agent.Settings;
using Deckhand.Agent.Skills;
using Deckhand.Agent.Steps;
using Deckhand.Agent.Tests.Fakes;
using Deckhand.Agent.Tools;
using NSubstitute;

namespace Deckhand.Agent.Tests;

public class AgentFactoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static AgentDependencies Dependencies(IChatClient model, ChatSettings? chat = null, ToolCatalog? catalog = null)
    {
        catalog ??= new ToolCatalog();
        if (catalog.Find("net_ping") is null)
        {
            catalog.Add(AIFunctionFactory.Create((string host) => $"reply from {host}", "net_ping", "Ping a host."), "network", RiskLevel.Read);
            catalog.Add(AIFunctionFactory.Create((string path) => $"wallpaper is {path}", "wallpaper_set", "Set the wallpaper."), "personalize", RiskLevel.Change);
        }

        var skillRoot = Directory.CreateTempSubdirectory("deckhand-skills-").FullName;
        return new AgentDependencies(
            chat ?? new ChatSettings(), model, catalog, SkillCatalog.Load([(skillRoot, true)]), [skillRoot],
            new ListAuditLog(), [], Substitute.For<IStepLimitPrompter>(), DocsAvailable: false);
    }

    [Fact]
    public async Task Structured_session_sends_a_step_schema_with_base_tools_only()
    {
        var model = new ScriptedChatClient().Reply("""{"thought":"t","action":"final_answer","answer":"Hi."}""");
        var session = await new AgentFactory(Dependencies(model)).CreateSessionAsync(Ct);

        var response = await session.Agent.RunAsync(TranscriptWriter.NewRequest("hello"), session.Session, cancellationToken: Ct);

        Assert.Equal("Hi.", response.Text);
        var (messages, options) = model.Calls[0];
        Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat);
        Assert.Contains("- enable_tools(group: string)", messages[0].Text);
        Assert.DoesNotContain("net_ping", messages[0].Text.Split("Actions:")[1]);
        Assert.Contains("You are Deckhand", messages[0].Text);
    }

    [Fact]
    public async Task Native_mode_sends_native_tools()
    {
        var model = new ScriptedChatClient().Reply("Hi.");
        var deps = Dependencies(model, new ChatSettings { StepMode = StepMode.Native });
        var session = await new AgentFactory(deps).CreateSessionAsync(Ct);

        await session.Agent.RunAsync("hello", session.Session, cancellationToken: Ct);

        Assert.Contains("enable_tools", model.Calls[0].Options!.Tools!.Select(t => t.Name));
        Assert.Null(model.Calls[0].Options!.ResponseFormat);
    }

    [Fact]
    public async Task All_mode_has_no_enable_tools_and_sends_every_tool()
    {
        var model = new ScriptedChatClient().Reply("Hi.");
        var deps = Dependencies(model, new ChatSettings { StepMode = StepMode.Native, ToolSelection = ToolSelectionMode.All });
        var session = await new AgentFactory(deps).CreateSessionAsync(Ct);

        await session.Agent.RunAsync("hello", session.Session, cancellationToken: Ct);

        var names = model.Calls[0].Options!.Tools!.Select(t => t.Name).ToList();
        Assert.DoesNotContain("enable_tools", names);
        Assert.Contains("net_ping", names);
        Assert.Contains("wallpaper_set", names);
    }

    [Fact]
    public async Task Change_tools_are_approval_wrapped_and_each_session_has_its_own_state()
    {
        var factory = new AgentFactory(Dependencies(new ScriptedChatClient()));

        var a = await factory.CreateSessionAsync(Ct);
        var b = await factory.CreateSessionAsync(Ct);

        Assert.IsType<ApprovalRequiredAIFunction>(a.Tools.Single(t => t.Name == "wallpaper_set"));
        Assert.NotSame(a.Selection, b.Selection);
        Assert.NotSame(a.Policy, b.Policy);
    }

    [Fact]
    public void System_prompt_has_the_docs_rule_only_when_docs_are_available()
    {
        Assert.Contains("docs_search", Prompts.System(docsAvailable: true));
        Assert.DoesNotContain("docs_search", Prompts.System(docsAvailable: false));
        Assert.Contains("{skills}", Prompts.SkillsInstruction);
    }
}
