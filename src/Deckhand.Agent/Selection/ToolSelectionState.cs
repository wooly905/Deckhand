using Deckhand.Agent.Tools;

namespace Deckhand.Agent.Selection;

public sealed record ActivationResult(string Key, IReadOnlyList<(string Key, IReadOnlyList<string> Tools)> Removed)
{
    public string Describe()
    {
        var text = $"Enabled: {ShortName(Key)}.";
        if (Removed.Count > 0)
        {
            var parts = Removed.Select(r => $"{ShortName(r.Key)} ({string.Join(", ", r.Tools)} are no longer available)");
            text += $" Removed: {string.Join("; ", parts)}.";
        }

        return text;
    }

    internal static string ShortName(string key) => key[(key.IndexOf(':') + 1)..];
}

/// <summary>Active skills and groups, and the step counter, for the current request of one session.</summary>
public sealed class ToolSelectionState(ToolCatalog catalog, int maxActiveTools)
{
    public const int BaseLimit = 10;
    public const int AppControlLimit = 25;
    public const int ExtensionSteps = 10;

    private readonly Dictionary<string, (IReadOnlyList<string> Tools, long LastUsed)> _active = [];
    private long _clock;
    private bool _appControlUsed;
    private readonly List<string> _history = [];

    /// <summary>Every key activated in the current request, in order (for the evaluation).</summary>
    public IReadOnlyList<string> ActivationHistory => _history;

    public IReadOnlyList<string> ActiveKeys => _active.Keys.ToList();

    public IReadOnlySet<string> ActiveToolNames => _active.Values.SelectMany(v => v.Tools).ToHashSet();

    public int StepCount { get; private set; }

    public int StepLimit => (_appControlUsed ? AppControlLimit : BaseLimit) + (ExtensionUsed ? ExtensionSteps : 0);

    public bool ExtensionUsed { get; private set; }

    /// <summary>The user did not extend the limit: the model may only give a final answer.</summary>
    public bool MustFinish { get; set; }

    /// <summary>The last run ended with ask_user; the next user message is a reply.</summary>
    public bool AwaitingReply { get; set; }

    public bool IsActive(string toolName) => catalog.IsBaseTool(toolName) || ActiveToolNames.Contains(toolName);

    public ActivationResult Activate(string key, IReadOnlyList<string> tools)
    {
        _history.Add(key);
        _active[key] = (tools, ++_clock);
        if (ToolCatalog.AppControlGroups.Contains(ActivationResult.ShortName(key)) && key.StartsWith("group:", StringComparison.Ordinal))
        {
            _appControlUsed = true;
        }

        var removed = new List<(string, IReadOnlyList<string>)>();
        while (CountedTools() > maxActiveTools && _active.Count > 1)
        {
            var oldest = _active.Where(a => a.Key != key).MinBy(a => a.Value.LastUsed);
            _active.Remove(oldest.Key);
            removed.Add((oldest.Key, oldest.Value.Tools.Where(t => !catalog.IsBaseTool(t)).ToList()));
        }

        return new ActivationResult(key, removed);
    }

    public void MarkUsed(string toolName)
    {
        foreach (var key in _active.Where(a => a.Value.Tools.Contains(toolName)).Select(a => a.Key).ToList())
        {
            _active[key] = (_active[key].Tools, ++_clock);
        }
    }

    public void CountStep() => StepCount++;

    public void Extend()
    {
        if (ExtensionUsed)
        {
            throw new InvalidOperationException("The step limit was already extended for this request.");
        }

        ExtensionUsed = true;
    }

    public void StartRequest()
    {
        _active.Clear();
        _history.Clear();
        _appControlUsed = false;
        StepCount = 0;
        ExtensionUsed = false;
        MustFinish = false;
        AwaitingReply = false;
    }

    private int CountedTools() => ActiveToolNames.Count(t => !catalog.IsBaseTool(t));
}
