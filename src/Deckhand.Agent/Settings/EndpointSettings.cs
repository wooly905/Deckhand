
namespace Deckhand.Agent.Settings;

public sealed record EndpointSettings
{
    public required string BaseUrl
    {
        get; init;
    }

    /// <summary>A reference "credential:&lt;name&gt;" to Windows Credential Manager, or null.</summary>
    public string? ApiKey
    {
        get; init;
    }
}
