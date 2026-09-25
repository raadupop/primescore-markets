using System.Text.Json.Nodes;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Configuration.Settings;

/// <summary>What changed between two versions, one <c>path: old → new</c> line per leaf (the audit's diff).</summary>
internal static class SettingsDiff
{
    public static IReadOnlyList<string> Between(EngineSettings? before, EngineSettings after)
    {
        var old = Flatten(before is null ? null : JsonNode.Parse(CanonicalJson.Serialize(before)));
        var now = Flatten(JsonNode.Parse(CanonicalJson.Serialize(after)));
        return old.Keys.Union(now.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Where(path => !string.Equals(old.GetValueOrDefault(path), now.GetValueOrDefault(path), StringComparison.Ordinal))
            .Select(path => $"{path}: {old.GetValueOrDefault(path) ?? "(none)"} → {now.GetValueOrDefault(path) ?? "(none)"}")
            .ToArray();
    }

    private static Dictionary<string, string> Flatten(JsonNode? node)
    {
        var leaves = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(node, "", leaves);
        return leaves;
    }

    private static void Walk(JsonNode? node, string path, Dictionary<string, string> leaves)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var (name, child) in item)
                {
                    Walk(child, path.Length == 0 ? name : $"{path}.{name}", leaves);
                }

                break;
            case JsonArray array:
                // Contexts are addressed by name so reordering or adding one does not rewrite every line.
                for (var index = 0; index < array.Count; index++)
                {
                    var key = array[index] is JsonObject element && element["name"] is JsonValue name ? name.ToString() : index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Walk(array[index], $"{path}[{key}]", leaves);
                }

                break;
            case null:
                break;
            default:
                leaves[path] = node.ToJsonString();
                break;
        }
    }
}
