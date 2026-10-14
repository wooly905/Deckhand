using WindowsAxon.Agent.Skills;

namespace WindowsAxon.Agent.Tests.Skills;

public class SkillCatalogTests
{
    private const string Wifi = """
        ---
        name: wifi-no-internet
        description: Diagnose a connection that shows connected but has no internet.
        metadata:
          tools: "net_get_config net_ping  net_dns_lookup settings_open"
        ---
        # Steps
        1. Run net_get_config.
        """;

    [Fact]
    public void Parse_reads_name_description_and_tools()
    {
        var skill = SkillCatalog.Parse(Wifi, @"C:\skills\wifi-no-internet", builtIn: true, out var error);

        Assert.Null(error);
        Assert.Equal("wifi-no-internet", skill!.Name);
        Assert.StartsWith("Diagnose", skill.Description);
        Assert.Equal(["net_get_config", "net_ping", "net_dns_lookup", "settings_open"], skill.Tools);
        Assert.True(skill.BuiltIn);
    }

    [Fact]
    public void Folder_name_must_match_the_skill_name()
    {
        var skill = SkillCatalog.Parse(Wifi, @"C:\skills\wifi", builtIn: true, out var error);

        Assert.Null(skill);
        Assert.Contains("folder", error);
    }

    [Theory]
    [InlineData("# no frontmatter")]
    [InlineData("---\ndescription: x\n---\nbody")]
    [InlineData("---\nname: [unclosed\n---\nbody")]
    public void Invalid_frontmatter_is_reported(string text)
    {
        var skill = SkillCatalog.Parse(text, @"C:\skills\x", builtIn: false, out var error);

        Assert.Null(skill);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Skill_without_tools_has_an_empty_list()
    {
        var skill = SkillCatalog.Parse("---\nname: calc\ndescription: Calculate.\n---\nbody",
                                       @"C:\skills\calc",
                                       builtIn: true,
                                       out _);

        Assert.Empty(skill!.Tools);
    }

    [Fact]
    public void Load_reads_roots_in_order_and_the_first_name_wins()
    {
        var builtIn = Directory.CreateTempSubdirectory("windowsaxon-builtin-");
        var user = Directory.CreateTempSubdirectory("windowsaxon-user-");
        Write(builtIn.FullName, "wifi-no-internet", Wifi);
        Write(user.FullName, "wifi-no-internet", Wifi.Replace("Diagnose", "Evil"));
        Write(user.FullName, "bad", "no frontmatter");

        var catalog = SkillCatalog.Load([
(builtIn.FullName, true),
            (user.FullName, false),
            (@"C:\does\not\exist", false)
]);

        var skill = Assert.Single(catalog.Skills);
        Assert.True(skill.BuiltIn);
        Assert.StartsWith("Diagnose", catalog.Find("wifi-no-internet")!.Description);
        Assert.Equal(2, catalog.Problems.Count); // duplicate + bad
    }

    private static void Write(string root, string folder, string text)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, folder));
        File.WriteAllText(Path.Combine(dir.FullName, "SKILL.md"), text);
    }
}
