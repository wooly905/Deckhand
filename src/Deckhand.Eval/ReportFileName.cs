namespace Deckhand.Eval;

/// <summary>Builds the report file name. Model names can contain '/', ':' and other chars that are not valid in a file name.</summary>
public static class ReportFileName
{
    private static readonly HashSet<char> Invalid = [.. Path.GetInvalidFileNameChars(), '/', '\\',':', '*', '?', '"', '<', '>', '|'];

    public static string For(string model, DateTime time) =>
        $"{time:yyyyMMdd-HHmmss}-{new string(model.Select(c => Invalid.Contains(c) ? '_' : c).ToArray())}.json";
}
