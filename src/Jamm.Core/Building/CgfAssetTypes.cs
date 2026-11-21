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
    private static readonly IReadOnlyDictionary<string, (int MinArgs, int MaxArgs, (int Arg, string Family)[] Typed)> ApiFunctions =
        new Dictionary<string, (int, int, (int, string)[])>(StringComparer.Ordinal)
        {
            ["jamm_catalog_register"] = (4, 10, [(1, "objects")]),
            ["jamm_fish_register"] = (10, 16, [(1, "objects")]),
            ["jamm_boat_register"] = (6, 7, [(3, "sprites"), (4, "objects")]),
            ["jamm_rod_register"] = (9, 11, [(8, "sprites")]),
            ["jamm_lure_register"] = (6, 7, [(3, "objects"), (4, "sprites")]),
            ["jamm_tool_register"] = (8, 9, [(4, "objects"), (5, "objects"), (6, "sprites")]),
            ["jamm_hat_register"] = (6, 9, [(1, "objects")]),
            ["jamm_upgrade_register"] = (9, 9, [(5, "sprites"), (7, "scripts"), (8, "scripts")]),
            ["part_type_sprite"] = (5, 5, [(1, "sprites")]),
            // Endless Ocean passes its load/release scripts by index; untyped they would keep a stale
            // index when another mod adds scripts first (crash in a silo fish step while streaming).
            ["jamm_chunk_stream"] = (5, 5, [(3, "scripts"), (4, "scripts")])
        };
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
        // HUD setters preserve the built-in argument types after calls are redirected.
        // Without these, decompilation turns alignment constants into bare numbers.
        foreach (var (function, type) in new[]
        {
            ("jamm_hud_set_font", "Asset.Font"),
            ("jamm_hud_set_halign", "Constant.HAlign"),
            ("jamm_hud_set_valign", "Constant.VAlign")
        })
            registry.MacroResolver.GlobalNames.DefineFunctionArgumentsType(function,
                new FunctionArgsMacroType([registry.FindType(type)]));
        foreach (var (function, (minArgs, maxArgs, typed)) in ApiFunctions)
        {
            List<IMacroType> signatures = [];
            for (int count = minArgs; count <= maxArgs; count++)
            {
                IMacroType?[] args = new IMacroType?[count];
                foreach (var (arg, family) in typed)
                    if (arg < count)
                    {
                        string assetType = family switch
                        {
                            "sprites" => "Asset.Sprite",
                            "objects" => "Asset.Object",
                            "scripts" => "Asset.Script",
                            _ => throw new InvalidDataException($"Unknown asset family '{family}' in API function '{function}'.")
                        };
                        args[arg] = registry.FindType(assetType);
                    }
                signatures.Add(new FunctionArgsMacroType(args));
            }
            registry.MacroResolver.GlobalNames.DefineFunctionArgumentsType(function,
                signatures.Count == 1 ? signatures[0] : new UnionMacroType(signatures));
        }
        Dictionary<string, NameMacroTypeResolver> scopes = new(StringComparer.Ordinal);
        foreach (var (container, definition) in Definitions)
        {
            var type = registry.FindType("Asset." + (definition.Family switch
            {
                "objects" => "Object", "sprites" => "Sprite", "sounds" => "Sound", "paths" => "Path", "fonts" => "Font",
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

    internal static bool HasTypedArguments(string function) => ApiFunctions.ContainsKey(function)
        || Definitions.TryGetValue(function, out var definition) && definition.Argument is not null;

    internal static string? ArgumentFamily(string function, int argument)
    {
        if (ApiFunctions.TryGetValue(function, out var api))
        {
            foreach (var (arg, family) in api.Typed)
                if (arg == argument) return family;
            return null;
        }
        return Definitions.TryGetValue(function, out var definition) && definition.Argument == argument ? definition.Family : null;
    }
}
