using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent.Steps;

/// <summary>Uses structured steps when a native endpoint returns reasoning without an answer or tool call.</summary>
internal sealed class NativeFallbackChatClient(IChatClient inner,
                                               ReActStepChatClient fallback,
                                               string structuredInstructions,
                                               Action<ChatResponse> onFallback) : DelegatingChatClient(inner)
{
    private bool _useStructuredSteps;

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
                                                              ChatOptions? options = null,
                                                              CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        if (!_useStructuredSteps)
        {
            var response = await base.GetResponseAsync(history, options, cancellationToken);
            var contents = response.Messages.SelectMany(m => m.Contents).ToList();
            var reasoningOnly = string.IsNullOrWhiteSpace(response.Text)
                && contents.OfType<TextReasoningContent>().Any(c => !string.IsNullOrWhiteSpace(c.Text))
                && !contents.OfType<FunctionCallContent>().Any();
            if (!reasoningOnly)
            {
                return response;
            }

            // Retrying native mode can repeat the same incomplete response, so retain the fallback for this session.
            _useStructuredSteps = true;
            onFallback(response);
        }

        var structuredOptions = options?.Clone() ?? new ChatOptions();
        structuredOptions.Instructions = structuredInstructions;
        return await fallback.GetResponseAsync(history, structuredOptions, cancellationToken);
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
}
