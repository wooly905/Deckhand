using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Deckhand.Eval;

public sealed class EvalCase
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public string Id { get; set; } = "";

    public string Request { get; set; } = "";

    public string? ExpectSkill
    {
        get; set;
    }

    public string? ExpectGroup
    {
        get; set;
    }

    public List<string> ExpectToolsAny { get; set; } = [];

    public List<string> ExpectToolsAll { get; set; } = [];

    public List<string> ForbidTools { get; set; } = [];

    public Dictionary<string, Dictionary<string, string>> ExpectArgs { get; set; } = [];

    public Dictionary<string, string> Mock { get; set; } = [];

    public List<string> ExpectAnswerContains { get; set; } = [];

    /// <summary>Answers to ask_user questions, used in order.</summary>
    public List<string> Replies { get; set; } = [];

    public static List<EvalCase> LoadFolder(string folder) =>
        Directory.EnumerateFiles(folder, "*.yaml").Order(StringComparer.Ordinal)
        .SelectMany(file => Yaml.Deserialize<List<EvalCase>>(File.ReadAllText(file)) ?? [])
        .ToList();

    /// <summary>A mock value that names a file under the eval folder is replaced with the file content.</summary>
    public IReadOnlyDictionary<string, string> ResolveMocks(string evalRoot)
    {
        return Mock.ToDictionary(m => m.Key,
                                 m =>
                                 {
                                     var path = Path.Combine(evalRoot, m.Value.Replace('/', Path.DirectorySeparatorChar));
                                     return File.Exists(path) ? File.ReadAllText(path) : m.Value;
                                 });
    }
}
