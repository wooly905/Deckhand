using System.Runtime.CompilerServices;
using System.Text.Json;
using WindowsAxon.Agent.Skills;
using WindowsAxon.Agent.Steps;
using Microsoft.Extensions.AI;

namespace WindowsAxon.Agent.Selection;

/// <summary>Shows the model only the base tools and the active skills and groups, and enforces the step limit.</summary>
public sealed class ToolScopeChatClient(IChatClient inner,
                                        ToolSelectionState state,
                                        SkillCatalog skills,
                                        IStepLimitPrompter prompter,
                                        bool scopeTools)
    : DelegatingChatClient(inner)
{
    public const string FinishMessage = "Give a final_answer now. Say what was done and what is left.";

    private readonly HashSet<string> _processedSkillCalls = [];

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
                                                              ChatOptions? options = null,
                                                              CancellationToken cancellationToken = default)
    {
        var (list, scoped) = await PrepareAsync(messages, options, cancellationToken);
        return await base.GetResponseAsync(list, scoped, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
                                                                                         ChatOptions? options = null,
                                                                                         [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (list, scoped) = await PrepareAsync(messages, options, cancellationToken);
        await foreach (var update in base.GetStreamingResponseAsync(list, scoped, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<(List<ChatMessage>, ChatOptions)> PrepareAsync(IEnumerable<ChatMessage> messages,
                                                                      ChatOptions? options,
                                                                      CancellationToken cancellationToken)
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
        foreach (var (key, call, result) in LoadSkillPairs(messages))
        {
            if (!_processedSkillCalls.Add(key))
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

    /// <summary>
    /// Pairs each load_skill call with the first later result that has the same call ID, in history order.
    /// Native mode takes call IDs from the server, and some servers reuse them (for example call_0) across turns,
    /// so the key is the call ID plus how many earlier load_skill pairs used the same ID.
    /// </summary>
    private static IEnumerable<(string Key,
                                FunctionCallContent Call,
                                FunctionResultContent Result)> LoadSkillPairs(List<ChatMessage> messages)
    {
        var pending = new Dictionary<string, Queue<FunctionCallContent>>();
        var seen = new Dictionary<string, int>();
        foreach (var content in messages.SelectMany(m => m.Contents))
        {
            switch (content)
            {
                case FunctionCallContent { Name: "load_skill" } call:
                    if (!pending.TryGetValue(call.CallId, out var queue))
                    {
                        pending[call.CallId] = queue = new Queue<FunctionCallContent>();
                    }

                    queue.Enqueue(call);
                    break;
                case FunctionResultContent result when pending.TryGetValue(result.CallId, out var waiting)
                && waiting.Count > 0:
                    var occurrence = seen.GetValueOrDefault(result.CallId);
                    seen[result.CallId] = occurrence + 1;
                    yield return ($"{result.CallId}#{occurrence}", waiting.Dequeue(), result);
                    break;
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
