using WindowsAxon.Agent.Tools;

namespace WindowsAxon.Agent.Selection;

/// <summary>Active skills and groups, and the step counter, for the current request of one session.</summary>
public sealed class ToolSelectionState(ToolCatalog catalog, int maxActiveTools)
{
    public const int BaseLimit = 50;
    public const int AppControlLimit = 50;
    public const int ExtensionSteps = 10;

    private readonly Dictionary<string, (IReadOnlyList<string> Tools, long LastUsed)> _active = [];
    private long _clock;
    private bool _appControlUsed;
    private readonly List<string> _history = [];

    /// <summary>Every key activated in the current request, in order (for the evaluation).</summary>
    public IReadOnlyList<string> ActivationHistory => _history;

    public IReadOnlyList<string> ActiveKeys => _active.Keys.ToList();

    public IReadOnlySet<string> ActiveToolNames => _active.Values.SelectMany(v => v.Tools).ToHashSet();

    public int StepCount
    {
        get; private set;
    }

    public int StepLimit => (_appControlUsed ? AppControlLimit : BaseLimit) + (ExtensionUsed ? ExtensionSteps : 0);

    public bool ExtensionUsed
    {
        get; private set;
    }

    /// <summary>The user did not extend the limit: the model may only give a final answer.</summary>
    public bool MustFinish
    {
        get; set;
    }

    /// <summary>The last run ended with ask_user; the next user message is a reply.</summary>
    public bool AwaitingReply
    {
        get; set;
    }

    /// <summary>ToolSelection = All: the model sees every tool, so every tool is active.</summary>
    public bool AllToolsActive
    {
        get; init;
    }

    public bool IsActive(string toolName) => AllToolsActive || catalog.IsBaseTool(toolName)
        || ActiveToolNames.Contains(toolName);

    public ActivationResult Activate(string key, IReadOnlyList<string> tools)
    {
        _history.Add(key);
        _active[key] = (tools, ++_clock);
        var appControlGroup = key.StartsWith("group:", StringComparison.Ordinal)
            && ToolCatalog.AppControlGroups.Contains(ActivationResult.ShortName(key));
        if (appControlGroup || tools.Any(IsAppControlTool))
        {
            _appControlUsed = true; // a group or a skill with browser or desktop_ui tools
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
        if (IsAppControlTool(toolName))
        {
            _appControlUsed = true; // covers ToolSelection = All, where no group is activated
        }

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

    private bool IsAppControlTool(string toolName) =>
        catalog.Find(toolName)?.Group is { } group && ToolCatalog.AppControlGroups.Contains(group);

    private int CountedTools() => ActiveToolNames.Count(t => !catalog.IsBaseTool(t));
}
