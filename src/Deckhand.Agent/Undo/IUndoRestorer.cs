
namespace Deckhand.Agent.Undo;

/// <summary>Restores one kind of setting (wallpaper, registry, ...). M1b implements one per settings tool.</summary>
public interface IUndoRestorer
{
    string Kind
    {
        get;
    }

    string? ReadCurrent(string target);

    void Restore(string target, string? previousValue);
}
