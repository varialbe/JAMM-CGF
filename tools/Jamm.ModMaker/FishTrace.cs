using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleInstruction;

namespace Jamm.ModMaker;

// Keep benchmark time and random instrumentation out of ordinary Perf builds.
internal static class FishTrace
{
    private static readonly HashSet<string> RandomCalls = new(StringComparer.Ordinal)
    {
        "random", "random_range", "irandom", "irandom_range", "choose",
        "random_set_seed", "ds_list_shuffle"
    };

    internal static void Instrument(UndertaleData data, int seed)
    {
        if (data.GameObjects.ByName("Fish") is null || data.GeneralInfo.Major != 1
            || data.GeneralInfo.BytecodeVersion != 16 || data.Code.Any(code => code.ParentEntry is not null))
            throw new InvalidDataException("Fish traces require the legacy Cat Goes Fishing runner and Fish object.");

        var originals = data.Code.ToArray();
        var scripts = data.Scripts.Select(script => script.Name.Content).ToHashSet(StringComparer.Ordinal);
        var compiler = new CompileGroup(data);
        var clock = Add("clock", """
            if (!variable_global_exists("jamm_trace_tick")) return 0;
            return floor(global.jamm_trace_tick * 1000 / 30);
            """);
        var delta = Add("delta", "return 33333;");
        var fps = Add("fps", "return 29;");
        var raster = Add("raster", "return 0;");
        var date = Add("date", "return date_create_datetime(2023, 8, 16, 12, 0, 0);");
        var rng = Add("rng", $$"""
            if (!variable_global_exists("jamm_trace_rng"))
            {
                global.jamm_trace_rng = 0;
                random_set_seed({{seed}});
            }
            global.jamm_trace_rng += 1;
            """);
        var randomize = Add("randomize", $"jamm_trace_rng(); random_set_seed({seed});");
        ModObjects.Compile(compiler);
        foreach (var code in originals)
        {
            // Only discard calls whose results are immediately dropped. Draw events,
            // including their RNG, state updates and render-state setters, still run.
            var rasterCalls = code.Instructions.Zip(code.Instructions.Skip(1))
                .Where(pair => pair.Second.Kind == Opcode.Popz && pair.First.Kind == Opcode.Call
                    && pair.First.ArgumentsCount <= 16 && pair.First.ValueFunction?.Name.Content is { } name
                    && !scripts.Contains(name) && IsRasterCall(name))
                .Select(pair => pair.First).ToHashSet();
            LegacyCodeHooks.Insert(code, instruction => rasterCalls.Contains(instruction)
                ? new(ReplaceCall: raster)
                : Select(instruction));
        }

        LegacyCodeHooks.Hook Select(UndertaleInstruction instruction)
        {
            if (instruction.Kind == Opcode.Call && instruction.ValueFunction?.Name.Content is { } name)
            {
                if (RandomCalls.Contains(name)) return new(Before: rng);
                if (name == "randomize" && instruction.ArgumentsCount == 0) return new(ReplaceCall: randomize);
                if (name == "date_current_datetime" && instruction.ArgumentsCount == 0) return new(ReplaceCall: date);
            }
            if (instruction.Kind is Opcode.PushBltn or Opcode.Push
                && instruction.Type1 == DataType.Variable
                && instruction.TypeInst is not (InstanceType.Global or InstanceType.Local))
            {
                if (instruction.ValueVariable?.Name.Content == "current_time") return new(ReplaceCall: clock);
                if (instruction.ValueVariable?.Name.Content == "delta_time") return new(ReplaceCall: delta);
                if (instruction.ValueVariable?.Name.Content is "fps" or "fps_real") return new(ReplaceCall: fps);
            }
            return default;
        }

        UndertaleFunction Add(string name, string body)
        {
            name = "jamm_trace_" + name;
            if (data.Scripts.ByName(name) is not null)
                throw new InvalidDataException($"'{name}' already exists. Choose a fresh input archive for fish tracing.");
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
            compiler.QueueCodeReplace(code, body);
            return data.Functions.EnsureDefined(name, data.Strings);
        }
    }

    private static readonly string[] RasterPrefixes =
    [
        "draw_background", "draw_circle", "draw_clear", "draw_ellipse", "draw_line", "draw_path",
        "draw_point", "draw_primitive", "draw_rectangle", "draw_roundrect", "draw_sprite", "draw_surface",
        "draw_text", "draw_tile", "draw_triangle", "draw_vertex"
    ];

    private static bool IsRasterCall(string name)
        => Array.Exists(RasterPrefixes, prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    internal static string Create => """
        global.jamm_trace_tick = 0;
        jamm_trace_n = 0;
        jamm_trace_buffer = buffer_create(65536, buffer_grow, 1);
        """;

    internal static string Start => """
        jamm_trace_n = 0;
        var trace = file_text_open_write("jamm_trace_fish_" + jamm_b_scene + ".csv");
        if (trace < 0) show_error("Could not open the fish trace file.", true);
        file_text_write_string(trace, "tick,fish,explicit_rng_calls,md5");
        file_text_writeln(trace);
        file_text_close(trace);
        """;

    // Include the fields read or updated by the neighbour scripts and the base state
    // machine. Presence masks distinguish fields that are unset without a buffer
    // call for each absent field. Each mask covers at most 30 bits in GM1.
    private static readonly string[] OptionalFields =
    [
        "state", "spd", "life", "target", "target_x", "target_y", "pal", "impulse", "sdist", "dir",
        "collision", "lungeCooldown", "lunge_mod", "base_lunge_mod", "base_lunge_offset",
        "LUNGE_RANGE_START", "LUNGE_RANGE_STOP", "avoid_large_range", "avoid_self_range",
        "lure_distance_squared", "deactivated", "isVisible", "isDragging", "onBoat", "always_zoned",
        "full_reset", "frames_since_last_flip", "start_x", "start_y", "size", "weight", "thrust",
        "fish_target", "target_time", "respawn_time"
    ];

    internal static string Sample()
    {
        string required = Writes("id", "object_index", "x", "y", "xprevious", "yprevious", "hspeed", "vspeed",
            "speed", "direction", "image_xscale", "image_yscale", "image_angle",
            "path_index", "path_position", "path_speed");
        string optional = string.Join("\n", OptionalFields.Chunk(30).Select((fields, group) =>
        {
            string mask = $"jamm_trace_mask_{group}";
            string readPresence = string.Join("\n", fields.Select((field, bit) =>
                $"if (variable_instance_exists(id, \"{field}\")) {mask} += {1 << bit};"));
            string writeValues = string.Join("\n", fields.Select((field, bit) =>
                $"if (({mask} & {1 << bit}) != 0) buffer_write(b, buffer_f64, {field});"));
            return $"var {mask} = 0;\n{readPresence}\nbuffer_write(b, buffer_u32, {mask});\n{writeValues}";
        }));

        return $$"""
            var b = jamm_trace_buffer;
            buffer_seek(b, buffer_seek_start, 0);
            var n = 0;
            with (Fish)
            {
                n += 1;
                {{required}}
                {{optional}}
            }
            jamm_trace_n += 1;
            var hash = buffer_md5(b, 0, buffer_tell(b));
            var trace = file_text_open_append("jamm_trace_fish_" + jamm_b_scene + ".csv");
            if (trace < 0) show_error("Could not append the fish trace file.", true);
            var rng = 0;
            if (variable_global_exists("jamm_trace_rng")) rng = global.jamm_trace_rng;
            file_text_write_string(trace, string(jamm_trace_n) + "," + string(n) + "," + string(rng) + "," + hash);
            file_text_writeln(trace);
            file_text_close(trace);
            """;

        static string Writes(params string[] fields)
            => string.Join("\n", fields.Select(field => $"buffer_write(b, buffer_f64, {field});"));
    }
}
