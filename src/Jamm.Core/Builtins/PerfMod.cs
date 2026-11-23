using UndertaleModLib;
using UndertaleModLib.Compiler;

namespace Jamm.ModMaker;

/// <summary>Phase 6a optimizations plus switchable paused-fish scheduling, independent of Smooth.</summary>
public static class PerfMod
{
    /// <summary>Applies guarded source edits to an original legacy game.</summary>
    public static void Apply(UndertaleData data, bool debug = false, bool applyFishGrid = false,
        bool applyScheduler = true, bool applyCoreOptimizations = true, bool applyDecorSleep = false,
        bool applyFishSleep = false, List<string>? decisions = null,
        bool masksRect = false, bool fishLod = false, bool decorAnimNative = false,
        bool? cacheHot = null, bool particlesFx = false, bool applyCaveRockVisibility = false)
    {
        bool applyCacheHot = cacheHot ?? false;
        if (data.GeneralInfo.Major != 1 || data.GeneralInfo.BytecodeVersion != 16
            || data.Code.ByName("gml_Object_Fish_Step_2") is null
            || data.Code.ByName("gml_Script_fish_check_trawler") is null
            || data.Code.ByName("gml_Script_fish_avoid_fish") is null)
            throw new InvalidDataException("Perf requires the supported legacy GameMaker 1 archive. Select the August 2023 legacy data.win.");

        var compiler = new CompileGroup(data);
        if (applyCoreOptimizations)
        {
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

        }

        if ((applyCoreOptimizations || applyCaveRockVisibility)
            && CaveRockVisibility.Queue(data, compiler) is { } caveDecision)
            decisions?.Add(caveDecision);

        if (applyScheduler)
        {
            var fishStep = data.Code.ByName("gml_Object_Fish_Step_2")
                ?? throw new InvalidDataException("Fish End Step missing: use the original legacy data.win.");
            compiler.QueueCodeReplace(fishStep, PerfMode.Prefix(data) + ModObjects.Decompile(data, fishStep));
        }

        // Leave SceneItem's per-instance depth transform alone. Runtime profiling on large
        // packs showed the cache probes cost more than the small transform they replace.
        if (applyScheduler) PerfMode.Queue(data, compiler, debug);
        ModObjects.Compile(compiler);
        if (applyFishGrid)
        {
            string gridDecision = FishGrid.Apply(data);
            decisions?.Add(gridDecision);
        }
        if (applyDecorSleep)
        {
            var decorDecisions = DecorSleep.Apply(data);
            decisions?.AddRange(decorDecisions);
        }
        if (applyFishSleep)
        {
            var sleepDecisions = FishSleep.Apply(data, [], debug: debug);
            decisions?.AddRange(sleepDecisions);
        }
        if (masksRect || fishLod || decorAnimNative || applyCacheHot || particlesFx)
        {
            var cheapWinDecisions = GmlCheapWins.Apply(data, [], masksRect, fishLod, decorAnimNative, applyCacheHot, particlesFx);
            decisions?.AddRange(cheapWinDecisions);
        }

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
