using System.Text.Json;
using Deckhand.Agent.Approval;
using Deckhand.App;

namespace Deckhand.Agent.Tests.Tools;

public sealed class WindowsAppToolsTests
{
    private static readonly InstalledApp Paint = new("Microsoft.Paint_8wekyb3d8bbwe!App", "小畫家");

    [Fact]
    public void English_paint_matches_the_localized_installed_application()
    {
        var tools = new WindowsAppTools(() => [Paint], (_, _) => Task.FromResult("opened"));

        Assert.Equal(Paint, Assert.Single(tools.Find("PAINT")));
        Assert.Equal(Paint, Assert.Single(tools.Find("小畫家")));
        Assert.Empty(tools.Find("not-installed"));
    }

    [Fact]
    public void Search_limits_results_and_prefers_an_exact_name()
    {
        var apps = Enumerable.Range(1, 8).Select(i => new InstalledApp($"app-{i}", $"Paint utility {i}")).Append(Paint)
            .ToArray();
        var tools = new WindowsAppTools(() => apps, (_, _) => Task.FromResult("opened"));

        var matches = tools.Find("paint");

        Assert.Equal(5, matches.Count);
        Assert.Equal(Paint, matches[0]);
    }

    [Fact]
    public void Refresh_discovers_a_newly_installed_application()
    {
        IReadOnlyList<InstalledApp> apps = [];
        var tools = new WindowsAppTools(() => apps, (_, _) => Task.FromResult("opened"));
        Assert.Empty(tools.Find("paint"));
        apps = [Paint];

        tools.Refresh();

        Assert.Equal(Paint, Assert.Single(tools.Find("paint")));
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("https://example.com")]
    [InlineData("Microsoft.Paint_8wekyb3d8bbwe!DifferentApp")]
    public async Task Launch_refuses_unindexed_targets(string id)
    {
        var called = false;
        var tools = new WindowsAppTools(() => [Paint],
                                        (_, _) =>
                                        {
                                            called = true;
                                            return Task.FromResult("opened");
                                        });

        await Assert.ThrowsAsync<ArgumentException>(() => tools.LaunchAsync(id, TestContext.Current.CancellationToken));

        Assert.False(called);
    }

    [Fact]
    public async Task Launch_uses_the_index_entry_and_returns_the_actual_launcher_result()
    {
        InstalledApp? launched = null;
        var tools = new WindowsAppTools(() => [Paint],
                                        (app, _) =>
                                        {
                                            launched = app;
                                            return Task.FromResult("Launch requested; no verified window.");
                                        });

        var result = await tools.LaunchAsync(Paint.Id, TestContext.Current.CancellationToken);

        Assert.Equal(Paint, launched);
        Assert.Equal("Launch requested; no verified window.", result);
    }

    [Fact]
    public async Task Launch_errors_are_not_replaced_with_a_success_response()
    {
        var tools = new WindowsAppTools(() => [Paint],
                                        (_, _) => throw new InvalidOperationException("Activation failed."));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => tools.LaunchAsync(Paint.Id,
                                                                                                TestContext.Current.CancellationToken));

        Assert.Equal("Activation failed.", error.Message);
    }

    [Fact]
    public async Task Cancellation_prevents_launch()
    {
        var called = false;
        var tools = new WindowsAppTools(() => [Paint],
                                        (_, _) =>
                                        {
                                            called = true;
                                            return Task.FromResult("opened");
                                        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.LaunchAsync(Paint.Id, cancellation.Token));

        Assert.False(called);
    }

    [Fact]
    public void Only_an_indexed_id_without_extra_arguments_is_auto_approved()
    {
        var tools = new WindowsAppTools(() => [Paint], (_, _) => Task.FromResult("opened"));
        var plain = new FunctionCallContent("1", "app_launch", new Dictionary<string, object?> { ["id"] = Paint.Id });
        var json = new FunctionCallContent("2",
                                           "app_launch",
                                           new Dictionary<string, object?> { ["id"] = JsonSerializer.SerializeToElement(Paint.Id) });
        var extra = new FunctionCallContent("3",
                                            "app_launch",
                                            new Dictionary<string, object?>
                                            {
                                                ["id"] = Paint.Id,
                                                ["arguments"] = "/untrusted"
                                            });
        var unknown = new FunctionCallContent("4", "app_launch", new Dictionary<string, object?> { ["id"] = "cmd.exe" });

        Assert.Equal(ApprovalOutcome.AutoApprove, tools.Decide(plain).Outcome);
        Assert.Equal(ApprovalOutcome.AutoApprove, tools.Decide(json).Outcome);
        Assert.Equal(ApprovalOutcome.Ask, tools.Decide(extra).Outcome);
        Assert.Equal(ApprovalOutcome.Ask, tools.Decide(unknown).Outcome);
    }
}
