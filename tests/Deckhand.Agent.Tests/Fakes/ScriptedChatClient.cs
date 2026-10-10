using System.Runtime.CompilerServices;

namespace Deckhand.Agent.Tests.Fakes;

/// <summary>An IChatClient that returns queued replies and records each call.</summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>> _replies = new();

    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public ScriptedChatClient Reply(string text) =>
        ReplyWith((_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

    public ScriptedChatClient ReplyCall(string name, IDictionary<string, object?>? args = null, string? callId = null) =>
        ReplyWith((_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId ?? Guid.NewGuid().ToString("N"), name, args ?? new Dictionary<string, object?>())])));

    public ScriptedChatClient ReplyWith(Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse> reply)
    {
        _replies.Enqueue(reply);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = messages.ToList();
        Calls.Add((list, options?.Clone()));
        if (_replies.Count == 0)
        {
            throw new InvalidOperationException("No scripted reply left.");
        }

        return Task.FromResult(_replies.Dequeue()(list, options));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
