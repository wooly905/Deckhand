using System.Runtime.CompilerServices;
using Deckhand.Agent.Settings;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent.Steps;

/// <summary>Changes native tool calling into one JSON step per model call, for small models.</summary>
public sealed class ReActStepChatClient(IChatClient inner, ArgsSchemaMode argsMode, TranscriptWriter writer, StepEvents events)
    : DelegatingChatClient(inner)
{
    private const string GiveUpAnswer = "Sorry, the model did not give a valid step. Try again, or select a different model in Settings.";

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var tools = options?.Tools?.OfType<AIFunctionDeclaration>().ToList() ?? [];
        var transcript = writer.Write(messages, options?.Instructions, tools);

        var innerOptions = options?.Clone() ?? new ChatOptions();
        innerOptions.Tools = null;
        innerOptions.ToolMode = null;
        innerOptions.Instructions = null; // already in the system message
        innerOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(StepSchemaBuilder.Build(tools, argsMode), "step");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await base.GetResponseAsync(transcript, innerOptions, cancellationToken);
            if (StepParser.TryParse(response.Text, out var step, out var error))
            {
                events.RecordValid(step!);
                return ToResponse(step!, response);
            }

            events.RecordInvalid();
            transcript.Add(new ChatMessage(ChatRole.Assistant, response.Text));
            transcript.Add(new ChatMessage(ChatRole.User, $"That reply was not a valid step: {error} Reply with one JSON step."));
        }

        events.RecordGiveUp();
        return new ChatResponse(FinalMessage("", StepSchemaBuilder.FinalAnswer, GiveUpAnswer));
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    private static ChatResponse ToResponse(Step step, ChatResponse raw)
    {
        var message = step.IsFinal
            ? FinalMessage(step.Thought, step.Action, step.Answer)
            : new ChatMessage(ChatRole.Assistant,
            [
                new TextReasoningContent(step.Thought),
                new FunctionCallContent(Guid.NewGuid().ToString("N"), step.Action, new Dictionary<string, object?>(step.Args)),
            ]);

        return new ChatResponse(message) { ModelId = raw.ModelId, Usage = raw.Usage, FinishReason = raw.FinishReason };
    }

    private static ChatMessage FinalMessage(string thought, string action, string answer) =>
        new(ChatRole.Assistant, [new TextReasoningContent(thought), new TextContent(answer)])
        {
            AdditionalProperties = new() { [TranscriptWriter.ActionKey] = action },
        };
}
