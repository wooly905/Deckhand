using System.Text.Json;
using Deckhand.Agent.Audit;
using Deckhand.Agent.Selection;
using Deckhand.Agent.Steps;
using Deckhand.Agent.Tools;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent.Guard;

/// <summary>ToolGuard: checks, timeout, observation cut, masking, and audit around one tool.</summary>
public sealed class GuardedAIFunction(ToolEntry entry, ToolSelectionState state, CallTracker tracker, ApprovalLedger ledger, IAuditLog audit)
    : DelegatingAIFunction(entry.Function)
{
    public const int MaxObservationChars = 1500;
    public const int MaxErrorChars = 300;
    public const string NotEnabledMessage = "This tool is not enabled. Call load_skill or enable_tools first.";
    public const string RepeatMessage = "You already ran this. Use the result or select a different action.";
    private const string StoppedMessage = "Stopped: the same action was repeated 3 times.";

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!state.IsActive(entry.Name))
        {
            return NotEnabledMessage;
        }

        var context = FunctionInvokingChatClient.CurrentContext;
        var callId = context?.CallContent.CallId ?? "";
        var argumentsJson = JsonSerializer.Serialize(arguments.ToDictionary(a => a.Key, a => a.Value));

        var repeat = tracker.RecordCall(entry.Name + argumentsJson);
        if (repeat == 2)
        {
            return RepeatMessage;
        }

        if (repeat >= 3)
        {
            if (context is not null)
            {
                context.Terminate = true;
            }

            return StoppedMessage;
        }

        var decision = "not needed";
        if (entry.NeedsApproval)
        {
            decision = ledger.Find(callId) == true ? "approved" : "missing";
            if (decision == "missing")
            {
                ledger.CountUnapproved();
            }
        }

        state.MarkUsed(entry.Name);
        string text;
        bool success;
        try
        {
            (text, success) = await RunAsync(arguments, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped while the tool ran: the tool may already have changed something, so keep a record.
            audit.Write(new AuditEntry(DateTimeOffset.Now, "call", ledger.CurrentRequest, entry.Name, argumentsJson, entry.Risk.ToString(), decision, false));
            throw;
        }

        if (tracker.RecordResult(success) >= 2)
        {
            state.MustFinish = true;
        }

        audit.Write(new AuditEntry(DateTimeOffset.Now, "call", ledger.CurrentRequest, entry.Name, argumentsJson, entry.Risk.ToString(), decision, success));
        return Cut(CardNumberMasker.Mask(text));
    }

    private async Task<(string Text, bool Success)> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(entry.Timeout);
        try
        {
            var result = await base.InvokeCoreAsync(arguments, timeout.Token);
            return (TranscriptWriter.ObservationText(result), true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ($"Error: the tool did not finish in {(int)entry.Timeout.TotalSeconds} seconds.", false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = ex.Message.Length > MaxErrorChars ? ex.Message[..MaxErrorChars] : ex.Message;
            return ("Error: " + message, false);
        }
    }

    private static string Cut(string text) =>
        text.Length <= MaxObservationChars
            ? text
            : $"{text[..MaxObservationChars]}... (cut: showing {MaxObservationChars} of {text.Length} characters)";
}
