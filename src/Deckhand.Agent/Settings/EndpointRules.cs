
namespace Deckhand.Agent.Settings;

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
