
namespace WindowsAxon.Agent.Tests.Fakes;

public static class RepoPaths
{
    public static string File(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "WindowsAxon.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."),
                            relative);
    }
}
