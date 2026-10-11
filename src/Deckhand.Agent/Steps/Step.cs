
namespace Deckhand.Agent.Steps;

/// <summary>One model output: a thought plus one action, or a final answer.</summary>
public sealed record Step(string Thought, string Action, IReadOnlyDictionary<string, object?> Args, string Answer)
{
    public bool IsFinal => Action is StepSchemaBuilder.FinalAnswer or StepSchemaBuilder.AskUser;
}
