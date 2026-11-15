using System.Text.Json;

namespace Jamm.Core.Patching;

/// <summary>Pure semantic field comparison, shared by patch creation and synthetic model tests.</summary>
public static class FieldDiff
{
    /// <summary>Compares JSON objects recursively; arrays use indices and named maps use stable keys.</summary>
    public static IReadOnlyList<FieldChange> Compare(JsonElement? before, JsonElement? after)
    {
        List<FieldChange> result = [];
        Visit("", before, after);
        return result;

        void Visit(string path, JsonElement? a, JsonElement? b)
        {
            if (a is { ValueKind: JsonValueKind.Object } left && b is { ValueKind: JsonValueKind.Object } right)
            {
                var names = left.EnumerateObject().Select(x => x.Name).Union(right.EnumerateObject().Select(x => x.Name))
                    .Order(StringComparer.Ordinal);
                foreach (string name in names)
                    Visit(path + "/" + name.Replace("~", "~0").Replace("/", "~1"),
                        left.TryGetProperty(name, out var av) ? av : null, right.TryGetProperty(name, out var bv) ? bv : null);
            }
            else if (a is { ValueKind: JsonValueKind.Array } la && b is { ValueKind: JsonValueKind.Array } ra)
            {
                for (int i = 0; i < Math.Max(la.GetArrayLength(), ra.GetArrayLength()); i++)
                    Visit(path + "/" + i, i < la.GetArrayLength() ? la[i] : null, i < ra.GetArrayLength() ? ra[i] : null);
            }
            else if (a is null || b is null || !JsonElement.DeepEquals(a.Value, b.Value))
                result.Add(new(path, a, b));
        }
    }
}
