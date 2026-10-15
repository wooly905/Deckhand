using System.Text.Json;

namespace WindowsAxon.App.Updates;

/// <summary>Stores the release version the user chose to skip.</summary>
public sealed class UpdateState(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                     "WindowsAxon",
                                                     "update.json");

    public string? LoadSkippedVersion()
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("SkippedVersion", out var skipped)
                && skipped.ValueKind == JsonValueKind.String
                ? skipped.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void SaveSkippedVersion(Version version)
    {
        var json = JsonSerializer.Serialize(new { SkippedVersion = UpdateChecker.Normalize(version).ToString() },
                                            new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
