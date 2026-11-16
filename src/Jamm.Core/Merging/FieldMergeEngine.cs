using System.Text.Json;
using System.Text.Json.Nodes;
using Jamm.Core.Patching;

namespace Jamm.Core.Merging;

internal static class FieldMergeEngine
{
    internal static bool Overlaps(string a, string b) => a == b || a.StartsWith(b + "/", StringComparison.Ordinal)
        || b.StartsWith(a + "/", StringComparison.Ordinal) || a == "" || b == "";

    internal static JsonElement? At(JsonElement? value, string pointer)
    {
        foreach (string part in Parts(pointer))
        {
            if (value is { ValueKind: JsonValueKind.Object } obj) value = obj.TryGetProperty(part, out var v) ? v : null;
            else if (value is { ValueKind: JsonValueKind.Array } arr && int.TryParse(part, out int i) && i >= 0 && i < arr.GetArrayLength()) value = arr[i];
            else return null;
        }
        return value;
    }

    internal static bool Equal(JsonElement? a, JsonElement? b) => a is null ? b is null : b is not null && JsonElement.DeepEquals(a.Value, b.Value);

    internal static JsonElement Apply(JsonElement ancestor, IEnumerable<FieldChange> changes, JsonElement? source = null)
    {
        JsonNode? root = JsonNode.Parse(ancestor.GetRawText());
        // Descending array deletions keep subsequent original coordinates valid.
        foreach (var field in changes.Where(f => f.Mod is not null).Concat(changes.Where(f => f.Mod is null)
            .OrderByDescending(f => f.Path, PointerComparer.Instance)))
        {
            string[] parts = Parts(field.Path);
            JsonNode? replacement = field.Mod is { } value ? JsonNode.Parse(value.GetRawText()) : null;
            if (parts.Length == 0) { root = replacement; continue; }
            JsonNode? node = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (node is JsonObject obj)
                {
                    if (obj[parts[i]] is null)
                    {
                        string prefix = "/" + string.Join('/', parts.Take(i + 1).Select(p => p.Replace("~", "~0").Replace("/", "~1")));
                        obj[parts[i]] = At(source, prefix) is { } fallback ? JsonNode.Parse(fallback.GetRawText()) : new JsonObject();
                    }
                    node = obj[parts[i]];
                }
                else if (node is JsonArray array && int.TryParse(parts[i], out int index) && index < array.Count) node = array[index];
                else { node = null; break; }
            }
            string key = parts[^1];
            if (node is JsonObject target)
            { if (field.Mod is null) target.Remove(key); else target[key] = replacement; }
            else if (node is JsonArray array && int.TryParse(key, out int index))
            {
                if (field.Mod is null) { if (index < array.Count) array.RemoveAt(index); }
                else if (index < array.Count) array[index] = replacement;
                else if (index == array.Count) array.Add(replacement);
            }
        }
        return JsonSerializer.SerializeToElement(root);
    }

    private static string[] Parts(string path) => path.Length == 0 ? [] : path[1..].Split('/')
        .Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToArray();

    private sealed class PointerComparer : IComparer<string>
    {
        internal static readonly PointerComparer Instance = new();
        public int Compare(string? x, string? y)
        {
            string[] a = Parts(x ?? ""), b = Parts(y ?? "");
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int compare = int.TryParse(a[i], out int ai) && int.TryParse(b[i], out int bi) ? ai.CompareTo(bi) : StringComparer.Ordinal.Compare(a[i], b[i]);
                if (compare != 0) return compare;
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}
