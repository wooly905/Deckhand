using Deckhand.Agent.Approval;
using Deckhand.Agent.Tools;

namespace Deckhand.Agent.Tests.Approval;

public class ApprovalPolicyTests
{
    private static ApprovalPolicy Policy(bool remote = false, bool autoApprove = false)
    {
        var catalog = new ToolCatalog();
        catalog.Add(AIFunctionFactory.Create(() => "", "wallpaper_set"), "personalize", RiskLevel.Change);
        catalog.Add(AIFunctionFactory.Create(() => "", "fs_delete"), "files", RiskLevel.Danger);
        catalog.Add(AIFunctionFactory.Create(() => "", "fs_read_text"), "files", RiskLevel.Read, policyDriven: true);
        return new ApprovalPolicy(catalog, [new SensitiveReadRule(remote, "api.example.com")], autoApprove);
    }

    private static FunctionCallContent Call(string name) => new("c1", name);

    [Theory]
    [InlineData("wallpaper_set")]
    [InlineData("fs_delete")]
    [InlineData("fs_read_text")]
    public void Auto_approve_runs_known_tools_without_asking(string tool)
    {
        Assert.Equal(ApprovalOutcome.AutoApprove, Policy(remote: true, autoApprove: true).Decide(Call(tool)).Outcome);
    }

    [Fact]
    public void Auto_approve_still_asks_for_an_unknown_tool()
    {
        Assert.Equal(ApprovalOutcome.Ask, Policy(autoApprove: true).Decide(Call("mystery")).Outcome);
    }

    [Fact]
    public void Auto_approve_is_the_default_setting()
    {
        Assert.True(new Deckhand.Agent.Settings.ChatSettings().AutoApproveTools);
    }

    [Fact]
    public void Change_tool_asks_and_offers_allow_for_conversation()
    {
        var decision = Policy().Decide(Call("wallpaper_set"));

        Assert.Equal(new ApprovalDecision(ApprovalOutcome.Ask, true, null), decision);
    }

    [Fact]
    public void Change_tool_allowed_for_the_conversation_is_approved_automatically()
    {
        var policy = Policy();
        policy.AllowForConversation("wallpaper_set");

        Assert.Equal(ApprovalOutcome.AutoApprove, policy.Decide(Call("wallpaper_set")).Outcome);

        policy.Reset();
        Assert.Equal(ApprovalOutcome.Ask, policy.Decide(Call("wallpaper_set")).Outcome);
    }

    [Fact]
    public void Danger_tool_asks_every_time()
    {
        var policy = Policy();
        policy.AllowForConversation("fs_delete");

        Assert.Equal(new ApprovalDecision(ApprovalOutcome.Ask, false, null), policy.Decide(Call("fs_delete")));
    }

    [Fact]
    public void Unknown_tool_asks_without_allow_always()
    {
        Assert.Equal(new ApprovalDecision(ApprovalOutcome.Ask, false, null), Policy().Decide(Call("mystery")));
    }

    [Fact]
    public void Sensitive_read_runs_without_approval_on_a_local_endpoint()
    {
        Assert.Equal(ApprovalOutcome.AutoApprove, Policy(remote: false).Decide(Call("fs_read_text")).Outcome);
    }

    [Fact]
    public void Sensitive_read_asks_on_a_remote_endpoint_and_names_the_host()
    {
        var policy = Policy(remote: true);

        var decision = policy.Decide(Call("fs_read_text"));

        Assert.Equal(ApprovalOutcome.Ask, decision.Outcome);
        Assert.True(decision.AllowForConversation);
        Assert.Equal("The content will be sent to api.example.com.", decision.Note);

        policy.AllowForConversation("fs_read_text");
        Assert.Equal(ApprovalOutcome.AutoApprove, policy.Decide(Call("fs_read_text")).Outcome);
    }
}
