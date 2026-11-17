using UndertaleModLib;
using UndertaleModLib.Compiler;

namespace Jamm.ModMaker;

/// <summary>Phase 6a optimizations plus switchable paused-fish scheduling, independent of Smooth.</summary>
public static class PerfMod
{
    /// <summary>Applies guarded source edits to an original legacy game.</summary>
    public static void Apply(UndertaleData data, bool debug = false)
    {
        var compiler = new CompileGroup(data);
        // FishThinner destroys owned lights before deactivation/death; reactivation and
        // fish_respawn's instance_copy can leave the dead id behind. Preserve all other
        // drawing/state work, and do not recreate lights or change the culling policy.
        Patch("gml_Object_Magmer_Draw_0",
            "light.image_alpha = 1 - power(1 - heat_level, 2);",
            "if (instance_exists(light))\n    light.image_alpha = 1 - power(1 - heat_level, 2);");
        Patch("gml_Object_Volteen_Other_13",
            "light.add_blend = 4210752;\nlight.sub_blend = 4210752;",
            "if (instance_exists(light))\n{\n    light.add_blend = 4210752;\n    light.sub_blend = 4210752;\n}");
        Patch("gml_Object_Volteen_Other_10",
            "    light.add_blend = 0;\n    light.sub_blend = 0;",
            "    if (instance_exists(light))\n    {\n        light.add_blend = 0;\n        light.sub_blend = 0;\n    }",
            "    light.add_blend = 4210752;\n    light.sub_blend = 4210752;",
            "    if (instance_exists(light))\n    {\n        light.add_blend = 4210752;\n        light.sub_blend = 4210752;\n    }");
        // GM1's boolean evaluation must not be relied on to short circuit. The outer guard
        // prevents a precise CaveRockBig collision query for states that cannot use its result.
        Patch("gml_Object_Fish_Step_2",
            "if (y > 5000 && !deepfish)",
            "if (state == 0 || state == 1 || state == 9 || state == 5)\nif (y > 5000 && !deepfish)");

        // A collision query has no game side effects. Preserve Net existence checks and
        // every transition, but do not query a net for states that cannot be caught.
        Patch("gml_Script_fish_check_trawler",
            "if (collision_line(Net.top.x, Net.top.y, Net.bot.x, Net.bot.y, id, true, false))",
            "if (state == 0 || state == 2 || state == 1 || state == 5 || state == 8 || state == 9 || state == 10)\nif (collision_line(Net.top.x, Net.top.y, Net.bot.x, Net.bot.y, id, true, false))");

        // All original callers pass object assets (including object_index), so with must
        // retain descendant traversal and engine order. Hoist only arithmetic invariants:
        // no neighbour is omitted and the sum, including coincident-point behaviour, is unchanged.
        var avoid = data.Code.ByName("gml_Script_fish_avoid_fish")
            ?? throw new InvalidDataException("fish_avoid_fish missing: use the original legacy data.win.");
        string avoidance = ModObjects.Decompile(data, avoid);
        if (!avoidance.Contains("(argument1 * argument1)", StringComparison.Ordinal)
            || avoidance.Split("(100000 * argument2)", StringSplitOptions.None).Length != 3)
            throw new InvalidDataException("fish_avoid_fish differs: use the original legacy data.win.");
        compiler.QueueCodeReplace(avoid,
            "var jamm_perf_radius2 = argument1 * argument1;\nvar jamm_perf_force = 100000 * argument2;\n"
            + avoidance.Replace("(argument1 * argument1)", "jamm_perf_radius2", StringComparison.Ordinal)
                .Replace("(100000 * argument2)", "jamm_perf_force", StringComparison.Ordinal));

        // Parallax still follows the camera every tick, including off-screen scenery (other
        // objects may read its coordinates). Only the pure depth transform is memoized.
        var scene = data.Code.ByName("gml_Object_SceneItem_Step_2")
            ?? throw new InvalidDataException("SceneItem step missing: use the original legacy data.win.");
        string source = ModObjects.Decompile(data, scene);
        int end = source.IndexOf("x = home_x", StringComparison.Ordinal);
        if (end < 0 || !source.StartsWith("scene_depth = depth + 18;", StringComparison.Ordinal))
            throw new InvalidDataException("SceneItem step differs: use the original legacy data.win.");
        compiler.QueueCodeReplace(scene, "if (!variable_instance_exists(id, \"jamm_perf_depth\")) { jamm_perf_depth = depth + 1; jamm_perf_object = object_index; }\n"
            + "if (jamm_perf_depth != depth || jamm_perf_object != object_index)\n{\n" + source[..end]
            + "jamm_perf_scene_depth = scene_depth;\njamm_perf_depth = depth;\njamm_perf_object = object_index;\n}\nscene_depth = jamm_perf_scene_depth;\n" + source[end..]);
        PerfMode.Queue(data, compiler, debug);
        ModObjects.Compile(compiler);

        void Patch(string name, string before, string after, string? secondBefore = null, string? secondAfter = null)
        {
            var code = data.Code.ByName(name)
                ?? throw new InvalidDataException($"'{name}' missing: use the original legacy data.win.");
            string text = ModObjects.Decompile(data, code);
            if (text.Split(before, StringSplitOptions.None).Length != 2)
                throw new InvalidDataException($"'{name}' differs: use the original legacy data.win.");
            if (before.StartsWith("if (", StringComparison.Ordinal)) AssertNoElse(text, before, name);
            text = text.Replace(before, after, StringComparison.Ordinal);
            if (secondBefore is not null)
            {
                if (text.Split(secondBefore, StringSplitOptions.None).Length != 2)
                    throw new InvalidDataException($"'{name}' differs: use the original legacy data.win.");
                text = text.Replace(secondBefore, secondAfter!, StringComparison.Ordinal);
            }
            if (name == "gml_Object_Fish_Step_2") text = PerfMode.Prefix(data) + text;
            compiler.QueueCodeReplace(code, text);
        }
    }
    // WHY: wrapping an if that owns an else changes which if receives that else in GM1.
    private static void AssertNoElse(string source, string before, string name)
    {
        int start = source.IndexOf(before, StringComparison.Ordinal) + before.Length;
        int brace = source.IndexOf('{', start);
        if (brace < 0) throw new InvalidDataException($"'{name}': expected a braced guarded statement; use the original legacy data.win.");
        int end = brace + 1, depth = 1;
        for (; end < source.Length && depth > 0; end++)
        {
            if (source[end] == '{') depth++;
            if (source[end] == '}') depth--;
        }
        if (depth != 0 || source[end..].TrimStart().StartsWith("else", StringComparison.Ordinal))
            throw new InvalidDataException($"'{name}': guarded statement has an else or unexpected shape; cannot safely apply Perf. Use the original legacy data.win.");
    }

}
