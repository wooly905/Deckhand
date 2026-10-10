using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckhand.Agent.Settings;

public enum StepMode { Structured, Native }

public enum ArgsSchemaMode { PerTool, Free }

public enum ToolSelectionMode { Groups, All }

public sealed record EndpointSettings
{
    public required string BaseUrl { get; init; }

    /// <summary>A reference "credential:&lt;name&gt;" to Windows Credential Manager, or null.</summary>
    public string? ApiKey { get; init; }
}

public sealed record ChatSettings
{
    public string Endpoint { get; init; } = "local";
    public string Model { get; init; } = "deckhand-qwen3.5-9b";
    public int ContextWindow { get; init; } = 16384;
    public StepMode StepMode { get; init; } = StepMode.Structured;
    public ArgsSchemaMode ArgsSchema { get; init; } = ArgsSchemaMode.PerTool;
    public bool Thinking { get; init; }

    /// <summary>The reasoning_effort value sent when Thinking is false. Null sends nothing.</summary>
    public string? ReasoningEffortWhenThinkingOff { get; init; } = "none";

    public ToolSelectionMode ToolSelection { get; init; } = ToolSelectionMode.Groups;
    public int MaxActiveTools { get; init; } = 14;
}

public sealed record ModelSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Dictionary<string, EndpointSettings> Endpoints { get; init; } = new()
    {
        ["local"] = new EndpointSettings { BaseUrl = "http://localhost:11434/v1" },
    };

    public ChatSettings Chat { get; init; } = new();

    public EndpointSettings ChatEndpoint =>
        Endpoints.TryGetValue(Chat.Endpoint, out var endpoint)
            ? endpoint
            : throw new InvalidOperationException($"Model endpoint '{Chat.Endpoint}' is not defined in Models.Endpoints.");

    public bool IsChatRemote => EndpointRules.IsRemote(ChatEndpoint.BaseUrl);

    /// <summary>Reads the "Models" section of a configuration JSON document.</summary>
    public static ModelSettings Load(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Models", out var models))
        {
            return new ModelSettings();
        }

        var loaded = models.Deserialize<ModelSettings>(JsonOptions) ?? new ModelSettings();
        return loaded with
        {
            Endpoints = loaded.Endpoints.Count == 0 ? new ModelSettings().Endpoints : loaded.Endpoints,
            Chat = loaded.Chat ?? new ChatSettings(),
        };
    }
}

public static class EndpointRules
{
    public static bool IsRemote(string baseUrl) => !new Uri(baseUrl).IsLoopback;

    /// <summary>An API key goes only over https, or to a loopback address.</summary>
    public static bool MaySendApiKey(string baseUrl)
    {
        var uri = new Uri(baseUrl);
        return uri.IsLoopback || uri.Scheme == Uri.UriSchemeHttps;
    }
}
