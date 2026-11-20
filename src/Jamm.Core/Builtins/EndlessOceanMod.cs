using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Extends legacy NewWorld with a standalone, seeded ocean controller.</summary>
public static class EndlessOceanMod
{
    /// <summary>The seed used when neither the recipe nor the player's INI supplies one.</summary>
    public const int DefaultSeed = 271828;

    /// <summary>Largest seed supported by the runtime sampler.</summary>
    public const int MaxSeed = 2147483646;

    internal const string Controller = "obj_jmk_endless_ocean";
    internal const int KnownEastEdge = 170000;
    internal const int ChunkCount = 488;
    internal const int ExtensionWidth = ChunkCount * 2048;

    private static readonly string[] Scripts =
    [
        "jamm_chunk_unit", "jamm_chunk_stream", "jmk_endless_ocean_floor",
        "jmk_endless_ocean_layout", "jmk_endless_ocean_load", "jmk_endless_ocean_dispose", "jmk_endless_ocean_release"
    ];

    /// <summary>Applies the recipe to an in-memory vanilla or already merged archive.</summary>
    public static void Apply(UndertaleData data, int seed = DefaultSeed, Action<string>? progress = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seed, MaxSeed);
        var world = data.Rooms.ByName("NewWorld")
            ?? throw new InvalidDataException("The NewWorld room is missing. Choose the August 2023 legacy archive or a compatible JAMM build.");
        foreach (string name in new[] { "Camera", "Boat7", "SaveManager", "Rod", "Lure", "Collidable", "CaveRockBig",
            "Celad", "Inno", "Mustardfish", "Elder_Mustardfish", "Dijon", "Garm" })
        {
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"Endless Ocean needs the '{name}' object. Choose a compatible legacy archive.");
        }

        if (data.GameObjects.ByName(Controller) is not null ||
            Scripts.Any(name => data.Scripts.ByName(name) is not null || data.Code.ByName("gml_Script_" + name) is not null))
            throw new InvalidDataException("This archive already contains Endless Ocean or conflicting chunk scripts. Build the recipe on a compatible archive without those assets.");

        uint start = ProtectedEastEdge(world);
        uint end = checked(start + ExtensionWidth);
        progress?.Invoke($"Endless Ocean: generating from x={start} to x={end} ({ChunkCount} chunks).");

        var compiler = new CompileGroup(data);
        foreach (string script in Scripts)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + script);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(script), Code = code });
            compiler.QueueCodeReplace(code, Source(script));
        }

        var controller = ModObjects.Create(data, Controller, persistent: false);
        Queue(EventType.Create, 0, """
            ocean_chunks = -1;
            """);
        Queue(EventType.Other, (uint)EventSubtypeOther.RoomStart, FormattableString.Invariant($$"""
            if (ds_exists(ocean_chunks, ds_type_map)) exit;
            ocean_start = {{start}};
            ocean_end = {{end}};
            ocean_chunk_count = {{ChunkCount}};
            ocean_chunks = ds_map_create();
            ocean_catch = ds_list_create();
            ocean_first = 0;
            ocean_last = -1;

            ini_open("jamm_endless_ocean.ini");
            ocean_seed = clamp(floor(ini_read_real("World", "seed", {{seed}})), 0, {{MaxSeed}});
            if (!ini_key_exists("World", "seed")) ini_write_real("World", "seed", ocean_seed);
            ini_close();
            """));
        Queue(EventType.Step, (uint)EventSubtypeStep.BeginStep, Source("step"));

        const string clear = """
            if (ds_exists(ocean_chunks, ds_type_map))
            {
                jamm_chunk_stream(ocean_chunks, 0, -1, jmk_endless_ocean_load, jmk_endless_ocean_release);
                ds_map_destroy(ocean_chunks);
                ds_list_destroy(ocean_catch);
                ocean_chunks = -1;
            }
            """;
        Queue(EventType.Other, (uint)EventSubtypeOther.RoomEnd, "if (room_persistent) exit;\n" + clear);
        Queue(EventType.Destroy, 0, clear);

        ExcludeGeneratedFishFromSaves(data, compiler);
        ModObjects.Compile(compiler);
        world.Width = end;
        ModObjects.PlaceInstance(data, "NewWorld", controller, first: true);

        void Queue(EventType type, uint subtype, string gml) =>
            compiler.QueueCodeReplace(controller.EventHandlerFor(type, subtype, data), gml);
    }

    internal static uint ProtectedEastEdge(UndertaleRoom world)
    {
        double east = Math.Max(world.Width, KnownEastEdge);
        foreach (var placement in world.GameObjects)
        {
            east = Math.Max(east, placement.X);
            if (placement.ObjectDefinition is not { } obj)
                continue;

            Include(obj.Sprite);
            Include(obj.TextureMaskId);

            void Include(UndertaleSprite? sprite)
            {
                if (sprite is null)
                    return;
                double radians = placement.Rotation * Math.PI / 180;
                double horizontal = placement.ScaleX * Math.Cos(radians);
                double vertical = placement.ScaleY * Math.Sin(radians);
                double right = Math.Max(-sprite.OriginX * horizontal, (sprite.Width - (double)sprite.OriginX) * horizontal);
                double bottom = Math.Max(-sprite.OriginY * vertical, (sprite.Height - (double)sprite.OriginY) * vertical);
                east = Math.Max(east, placement.X + right + bottom);
            }
        }
        return checked((uint)Math.Ceiling(east));
    }

    private static void ExcludeGeneratedFishFromSaves(UndertaleData data, CompileGroup compiler)
    {
        // The vanilla saves enumerate every live Fish. Keep the regular catch, quest and catalog writes.
        const string fishWrite = """(?m)^([ \t]*)(ini_write_real\("Fishes", string\(id\),[^;\r\n]*\);)""";
        const string guardedWrite = """
            $1{
            $1    if (!variable_instance_exists(id, "jmk_endless_ocean_fish")) $2
            $1}
            """;
        foreach (string name in new[] { "gml_Object_SaveManager_Other_14", "gml_Object_SaveManager_Other_15" })
        {
            var code = data.Code.ByName(name);
            if (code is null)
                throw new InvalidDataException($"Endless Ocean needs '{name}'. Choose a compatible legacy archive.");

            string source = ModObjects.Decompile(data, code);
            if (Regex.Matches(source, fishWrite).Count != 2)
                throw new InvalidDataException($"Endless Ocean could not recognize the fish-state writes in '{name}'. Choose a compatible legacy archive.");
            compiler.QueueCodeReplace(code, Regex.Replace(source, fishWrite, guardedWrite));
        }
    }

    private static string Source(string name)
    {
        using var stream = typeof(EndlessOceanMod).Assembly.GetManifestResourceStream($"Jamm.Worldgen.{name}")
            ?? throw new InvalidOperationException($"Missing embedded Endless Ocean script '{name}'. Rebuild JAMM.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
