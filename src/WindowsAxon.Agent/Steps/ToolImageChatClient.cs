using Microsoft.Extensions.AI;

namespace WindowsAxon.Agent.Steps;

/// <summary>Moves the newest tool image into a user message, because tool messages carry text only.</summary>
internal sealed class ToolImageChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
                                                        ChatOptions? options = null,
                                                        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(Rewrite(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
                                                                                  ChatOptions? options = null,
                                                                                  CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(Rewrite(messages), options, cancellationToken);

    internal static List<ChatMessage> Rewrite(IEnumerable<ChatMessage> messages)
    {
        var all = messages.ToList();
        var last = all.SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .LastOrDefault(r => TranscriptWriter.Images(r.Result).Count > 0);
        if (last is null)
        {
            return all;
        }

        var result = new List<ChatMessage>();
        ChatMessage? pending = null;
        foreach (var message in all)
        {
            // The image message must follow the whole group of tool results.
            if (pending is not null && message.Role != ChatRole.Tool)
            {
                result.Add(pending);
                pending = null;
            }

            if (!message.Contents.OfType<FunctionResultContent>().Any(r => TranscriptWriter.Images(r.Result).Count > 0))
            {
                result.Add(message);
                continue;
            }

            var contents = new List<AIContent>();
            foreach (var content in message.Contents)
            {
                if (content is not FunctionResultContent functionResult || TranscriptWriter.Images(functionResult.Result).Count == 0)
                {
                    contents.Add(content);
                    continue;
                }

                contents.Add(new FunctionResultContent(functionResult.CallId,
                                                       TranscriptWriter.ObservationText(functionResult.Result)));
                if (functionResult == last)
                {
                    pending = new ChatMessage(ChatRole.User,
                                              [new TextContent("The image from the last tool result:"), .. TranscriptWriter.Images(functionResult.Result)]);
                }
            }

            result.Add(new ChatMessage(message.Role, contents)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
                AdditionalProperties = message.AdditionalProperties,
            });
        }

        if (pending is not null)
        {
            result.Add(pending);
        }

        return result;
    }
}
