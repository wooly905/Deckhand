
namespace WindowsAxon.Agent.Selection;

/// <summary>Asks the user if the run may continue after the step limit.</summary>
public interface IStepLimitPrompter
{
    Task<bool> AskToContinueAsync(int stepsUsed, CancellationToken cancellationToken);
}
