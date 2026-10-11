using Deckhand.Agent.Selection;
using Deckhand.Agent.Tools;

namespace Deckhand.Agent.Tests.Selection;

public class ToolSelectionStateTests
{
    private static ToolCatalog Catalog()
    {
        var catalog = new ToolCatalog();
        catalog.Add(AIFunctionFactory.Create(() => "", "app_find"), ToolCatalog.BaseGroup, RiskLevel.Read);
        return catalog;
    }

    private static IReadOnlyList<string> Tools(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => $"{prefix}_{i}").ToList();

    [Fact]
    public void Activated_tools_become_active_and_base_tools_are_always_active()
    {
        var state = new ToolSelectionState(Catalog(), 14);

        state.Activate("group:network", Tools("net", 3));

        Assert.True(state.IsActive("net_1"));
        Assert.True(state.IsActive("app_find"));
        Assert.True(state.IsActive("load_skill"));
        Assert.False(state.IsActive("fs_1"));
    }

    [Fact]
    public void Least_recently_used_set_is_removed_when_over_the_limit()
    {
        var state = new ToolSelectionState(Catalog(), 10);
        state.Activate("group:files", Tools("fs", 4));
        state.Activate("skill:find-large-files", Tools("disk", 4));
        state.MarkUsed("fs_1"); // files is now more recent than the skill

        var result = state.Activate("group:system", Tools("system", 4));

        Assert.Equal(["group:files", "group:system"], state.ActiveKeys.Order());
        Assert.Equal("skill:find-large-files", Assert.Single(result.Removed).Key);
        Assert.Equal("Enabled: system. Removed: find-large-files (disk_1, disk_2, disk_3, disk_4 are no longer available).",
                     result.Describe());
    }

    [Fact]
    public void Base_tools_in_a_skill_do_not_count_against_the_limit()
    {
        var state = new ToolSelectionState(Catalog(), 4);
        state.Activate("group:files", Tools("fs", 2));

        var result = state.Activate("skill:open-app", ["app_find", "x_1", "x_2"]);

        Assert.Empty(result.Removed);
    }

    [Fact]
    public void Activating_an_active_set_again_removes_nothing()
    {
        var state = new ToolSelectionState(Catalog(), 4);
        state.Activate("group:files", Tools("fs", 4));

        var result = state.Activate("group:files", Tools("fs", 4));

        Assert.Empty(result.Removed);
        Assert.Equal("Enabled: files.", result.Describe());
    }

    [Fact]
    public void Step_limit_is_10_and_sticky_25_after_app_control()
    {
        var state = new ToolSelectionState(Catalog(), 14);
        Assert.Equal(10, state.StepLimit);

        state.Activate("group:browser", Tools("browser", 11));
        state.Activate("group:files", Tools("fs", 9)); // removes browser

        Assert.DoesNotContain("group:browser", state.ActiveKeys);
        Assert.Equal(25, state.StepLimit);
    }

    [Fact]
    public void Skill_or_call_that_uses_app_control_tools_also_sets_the_sticky_limit()
    {
        var catalog = Catalog();
        catalog.Add(AIFunctionFactory.Create(() => "", "browser_open"), "browser", RiskLevel.Read);
        catalog.Add(AIFunctionFactory.Create(() => "", "ui_click"), "desktop_ui", RiskLevel.Change);

        var bySkill = new ToolSelectionState(catalog, 14);
        bySkill.Activate("skill:open-website", ["browser_open"]);
        Assert.Equal(25, bySkill.StepLimit);

        var byCall = new ToolSelectionState(catalog, 14) { AllToolsActive = true };
        Assert.True(byCall.IsActive("ui_click"));
        byCall.MarkUsed("ui_click");
        Assert.Equal(25, byCall.StepLimit);
    }

    [Fact]
    public void Extension_adds_10_steps_one_time()
    {
        var state = new ToolSelectionState(Catalog(), 14);

        state.Extend();

        Assert.Equal(20, state.StepLimit);
        Assert.True(state.ExtensionUsed);
        Assert.Throws<InvalidOperationException>(state.Extend);
    }

    [Fact]
    public void StartRequest_resets_the_request()
    {
        var state = new ToolSelectionState(Catalog(), 14);
        state.Activate("group:browser", Tools("browser", 2));
        state.CountStep();
        state.Extend();
        state.MustFinish = true;
        state.AwaitingReply = true;

        state.StartRequest();

        Assert.Empty(state.ActiveKeys);
        Assert.Equal(0, state.StepCount);
        Assert.Equal(10, state.StepLimit);
        Assert.False(state.ExtensionUsed);
        Assert.False(state.MustFinish);
        Assert.False(state.AwaitingReply);
    }

    [Fact]
    public void Activation_history_keeps_every_key_until_a_new_request()
    {
        var state = new ToolSelectionState(Catalog(), 4);
        state.Activate("group:files", Tools("fs", 4));
        state.Activate("group:network", Tools("net", 4)); // removes files

        Assert.Equal(["group:files", "group:network"], state.ActivationHistory);

        state.StartRequest();
        Assert.Empty(state.ActivationHistory);
    }
}
