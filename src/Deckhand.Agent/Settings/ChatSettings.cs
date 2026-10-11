
namespace Deckhand.Agent.Settings;

public sealed record ChatSettings
{
    public string Endpoint { get; init; } = "local";

    public string Model { get; init; } = "deckhand-qwen3.5-9b";

    public int ContextWindow { get; init; } = 16384;

    public StepMode StepMode { get; init; } = StepMode.Structured;

    public ArgsSchemaMode ArgsSchema { get; init; } = ArgsSchemaMode.PerTool;

    public bool Thinking
    {
        get; init;
    }

    /// <summary>The reasoning_effort value sent when Thinking is false. Null sends nothing.</summary>
    public string? ReasoningEffortWhenThinkingOff { get; init; } = "none";

    public ToolSelectionMode ToolSelection { get; init; } = ToolSelectionMode.Groups;

    public int MaxActiveTools { get; init; } = 14;

    /// <summary>Run every known tool without asking. Set false to ask before changes.</summary>
    public bool AutoApproveTools { get; init; } = true;
}
