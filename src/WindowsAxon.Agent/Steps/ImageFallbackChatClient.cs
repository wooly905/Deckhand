using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace WindowsAxon.Agent.Steps;

/// <summary>Retries without images when the endpoint rejects them, then stops sending images for the session.</summary>
internal sealed class ImageFallbackChatClient(IChatClient inner, Action<Exception> onFallback) : DelegatingChatClient(inner)
{
    internal const string Note = "[Image removed: this model cannot read images. Use ui_snapshot or ui_read_text instead of ui_screenshot.]";

    private bool _imagesRejected;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
                                                              ChatOptions? options = null,
                                                              CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        if (!history.Any(HasImage))
        {
            return await base.GetResponseAsync(history, options, cancellationToken);
        }

        if (_imagesRejected)
        {
            return await base.GetResponseAsync(WithoutImages(history), options, cancellationToken);
        }

        Exception rejected;
        try
        {
            return await base.GetResponseAsync(history, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rejected = ex;
        }

        // If the retry also fails, the images were not the cause, so keep sending them.
        var response = await base.GetResponseAsync(WithoutImages(history), options, cancellationToken);
        _imagesRejected = true;
        onFallback(rejected);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
                                                                                         ChatOptions? options = null,
                                                                                         [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    private static bool IsImage(AIContent content) => content is DataContent data && data.HasTopLevelMediaType("image");

    private static bool HasImage(ChatMessage message) => message.Contents.Any(IsImage);

    private static List<ChatMessage> WithoutImages(List<ChatMessage> history) =>
        history.Select(message => !HasImage(message)
            ? message
            : new ChatMessage(message.Role,
                              message.Contents.Select(c => IsImage(c) ? new TextContent(Note) : c).ToList())
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
                AdditionalProperties = message.AdditionalProperties,
            }).ToList();
}
