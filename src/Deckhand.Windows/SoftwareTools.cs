using System.ComponentModel;
using Deckhand.Agent.Approval;
using Deckhand.Agent.Tools;
using Microsoft.Extensions.AI;

namespace Deckhand.Windows;

public sealed class SoftwareTools : IApprovalRule
{
    [WindowsTool("winget_search", "apps", timeoutSeconds: 60)]
    [Description("Search the winget source for software. Does not install packages.")]
    public Task<string> Search(string query, CancellationToken cancellationToken) => WindowsCommand.RunAsync("winget.exe",
                                                                                                             [
"search",
        query,
        "--source",
        "winget",
        "--accept-source-agreements",
        "--disable-interactivity"
],
                                                                                                             cancellationToken);

    [WindowsTool("winget_install", "apps", RiskLevel.Danger, policyDriven: true, timeoutSeconds: 300)]
    [Description("Install an exact winget package ID for the current user. Always requires approval. Accepts package and source agreements. Does not request administrator rights or override hash checks. No undo.")]
    public Task<string> Install(string id, CancellationToken cancellationToken) => WindowsCommand.RunAsync("winget.exe",
                                                                                                           [
"install",
        "--id",
        id,
        "--exact",
        "--source",
        "winget",
        "--scope",
        "user",
        "--silent",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--disable-interactivity"
],
                                                                                                           cancellationToken);

    public bool AppliesTo(string toolName) => toolName == "winget_install";

    public ApprovalDecision Decide(FunctionCallContent call)
    {
        var id = call.Arguments is { } arguments
            && arguments.TryGetValue("id", out var value) ? value?.ToString() ?? "" : "";
        string details;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            details = WindowsCommand.RunAsync("winget.exe",
                                              [
"show",
                "--id",
                id,
                "--exact",
                "--source",
                "winget",
                "--accept-source-agreements",
                "--disable-interactivity"
],
                                              timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            details = "Package metadata unavailable: " + error.Message;
        }
        return new ApprovalDecision(ApprovalOutcome.Ask,
                                    false,
                                    $"Install {id}, source winget, current user. This accepts package and source agreements. No undo.\n{details}");
    }
}
