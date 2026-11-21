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

        // Guard FishSmallSwarm's array-indexed instance_nearest lookups when fish-cap wipes out a tier.
        const string swarmCode = "gml_Object_FishSmallSwarm_Other_10";
        if (data.GameObjects.ByName("FishMedium") is not null
            && data.GameObjects.ByName("FishLargeEarly") is not null
            && data.GameObjects.ByName("FishHuge") is not null
            && (recompile.TryGetValue(swarmCode, out string? swarmText)
                || (baseline.TryGetValue(swarmCode, out var swarmBase) && swarmBase.Format == "gml" && (swarmText = swarmBase.Text) is not null)))
        {
            string guardedSwarm = GuardFishSmallSwarm(swarmText);
            if (guardedSwarm != swarmText)
                recompile[swarmCode] = guardedSwarm;
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

    private static string GuardFishSmallSwarm(string text)
    {
        if (text.Contains("instance_exists(FishMedium)", StringComparison.Ordinal))
            return text;
        var match = Regex.Match(text, @"(?m)^([ \t]*)if \(other\.isVisible\)\s*$");
        if (!match.Success) return text;
        string med = AssetSymbols.Symbol("objects", "FishMedium");
        string large = AssetSymbols.Symbol("objects", "FishLargeEarly");
        string huge = AssetSymbols.Symbol("objects", "FishHuge");
        return text[..match.Index]
            + $"{match.Groups[1].Value}if (other.isVisible && instance_exists({med}) && instance_exists({large}) && instance_exists({huge}))"
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
