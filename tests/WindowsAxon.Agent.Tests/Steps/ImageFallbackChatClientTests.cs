using WindowsAxon.Agent.Steps;

namespace WindowsAxon.Agent.Tests.Steps;

public class ImageFallbackChatClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeModel(bool rejectAll = false) : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
                                                   ChatOptions? options = null,
                                                   CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            Calls.Add(list);
            if (rejectAll || list.SelectMany(m => m.Contents).OfType<DataContent>().Any())
            {
                throw new InvalidOperationException("image input is not supported");
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
                                                                            ChatOptions? options = null,
                                                                            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static List<ChatMessage> WithImage() =>
    [
        new(ChatRole.User, "look"),
        new(ChatRole.User, [new TextContent("<observation tool=\"ui_screenshot\">Paint</observation>"), new DataContent(new byte[] { 1 }, "image/png")]),
    ];

    [Fact]
    public async Task Rejected_image_is_removed_and_not_sent_again()
    {
        var model = new FakeModel();
        var errors = new List<Exception>();
        using var client = new ImageFallbackChatClient(model, errors.Add);

        var response = await client.GetResponseAsync(WithImage(), cancellationToken: Ct);

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, model.Calls.Count);
        Assert.Contains(ImageFallbackChatClient.Note, model.Calls[1][1].Text);
        Assert.Single(errors);

        await client.GetResponseAsync(WithImage(), cancellationToken: Ct);

        Assert.Equal(3, model.Calls.Count);
        Assert.Empty(model.Calls[2].SelectMany(m => m.Contents).OfType<DataContent>());
    }

    [Fact]
    public async Task Failure_unrelated_to_images_is_reported_and_images_stay_enabled()
    {
        var model = new FakeModel(rejectAll: true);
        var errors = new List<Exception>();
        using var client = new ImageFallbackChatClient(model, errors.Add);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(WithImage(), cancellationToken: Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(WithImage(), cancellationToken: Ct));

        Assert.Empty(errors);
        Assert.Single(model.Calls[2].SelectMany(m => m.Contents).OfType<DataContent>());
    }
}
