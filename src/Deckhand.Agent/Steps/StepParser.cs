using System.Text.Json;

namespace Deckhand.Agent.Steps;

public static class StepParser
{
    public static bool TryParse(string text, out Step? step, out string? error)
    {
        step = null;
        var json = ExtractObject(text);
        if (json is null)
        {
            error = "The reply has no JSON object.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var action = GetString(root, "action");
            if (string.IsNullOrWhiteSpace(action))
            {
                error = "The step has no action.";
                return false;
            }

            var args = new Dictionary<string, object?>();
            if (root.TryGetProperty("args", out var argsElement) && argsElement.ValueKind != JsonValueKind.Null)
            {
                if (argsElement.ValueKind != JsonValueKind.Object)
                {
                    error = "args must be a JSON object.";
                    return false;
                }

                foreach (var property in argsElement.EnumerateObject())
                {
                    args[property.Name] = property.Value.Clone();
                }
            }

            var answer = GetString(root, "answer") ?? "";
            var candidate = new Step(GetString(root, "thought") ?? "", action, args, answer);
            if (candidate.IsFinal && string.IsNullOrWhiteSpace(answer))
            {
                error = $"{action} needs an answer.";
                return false;
            }

            step = candidate;
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = "The reply is not valid JSON: " + ex.Message;
            return false;
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Returns the first balanced {...} object in the text, ignoring braces inside strings.</summary>
    private static string? ExtractObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }

                    break;
            }
        }

        return text[start..]; // unbalanced: let JsonDocument report the error
    }
}
