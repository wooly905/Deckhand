using WindowsAxon.Eval;

namespace WindowsAxon.Agent.Tests.Eval;

public class EvalCaseTests
{
    [Fact]
    public void Cases_load_from_yaml_with_snake_case_keys()
    {
        var folder = Directory.CreateTempSubdirectory("windowsaxon-cases-");
        Directory.CreateDirectory(Path.Combine(folder.FullName, "cases"));
        Directory.CreateDirectory(Path.Combine(folder.FullName, "fixtures"));
        File.WriteAllText(Path.Combine(folder.FullName, "fixtures", "cfg.txt"), "no gateway");
        File.WriteAllText(Path.Combine(folder.FullName, "cases", "a.yaml"),
                          """
            - id: net-001
              request: "wifi broken"
              expect_skill: wifi-no-internet
              expect_tools_any: [net_get_config, net_ping]
              forbid_tools: [script_create]
              expect_args:
                net_ping: { host: "1.1.1.1" }
              mock:
                net_get_config: "fixtures/cfg.txt"
                net_ping: "timeout"
              expect_answer_contains: ["gateway"]
              replies: ["yes"]
            """);

        var c = Assert.Single(EvalCase.LoadFolder(Path.Combine(folder.FullName, "cases")));

        Assert.Equal("net-001", c.Id);
        Assert.Equal("wifi-no-internet", c.ExpectSkill);
        Assert.Equal(["net_get_config", "net_ping"], c.ExpectToolsAny);
        Assert.Equal("1.1.1.1", c.ExpectArgs["net_ping"]["host"]);
        Assert.Equal(["yes"], c.Replies);
        var mocks = c.ResolveMocks(folder.FullName);
        Assert.Equal("no gateway", mocks["net_get_config"]);
        Assert.Equal("timeout", mocks["net_ping"]);
    }
}
