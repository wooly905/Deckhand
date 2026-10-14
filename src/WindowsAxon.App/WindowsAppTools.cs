using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using WindowsAxon.Agent.Approval;
using WindowsAxon.Agent.Tools;
using Microsoft.Extensions.AI;

namespace WindowsAxon.App;

internal sealed record InstalledApp(string Id, string Name);

internal sealed class WindowsAppTools : IApprovalRule
{
    private readonly Func<IReadOnlyList<InstalledApp>> _readIndex;
    private readonly Func<InstalledApp, CancellationToken, Task<string>> _launch;
    private IReadOnlyList<InstalledApp> _apps;

    public WindowsAppTools() : this(ReadIndex, LaunchWindowsAppAsync)
    {
    }

    internal WindowsAppTools(Func<IReadOnlyList<InstalledApp>> readIndex,
                             Func<InstalledApp, CancellationToken, Task<string>> launch)
    {
        _readIndex = readIndex;
        _launch = launch;
        _apps = readIndex();
    }

    public ToolCatalog BuildCatalog()
    {
        var catalog = new ToolCatalog();
        catalog.Add(AIFunctionFactory.Create(Find,
                                             "app_find",
                                             "Find installed applications by name. Returns up to five matches with IDs."),
                    ToolCatalog.BaseGroup,
                    RiskLevel.Read);
        catalog.Add(AIFunctionFactory.Create(LaunchAsync,
                                             "app_launch",
                                             "Start an installed application using an ID returned by app_find. Files and command-line arguments are not supported. A launch request alone does not confirm a window opened."),
                    ToolCatalog.BaseGroup,
                    RiskLevel.Read,
                    policyDriven: true);
        catalog.Add(AIFunctionFactory.Create(Refresh, "app_refresh_index", "Refresh the installed application index."),
                    ToolCatalog.BaseGroup,
                    RiskLevel.Read);
        catalog.Add(AIFunctionFactory.Create(List,
                                             "app_list",
                                             "List up to 50 indexed applications, optionally filtering by name."),
                    "apps",
                    RiskLevel.Read);
        return catalog;
    }

    internal IReadOnlyList<InstalledApp> Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Enter an application name.");
        }

        name = name.Trim();
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _apps.Select(app => (App: app, Score: app.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || name.Equals("paint", StringComparison.OrdinalIgnoreCase)
            && app.Id.StartsWith("Microsoft.Paint_",
                                         StringComparison.OrdinalIgnoreCase)
            ? 0 : tokens.All(token => app.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) ? 1 : 2))
            .Where(match => match.Score < 2)
            .OrderBy(match => match.Score)
            .ThenBy(match => match.App.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(match => match.App)
            .ToList();
    }

    internal Task<string> LaunchAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var app = _apps.FirstOrDefault(app => app.Id.Equals(id, StringComparison.Ordinal));
        if (app is null)
        {
            throw new ArgumentException("The application ID is not in the installed app index. Use app_find first.");
        }
        return _launch(app, cancellationToken);
    }

    internal string Refresh()
    {
        _apps = _readIndex();
        return $"Indexed {_apps.Count} installed applications.";
    }

    public bool AppliesTo(string toolName) => toolName == "app_launch";

    internal IReadOnlyList<InstalledApp> List(string name = "") => _apps.Where(app => app.Name.Contains(name,
                                                                                                        StringComparison.OrdinalIgnoreCase))
        .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
        .Take(50)
        .ToArray();

    public ApprovalDecision Decide(FunctionCallContent call)
    {
        var idValue = call.Arguments is { } arguments
            && arguments.TryGetValue("id", out var argument) ? argument : null;
        var id = idValue switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } value => value.GetString(),
            _ => null,
        };
        return call.Arguments?.Count == 1 && _apps.Any(app => app.Id == id)
            ? new ApprovalDecision(ApprovalOutcome.AutoApprove, false, null)
            : new ApprovalDecision(ApprovalOutcome.Ask,
                                   false,
                                   "Only an installed application ID without extra arguments can be opened automatically.");
    }

    private static IReadOnlyList<InstalledApp> ReadIndex()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Installed application tools require Windows.");
        }

        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
        dynamic? folder = null;
        dynamic? items = null;
        try
        {
            folder = shell.Namespace("shell:AppsFolder") ?? throw new InvalidOperationException("Windows could not read the installed app index.");
            items = folder.Items();
            var apps = new List<InstalledApp>();
            for (var index = 0; index < (int)items.Count; index++)
            {
                dynamic item = items.Item(index);
                try
                {
                    var id = (string)item.Path;
                    var name = (string)item.Name;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    {
                        apps.Add(new InstalledApp(id, name));
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            return apps.DistinctBy(app => app.Id).ToList();
        }
        finally
        {
            if (items is not null)
            {
                Marshal.ReleaseComObject(items);
            }

            if (folder is not null)
            {
                Marshal.ReleaseComObject(folder);
            }

            Marshal.ReleaseComObject(shell);
        }
    }

    private static async Task<string> LaunchWindowsAppAsync(InstalledApp app, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Application launch requires Windows.");
        }

        if (!app.Id.Contains('!'))
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
            try
            {
                shell.ShellExecute($"shell:AppsFolder\\{app.Id}", "", "", "open", 1);
                return $"Launch requested for {app.Name}. Windows accepted the request; its window has not been verified.";
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }

        var manager = (IApplicationActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"),
                                                                                                    throwOnError: true)!)!;
        uint processId;
        try
        {
            Marshal.ThrowExceptionForHR(manager.ActivateApplication(app.Id, "", 2, out processId));
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }

        using var process = Process.GetProcessById(checked((int)processId));
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited)
            {
                break;
            }

            if (process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
            {
                return $"Opened {app.Name}. Process ID: {processId}. A visible application window was verified.";
            }
            await Task.Delay(100, cancellationToken);
        }
        throw new InvalidOperationException($"Windows started {app.Name}, but a visible window could not be verified. Do not report that it is open.");
    }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                                [MarshalAs(UnmanagedType.LPWStr)] string arguments,
                                uint options,
                                out uint processId);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
}
