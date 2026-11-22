using System.Text.RegularExpressions;
using Jamm.Core;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleInstruction;

namespace Jamm.ModMaker;

/// <summary>
/// Test-only fish experiment instrumentation for <see cref="BenchmarkMod"/>:
/// guards external fish references, injects runtime <c>nostep</c>/<c>nodraw</c> early-exit guards,
/// wraps fish step events with cheap <c>get_timer()</c> timing hooks, and freezes spawners/thinners.
/// </summary>
internal static class BenchmarkFishExperiments
{
    internal const string EnterScript = "jamm_b_fish_step_enter";
    internal const string LeaveScript = "jamm_b_fish_step_leave";

    /// <summary>Returns all objects in the <c>Fish</c> parent hierarchy (including <c>Fish</c> itself).</summary>
    internal static List<UndertaleGameObject> FindFishHierarchy(UndertaleData data)
    {
        var fishRoot = data.GameObjects.ByName("Fish");
        if (fishRoot is null) return [];
        var result = new List<UndertaleGameObject>();
        foreach (var obj in data.GameObjects)
        {
            if (InheritsFrom(obj, fishRoot))
                result.Add(obj);
        }
        return result;
    }

    internal static bool InheritsFrom(UndertaleGameObject obj, UndertaleGameObject root)
    {
        int guard = 0;
        for (var cursor = obj; cursor is not null && guard++ < 256; cursor = cursor.ParentId)
        {
            if (cursor == root) return true;
        }
        return false;
    }

    /// <summary>
    /// Runs JAMM's <see cref="InstanceGuard"/> over fish references so <c>nostep</c>,
    /// <c>deactivate</c>, and <c>fish-cap</c> never crash on unguarded references.
    /// </summary>
    internal static void GuardFishReferences(UndertaleData data)
    {
        if (data.GameObjects.ByName("Fish") is null) return;

        var names = SemanticReferences.Names(data);
        var ambiguous = AssetSymbols.Ambiguous([names]);
        var baseline = CodeDecompiler.ReadAll(data, "bench", _ => { }, ambiguous, AnalysisOptions.Default);

        var recompile = new Dictionary<string, string>(StringComparer.Ordinal);
        var generated = new Dictionary<string, string>(StringComparer.Ordinal);
        var decisions = new List<string>();

        InstanceGuard.Apply(data, data, baseline, recompile, generated, decisions);

        // Guard every nearest[i] = instance_nearest(...) block (FishSmallSwarm, Cuddlefish, Bulbfish, Snobfish, etc.)
        // when --fish-cap or deactivate removes all instances of a referenced tier/object.
        var objectNames = data.GameObjects.Select(o => o.Name.Content).ToHashSet(StringComparer.Ordinal);
        foreach (var (codeName, entry) in baseline)
        {
            if (entry.Format != "gml") continue;
            string current = recompile.GetValueOrDefault(codeName) ?? entry.Text;
            string guarded = GuardSwarmNearestBlocks(data, current, objectNames);
            if (guarded != current)
                recompile[codeName] = guarded;
        }

        // Guard stale instance references after fish-cap destroys instances during gameplay or setup.
        PatchEntry("gml_Script_fish_flock",
            "if (jamm_iv_state(pal) != 0",
            "if (pal > 0 && !instance_exists(pal)) pal = -1;\nif (jamm_iv_state(pal) != 0");
        PatchEntry("gml_Script_fish_deloop",
            "if (target > 0 && object_is_ancestor(",
            "if (target > 0 && !instance_exists(target)) target = -1;\nif (target > 0 && object_is_ancestor(");
        PatchEntry("gml_Script_fish_deloop",
            "&& target.target > 0)",
            "&& target.target > 0 && instance_exists(target.target))");
        PatchEntry("gml_Script_fish_target_nearest_fish_late",
            "&& target.target.id != id)",
            "&& target.target > 0 && instance_exists(target.target) && target.target.id != id)");
        PatchEntry("gml_Script_fish_target_furthest_fish",
            "while (instance_number(argument0) > 0 && target.state != 0)",
            "while (instance_number(argument0) > 0 && target > 0 && instance_exists(target) && target.state != 0)");
        PatchEntry("gml_Script_fish_die",
            "if (instance_exists(CatSwim) && CatSwim.holding.id == id)",
            "if (instance_exists(CatSwim) && CatSwim.holding > 0 && instance_exists(CatSwim.holding) && CatSwim.holding.id == id)");
        PatchEntry("gml_Object_Bonefish_Other_10",
            "    if (target > 0)\n    {\n        fish_elast_towards_target",
            "    if (target > 0 && !instance_exists(target)) target = -1;\n    if (target > 0)\n    {\n        fish_elast_towards_target");
        PatchEntry("gml_Object_Bonefish_Other_10",
            "        if (object_is_ancestor(target.target.object_index,",
            "        if (target.target > 0 && instance_exists(target.target) && object_is_ancestor(target.target.object_index,");
        PatchEntry("gml_Object_Stalker_Other_10",
            "    if (point_distance(x, y, target.x, target.y) > 400)",
            "    if (target > 0 && instance_exists(target) && point_distance(x, y, target.x, target.y) > 400)");
        PatchEntry("gml_Object_Snobfish_Other_22",
            "if (bro > 0 && bro.object_index ==",
            "if (bro > 0 && instance_exists(bro) && bro.object_index ==");
        PatchEntry("gml_Object_Curby_Other_13",
            "if (thrall_target_fish > 0 && point_distance(",
            "if (thrall_target_fish > 0 && instance_exists(thrall_target_fish) && point_distance(");
        PatchEntry("gml_Object_EvilCouple_Other_10",
            "if (partner > 0)",
            "if (partner > 0 && !instance_exists(partner)) partner = -1;\nif (partner > 0)");
        PatchEntry("gml_Object_FishYinYang_Other_10",
            "if (partner > 0)",
            "if (partner > 0 && !instance_exists(partner)) partner = -1;\nif (partner > 0)");
        PatchEntry("gml_Object_Centy_Other_10",
            "if (leader > 0)",
            "if (leader > 0 && !instance_exists(leader)) leader = -1;\nif (leader > 0)");
        PatchEntry("gml_Object_Chrono_Other_10",
            "if (leader > 0)",
            "if (leader > 0 && !instance_exists(leader)) leader = -1;\nif (leader > 0)");
        PatchEntry("gml_Object_Thermo_Other_10",
            "if (leader > 0)",
            "if (leader > 0 && !instance_exists(leader)) leader = -1;\nif (leader > 0)");
        PatchEntry("gml_Object_Bombig_Other_10",
            "if (inhale_target > 0 && inhale_timer < 45)",
            "if (inhale_target > 0 && !instance_exists(inhale_target)) { inhale_target = -1; inhaling = 0; }\nif (inhale_target > 0 && inhale_timer < 45)");
        PatchEntry("gml_Object_Baby_Sizzer_Other_10",
            "if (parent.state == 6 || parent.state == 4)",
            "if (parent <= 0 || !instance_exists(parent)) { start_x = x; start_y = y; } else if (parent.state == 6 || parent.state == 4)");
        PatchEntry("gml_Object_Carver_Other_13",
            "target_angle = point_direction(x, y, trail.x, trail.y);",
            "if (trail <= 0 || !instance_exists(trail)) { isDragging = 0; exit; }\ntarget_angle = point_direction(x, y, trail.x, trail.y);");
        PatchEntry("gml_Object_Bomb_Step_2",
            "if (pal > 0)",
            "if (pal > 0 && instance_exists(pal))");
        PatchEntry("gml_Object_BombRock_Step_2",
            "if (pal > 0)",
            "if (pal > 0 && instance_exists(pal))");
        PatchEntry("gml_Object_Book_Step_2",
            "if (pal > 0)",
            "if (pal > 0 && instance_exists(pal))");
        PatchEntry("gml_Object_Andlit_Other_10",
            "target_x = pather.x;",
            "if (pather <= 0 || !instance_exists(pather)) exit;\ntarget_x = pather.x;");
        PatchEntry("gml_Object_Bramble_Other_10",
            "target_x = pather.x;",
            "if (pather <= 0 || !instance_exists(pather)) exit;\ntarget_x = pather.x;");
        PatchEntry("bluo_other",
            "target_x = pather.x;",
            "if (pather <= 0 || !instance_exists(pather)) exit;\ntarget_x = pather.x;");
        PatchEntry("gml_Object_Great_Carver_Other_10",
            "target_x = target.x;",
            "if (target <= 0 || !instance_exists(target)) { target_x = start_x; target_y = start_y; fish_elast_towards_target(0.02, 0.9); fish_lunge(); exit; }\ntarget_x = target.x;");
        PatchEntry("gml_Object_Glutton_Other_10",
            "if (point_distance(x, y, potential.x, potential.y) < 1000)",
            "if (potential > 0 && instance_exists(potential) && point_distance(x, y, potential.x, potential.y) < 1000)");
        PatchEntry("gml_Object_Glutton_Other_10",
            "if (target > 0)",
            "if (target > 0 && instance_exists(target))");
        PatchEntry("gml_Object_Rodilav_Other_10",
            "if (point_distance(x, y, potential.x, potential.y) < 1000)",
            "if (potential > 0 && instance_exists(potential) && point_distance(x, y, potential.x, potential.y) < 1000)");
        PatchEntry("gml_Object_Rodilav_Other_10",
            "if (target > 0)",
            "if (target > 0 && instance_exists(target))");
        PatchEntry("gml_Object_Conpas_Other_10",
            "direction = point_direction(x, y, target.x, target.y);",
            "if (target > 0 && instance_exists(target)) direction = point_direction(x, y, target.x, target.y);");
        PatchEntry("gml_Object_Conpas_Other_13",
            "target_angle = point_direction(x, y, target.x, target.y);",
            "if (target > 0 && instance_exists(target)) target_angle = point_direction(x, y, target.x, target.y); else target_angle = direction;");
        PatchEntry("gml_Object_Tik_Tik_Other_10",
            "if (object_is_ancestor(target.object_index, FishLargeLate) && target.target > 0)",
            "if (target > 0 && instance_exists(target) && object_is_ancestor(target.object_index, FishLargeLate) && target.target > 0 && instance_exists(target.target))");
        PatchEntry("gml_Object_Chompy_Other_11",
            "if (hope > 0 && hope.state != 6)",
            "if (hope > 0 && instance_exists(hope) && hope.state != 6)");
        PatchEntry("gml_Object_Egg_Step_2",
            "if (pal > 0)",
            "if (pal > 0 && instance_exists(pal))");
        PatchEntry("gml_Object_SmileyEgg_Step_2",
            "if (pal > 0)",
            "if (pal > 0 && instance_exists(pal))");
        PatchEntry("gml_Object_Edible_Step_2",
            "if (pal > 0)",
            "if (pal > 0 && instance_exists(pal))");
        PatchEntry("gml_Object_Swordfish_Other_10",
            "if (target > 0)",
            "if (target > 0 && instance_exists(target))");
        PatchEntry("AkiOther10Fixed",
            "if (parent.state == 6 || parent.state == 4)",
            "if (parent <= 0 || !instance_exists(parent)) { start_x = x; start_y = y; } else if (parent.state == 6 || parent.state == 4)");
        PatchEntry("gml_Object_Bishop_Other_10",
            "if (escort_target.state == 3)",
            "if (escort_target <= 0 || !instance_exists(escort_target)) { target_x = start_x; target_y = start_y; if (point_distance(x, y, target_x, target_y) > 200) fish_elast_towards_target(0.01, 0.985); fish_lunge(); exit; }\nif (escort_target.state == 3)");
        PatchEntry("gml_Object_Bishop_Other_18",
            "target_x = pather.x;",
            "if (pather <= 0 || !instance_exists(pather)) exit;\ntarget_x = pather.x;");
        PatchEntry("MagnapinnaDestroy",
            "for (var j = 0; j < tentacleLength; j++)",
            "for (var j = 0; j < arm_lengths[i]; j++)");
        PatchEntry("MagnapinnaEndstep",
            "var prev_joint_x = tentacle_body.x",
            "if (!instance_exists(tentacle_body)) continue;\n    var prev_joint_x = tentacle_body.x");
        PatchEntry("gml_Object_Lindfin_Other_10",
            "target_x = pather.x;",
            "if (pather <= 0 || !instance_exists(pather)) exit;\ntarget_x = pather.x;");
        PatchEntry("gml_Object_DJfin_Other_12",
            "if (mp3 > 0)",
            "if (mp3 > 0 && instance_exists(mp3))");
        PatchEntry("gml_Objct_DJfin_Other_17",
            "if (mp3 > 0 && mp3.hooked_to_lure)",
            "if (mp3 > 0 && instance_exists(mp3) && mp3.hooked_to_lure)");
        PatchEntry("gml_Object_Felish_Step_0",
            "target.hasFelish = 0;",
            "if (instance_exists(target)) target.hasFelish = 0;");
        PatchEntry("gml_Object_Spotter_Step_0",
            "target.hasFelish = 0;",
            "if (instance_exists(target)) target.hasFelish = 0;");
        PatchEntry("gml_Object_Chaw_Other_18",
            "angle_lock = 0;",
            "if (piercer <= 0 || !instance_exists(piercer)) exit;\nangle_lock = 0;");
        PatchEntry("gml_Object_Piercer_Draw_72",
            "    with (chaw)",
            "    if (chaw > 0 && instance_exists(chaw)) with (chaw)");
        PatchEntry("gml_Object_Lav_Other_10",
            "target_x = pather.x;",
            "if (pather <= 0 || !instance_exists(pather)) exit;\ntarget_x = pather.x;");
        PatchEntry("gml_Object_Lav_Other_10",
            "if (instance_exists(Rodi))",
            "if (instance_exists(Rodi) && Rodi.pather > 0 && instance_exists(Rodi.pather))");
        PatchEntry("gml_Object_Chawsal_Other_10",
            "target_x = pather.x;",
            "if (pather <= 0 || !instance_exists(pather)) exit;\ntarget_x = pather.x;");
        PatchEntry("gml_Object_Chawk_Other_10",
            "if (Lure.fish > 0 && Lure.fish.object_index == Chaw)",
            "if (Lure.fish > 0 && instance_exists(Lure.fish) && Lure.fish.object_index == Chaw)");
        PatchEntry("gml_Object_Flash_Other_18",
            "if (hit_fish > 0)",
            "if (hit_fish > 0 && instance_exists(hit_fish))");
        PatchEntry("gml_Object_Flechette_Other_18",
            "if (point_distance(x, y, master.x, master.y) < 50)",
            "if (master <= 0 || !instance_exists(master)) { state = 2; exit; }\nif (point_distance(x, y, master.x, master.y) < 50)");
        PatchEntry("gml_Object_Glutton_Other_18",
            "if (Lure.fish.object_index != Gawll)",
            "if (Lure.fish <= 0 || !instance_exists(Lure.fish)) { state = 2; exit; }\nif (Lure.fish.object_index != Gawll)");
        PatchEntry("gml_Object_Rodilav_Other_18",
            "if (Lure.fish.object_index != Rodi && Lure.fish.object_index != Lav)",
            "if (Lure.fish <= 0 || !instance_exists(Lure.fish)) { state = 2; exit; }\nif (Lure.fish.object_index != Rodi && Lure.fish.object_index != Lav)");
        PatchEntry("gml_Object_Torby_Other_18",
            "if (mate.y > 1594",
            "if (mate <= 0 || !instance_exists(mate)) { state = 2; exit; }\nif (mate.y > 1594");
        PatchEntry("gml_Object_Snobfish_Other_10",
            "if (bro > 0 && ",
            "if (bro > 0 && !instance_exists(bro)) bro = -1;\nif (bro > 0 && ");

        void PatchEntry(string name, string before, string after)
        {
            string? text = recompile.GetValueOrDefault(name)
                ?? (baseline.TryGetValue(name, out var baseCode) && baseCode.Format == "gml" ? baseCode.Text : null);
            if (text is null || !text.Contains(before, StringComparison.Ordinal) || text.Contains(after, StringComparison.Ordinal))
                return;
            recompile[name] = text.Replace(before, after, StringComparison.Ordinal);
        }

        if (recompile.Count == 0) return;

        var compiler = new CompileGroup(data);
        var finalNames = SemanticReferences.Names(data);
        foreach (var (name, gml) in recompile)
        {
            var code = data.Code.ByName(name) ?? UndertaleCode.CreateEmptyEntry(data, name);
            compiler.QueueCodeReplace(code, AssetSymbols.Link(gml, name, finalNames));
        }
        ModObjects.Compile(compiler);
    }

    private static string GuardSwarmNearestBlocks(UndertaleData data, string text, HashSet<string> objectNames)
    {
        if (!text.Contains("nearest[", StringComparison.Ordinal))
            return text;
        var match = Regex.Match(text, @"(?m)^([ \t]*)if \(other\.isVisible\)\s*$");
        if (!match.Success) return text;

        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"nearest\[\s*\d+\s*\]\s*=\s*instance_nearest\s*\([^,]+,[^,]+,\s*([A-Za-z_]\w*)\s*\)"))
        {
            string target = m.Groups[1].Value;
            if (AssetSymbols.Parse(target, out string family, out string objName) && family == "objects")
                needed.Add(AssetSymbols.Symbol("objects", objName));
            else if (objectNames.Contains(target) && data.GameObjects.ByName(target) is not null)
                needed.Add(AssetSymbols.Symbol("objects", target));
        }
        if (needed.Count == 0) return text;

        string checks = string.Join(" && ", needed.OrderBy(s => s, StringComparer.Ordinal).Select(s => $"instance_exists({s})"));
        return text[..match.Index]
            + $"{match.Groups[1].Value}if (other.isVisible && {checks})"
            + text[(match.Index + match.Length)..];
    }

    /// <summary>
    /// Instruments all <c>Fish</c> hierarchy step/draw events with runtime-toggleable early-exit
    /// guards and <c>fish_step_us</c> enter/leave hooks, and adds spawner/thinner freeze guards.
    /// </summary>
    internal static void InstrumentFishAndSpawners(UndertaleData data)
    {
        if (data.GameObjects.ByName("Fish") is null) return;

        var (enterFn, leaveFn) = AddTimingScripts(data);

        var nostepPrefix = CompileGuardSnippet(data, "if (global.jamm_b_fish_nostep) exit;");
        var nodrawPrefix = CompileGuardSnippet(data, "if (global.jamm_b_fish_nodraw) exit;");
        var freezeExitPrefix = CompileGuardSnippet(data, "if (global.jamm_b_freeze_spawners) exit;");
        var thinExitPrefix = CompileGuardSnippet(data, "if (global.jamm_b_freeze_spawners || global.jamm_b_no_thin) exit;");
        var freezeReturnNeg4Prefix = CompileGuardSnippet(data, "if (global.jamm_b_freeze_spawners) return -4;");
        var freezeReturnZeroPrefix = CompileGuardSnippet(data, "if (global.jamm_b_freeze_spawners) return 0;");

        var fishObjects = FindFishHierarchy(data);
        var fishStepCodes = CollectUniqueEventCodes(fishObjects, EventType.Step);
        var fishDrawCodes = CollectUniqueEventCodes(fishObjects, EventType.Draw);

        foreach (var code in fishStepCodes)
        {
            if (!LegacyCodeHooks.Supports(code)) continue;
            LegacyCodeHooks.Insert(code, _ => default, enterFn, leaveFn);
            PrependInstructions(code, nostepPrefix);
        }

        foreach (var code in fishDrawCodes)
        {
            if (!LegacyCodeHooks.Supports(code)) continue;
            PrependInstructions(code, nodrawPrefix);
        }

        // Guard respawn and modded population scripts.
        var handledCodes = new HashSet<UndertaleCode>(fishStepCodes);
        handledCodes.UnionWith(fishDrawCodes);

        GuardScript("gml_Script_fish_respawn", freezeReturnNeg4Prefix);
        GuardScript("gml_Script_jamm_weather_population", freezeReturnZeroPrefix);
        GuardScript("gml_Script_jamm_weather_gate", freezeReturnZeroPrefix);
        GuardScript("gml_Script_jamm_weather_restore_fish", freezeReturnZeroPrefix);

        if (data.Code.ByName("gml_Object_FishThinner_Other_10") is { } thinnerUser0
            && handledCodes.Add(thinnerUser0) && LegacyCodeHooks.Supports(thinnerUser0))
        {
            PrependInstructions(thinnerUser0, thinExitPrefix);
        }

        var swarmRoot = data.GameObjects.ByName("Swarm");
        foreach (var obj in data.GameObjects)
        {
            string name = obj.Name.Content;
            if (name.StartsWith("obj_jamm_", StringComparison.Ordinal)) continue;
            bool isSpawnerOrThinner = name.Contains("Spawner", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Thinner", StringComparison.OrdinalIgnoreCase)
                || (swarmRoot is not null && InheritsFrom(obj, swarmRoot));
            if (!isSpawnerOrThinner) continue;

            foreach (var code in CollectSpawnerEventCodes(obj))
            {
                if (handledCodes.Add(code) && LegacyCodeHooks.Supports(code))
                    PrependInstructions(code, freezeExitPrefix);
            }
        }

        void GuardScript(string codeName, IReadOnlyList<UndertaleInstruction> prefix)
        {
            var code = data.Code.ByName(codeName);
            if (code is not null && code.ParentEntry is null && handledCodes.Add(code) && LegacyCodeHooks.Supports(code))
                PrependInstructions(code, prefix);
        }
    }

    private static (UndertaleFunction Enter, UndertaleFunction Leave) AddTimingScripts(UndertaleData data)
    {
        var compiler = new CompileGroup(data);
        var enter = Add(EnterScript, """
            if (variable_global_exists("jamm_b_fish_depth"))
            {
                if (global.jamm_b_fish_depth == 0)
                {
                    global.jamm_b_fish_t0 = get_timer();
                }
                global.jamm_b_fish_depth += 1;
            }
            """);
        var leave = Add(LeaveScript, """
            if (variable_global_exists("jamm_b_fish_depth"))
            {
                global.jamm_b_fish_depth -= 1;
                if (global.jamm_b_fish_depth <= 0)
                {
                    global.jamm_b_fish_depth = 0;
                    global.jamm_b_fish_step_us += max(0, get_timer() - global.jamm_b_fish_t0);
                }
            }
            """);
        ModObjects.Compile(compiler);
        return (enter, leave);

        UndertaleFunction Add(string name, string body)
        {
            if (data.Scripts.ByName(name) is not null)
                throw new InvalidDataException($"'{name}' already exists: use an uninstrumented input archive.");
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
            compiler.QueueCodeReplace(code, body);
            return data.Functions.EnsureDefined(name, data.Strings);
        }
    }

    private static List<UndertaleCode> CollectUniqueEventCodes(IEnumerable<UndertaleGameObject> objects, EventType eventType)
    {
        var seen = new HashSet<UndertaleCode>();
        var list = new List<UndertaleCode>();
        int typeIndex = (int)eventType;
        foreach (var obj in objects)
        {
            if (typeIndex >= obj.Events.Count) continue;
            foreach (var ev in obj.Events[typeIndex])
            foreach (var action in ev.Actions)
            {
                if (action.CodeId is { ParentEntry: null } code && seen.Add(code))
                    list.Add(code);
            }
        }
        return list;
    }

    private static IEnumerable<UndertaleCode> CollectSpawnerEventCodes(UndertaleGameObject obj)
    {
        foreach (EventType type in new[] { EventType.Step, EventType.Alarm, EventType.Other })
        {
            int typeIndex = (int)type;
            if (typeIndex >= obj.Events.Count) continue;
            foreach (var ev in obj.Events[typeIndex])
            {
                if (type == EventType.Other && ev.EventSubtype < 10) continue;
                foreach (var action in ev.Actions)
                {
                    if (action.CodeId is { ParentEntry: null } code)
                        yield return code;
                }
            }
        }
    }

    private static List<UndertaleInstruction> CompileGuardSnippet(UndertaleData data, string gml)
    {
        const string tempName = "__jamm_b_tmp_guard";
        var temp = UndertaleCode.CreateEmptyEntry(data, tempName);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(temp, gml);
        ModObjects.Compile(compiler);
        var instructions = temp.Instructions.ToList();
        data.Code.Remove(temp);
        if (data.CodeLocals.ByName(tempName) is { } locals)
            data.CodeLocals.Remove(locals);
        return instructions;
    }

    private static void PrependInstructions(UndertaleCode code, IReadOnlyList<UndertaleInstruction> prefix)
    {
        if (prefix.Count == 0 || code.Instructions.Count == 0) return;
        var cloned = new List<UndertaleInstruction>(prefix.Count + code.Instructions.Count);
        foreach (var instr in prefix)
            cloned.Add(CloneInstruction(instr));
        cloned.AddRange(code.Instructions);
        code.Instructions.Clear();
        code.Instructions.AddRange(cloned);
        code.UpdateLength();
    }

    private static UndertaleInstruction CloneInstruction(UndertaleInstruction src)
    {
        var copy = new UndertaleInstruction
        {
            Kind = src.Kind,
            Type1 = src.Type1,
            Type2 = src.Type2,
            TypeInst = src.TypeInst,
            ValueLong = src.ValueLong
        };
        if (src.Kind == Opcode.Cmp)
            copy.ComparisonKind = src.ComparisonKind;
        if (GetInstructionType(src.Kind) == InstructionType.GotoInstruction)
            copy.JumpOffset = src.JumpOffset;
        if (src.Kind == Opcode.Call)
            copy.ArgumentsCount = src.ArgumentsCount;
        if (src.Kind == Opcode.Dup)
            copy.Extra = src.Extra;
        if (src.ValueVariable is not null)
        {
            copy.ValueVariable = src.ValueVariable;
            copy.ReferenceType = src.ReferenceType;
        }
        else if (src.ValueFunction is not null)
        {
            copy.ValueFunction = src.ValueFunction;
        }
        else if (src.ValueString is not null)
        {
            copy.ValueString = src.ValueString;
        }
        return copy;
    }
}
