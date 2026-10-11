
namespace Deckhand.Agent.Settings;

/// <summary>Reads secrets by name. M1c implements it with Windows Credential Manager.</summary>
public interface ISecretStore
{
    string? Read(string name);
}
