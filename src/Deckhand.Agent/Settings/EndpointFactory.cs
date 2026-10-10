using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using ChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;
using ChatReasoningEffortLevel = OpenAI.Chat.ChatReasoningEffortLevel;

namespace Deckhand.Agent.Settings;

/// <summary>Reads secrets by name. M1c implements it with Windows Credential Manager.</summary>
public interface ISecretStore
{
    string? Read(string name);
}

public sealed class EndpointFactory(ModelSettings settings, ISecretStore secrets)
{
    private const string CredentialPrefix = "credential:";

    public ModelSettings Settings { get; } = settings;

    public IChatClient CreateChatClient()
    {
        var endpoint = Settings.ChatEndpoint;
        var client = new OpenAIClient(
            new ApiKeyCredential(ResolveKey(endpoint) ?? "none"), // the SDK needs a value for servers without a key
            new OpenAIClientOptions { Endpoint = new Uri(endpoint.BaseUrl), NetworkTimeout = TimeSpan.FromMinutes(5) });
        IChatClient chat = client.GetChatClient(Settings.Chat.Model).AsIChatClient();

        return Settings.Chat.Thinking || Settings.Chat.ReasoningEffortWhenThinkingOff is null
            ? chat
            : new ReasoningEffortChatClient(chat, Settings.Chat.ReasoningEffortWhenThinkingOff);
    }

    internal string? ResolveKey(EndpointSettings endpoint)
    {
        if (string.IsNullOrEmpty(endpoint.ApiKey))
        {
            return null;
        }

        if (!endpoint.ApiKey.StartsWith(CredentialPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ApiKey must be a credential reference (credential:<name>), not a key.");
        }

        if (!EndpointRules.MaySendApiKey(endpoint.BaseUrl))
        {
            throw new InvalidOperationException("Deckhand does not send an API key over http to a non-loopback address. Use https.");
        }

        return secrets.Read(endpoint.ApiKey[CredentialPrefix.Length..]);
    }
}

/// <summary>Sets reasoning_effort on every request, to turn off the thinking mode of the model.</summary>
public sealed class ReasoningEffortChatClient(IChatClient inner, string effort) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Apply(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Apply(options), cancellationToken);

    private ChatOptions Apply(ChatOptions? options)
    {
        var result = options?.Clone() ?? new ChatOptions();
        var previous = result.RawRepresentationFactory;
        result.RawRepresentationFactory = client =>
        {
            var raw = previous?.Invoke(client) as ChatCompletionOptions ?? new ChatCompletionOptions();
            raw.ReasoningEffortLevel = new ChatReasoningEffortLevel(effort);
            return raw;
        };
        return result;
    }
}
