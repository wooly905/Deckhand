
namespace WindowsAxon.Agent.Steps;

/// <summary>Step results of one session, written by ReActStepChatClient.</summary>
public sealed class StepEvents
{
    public int ValidSteps
    {
        get; private set;
    }

    public int InvalidSteps
    {
        get; private set;
    }

    /// <summary>"final_answer" or "ask_user" when the last step ended the run; null after a tool step.</summary>
    public string? LastFinalAction
    {
        get; private set;
    }

    internal void RecordValid(Step step)
    {
        ValidSteps++;
        LastFinalAction = step.IsFinal ? step.Action : null;
    }

    internal void RecordInvalid() => InvalidSteps++;

    internal void RecordGiveUp() => LastFinalAction = StepSchemaBuilder.FinalAnswer;

    public void Reset()
    {
        ValidSteps = 0;
        InvalidSteps = 0;
        LastFinalAction = null;
    }
}
