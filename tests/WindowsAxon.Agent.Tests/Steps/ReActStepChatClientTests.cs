using WindowsAxon.Agent.Settings;
using WindowsAxon.Agent.Steps;
using WindowsAxon.Agent.Tests.Fakes;

namespace WindowsAxon.Agent.Tests.Steps;

public class ReActStepChatClientTests
{
    private static readonly AIFunction Ping = AIFunctionFactory.Create((string host) => "", "net_ping", "Ping a host.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ReActStepChatClient Client,
                    ScriptedChatClient Inner,
                    StepEvents Events) Create(params string[] replies)
    {
        var inner = new ScriptedChatClient();
        foreach (var reply in replies)
        {
            inner.Reply(reply);
        }

        var events = new StepEvents();
        return (new ReActStepChatClient(inner, ArgsSchemaMode.PerTool, new TranscriptWriter(16384), events), inner, events);
    }

    private static ChatOptions Options() => new() { Instructions = "RULES", Tools = [Ping] };

    [Fact]
    public async Task Sends_a_json_schema_and_no_native_tools()
    {
        var (client, inner, _) = Create("""{"thought":"t","action":"final_answer","answer":"ok"}""");

        await client.GetResponseAsync([TranscriptWriter.NewRequest("hi")], Options(), Ct);

        var (messages, options) = inner.Calls[0];
        Assert.Null(options!.Tools);
        Assert.Null(options.Instructions);
        var format = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.NotNull(format.Schema);
        Assert.Contains("net_ping", messages[0].Text);
    }

    [Fact]
    public async Task Tool_step_becomes_a_function_call()
    {
        var (client, _, events) = Create("""{"thought":"check","action":"net_ping","args":{"host":"1.1.1.1"}}""");

        var response = await client.GetResponseAsync([TranscriptWriter.NewRequest("ping")], Options(), Ct);

        var contents = response.Messages.Single().Contents;
        Assert.Equal("check", contents.OfType<TextReasoningContent>().Single().Text);
        var call = contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("net_ping", call.Name);
        Assert.Equal("1.1.1.1", call.Arguments!["host"]!.ToString());
        Assert.Equal(1, events.ValidSteps);
        Assert.Null(events.LastFinalAction);
    }

    [Theory]
    [InlineData("final_answer")]
    [InlineData("ask_user")]
    public async Task Final_step_becomes_text_and_records_the_action(string action)
    {
        var (client, _, events) = Create($$"""{"thought":"t","action":"{{action}}","answer":"Hello."}""");

        var response = await client.GetResponseAsync([TranscriptWriter.NewRequest("hi")], Options(), Ct);

        var message = response.Messages.Single();
        Assert.Equal("Hello.", message.Text);
        Assert.Equal(action, message.AdditionalProperties![TranscriptWriter.ActionKey]);
        Assert.Equal(action, events.LastFinalAction);
    }

    [Fact]
    public async Task Invalid_step_is_retried_one_time_with_the_error()
    {
        var (client, inner, events) = Create("oops", """{"thought":"t","action":"final_answer","answer":"ok"}""");

        var response = await client.GetResponseAsync([TranscriptWriter.NewRequest("hi")], Options(), Ct);

        Assert.Equal("ok", response.Text);
        Assert.Equal(1, events.InvalidSteps);
        Assert.Contains("not a valid step", inner.Calls[1].Messages[^1].Text);
    }

    [Fact]
    public async Task Two_invalid_steps_end_with_an_error_answer()
    {
        var (client, _, events) = Create("oops", "still oops");

        var response = await client.GetResponseAsync([TranscriptWriter.NewRequest("hi")], Options(), Ct);

        Assert.Contains("did not give a valid step", response.Text);
        Assert.Equal(2, events.InvalidSteps);
        Assert.Equal("final_answer", events.LastFinalAction);
    }
}
