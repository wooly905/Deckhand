using System.Text.Json;
using System.Text.Json.Serialization;
using Deckhand.Agent.Steps;
using Deckhand.Agent.Tools;
using Microsoft.Extensions.AI;

namespace Deckhand.Eval;

public sealed record FakeToolSpec(string Name, string Description, string Group, RiskLevel Risk, bool PolicyDriven, JsonElement Parameters, string Output);

public sealed record ToolCallRecord(string Name, IReadOnlyDictionary<string, string> Args);

/// <summary>A tool with the real name and schema and a fixed output. Results do not depend on the computer.</summary>
public sealed class FakeAIFunction(FakeToolSpec spec, string output, List<ToolCallRecord> calls) : AIFunction
{
    public override string Name => spec.Name;

    public override string Description => spec.Description;

    public override JsonElement JsonSchema => spec.Parameters;

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        lock (calls)
        {
            calls.Add(new ToolCallRecord(spec.Name, arguments.ToDictionary(a => a.Key, a => TranscriptWriter.ObservationText(a.Value))));
        }

        return ValueTask.FromResult<object?>(output);
    }
}

public static class FakeToolManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<FakeToolSpec> Load(string path) =>
        JsonSerializer.Deserialize<List<FakeToolSpec>>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path} has no tools.");

    public static ToolCatalog BuildCatalog(IReadOnlyList<FakeToolSpec> specs, IReadOnlyDictionary<string, string> outputs, List<ToolCallRecord> calls)
    {
        var catalog = new ToolCatalog();
        foreach (var spec in specs)
        {
            var output = outputs.GetValueOrDefault(spec.Name, spec.Output);
            catalog.Add(new FakeAIFunction(spec, output, calls), spec.Group, spec.Risk, spec.PolicyDriven);
        }

        return catalog;
    }
}
