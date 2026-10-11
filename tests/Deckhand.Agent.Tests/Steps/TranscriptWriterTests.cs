using Deckhand.Agent.Steps;

namespace Deckhand.Agent.Tests.Steps;

public class TranscriptWriterTests
{
    private static readonly AIFunction Ping = AIFunctionFactory.Create((string host) => "", "net_ping", "Ping a host.");

    private static ChatMessage Call(string id, string name, string thought) =>
        new(ChatRole.Assistant,
            [
new TextReasoningContent(thought),
            new FunctionCallContent(id,
                                    name,
                                    new Dictionary<string, object?> { ["host"] = "1.1.1.1" })
]);

    private static ChatMessage Result(string id, string text) => new(ChatRole.Tool,
                                                                     [new FunctionResultContent(id, text)]);

    [Fact]
    public void System_message_has_instructions_tools_and_final_actions()
    {
        var messages = new TranscriptWriter(16384).Write([TranscriptWriter.NewRequest("hi")], "RULES", [Ping]);

        Assert.Equal(ChatRole.System, messages[0].Role);
        var system = messages[0].Text;
        Assert.StartsWith("RULES", system);
        Assert.Contains("- net_ping(host: string): Ping a host.", system);
        Assert.Contains("final_answer", system);
        Assert.Contains("ask_user", system);
    }

    [Fact]
    public void Tool_calls_become_step_json_and_results_become_observations()
    {
        var history = new List<ChatMessage>
        {
            TranscriptWriter.NewRequest("ping it"),
            Call("c1", "net_ping", "check"),
            Result("c1",
                   "time=4ms")
        };

        var messages = new TranscriptWriter(16384).Write(history, "RULES", [Ping]);

        Assert.Equal("ping it", messages[1].Text);
        Assert.Equal(ChatRole.Assistant, messages[2].Role);
        Assert.Equal("""{"thought":"check","action":"net_ping","args":{"host":"1.1.1.1"}}""", messages[2].Text);
        Assert.Equal(ChatRole.User, messages[3].Role);
        Assert.Equal("<observation tool=\"net_ping\">time=4ms</observation>", messages[3].Text);
    }

    [Fact]
    public void Earlier_requests_keep_only_the_request_and_the_final_answer()
    {
        var history = new List<ChatMessage>
        {
            TranscriptWriter.NewRequest("first"), Call("c1", "net_ping", "t"), Result("c1", "r"),
            new(ChatRole.Assistant, [new TextReasoningContent("done"), new TextContent("First answer.")]),
            TranscriptWriter.NewRequest("second"),
        };

        var messages = new TranscriptWriter(16384).Write(history, "RULES", [Ping]);

        Assert.Equal(["first", "First answer.", "second"], messages.Skip(1).Select(m => m.Text));
    }

    [Fact]
    public void Ask_user_text_keeps_its_action()
    {
        var question = new ChatMessage(ChatRole.Assistant, "Which one?") { AdditionalProperties = new() { [TranscriptWriter.ActionKey] = "ask_user" } };
        var history = new List<ChatMessage> { TranscriptWriter.NewRequest("open vs"), question, new(ChatRole.User,
                                                                                                    "the 2026 one") };

        var messages = new TranscriptWriter(16384).Write(history, "RULES", []);

        Assert.Equal("""{"thought":"","action":"ask_user","answer":"Which one?"}""", messages[2].Text);
        Assert.Equal("the 2026 one", messages[3].Text);
    }

    [Fact]
    public void Oldest_observations_are_replaced_when_over_budget()
    {
        var big = new string('x', 6000); // about 1,500 tokens each
        var history = new List<ChatMessage> { TranscriptWriter.NewRequest("go") };
        for (var i = 0; i < 4; i++)
        {
            history.Add(Call($"c{i}", "net_ping", "t"));
            history.Add(Result($"c{i}", big));
        }

        var messages = new TranscriptWriter(8192).Write(history, "RULES", [Ping]); // budget 2,192 tokens

        var observations = messages.Where(m => m.Text.StartsWith("<observation")).Select(m => m.Text).ToList();
        Assert.Equal(4, observations.Count);
        Assert.Contains("Removed to save space", observations[0]);
        Assert.DoesNotContain("Removed to save space", observations[^1]);
    }

    [Fact]
    public void History_without_a_marker_is_all_current_request()
    {
        var history = new List<ChatMessage> { new(ChatRole.User, "hello"), Call("c1", "net_ping", "t"), Result("c1",
                                                                                                               "r") };

        var messages = new TranscriptWriter(16384).Write(history, "RULES", [Ping]);

        Assert.Equal(4, messages.Count);
    }
}
