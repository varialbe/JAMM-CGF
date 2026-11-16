using System.Text.Json;

namespace Jamm.Core.Building;

// Read the pinned decompiler's definitions so validation covers the same built-in signatures.
// Instance-or-object unions remain dynamic: a positive literal can be an instance handle.
internal static class BuiltinAssetArguments
{
    private static readonly JsonElement Definitions = Read();

    private static JsonElement Read()
    {
        using var stream = typeof(BuiltinAssetArguments).Assembly.GetManifestResourceStream("Jamm.Core.Building.gamemaker.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    internal static IReadOnlyList<(int Argument, string Family)> ForCall(string function, IReadOnlyList<string> arguments)
    {
        // Underanalyzer handles this variadic built-in separately from the JSON registry.
        if (function == "script_execute" && arguments.Count > 0) return [(0, "scripts")];
        if (!Definitions.GetProperty("GlobalNames").GetProperty("FunctionArguments").TryGetProperty(function, out var definition)) return [];
        foreach (var signature in Signatures(definition))
        {
            var types = signature.EnumerateArray().ToArray();
            if (types.Length != arguments.Count || types.Where((t, i) => !Matches(t, arguments[i])).Any()) continue;
            return types.Select((t, i) => (Argument: i, Family: Family(t)))
                .Where(t => t.Family is not null).Select(t => (t.Argument, t.Family!)).ToArray();
        }
        return [];
    }

    private static IEnumerable<JsonElement> Signatures(JsonElement definition)
    {
        if (definition.ValueKind == JsonValueKind.Array) yield return definition;
        else if (definition.ValueKind == JsonValueKind.String
            && Definitions.GetProperty("Types").GetProperty("General").TryGetProperty(definition.GetString()!, out var named))
        {
            foreach (var signature in Signatures(named)) yield return signature;
        }
        else if (definition.ValueKind == JsonValueKind.Object && definition.GetProperty("MacroType").GetString() == "Union")
            foreach (var option in definition.GetProperty("Macros").EnumerateArray())
                foreach (var signature in Signatures(option)) yield return signature;
    }

    private static bool Matches(JsonElement type, string argument)
    {
        if (type.ValueKind != JsonValueKind.Object) return true;
        string? kind = type.GetProperty("MacroType").GetString();
        if (kind == "Match")
        {
            string expected = type.GetProperty("ConditionalValue").GetString()!;
            if (argument == expected) return true;
            if (Definitions.GetProperty("Types").GetProperty("Constants").EnumerateObject().Any(c =>
                c.Value.TryGetProperty(argument, out var value) && value.GetRawText() == expected)) return true;
            // Decompilation may have emitted a named constant or enum discriminant.
            return Definitions.GetProperty("Types").GetProperty("Enums").EnumerateObject().Any(e =>
                e.Value.GetProperty("Values").EnumerateObject().Any(v =>
                    argument == e.Value.GetProperty("Name").GetString() + "." + v.Name && v.Value.GetRawText() == expected));
        }
        if (kind == "Intersect") return type.GetProperty("Macros").EnumerateArray().All(t => Matches(t, argument));
        if (kind == "Union") return type.GetProperty("Macros").EnumerateArray().Any(t => Matches(t, argument));
        return true;
    }

    private static string? Family(JsonElement type) => type.ValueKind == JsonValueKind.String ? type.GetString() switch
    {
        "Asset.Object" => "objects", "Asset.Sprite" => "sprites", "Asset.Sound" => "sounds",
        "Asset.Background" or "Asset.Tileset" => "backgrounds", "Asset.Path" => "paths",
        "Asset.Script" => "scripts", "Asset.Font" => "fonts", "Asset.Timeline" => "timelines",
        "Asset.Room" => "rooms", "Asset.Shader" => "shaders", _ => null
    } : null;
}
