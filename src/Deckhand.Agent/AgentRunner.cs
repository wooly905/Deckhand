using System.Text.Json;
using Deckhand.Agent.Approval;
using Deckhand.Agent.Audit;
using Deckhand.Agent.Steps;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent;

public sealed record RunResult(RunStatus Status, string Text, int Steps);

/// <summary>Runs one user message: new request or ask_user reply, approvals, Stop.</summary>
public sealed class AgentRunner(DeckhandSession session, IApprovalPrompter approvals, IAuditLog audit)
{
    private const int MaxReasonChars = 200;
    private const string InterruptedReason = "The request was stopped before this action finished.";
    private const string ContinueAfterEmptyResponse =
        "Your last response was empty. Continue the current request using the available tools if needed, "
        + "then give a non-empty answer. Do not repeat actions that already succeeded.";
    private const string EmptyResponseError =
        "The model returned an empty response twice. The request could not be completed. Try again or select a different model in Settings.";

    // Approval requests left in the session history by a run that was stopped or failed.
    // They are answered (denied) at the start of the next run, or the chat client rejects the history.
    private List<ToolApprovalRequestContent>? _unanswered;

    public bool AwaitingReply => session.Selection.AwaitingReply;

    public async Task<RunResult> RunAsync(string text,
                                          bool forceNewRequest = false,
                                          CancellationToken cancellationToken = default)
    {
        var isReply = session.Selection.AwaitingReply && !forceNewRequest;
        if (!isReply)
        {
            session.Selection.StartRequest();
            session.Calls.Reset();
            session.Ledger.CurrentRequest = text;
        }

        session.Selection.AwaitingReply = false;
        var messages = new List<ChatMessage>();
        if (_unanswered is { Count: > 0 } unanswered)
        {
            messages.Add(new ChatMessage(ChatRole.User,
                                         unanswered.Select(r => (AIContent)r.CreateResponse(false,
                                                                                            InterruptedReason))
                .ToList()));
        }

        messages.Add(isReply ? new ChatMessage(ChatRole.User, text) : TranscriptWriter.NewRequest(text));

        try
        {
            var response = await session.Agent.RunAsync(messages, session.Session, cancellationToken: cancellationToken);
            _unanswered = null;
            var retriedEmptyResponse = false;
            while (true)
            {
                while (response.Messages.SelectMany(m => m.Contents)
                    .OfType<ToolApprovalRequestContent>()
                    .ToList() is { Count: > 0 } requests)
                {
                    _unanswered = requests;
                    var answers = new List<AIContent>();
                    foreach (var request in requests)
                    {
                        answers.Add(await DecideAsync(request, cancellationToken));
                    }

                    response = await session.Agent.RunAsync(new ChatMessage(ChatRole.User, answers),
                                                            session.Session,
                                                            cancellationToken: cancellationToken);
                    _unanswered = null;
                }

                if (string.IsNullOrWhiteSpace(response.Text))
                {
                    audit.Write(new AuditEntry(DateTimeOffset.Now,
                                               "response",
                                               session.Ledger.CurrentRequest,
                                               "",
                                               "{}",
                                               "None",
                                               null,
                                               false,
                                               Status: "empty",
                                               Result: response.Text,
                                               Error: retriedEmptyResponse
                                                   ? EmptyResponseError : "Empty model answer; continuing the current request once."));
                    if (retriedEmptyResponse)
                    {
                        return new RunResult(RunStatus.Failed, EmptyResponseError, session.Selection.StepCount);
                    }

                    // Continue the existing session so tool results, approvals, and active groups are retained.
                    retriedEmptyResponse = true;
                    response = await session.Agent.RunAsync(new ChatMessage(ChatRole.User, ContinueAfterEmptyResponse),
                                                            session.Session,
                                                            cancellationToken: cancellationToken);
                    continue;
                }

                var asked = session.Steps.LastFinalAction == StepSchemaBuilder.AskUser;
                session.Selection.AwaitingReply = asked;
                return new RunResult(asked ? RunStatus.AskedUser : RunStatus.Answered,
                                     response.Text,
                                     session.Selection.StepCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunStatus.Cancelled, "Stopped.", session.Selection.StepCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // An OperationCanceledException without the caller's token is a model-side timeout.
            var reason = ex.Message.Length > MaxReasonChars ? ex.Message[..MaxReasonChars] : ex.Message;
            return new RunResult(RunStatus.Failed,
                                 $"The model service is busy or not available. {reason}",
                                 session.Selection.StepCount);
        }
    }

    private async Task<AIContent> DecideAsync(ToolApprovalRequestContent request, CancellationToken cancellationToken)
    {
        var call = (FunctionCallContent)request.ToolCall;
        var decision = await Task.Run(() => session.Policy.Decide(call), cancellationToken)
            .WaitAsync(cancellationToken);

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
        audit.Write(new AuditEntry(DateTimeOffset.Now,
                                   "approval",
                                   session.Ledger.CurrentRequest,
                                   call.Name,
                                   JsonSerializer.Serialize(call.Arguments),
                                   session.Policy.RiskOf(call.Name)?.ToString() ?? "Unknown",
                                   label,
                                   null,
                                   CallId: call.CallId,
                                   Status: label));

        return request.CreateResponse(approved, approved ? null : "The user denied this action.");
    }
}
