using Deckhand.Agent.Settings;
using Deckhand.Agent.Tests.Fakes;
using NSubstitute;
using ChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;

namespace Deckhand.Agent.Tests.Settings;

public class EndpointFactoryTests
{
    private static ModelSettings WithEndpoint(string url, string? key) => new()
    {
        Endpoints = new()
        {
            ["e"] = new EndpointSettings { BaseUrl = url, ApiKey = key }
        },
        Chat = new ChatSettings { Endpoint = "e" },
    };

    [Fact]
    public void Key_reference_is_resolved_from_the_secret_store()
    {
        var secrets = Substitute.For<ISecretStore>();
        secrets.Read("Deckhand/cloud").Returns("k-123");
        var factory = new EndpointFactory(WithEndpoint("https://api.example.com/v1", "credential:Deckhand/cloud"),
                                          secrets);

        Assert.Equal("k-123", factory.ResolveKey(factory.Settings.ChatEndpoint));
    }

    [Fact]
    public void Plain_text_key_in_configuration_is_rejected()
    {
        var factory = new EndpointFactory(WithEndpoint("https://api.example.com/v1", "sk-plain"),
                                          Substitute.For<ISecretStore>());

        Assert.Throws<InvalidOperationException>(() => factory.ResolveKey(factory.Settings.ChatEndpoint));
    }

    [Fact]
    public void Key_over_http_to_a_remote_address_is_rejected()
    {
        var factory = new EndpointFactory(WithEndpoint("http://192.168.1.20:8000/v1", "credential:x"),
                                          Substitute.For<ISecretStore>());

        var error = Assert.Throws<InvalidOperationException>(() => factory.ResolveKey(factory.Settings.ChatEndpoint));
        Assert.Contains("http", error.Message);
    }

    [Fact]
    public void Endpoint_without_key_resolves_to_null()
    {
        var factory = new EndpointFactory(WithEndpoint("http://localhost:11434/v1", null),
                                          Substitute.For<ISecretStore>());

        Assert.Null(factory.ResolveKey(factory.Settings.ChatEndpoint));
    }

    [Fact]
    public async Task Reasoning_effort_client_sets_the_raw_option()
    {
        var inner = new ScriptedChatClient().Reply("ok");
        using var client = new ReasoningEffortChatClient(inner, "none");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
                                      cancellationToken: TestContext.Current.CancellationToken);

        var factory = inner.Calls[0].Options!.RawRepresentationFactory!;
        var raw = Assert.IsType<ChatCompletionOptions>(factory(inner));
        Assert.Equal("none", raw.ReasoningEffortLevel?.ToString());
    }

    [Theory]
    [InlineData("https://api.example.com/v1", "auto", true)]
    [InlineData("https://api.example.com/v1", "gpt-5.4", false)]
    [InlineData("http://localhost:11434/v1", "auto", false)]
    [InlineData("http://localhost:11434/v1", "local-model", false)]
    public void Remote_auto_uses_responses_while_other_models_keep_chat_completions(string url,
                                                                                    string model,
                                                                                    bool responses)
    {
        var settings = WithEndpoint(url, null);
        settings = settings with
        {
            Chat = settings.Chat with
            {
                Model = model,
                ReasoningEffortWhenThinkingOff = null
            }
        };

        using var client = new EndpointFactory(settings, Substitute.For<ISecretStore>()).CreateChatClient();

        Assert.Equal(responses, client.GetService<OpenAI.Responses.ResponsesClient>() is not null);
        Assert.Equal(!responses, client.GetService<OpenAI.Chat.ChatClient>() is not null);
    }

    [Fact]
    public async Task Reasoning_effort_client_supports_responses_options()
    {
        var inner = new ScriptedChatClient().Reply("ok");
        using var client = new ReasoningEffortChatClient(inner, "low");
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
                                      cancellationToken: TestContext.Current.CancellationToken);

        using var responseClient = new OpenAI.OpenAIClient("test-placeholder").GetResponsesClient()
            .AsIChatClient("auto");
        var factory = inner.Calls[0].Options!.RawRepresentationFactory!;
        var raw = Assert.IsType<OpenAI.Responses.CreateResponseOptions>(factory(responseClient));
        Assert.Equal("low", raw.ReasoningOptions?.ReasoningEffortLevel?.ToString());
    }
}
