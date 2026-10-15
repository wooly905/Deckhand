using System.Diagnostics;
using System.Security.Cryptography;

namespace WindowsAxon.App.Updates;

/// <summary>Downloads a verified release executable and swaps it in place of the running one.</summary>
public sealed class UpdateInstaller(HttpClient http)
{
    public async Task<string> DownloadAsync(ReleaseInfo release, string folder, CancellationToken cancellationToken)
    {
        string expected;
        using (var request = CreateRequest(release.Sha256Url))
        using (var response = await http.SendAsync(request, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            expected = ParseSha256(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(release.ExeName));
        try
        {
            using (var request = CreateRequest(release.ExeUrl))
            using (var response = await http.SendAsync(request,
                                                       HttpCompletionOption.ResponseHeadersRead,
                                                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(target);
                await response.Content.CopyToAsync(file, cancellationToken);
            }

            string actual;
            await using (var file = File.OpenRead(target))
            {
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded file does not match its SHA-256 checksum.");
            }

            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>Reads the hash from a "&lt;hash&gt;  &lt;file name&gt;" checksum file.</summary>
    public static string ParseSha256(string text)
    {
        var hash = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The checksum file does not contain a SHA-256 hash.");
        }

        return hash.ToLowerInvariant();
    }

    /// <summary>
    /// True for the published single-file executable. A build-output exe sits beside its DLLs and must not be
    /// replaced by the single-file release.
    /// </summary>
    public static bool IsSingleFile(string executablePath) =>
        !File.Exists(Path.Combine(Path.GetDirectoryName(executablePath)!, "WindowsAxon.App.dll"));

    /// <summary>
    /// Windows can rename a running executable but not overwrite it, so the current file moves aside to
    /// "&lt;exe&gt;.old" and the new file takes its name. The original file is restored on failure.
    /// </summary>
    public static void Replace(string currentExe, string newExe)
    {
        var staged = $"{currentExe}.new";
        var old = $"{currentExe}.old";
        File.Copy(newExe, staged, overwrite: true);
        try
        {
            File.Delete(old);
            File.Move(currentExe, old);
            try
            {
                File.Move(staged, currentExe);
            }
            catch
            {
                File.Move(old, currentExe, overwrite: true);
                throw;
            }
        }
        finally
        {
            TryDelete(staged);
        }

        TryDelete(newExe);
    }

    /// <summary>Deletes the previous executable left by an update; it stays locked until that process exits.</summary>
    public static void CleanupOld(string currentExe) => TryDelete($"{currentExe}.old");

    /// <summary>Replaces the executable and starts it; if the start fails, the original executable is restored.</summary>
    public static void Install(string currentExe, string newExe, Action<string> launch)
    {
        Replace(currentExe, newExe);
        try
        {
            launch(currentExe);
        }
        catch
        {
            File.Move($"{currentExe}.old", currentExe, overwrite: true);
            throw;
        }
    }

    public static void Launch(string executablePath, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("WindowsAxon");
        return request;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
