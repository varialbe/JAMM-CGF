using System.Text.Json;
using Underanalyzer.Decompiler.GameSpecific;
using UndertaleModLib;

namespace Jamm.Core.Building;

/// <summary>Audited CGF container types registered with Underanalyzer without changing the library.</summary>
public static class CgfAssetTypes
{
    private sealed record Definition(string Family, string Reason, string[]? Entries, int? Argument, int? ArgumentCount);
    private static readonly Dictionary<string, Definition> Definitions = Read();
    internal static readonly string[] IndexedFamilies = ["objects", "sprites", "sounds", "backgrounds", "paths", "scripts", "fonts", "timelines", "rooms", "shaders"];
    private static Dictionary<string, Definition> Read()
    {
        using var stream = typeof(CgfAssetTypes).Assembly.GetManifestResourceStream("Jamm.Core.Building.cgf-asset-types.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, Definition>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>Registers container definitions on a disposable loaded model before decompiling.</summary>
    public static void Register(UndertaleData data)
    {
        data.GameSpecificRegistry ??= new();
        var registry = data.GameSpecificRegistry;
        registry.RegisterBasic();
        Dictionary<string, NameMacroTypeResolver> scopes = new(StringComparer.Ordinal);
        foreach (var (container, definition) in Definitions)
        {
            var type = registry.FindType("Asset." + (definition.Family switch
            {
                "objects" => "Object", "sprites" => "Sprite", "sounds" => "Sound", "paths" => "Path",
                _ => throw new InvalidDataException($"Unknown asset family '{definition.Family}' in CGF type definitions.")
            }));
            if (definition.Argument is { } argument)
            {
                IMacroType?[] types = new IMacroType?[definition.ArgumentCount ?? argument + 1];
                types[argument] = type;
                registry.MacroResolver.GlobalNames.DefineFunctionArgumentsType(container, new FunctionArgsMacroType(types));
            }
            else if (definition.Entries is null) registry.MacroResolver.GlobalNames.DefineVariableType(container, type);
            else foreach (string entry in definition.Entries)
            {
                if (!scopes.TryGetValue(entry, out var resolver)) scopes[entry] = resolver = new();
                resolver.DefineVariableType(container, type);
            }
        }
        foreach (var (entry, resolver) in scopes) registry.MacroResolver.DefineCodeEntry(entry, resolver);
    }

    internal static string? Family(string container, string entry) => container switch
    {
        "sprite_index" or "mask_index" => "sprites", "object_index" => "objects", "room" => "rooms", "path_index" => "paths",
        _ => DefinedFamily(container, entry)
    };

    private static string? DefinedFamily(string container, string entry) => Definitions.TryGetValue(container, out var definition)
        && definition.Argument is null && (definition.Entries is null || definition.Entries.Contains(entry, StringComparer.Ordinal)) ? definition.Family : null;

    internal static string? ArgumentFamily(string function, int argument) => Definitions.TryGetValue(function, out var definition)
        && definition.Argument == argument ? definition.Family : null;
}
