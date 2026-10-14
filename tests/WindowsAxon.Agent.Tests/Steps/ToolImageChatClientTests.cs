using WindowsAxon.Agent.Steps;

namespace WindowsAxon.Agent.Tests.Steps;

public class ToolImageChatClientTests
{
    private static FunctionResultContent Image(string id, string text) =>
        new(id, new List<AIContent> { new TextContent(text), new DataContent(new byte[] { 1 }, "image/png") });

    [Fact]
    public void Newest_image_moves_to_a_user_message_after_the_tool_results()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "look"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "ui_screenshot")]),
            new(ChatRole.Tool, [Image("c1", "first")]),
            new(ChatRole.Assistant, [new FunctionCallContent("c2", "ui_screenshot"), new FunctionCallContent("c3", "net_ping")]),
            new(ChatRole.Tool, [Image("c2", "second")]),
            new(ChatRole.Tool, [new FunctionResultContent("c3", "pong")]),
        };

        var messages = ToolImageChatClient.Rewrite(history);

        Assert.Equal(7, messages.Count);
        Assert.Equal("first", Assert.IsType<FunctionResultContent>(Assert.Single(messages[2].Contents)).Result);
        Assert.Equal("second", Assert.IsType<FunctionResultContent>(Assert.Single(messages[4].Contents)).Result);
        Assert.Equal(ChatRole.Tool, messages[5].Role);
        Assert.Equal(ChatRole.User, messages[6].Role);
        Assert.Single(messages[6].Contents.OfType<DataContent>());
        Assert.Single(messages.SelectMany(m => m.Contents).OfType<DataContent>());
    }

    [Fact]
    public void History_without_images_is_unchanged()
    {
        var history = new List<ChatMessage> { new(ChatRole.User, "hi"), new(ChatRole.Tool, [new FunctionResultContent("c1", "ok")]) };

        Assert.Equal(history, ToolImageChatClient.Rewrite(history));
    }
}
