using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace WindowsAxon.Agent.Skills;

public sealed record SkillInfo(string Name,
                               string Description,
                               IReadOnlyList<string> Tools,
                               string Folder,
                               bool BuiltIn);

/// <summary>Reads the frontmatter of each SKILL.md: name, description, and the WindowsAxon field metadata.tools.</summary>
public sealed partial class SkillCatalog
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    private readonly List<SkillInfo> _skills = [];
    private readonly List<string> _problems = [];

    public IReadOnlyList<SkillInfo> Skills => _skills;

    public IReadOnlyList<string> Problems => _problems;

    public SkillInfo? Find(string name) => _skills.FirstOrDefault(s => s.Name == name);

    public static SkillCatalog Load(IEnumerable<(string Root, bool BuiltIn)> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var catalog = new SkillCatalog();
        foreach (var (root, builtIn) in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                var file = Path.Combine(folder, "SKILL.md");
                if (!File.Exists(file))
                {
                    continue;
                }

                var skill = Parse(File.ReadAllText(file), folder, builtIn, out var error);
                if (skill is null)
                {
                    catalog._problems.Add($"{file}: {error}");
                }
                else if (catalog.Find(skill.Name) is not null)
                {
                    catalog._problems.Add($"{file}: a skill named '{skill.Name}' is already loaded.");
                }
                else
                {
                    catalog._skills.Add(skill);
                }
            }
        }

        return catalog;
    }

    internal static SkillInfo? Parse(string text, string folder, bool builtIn, out string? error)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---")
        {
            error = "SKILL.md must start with a --- frontmatter block.";
            return null;
        }

        var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
        if (end < 0)
        {
            error = "The frontmatter block has no closing ---.";
            return null;
        }

        Frontmatter? frontmatter;
        try
        {
            frontmatter = Yaml.Deserialize<Frontmatter>(string.Join('\n', lines[1..end]));
        }
        catch (YamlException ex)
        {
            error = "The frontmatter is not valid YAML: " + ex.Message;
            return null;
        }

        if (string.IsNullOrWhiteSpace(frontmatter?.Name))
        {
            error = "The frontmatter has no name.";
            return null;
        }

        if (!string.Equals(Path.GetFileName(folder.TrimEnd('\\', '/')), frontmatter.Name, StringComparison.Ordinal))
        {
            error = $"The folder name must be the same as the skill name '{frontmatter.Name}'.";
            return null;
        }

        var tools = frontmatter.Metadata?.GetValueOrDefault("tools")?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        error = null;
        return new SkillInfo(frontmatter.Name, frontmatter.Description ?? "", tools, folder, builtIn);
    }
}
