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
    internal const int ChunkCount = 488;
    internal const int ExtensionWidth = ChunkCount * EndlessOceanEdge.ChunkWidth;
    private const string Setup = "jmk_endless_ocean_setup";
    private const string SetupCode = "gml_Script_" + Setup;

    private static readonly string[] Scripts =
    [
        "jamm_chunk_unit", "jamm_chunk_stream", Setup, "jmk_endless_ocean_floor", "jmk_endless_ocean_height",
        "jmk_endless_ocean_biome", "jmk_endless_ocean_species", "jmk_endless_ocean_poi",
        "jmk_endless_ocean_layout", "jmk_endless_ocean_underlay_cell", "jmk_endless_ocean_ground", "jmk_endless_ocean_scenery",
        "jmk_endless_ocean_load", "jmk_endless_ocean_dispose", "jmk_endless_ocean_release"
    ];

    /// <summary>Applies the recipe to an in-memory vanilla or already merged archive.</summary>
    public static void Apply(UndertaleData data, int seed = DefaultSeed, Action<string>? progress = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seed, MaxSeed);
        var world = data.Rooms.ByName("NewWorld")
            ?? throw new InvalidDataException("The NewWorld room is missing. Choose the August 2023 legacy archive or a compatible JAMM build.");
        foreach (string name in new[] { "Camera", "Boat7", "SaveManager", "Rod", "Lure", "Collidable", "CaveRock", "CaveRockBig", "Light" })
        {
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"Endless Ocean needs the '{name}' object. Choose a compatible legacy archive.");
        }

        if (data.GameObjects.ByName(Controller) is not null ||
            Scripts.Any(name => data.Scripts.ByName(name) is not null || data.Code.ByName("gml_Script_" + name) is not null))
            throw new InvalidDataException("This archive already contains Endless Ocean or conflicting chunk scripts. Build the recipe on a compatible archive without those assets.");

        if (data.GameObjects.ByName("CaveRockBig")?.Sprite is not { Width: >= 512, Height: >= 512 } rock ||
            data.GameObjects.ByName("Boat3")?.Sprite is not { Width: >= 840, Height: >= 315 } hull)
            throw new InvalidDataException("Endless Ocean needs the original CaveRockBig and Boat3 sprites. Choose a compatible legacy archive.");

        var edge = EndlessOceanEdge.Find(world, world.Width);
        progress?.Invoke(Describe(edge));
        var ground = ModObjects.Create(data, "obj_jmk_ocean_ground", persistent: false);
        ground.ParentId = data.GameObjects.ByName("CaveRock");
        ground.Sprite = EndlessOceanSprites.Add(data, rock, hull);
        ground.Depth = 5;
        ModObjects.Create(data, "obj_jmk_ocean_decor", persistent: false).Depth = -3;

        var compiler = new CompileGroup(data);
        foreach (string script in Scripts)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + script);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(script), Code = code });
            compiler.QueueCodeReplace(code, script == Setup ? Settings(edge, world.Width) : Source(script));
        }
        compiler.QueueCodeReplace(ground.EventHandlerFor(EventType.Draw, 0u, data), Source("draw_ground"));

        var controller = ModObjects.Create(data, Controller, persistent: false);
        Queue(EventType.Create, 0, """
            ocean_chunks = -1;
            """);
        Queue(EventType.Other, (uint)EventSubtypeOther.RoomStart, FormattableString.Invariant($$"""
            if (ds_exists(ocean_chunks, ds_type_map)) exit;
            {{Setup}}();
            ocean_chunks = ds_map_create();
            ocean_catch = ds_list_create();
            ocean_first = 0;
            ocean_last = -1;

            var atlas_uvs = sprite_get_uvs(spr_jmk_ocean_underlay_tex, 0);
            var atlas_u0 = atlas_uvs[0];
            var atlas_v0 = atlas_uvs[1];
            var atlas_uw = atlas_uvs[2] - atlas_u0;
            var atlas_vh = atlas_uvs[3] - atlas_v0;
            var atlas_tex = sprite_get_texture(spr_jmk_ocean_underlay_tex, 0);
            for (var uv_i = 0; uv_i < {{EndlessOceanSprites.VariantCount}}; uv_i += 1)
            {
                var gx = uv_i mod {{EndlessOceanSprites.AtlasGrid}};
                var gy = uv_i div {{EndlessOceanSprites.AtlasGrid}};
                ocean_u0[uv_i] = atlas_u0 + ((gx * {{EndlessOceanSprites.SlotSize}} + {{EndlessOceanSprites.SlotPad}}) / {{EndlessOceanSprites.AtlasSize}}) * atlas_uw;
                ocean_v0[uv_i] = atlas_v0 + ((gy * {{EndlessOceanSprites.SlotSize}} + {{EndlessOceanSprites.SlotPad}}) / {{EndlessOceanSprites.AtlasSize}}) * atlas_vh;
                ocean_u1[uv_i] = atlas_u0 + ((gx * {{EndlessOceanSprites.SlotSize}} + {{EndlessOceanSprites.SlotPad + EndlessOceanSprites.CellSize}}) / {{EndlessOceanSprites.AtlasSize}}) * atlas_uw;
                ocean_v1[uv_i] = atlas_v0 + ((gy * {{EndlessOceanSprites.SlotSize}} + {{EndlessOceanSprites.SlotPad + EndlessOceanSprites.CellSize}}) / {{EndlessOceanSprites.AtlasSize}}) * atlas_vh;
                ocean_tex[uv_i] = atlas_tex;
            }

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
        var respawn = data.Code.ByName("gml_Script_fish_respawn")
            ?? throw new InvalidDataException("Endless Ocean needs the fish_respawn script. Choose a compatible legacy archive.");
        compiler.QueueCodeReplace(respawn, """
            if (variable_instance_exists(id, "jmk_endless_ocean_fish")) return -4;

            """ + ModObjects.Decompile(data, respawn));
        ModObjects.Compile(compiler);
        world.Width = edge.End;
        ModObjects.PlaceInstance(data, "NewWorld", controller, first: true);

        void Queue(EventType type, uint subtype, string gml) =>
            compiler.QueueCodeReplace(controller.EventHandlerFor(type, subtype, data), gml);
    }

    internal static void RefreshMergedLayout(UndertaleData data, Dictionary<string, string> recompile,
        Dictionary<string, string> generated, List<string> decisions)
    {
        if (data.GameObjects.ByName(Controller) is null || data.Code.ByName(SetupCode) is not { } code ||
            data.Rooms.ByName("NewWorld") is not { } world)
            return;

        string previous = recompile.TryGetValue(SetupCode, out var queued) ? queued : ModObjects.Decompile(data, code);
        var endLiteral = Regex.Match(previous, @"\bocean_end\s*=\s*([0-9]+);");
        uint sourceWidth = world.Width;
        if (uint.TryParse(endLiteral.Groups[1].Value, out uint previousEnd) && sourceWidth == previousEnd)
        {
            // The setup script returns the original width for subsequent profile builds.
            var widthLiteral = Regex.Match(previous, @"\breturn\s+([0-9]+);");
            if (uint.TryParse(widthLiteral.Groups[1].Value, out uint previousSourceWidth))
                sourceWidth = previousSourceWidth;
        }

        var edge = EndlessOceanEdge.Find(world, sourceWidth);
        world.Width = edge.End;
        generated[SetupCode] = recompile[SetupCode] = Settings(edge, sourceWidth);
        // `script_execute` receives script table indexes for the chunk callbacks. Those
        // indexes can shift when the recipe is merged after other mods, so rebuild the
        // controller step from symbols in the final archive instead of retaining the
        // indexes compiled when the recipe was first applied.
        const string stepCode = "gml_Object_" + Controller + "_Step_1";
        if (data.Code.ByName(stepCode) is not null)
            generated[stepCode] = recompile[stepCode] = Source("step");
        decisions.Add(Describe(edge));
    }

    private static string Settings(EndlessOceanEdge edge, uint sourceWidth) => FormattableString.Invariant($"""
        ocean_start = {edge.X};
        ocean_anchor_y = {edge.Floor};
        ocean_content_chunk = {edge.ContentChunk};
        ocean_chunk_count = {edge.ContentChunk + ChunkCount};
        ocean_end = {edge.End};
        return {sourceWidth};
        """);

    private static string Describe(EndlessOceanEdge edge) =>
        $"Endless Ocean: floor joins at ({edge.X}, {edge.Floor}), biomes start at x={edge.X + edge.ContentChunk * EndlessOceanEdge.ChunkWidth}, and travel ends at x={edge.End}.";

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
