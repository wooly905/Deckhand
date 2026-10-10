using System.Text.Json;
using Deckhand.Agent.Approval;
using Deckhand.Agent.Audit;
using Deckhand.Agent.Steps;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent;

public enum RunStatus
{
    Answered,
    AskedUser,
    Cancelled,
    Failed,
}

public sealed record RunResult(RunStatus Status, string Text, int Steps);

public enum ApprovalAnswer
{
    Deny,
    Allow,
    AllowForConversation,
}

/// <summary>Shows the approval card. M1c implements it in WPF.</summary>
public interface IApprovalPrompter
{
    Task<ApprovalAnswer> AskAsync(FunctionCallContent call, ApprovalDecision decision, CancellationToken cancellationToken);
}

/// <summary>Runs one user message: new request or ask_user reply, approvals, Stop.</summary>
public sealed class AgentRunner(DeckhandSession session, IApprovalPrompter approvals, IAuditLog audit)
{
    private const int MaxReasonChars = 200;

    public bool AwaitingReply => session.Selection.AwaitingReply;

    public async Task<RunResult> RunAsync(string text, bool forceNewRequest = false, CancellationToken cancellationToken = default)
    {
        var isReply = session.Selection.AwaitingReply && !forceNewRequest;
        if (!isReply)
        {
            session.Selection.StartRequest();
            session.Calls.Reset();
            session.Ledger.CurrentRequest = text;
        }

        session.Selection.AwaitingReply = false;
        var message = isReply ? new ChatMessage(ChatRole.User, text) : TranscriptWriter.NewRequest(text);

        try
        {
            var response = await session.Agent.RunAsync(message, session.Session, cancellationToken: cancellationToken);
            while (response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList() is { Count: > 0 } requests)
            {
                var answers = new List<AIContent>();
                foreach (var request in requests)
                {
                    answers.Add(await DecideAsync(request, cancellationToken));
                }

                response = await session.Agent.RunAsync(new ChatMessage(ChatRole.User, answers), session.Session, cancellationToken: cancellationToken);
            }

            var asked = session.Steps.LastFinalAction == StepSchemaBuilder.AskUser;
            session.Selection.AwaitingReply = asked;
            return new RunResult(asked ? RunStatus.AskedUser : RunStatus.Answered, response.Text, session.Selection.StepCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunStatus.Cancelled, "Stopped.", session.Selection.StepCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reason = ex.Message.Length > MaxReasonChars ? ex.Message[..MaxReasonChars] : ex.Message;
            return new RunResult(RunStatus.Failed, $"The model service is busy or not available. {reason}", session.Selection.StepCount);
        }
    }

    private async Task<AIContent> DecideAsync(ToolApprovalRequestContent request, CancellationToken cancellationToken)
    {
        var call = (FunctionCallContent)request.ToolCall;
        var decision = session.Policy.Decide(call);

        bool approved;
        string label;
        if (decision.Outcome == ApprovalOutcome.AutoApprove)
        {
            approved = true;
            label = "auto-approved";
        }
        else
        {
            var answer = await approvals.AskAsync(call, decision, cancellationToken);
            approved = answer != ApprovalAnswer.Deny;
            label = approved ? "approved" : "denied";
            if (answer == ApprovalAnswer.AllowForConversation && decision.AllowForConversation)
            {
                session.Policy.AllowForConversation(call.Name);
            }
        }

        session.Ledger.Record(call.CallId, approved);
        audit.Write(new AuditEntry(DateTimeOffset.Now, "approval", session.Ledger.CurrentRequest, call.Name,
            JsonSerializer.Serialize(call.Arguments), decision.AllowForConversation ? "Change" : "Danger", label, null));

        return request.CreateResponse(approved, approved ? null : "The user denied this action.");
    }
}
