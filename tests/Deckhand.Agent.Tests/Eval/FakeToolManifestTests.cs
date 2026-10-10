using Deckhand.Agent.Tools;
using Deckhand.Eval;

namespace Deckhand.Agent.Tests.Eval;

public class FakeToolManifestTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Deckhand.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, relative);
    }

    [Fact]
    public void Manifest_loads_and_builds_a_catalog_with_groups_and_risk()
    {
        var specs = FakeToolManifest.Load(RepoFile("eval/tools.json"));
        var catalog = FakeToolManifest.BuildCatalog(specs, new Dictionary<string, string>(), []);

        Assert.Equal(RiskLevel.Change, catalog.Find("wallpaper_set")!.Risk);
        Assert.True(catalog.Find("app_launch")!.PolicyDriven);
        Assert.True(catalog.IsBaseTool("app_find"));
        Assert.Contains("network", catalog.Groups);
    }

    [Fact]
    public async Task Fake_tool_records_the_call_and_returns_the_mock_output()
    {
        var specs = FakeToolManifest.Load(RepoFile("eval/tools.json"));
        var calls = new List<ToolCallRecord>();
        var catalog = FakeToolManifest.BuildCatalog(specs, new Dictionary<string, string> { ["net_ping"] = "timeout" }, calls);

        var result = await catalog.Find("net_ping")!.Function.InvokeAsync(new AIFunctionArguments { ["host"] = "1.1.1.1" }, TestContext.Current.CancellationToken);

        Assert.Equal("timeout", result?.ToString());
        var call = Assert.Single(calls);
        Assert.Equal("1.1.1.1", call.Args["host"]);
        Assert.Equal("object", catalog.Find("net_ping")!.Function.JsonSchema.GetProperty("type").GetString());
    }

    [Fact]
    public void Every_skill_tool_exists_in_the_manifest()
    {
        var names = FakeToolManifest.Load(RepoFile("eval/tools.json")).Select(s => s.Name).ToHashSet();
        var skills = Deckhand.Agent.Skills.SkillCatalog.Load([(RepoFile("skills"), true)]);

        Assert.Empty(skills.Problems);
        Assert.All(skills.Skills.SelectMany(s => s.Tools), tool => Assert.Contains(tool, names));
    }
}
