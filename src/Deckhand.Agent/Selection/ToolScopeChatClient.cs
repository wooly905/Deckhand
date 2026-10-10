using System.Runtime.CompilerServices;
using System.Text.Json;
using Deckhand.Agent.Skills;
using Deckhand.Agent.Steps;
using Microsoft.Extensions.AI;

namespace Deckhand.Agent.Selection;

/// <summary>Asks the user if the run may continue after the step limit.</summary>
public interface IStepLimitPrompter
{
    Task<bool> AskToContinueAsync(int stepsUsed, CancellationToken cancellationToken);
}

/// <summary>Shows the model only the base tools and the active skills and groups, and enforces the step limit.</summary>
public sealed class ToolScopeChatClient(IChatClient inner, ToolSelectionState state, SkillCatalog skills, IStepLimitPrompter prompter, bool scopeTools)
    : DelegatingChatClient(inner)
{
    public const string FinishMessage = "Give a final_answer now. Say what was done and what is left.";

    private readonly HashSet<string> _processedSkillCalls = [];

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var (list, scoped) = await PrepareAsync(messages, options, cancellationToken);
        return await base.GetResponseAsync(list, scoped, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (list, scoped) = await PrepareAsync(messages, options, cancellationToken);
        await foreach (var update in base.GetStreamingResponseAsync(list, scoped, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<(List<ChatMessage>, ChatOptions)> PrepareAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        var list = messages.ToList();
        ActivateLoadedSkills(list);

        if (!state.MustFinish && state.StepCount >= state.StepLimit)
        {
            if (!state.ExtensionUsed && await prompter.AskToContinueAsync(state.StepCount, cancellationToken))
            {
                state.Extend();
            }
            else
            {
                state.MustFinish = true;
            }
        }

        var scoped = options?.Clone() ?? new ChatOptions();
        if (state.MustFinish)
        {
            scoped.Tools = null;
            list.Add(new ChatMessage(ChatRole.User, FinishMessage));
        }
        else if (scopeTools && scoped.Tools is not null)
        {
            scoped.Tools = scoped.Tools.Where(t => state.IsActive(t.Name)).ToList();
        }

        state.CountStep();
        return (list, scoped);
    }

    private void ActivateLoadedSkills(List<ChatMessage> messages)
    {
        var results = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToDictionary(r => r.CallId);
        foreach (var call in messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Where(c => c.Name == "load_skill"))
        {
            if (!results.TryGetValue(call.CallId, out var result) || !_processedSkillCalls.Add(call.CallId))
            {
                continue;
            }

            var skill = SkillName(call) is { } name ? skills.Find(name) : null;
            if (skill is null)
            {
                continue;
            }

            var activation = state.Activate($"skill:{skill.Name}", skill.Tools);
            if (activation.Removed.Count > 0)
            {
                result.Result = TranscriptWriter.ObservationText(result.Result) + "\n\n" + activation.Describe();
            }
        }
    }

    /// <summary>The skill name is the first string argument of load_skill (MAF names it skillName or name).</summary>
    private static string? SkillName(FunctionCallContent call) =>
        call.Arguments?.Values.Select(v => v switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => null,
        }).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
}
