using YamlDotNet.Serialization;

namespace Deckhand.Agent.Skills;

public sealed partial class SkillCatalog
{
    private sealed class Frontmatter
    {
        [YamlMember(Alias = "name")]
        public string? Name
        {
            get; set;
        }

        [YamlMember(Alias = "description")]
        public string? Description
        {
            get; set;
        }

        [YamlMember(Alias = "metadata")]
        public Dictionary<string, string>? Metadata
        {
            get; set;
        }
    }
}
