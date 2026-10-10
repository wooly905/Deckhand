using Deckhand.Agent.Approval;
using Deckhand.Agent.Tests.Fakes;
using NSubstitute;

namespace Deckhand.Agent.Tests;

public class AgentRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ToolStep(string action, string args) => $$"""{"thought":"t","action":"{{action}}","args":{{args}}}""";

    private static string Final(string answer, string action = "final_answer") => $$"""{"thought":"t","action":"{{action}}","answer":"{{answer}}"}""";

    private static async Task<(AgentRunner Runner, DeckhandSession Session, ListAuditLog Audit, IApprovalPrompter Prompter)> Create(ScriptedChatClient model)
    {
        var deps = AgentFactoryTests.Dependencies(model);
        var session = await new AgentFactory(deps).CreateSessionAsync(Ct);
        var prompter = Substitute.For<IApprovalPrompter>();
        var audit = (ListAuditLog)deps.Audit;
        return (new AgentRunner(session, prompter, audit), session, audit, prompter);
    }

    [Fact]
    public async Task Enables_a_group_runs_a_read_tool_and_answers()
    {
        var model = new ScriptedChatClient()
            .Reply(ToolStep("enable_tools", """{"group":"network"}"""))
            .Reply(ToolStep("net_ping", """{"host":"1.1.1.1"}"""))
            .Reply(Final("The host answers."));
        var (runner, session, audit, _) = await Create(model);

        var result = await runner.RunAsync("is 1.1.1.1 up?", cancellationToken: Ct);

        Assert.Equal(new RunResult(RunStatus.Answered, "The host answers.", 3), result);
        Assert.Contains("<observation tool=\"net_ping\">reply from 1.1.1.1</observation>", model.Calls[2].Messages.Select(m => m.Text));
        Assert.Contains(audit.Entries, e => e is { Kind: "call", Tool: "net_ping", Success: true });
        Assert.Equal(0, session.Ledger.UnapprovedCalls);
    }

    [Fact]
    public async Task Change_tool_runs_after_the_user_allows_it()
    {
        var model = new ScriptedChatClient()
            .Reply(ToolStep("enable_tools", """{"group":"personalize"}"""))
            .Reply(ToolStep("wallpaper_set", """{"path":"C:\\b.jpg"}"""))
            .Reply(Final("Done."));
        var (runner, session, audit, prompter) = await Create(model);
        prompter.AskAsync(Arg.Any<FunctionCallContent>(), Arg.Any<ApprovalDecision>(), Arg.Any<CancellationToken>()).Returns(ApprovalAnswer.Allow);

        var result = await runner.RunAsync("set wallpaper", cancellationToken: Ct);

        Assert.Equal("Done.", result.Text);
        Assert.Contains(audit.Entries, e => e is { Kind: "approval", Tool: "wallpaper_set", Decision: "approved" });
        Assert.Contains(audit.Entries, e => e is { Kind: "call", Tool: "wallpaper_set", Decision: "approved" });
        Assert.Equal(0, session.Ledger.UnapprovedCalls);
    }

    [Fact]
    public async Task Denied_tool_does_not_run_and_the_model_continues()
    {
        var model = new ScriptedChatClient()
            .Reply(ToolStep("enable_tools", """{"group":"personalize"}"""))
            .Reply(ToolStep("wallpaper_set", """{"path":"C:\\b.jpg"}"""))
            .Reply(Final("OK, I did not change it."));
        var (runner, _, audit, prompter) = await Create(model);
        prompter.AskAsync(default!, default!, Ct).ReturnsForAnyArgs(ApprovalAnswer.Deny);

        var result = await runner.RunAsync("set wallpaper", cancellationToken: Ct);

        Assert.Equal("OK, I did not change it.", result.Text);
        Assert.DoesNotContain(audit.Entries, e => e is { Kind: "call", Tool: "wallpaper_set" });
        Assert.Contains(audit.Entries, e => e is { Kind: "approval", Decision: "denied" });
    }

    [Fact]
    public async Task Allow_for_conversation_skips_the_next_card()
    {
        var model = new ScriptedChatClient()
            .Reply(ToolStep("enable_tools", """{"group":"personalize"}"""))
            .Reply(ToolStep("wallpaper_set", """{"path":"C:\\a.jpg"}"""))
            .Reply(ToolStep("wallpaper_set", """{"path":"C:\\b.jpg"}"""))
            .Reply(Final("Done."));
        var (runner, _, _, prompter) = await Create(model);
        prompter.AskAsync(default!, default!, Ct).ReturnsForAnyArgs(ApprovalAnswer.AllowForConversation);

        await runner.RunAsync("set two wallpapers", cancellationToken: Ct);

        await prompter.ReceivedWithAnyArgs(1).AskAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task Reply_to_ask_user_continues_the_same_request()
    {
        var model = new ScriptedChatClient()
            .Reply(ToolStep("enable_tools", """{"group":"network"}"""))
            .Reply(Final("Which host?", "ask_user"))
            .Reply(ToolStep("net_ping", """{"host":"8.8.8.8"}"""))
            .Reply(Final("8.8.8.8 answers."));
        var (runner, session, _, _) = await Create(model);

        var first = await runner.RunAsync("ping something", cancellationToken: Ct);
        Assert.Equal(RunStatus.AskedUser, first.Status);
        Assert.True(runner.AwaitingReply);

        var second = await runner.RunAsync("8.8.8.8", cancellationToken: Ct);

        Assert.Equal(new RunResult(RunStatus.Answered, "8.8.8.8 answers.", 4), second);
        Assert.Contains("group:network", session.Selection.ActiveKeys);
        Assert.Contains(model.Calls[2].Messages, m => m.Text == "ping something");
    }

    [Fact]
    public async Task New_request_button_resets_the_request()
    {
        var model = new ScriptedChatClient()
            .Reply(ToolStep("enable_tools", """{"group":"network"}"""))
            .Reply(Final("Which host?", "ask_user"))
            .Reply(Final("Dark mode is on."));
        var (runner, session, _, _) = await Create(model);
        await runner.RunAsync("ping something", cancellationToken: Ct);

        var result = await runner.RunAsync("set dark mode", forceNewRequest: true, cancellationToken: Ct);

        Assert.Equal(1, result.Steps);
        Assert.Empty(session.Selection.ActiveKeys);
    }

    [Fact]
    public async Task Stop_returns_cancelled_and_the_next_message_is_a_new_request()
    {
        var model = new ScriptedChatClient().Reply(Final("never"));
        var (runner, _, _, _) = await Create(model);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await runner.RunAsync("hi", cancellationToken: cts.Token);

        Assert.Equal(RunStatus.Cancelled, result.Status);
        Assert.False(runner.AwaitingReply);
    }

    [Fact]
    public async Task Model_failure_is_reported_without_an_exception()
    {
        var model = new ScriptedChatClient().ReplyWith((_, _) => throw new HttpRequestException("503 Service Unavailable"));
        var (runner, _, _, _) = await Create(model);

        var result = await runner.RunAsync("hi", cancellationToken: Ct);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.StartsWith("The model service is busy or not available.", result.Text);
    }
}
