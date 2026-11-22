using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Building;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Build-time Fish Sleep eligibility record for one concrete fish object.</summary>
/// <param name="ObjectName">Fish object name.</param>
/// <param name="Covered">Whether instances of this fish may sleep outside the relevance zone.</param>
/// <param name="Reason">Short human-readable classification reason.</param>
/// <param name="Tier">Resolved standard parent tier (<c>FishSmall</c>, <c>FishMedium</c>, <c>FishLarge</c>, or <c>Other</c>).</param>
public sealed record FishSleepDecision(string ObjectName, bool Covered, string Reason, string Tier);

/// <summary>Axis-aligned relevance rectangle in world coordinates.</summary>
/// <param name="X1">Minimum X.</param>
/// <param name="Y1">Minimum Y.</param>
/// <param name="X2">Maximum X.</param>
/// <param name="Y2">Maximum Y.</param>
public readonly record struct FishSleepBox(double X1, double Y1, double X2, double Y2)
{
    /// <summary>Returns true when <paramref name="x"/>, <paramref name="y"/> lies within this rectangle expanded by <paramref name="expand"/>.</summary>
    public bool Contains(double x, double y, double expand = 0) =>
        x >= X1 - expand && x <= X2 + expand && y >= Y1 - expand && y <= Y2 + expand;
}

/// <summary>
/// Puts eligible ordinary fish outside the player's relevance zone to sleep (early-exit Step/Draw
/// while keeping instances, variables, IDs, population counts, and saves intact) and catches up
/// plausibly on wake.
/// </summary>
public static partial class FishSleep
{
    /// <summary>Persistent controller object that tracks the player relevance zone and sleep tick counter.</summary>
    public const string Controller = "obj_jamm_fish_sleep";

    /// <summary>Default Fish Sleep margin in pixels around views, lure, boat, Girl, sonar, and targets.</summary>
    public const int DefaultMargin = 2000;

    /// <summary>Minimum allowed Fish Sleep margin in pixels.</summary>
    public const int MinMargin = 1200;

    /// <summary>Maximum allowed Fish Sleep margin in pixels.</summary>
    public const int MaxMargin = 20000;

    private const string GuardLine = "if (variable_instance_exists(id, \"jamm_asleep\") && jamm_asleep) exit;";

    private static readonly HashSet<string> AbstractTiers = new(StringComparer.Ordinal)
    {
        "Fish", "FishSmall", "FishSmallTropic", "FishSmallSwarm", "FishNotSmall",
        "FishMedium", "FishMinorPiece", "FishLarge", "FishLargeEarly", "FishLargeLate",
        "FishLargePather", "FishHuge", "FishHugeEarly"
    };

    private static readonly HashSet<string> SpecialSpecies = new(StringComparer.Ordinal)
    {
        "Dragon", "Noctis", "Torby", "Ghost", "Abyssal", "Kingfish", "Shaku", "Bagu", "Curby",
        "Centy", "Chrono", "Thermo", "Goldfish", "EvilCoupleFish", "FishYinYang", "Rodi", "Lav"
    };

    private static readonly HashSet<string> SafeCustomStepObjects = new(StringComparer.Ordinal)
    {
        "Chaw", "Skrimp", "Bombat", "Reddart_Frog", "Boom_Puff", "Primal_Turgeon",
        "Glio", "Buffalofish", "Gloombat", "Anglerfish", "Airy", "Duleoculi",
        "Felish", "Spotter", "Ghast", "Tik_Tik", "Koin", "Vouval", "Bluecheek_Frog"
    };

    [GeneratedRegex(@"//[^\n]*|/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex Comments();

    [GeneratedRegex(@"if\s*\(\s*variable_instance_exists\s*\(\s*id\s*,\s*""jamm_asleep""\s*\)\s*&&\s*jamm_asleep\s*\)\s*(?:\{\s*exit\s*;\s*\}|exit\s*;)", RegexOptions.Compiled)]
    private static partial Regex GuardPrefixPattern();

    [GeneratedRegex(@"else\s*\{\s*cull_trigger\s*=\s*0\s*;\s*\}", RegexOptions.Compiled)]
    private static partial Regex FishThinnerResetBranch();

    [GeneratedRegex(@"^(?:(?:action_inherited|event_inherited)\s*\(\s*\)\s*;\s*)?if\s*\(\s*alarm\s*\[\s*\d+\s*\]\s*>\s*\d+\s*\)\s*\{\s*alarm\s*\[\s*\d+\s*\]\s*=\s*\d+\s*;\s*\}$", RegexOptions.Compiled)]
    private static partial Regex AlarmClampOnlyStep();

    [GeneratedRegex(@"^\s*(?:action_inherited|event_inherited)\s*\(\s*\)\s*;", RegexOptions.Compiled)]
    private static partial Regex InheritedCallPrefix();

    [GeneratedRegex(@"\b(instance_create|instance_destroy|audio_play_sound|global\.)", RegexOptions.Compiled)]
    private static partial Regex UnsafeStepSideEffects();

    [GeneratedRegex(@"^(?:if\s*\(\s*(?:state\s*==\s*1|id\s*>\s*other\.id\b[^)]*)\s*\)\s*\{[\s\S]*\}|//.*)$", RegexOptions.Compiled)]
    private static partial Regex ActiveStateOnlyCollision();

    [GeneratedRegex(@"\b(instance_create|instance_destroy|alarm\s*\[|state\s*=|global\s*\.)", RegexOptions.Compiled)]
    private static partial Regex DrawSideEffects();

    /// <summary>Analyzes all concrete <c>Fish</c> subclasses in <paramref name="data"/> for Fish Sleep eligibility.</summary>
    public static IReadOnlyList<FishSleepDecision> AnalyzeEligibility(UndertaleData data, IReadOnlySet<string>? areaFishNames = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var fishRoot = data.GameObjects.ByName("Fish");
        if (fishRoot is null) return [];

        var fishHuge = data.GameObjects.ByName("FishHuge");
        var fishLargePather = data.GameObjects.ByName("FishLargePather");
        var fishSmall = data.GameObjects.ByName("FishSmall");
        var fishMedium = data.GameObjects.ByName("FishMedium");
        var fishLarge = data.GameObjects.ByName("FishLarge");

        var areaPlaced = FindAreaPlacedFishNames(data, fishRoot);
        if (areaFishNames is not null) areaPlaced.UnionWith(areaFishNames);

        var results = new List<FishSleepDecision>();
        foreach (var obj in data.GameObjects)
        {
            string name = obj.Name.Content;
            if (!InheritsFrom(obj, fishRoot) || AbstractTiers.Contains(name))
                continue;

            string tier = fishLarge is not null && InheritsFrom(obj, fishLarge) ? "FishLarge"
                : fishMedium is not null && InheritsFrom(obj, fishMedium) ? "FishMedium"
                : fishSmall is not null && InheritsFrom(obj, fishSmall) ? "FishSmall"
                : "Other";

            var (covered, reason) = Classify(data, obj, fishRoot, fishHuge, fishLargePather, fishSmall, fishMedium, fishLarge, areaPlaced, tier);
            results.Add(new FishSleepDecision(name, covered, reason, tier));
        }

        return results;
    }

    private static (bool Covered, string Reason) Classify(
        UndertaleData data,
        UndertaleGameObject obj,
        UndertaleGameObject fishRoot,
        UndertaleGameObject? fishHuge,
        UndertaleGameObject? fishLargePather,
        UndertaleGameObject? fishSmall,
        UndertaleGameObject? fishMedium,
        UndertaleGameObject? fishLarge,
        HashSet<string> areaPlaced,
        string tier)
    {
        if (fishHuge is not null && InheritsFrom(obj, fishHuge))
            return (false, "huge/boss parent");
        if (fishLargePather is not null && InheritsFrom(obj, fishLargePather))
            return (false, "pather trail parent");

        bool hasStandardTier = (fishSmall is not null && InheritsFrom(obj, fishSmall))
            || (fishMedium is not null && InheritsFrom(obj, fishMedium))
            || (fishLarge is not null && InheritsFrom(obj, fishLarge));
        if (!hasStandardTier)
            return (false, "non-standard parent");

        if (obj.Name.Content.Contains("silo", StringComparison.OrdinalIgnoreCase) || areaPlaced.Contains(obj.Name.Content))
            return (false, "silo/area fish");

        for (var cursor = obj; cursor is not null && cursor != fishRoot; cursor = cursor.ParentId)
        {
            string cname = cursor.Name.Content;
            if (SpecialSpecies.Contains(cname))
                return (false, $"special/partner species ({cname})");
            if (AbstractTiers.Contains(cname))
                continue;

            if (HasNonTrivialStep(data, cursor))
                return (false, "custom Step event");
            if (HasAnyEvent(cursor, EventType.Alarm))
                return (false, "custom Alarm event");
            if (HasAnyEvent(cursor, EventType.Destroy))
                return (false, "custom Destroy event");
            if (HasUnsafeCollision(data, cursor))
                return (false, "custom Collision event");

            if (cursor.Events.Count > (int)EventType.Other)
            {
                foreach (var ev in cursor.Events[(int)EventType.Other])
                {
                    if (ev.Actions.Count == 0) continue;
                    // Subtypes 10..19, 21..23, 25 are standard Fish_Step_2 / hook / respawn / death user hooks
                    // (unreachable while Fish_Step_2 exits early at state == 0 || state == 12); keep non-user <10 & 20/24 exempt.
                    if (ev.EventSubtype is not (10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 21 or 22 or 23 or 25))
                        return (false, $"custom Other:{ev.EventSubtype} event");
                }
            }

            if (HasGameplayDrawLogic(data, cursor))
                return (false, "custom Draw logic");
        }

        return (true, $"standard {tier}");
    }

    private static bool HasAnyEvent(UndertaleGameObject obj, EventType type)
    {
        int idx = (int)type;
        return idx < obj.Events.Count && obj.Events[idx].Any(e => e.Actions.Count > 0);
    }

    private static bool HasUnsafeCollision(UndertaleData data, UndertaleGameObject obj)
    {
        int idx = (int)EventType.Collision;
        if (idx >= obj.Events.Count) return false;
        foreach (var ev in obj.Events[idx])
        {
            foreach (var action in ev.Actions)
            {
                if (action.CodeId is not { } code) continue;
                if (!TryDecompile(data, code, out string text)) return true;
                string stripped = GuardPrefixPattern().Replace(Comments().Replace(text, ""), "").Trim();
                if (stripped is "" or "action_inherited();" or "event_inherited();") continue;
                if (ActiveStateOnlyCollision().IsMatch(stripped)) continue;
                return true;
            }
        }
        return false;
    }

    private static bool HasNonTrivialStep(UndertaleData data, UndertaleGameObject obj)
    {
        int idx = (int)EventType.Step;
        if (idx >= obj.Events.Count) return false;
        bool isSafeCustomSpecies = SafeCustomStepObjects.Contains(obj.Name.Content);
        foreach (var ev in obj.Events[idx])
        {
            foreach (var action in ev.Actions)
            {
                if (action.CodeId is not { } code) continue;
                if (!TryDecompile(data, code, out string text)) return true;
                string stripped = GuardPrefixPattern().Replace(Comments().Replace(text, ""), "").Trim();
                if (stripped is "" or "action_inherited();" or "event_inherited();") continue;
                if (ev.EventSubtype == (uint)EventSubtypeStep.EndStep
                    && !(stripped.StartsWith("action_inherited();", StringComparison.Ordinal)
                         || stripped.StartsWith("event_inherited();", StringComparison.Ordinal)))
                {
                    return true;
                }
                if (AlarmClampOnlyStep().IsMatch(stripped)) continue;
                if (isSafeCustomSpecies && !UnsafeStepSideEffects().IsMatch(stripped)) continue;
                return true;
            }
        }
        return false;
    }

    private static bool HasGameplayDrawLogic(UndertaleData data, UndertaleGameObject obj)
    {
        int idx = (int)EventType.Draw;
        if (idx >= obj.Events.Count) return false;
        foreach (var ev in obj.Events[idx])
        {
            foreach (var action in ev.Actions)
            {
                if (action.CodeId is not { } code) continue;
                if (!TryDecompile(data, code, out string text)) return true;
                string stripped = Comments().Replace(text, "");
                if (DrawSideEffects().IsMatch(stripped)) return true;
            }
        }
        return false;
    }

    private static bool TryDecompile(UndertaleData data, UndertaleCode code, out string text)
    {
        try
        {
            text = ModObjects.Decompile(data, code);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            text = "";
            return false;
        }
    }

    private static HashSet<string> FindAreaPlacedFishNames(UndertaleData data, UndertaleGameObject fishRoot)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in data.Rooms)
        {
            foreach (var placement in room.GameObjects)
            {
                if (placement.ObjectDefinition is not { } obj || !InheritsFrom(obj, fishRoot))
                    continue;
                if (placement.CreationCode?.Name.Content is { } cc && cc.StartsWith("gml_RoomCC_jamm_area_", StringComparison.Ordinal))
                    names.Add(obj.Name.Content);
            }
        }
        return names;
    }

    private static bool InheritsFrom(UndertaleGameObject? obj, UndertaleGameObject? root)
    {
        if (root is null) return false;
        int guard = 0;
        for (var cursor = obj; cursor is not null && guard++ < 256; cursor = cursor.ParentId)
        {
            if (cursor == root) return true;
        }
        return false;
    }

    /// <summary>Computes the effective zone margin (always larger than the view width).</summary>
    public static double EffectiveMargin(double configuredMargin, double viewWidth) =>
        Math.Max(Math.Clamp(configuredMargin, MinMargin, MaxMargin), Math.Max(viewWidth, MinMargin) + 200.0);

    /// <summary>Computes hysteresis band width in pixels for a given effective margin.</summary>
    public static double HysteresisBand(double effectiveMargin) =>
        Math.Max(400.0, Math.Min(800.0, Math.Round(effectiveMargin * 0.25)));

    /// <summary>Returns true if <paramref name="x"/>, <paramref name="y"/> lies inside any box expanded by <paramref name="expand"/>.</summary>
    public static bool IsInZone(double x, double y, IReadOnlyList<FishSleepBox> boxes, double expand = 0)
    {
        for (int i = 0; i < boxes.Count; i++)
        {
            if (boxes[i].Contains(x, y, expand))
                return true;
        }
        return false;
    }

    /// <summary>Evaluates sleep/wake state transition with hysteresis.</summary>
    public static bool NextSleepState(bool currentlyAsleep, double x, double y, IReadOnlyList<FishSleepBox> boxes, double hysteresis)
        => currentlyAsleep ? !IsInZone(x, y, boxes, 0) : !IsInZone(x, y, boxes, hysteresis);

    /// <summary>Clamps a wake catch-up target position to water bounds and prevents popping inside the active view.</summary>
    public static (double X, double Y, bool Moved) ClampWakePosition(
        double prevX, double prevY, double candX, double candY,
        double minWaterX, double maxWaterX, double minWaterY, double maxWaterY,
        FishSleepBox? activeView, bool blockedByWall)
    {
        if (blockedByWall)
            return (prevX, prevY, false);

        double nx = Math.Clamp(candX, minWaterX, maxWaterX);
        double ny = Math.Clamp(candY, minWaterY, maxWaterY);
        if (activeView is { } view && view.Contains(nx, ny, 64))
            return (prevX, prevY, false);

        return (nx, ny, true);
    }

    /// <summary>Applies Fish Sleep to a final GM1 archive using default-enabled INI settings.</summary>
    public static IReadOnlyList<string> Apply(UndertaleData data) => Apply(data, []);

    /// <summary>
    /// Analyzes all fish objects in <paramref name="data"/>, emits <c>[CGF-PERF-SLEEP]</c> decisions,
    /// installs <see cref="Controller"/> and <c>jamm_sleep_*</c> helpers, and adds Step/Draw sleep guards
    /// on <c>Fish</c> and covered subclasses.
    /// </summary>
    internal static IReadOnlyList<string> Apply(
        UndertaleData data,
        List<UndertaleCode> edits,
        bool enabledByDefault = true,
        int margin = DefaultMargin,
        IReadOnlySet<string>? areaFishNames = null,
        bool debug = false)
    {
        var decisions = new List<string>();
        if (data.GameObjects.ByName(Controller) is not null)
        {
            decisions.Add("[CGF-PERF-SLEEP] skipped (runtime already installed)");
            return decisions;
        }
        var fishRoot = data.GameObjects.ByName("Fish");
        if (data.GeneralInfo.Major != 1 || data.GeneralInfo.BytecodeVersion != 16
            || fishRoot is null || data.Rooms.ByName("NewWorld") is null)
        {
            decisions.Add("[CGF-PERF-SLEEP] skipped (required GameMaker 1.4 layout, Fish, or NewWorld missing)");
            return decisions;
        }

        var eligibility = AnalyzeEligibility(data, areaFishNames);
        foreach (var entry in eligibility)
        {
            string kind = entry.Covered ? "covered" : "exempt";
            decisions.Add($"[CGF-PERF-SLEEP] {entry.ObjectName}: {kind} ({entry.Reason})");
        }

        var compiler = new CompileGroup(data);
        int clampedMargin = Math.Clamp(margin, MinMargin, MaxMargin);

        AddScript("jamm_sleep_init_types", InitTypesGml(eligibility));
        AddScript("jamm_sleep_add_box", AddBoxGml());
        AddScript("jamm_sleep_begin_step", BeginStepGml(data));
        AddScript("jamm_sleep_in_zone", InZoneGml());
        AddScript("jamm_sleep_should_sleep", ShouldSleepGml(data));
        AddScript("jamm_sleep_enter", EnterGml());
        AddScript("jamm_sleep_wake", WakeGml(data));

        var controller = ModObjects.Create(data, Controller, persistent: true);
        controller.Visible = false;
        AddEvent(EventType.Create, 0u, CreateControllerGml(enabledByDefault, clampedMargin, debug));
        AddEvent(EventType.Destroy, 0u, DestroyControllerGml());
        AddEvent(EventType.Step, (uint)EventSubtypeStep.BeginStep, BeginStepControllerGml(debug));

        // Ensure Fish Create_0 initializes jamm_asleep so direct field reads never throw.
        var fishCreate = fishRoot.Events[(int)EventType.Create]
            .FirstOrDefault(e => e.EventSubtype == 0u)?.Actions.FirstOrDefault()?.CodeId
            ?? data.Code.ByName("gml_Object_Fish_Create_0")
            ?? fishRoot.EventHandlerFor(EventType.Create, 0u, data);
        EnsurePrefix(fishCreate,
            "jamm_asleep = false;\njamm_sleep_tick = 0;\njamm_sleep_state = 0;\n",
            "jamm_asleep =");

        // Ensure Fish Step_2 has the FishSleep prefix (even if a mod replaced Fish_Step_2 during merge).
        var fishStep2 = fishRoot.Events[(int)EventType.Step]
            .FirstOrDefault(e => e.EventSubtype == (uint)EventSubtypeStep.EndStep)?.Actions.FirstOrDefault()?.CodeId
            ?? data.Code.ByName("gml_Object_Fish_Step_2")
            ?? fishRoot.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data);
        EnsurePrefix(fishStep2, Step2Prefix(), "jamm_sleep_wake");

        ApplyPopulationHooks(data, compiler, edits, fishRoot, decisions);

        // Ensure FishLarge Step_0 and Fish Draw_0 guard asleep instances.
        if (data.GameObjects.ByName("FishLarge")?.Events[(int)EventType.Step]
            .FirstOrDefault(e => e.EventSubtype == (uint)EventSubtypeStep.Step)?.Actions.FirstOrDefault()?.CodeId is { } largeStep)
        {
            EnsurePrefix(largeStep, GuardLine + "\n", "jamm_asleep");
        }
        if (fishRoot.Events[(int)EventType.Draw]
            .FirstOrDefault(e => e.EventSubtype == 0u)?.Actions.FirstOrDefault()?.CodeId is { } fishDraw)
        {
            EnsurePrefix(fishDraw, GuardLine + "\n", "jamm_asleep");
        }

        // Guard any covered subclass's own Step, Draw, or active-only Collision events while asleep.
        var coveredSet = eligibility.Where(e => e.Covered).Select(e => e.ObjectName).ToHashSet(StringComparer.Ordinal);
        var guardedCodes = new HashSet<UndertaleCode>();
        foreach (var obj in data.GameObjects)
        {
            if (!coveredSet.Contains(obj.Name.Content)) continue;
            for (var cursor = obj; cursor is not null && cursor != fishRoot; cursor = cursor.ParentId)
            {
                if (AbstractTiers.Contains(cursor.Name.Content)) continue;
                foreach (var et in new[] { EventType.Step, EventType.Draw, EventType.Collision })
                {
                    int idx = (int)et;
                    if (idx >= cursor.Events.Count) continue;
                    foreach (var ev in cursor.Events[idx])
                    foreach (var action in ev.Actions)
                    {
                        if (action.CodeId is { ParentEntry: null } code && guardedCodes.Add(code))
                        {
                            if (et == EventType.Step && ev.EventSubtype == (uint)EventSubtypeStep.EndStep)
                                EnsureGuardAfterInherited(code);
                            else
                                EnsurePrefix(code, GuardLine + "\n", "jamm_asleep");
                        }
                    }
                }
            }
        }

        ModObjects.Compile(compiler);

        foreach (string room in new[] { "NewWorld", "room11" })
        {
            if (data.Rooms.ByName(room) is not null)
                ModObjects.PlaceInstance(data, room, controller, first: true);
        }

        int coveredCount = eligibility.Count(e => e.Covered);
        decisions.Insert(0, $"[CGF-PERF-SLEEP] enabled (default {(enabledByDefault ? "on" : "off")}, margin {clampedMargin}, {coveredCount}/{eligibility.Count} fish types covered)");
        return decisions;

        void AddScript(string name, string body)
        {
            string codeName = "gml_Script_" + name;
            var code = data.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(data, codeName);
            if (data.Scripts.ByName(name) is { } script) script.Code = code;
            else data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
            compiler.QueueCodeReplace(code, body);
            edits.Add(code);
        }

        void AddEvent(EventType type, uint subtype, string body)
        {
            var code = controller.EventHandlerFor(type, subtype, data);
            compiler.QueueCodeReplace(code, body);
            edits.Add(code);
        }

        void EnsurePrefix(UndertaleCode code, string prefix, string marker)
        {
            if (!TryDecompile(data, code, out string current)) return;
            if (current.Contains(marker, StringComparison.Ordinal)) return;
            compiler.QueueCodeReplace(code, prefix + current);
            edits.Add(code);
        }

        void EnsureGuardAfterInherited(UndertaleCode code)
        {
            if (!TryDecompile(data, code, out string current)) return;
            if (current.Contains("jamm_asleep", StringComparison.Ordinal)) return;
            var match = InheritedCallPrefix().Match(current);
            if (!match.Success) return;
            string updated = current[..match.Length] + "\n" + GuardLine + current[match.Length..];
            compiler.QueueCodeReplace(code, updated);
            edits.Add(code);
        }
    }

    private static void ApplyPopulationHooks(UndertaleData data, CompileGroup compiler,
        List<UndertaleCode> edits, UndertaleGameObject fishRoot, List<string> decisions)
    {
        // FishThinner is adaptive: once sleeping fish raise FPS above 60, vanilla resets its
        // cull timer and ordinary fish accumulate indefinitely. Preserve its mild-cull cadence
        // while Fish Sleep is active, but leave the vanilla branch byte-for-byte alone when off.
        var thinner = data.GameObjects.ByName("FishThinner");
        var thinnerStep = thinner?.Events[(int)EventType.Step]
            .FirstOrDefault(e => e.EventSubtype == (uint)EventSubtypeStep.Step)?.Actions.FirstOrDefault()?.CodeId
            ?? data.Code.ByName("gml_Object_FishThinner_Step_0");
        bool thinnerPopulationHooked = false;
        if (thinnerStep is not null && TryDecompile(data, thinnerStep, out string thinnerText))
        {
            const string replacement = "else if (global.jamm_perf_sleep && global.jamm_sleep_asleep_count > 0)\n{\n    cull_trigger += 0.5;\n}\nelse\n{\n    cull_trigger = 0;\n}";
            string updated = FishThinnerResetBranch().Replace(thinnerText, replacement, 1);
            if (updated != thinnerText)
            {
                compiler.QueueCodeReplace(thinnerStep, updated);
                edits.Add(thinnerStep);
                thinnerPopulationHooked = true;
            }
        }

        // FishThinner deactivates fish rather than destroying them, so their Destroy event does
        // not run. Release their counter slot immediately before that deactivation.
        var thinnerCull = thinner?.Events[(int)EventType.Other]
            .FirstOrDefault(e => e.EventSubtype == 10u)?.Actions.FirstOrDefault()?.CodeId
            ?? data.Code.ByName("gml_Object_FishThinner_Other_10");
        bool cullCounterHooked = false;
        if (thinnerCull is not null && TryDecompile(data, thinnerCull, out string cullText)
            && !(cullText.Contains("if (jamm_asleep)", StringComparison.Ordinal)
                && cullText.Contains("global.jamm_sleep_asleep_count -= 1", StringComparison.Ordinal)))
        {
            const string deactivate = "instance_deactivate_object(id);";
            const string releaseAndDeactivate = "if (jamm_asleep)\n{\n    jamm_asleep = false;\n    if (global.jamm_sleep_asleep_count > 0) global.jamm_sleep_asleep_count -= 1;\n}\ninstance_deactivate_object(id);";
            string updated = cullText.Replace(deactivate, releaseAndDeactivate, StringComparison.Ordinal);
            if (updated != cullText)
            {
                compiler.QueueCodeReplace(thinnerCull, updated);
                edits.Add(thinnerCull);
                cullCounterHooked = true;
            }
        }

        var fishDestroy = fishRoot.Events[(int)EventType.Destroy]
            .FirstOrDefault(e => e.EventSubtype == 0u)?.Actions.FirstOrDefault()?.CodeId
            ?? fishRoot.EventHandlerFor(EventType.Destroy, 0u, data);
        if (TryDecompile(data, fishDestroy, out string destroyText)
            && !(destroyText.Contains("variable_instance_exists(id, \"jamm_asleep\")", StringComparison.Ordinal)
                && destroyText.Contains("global.jamm_sleep_asleep_count -= 1", StringComparison.Ordinal)))
        {
            const string release = "if (variable_instance_exists(id, \"jamm_asleep\") && jamm_asleep)\n{\n    jamm_asleep = false;\n    if (global.jamm_sleep_asleep_count > 0) global.jamm_sleep_asleep_count -= 1;\n}\n";
            compiler.QueueCodeReplace(fishDestroy, release + destroyText);
            edits.Add(fishDestroy);
        }

        decisions.Add(thinnerPopulationHooked
            ? "[CGF-PERF-SLEEP] FishThinner keeps its mild cull cadence while eligible fish sleep."
            : "[CGF-PERF-SLEEP] FishThinner population cadence unchanged (known reset branch not found).");
        if (thinner is not null && !cullCounterHooked)
            decisions.Add("[CGF-PERF-SLEEP] FishThinner deactivation counter hook not applied (known cull call not found).");
    }

    private static string CreateControllerGml(bool enabledByDefault, int margin, bool debug) => $$"""
        if (instance_number(object_index) > 1) { instance_destroy(); exit; }
        ini_open("jamm_perf.ini");
        global.jamm_perf_sleep = ini_read_real("Perf", "FishSleep", {{(enabledByDefault ? 1 : 0)}}) != 0;
        global.jamm_perf_sleep_margin = clamp(round(ini_read_real("Perf", "FishSleepMargin", {{margin}})), {{MinMargin}}, {{MaxMargin}});
        ini_close();
        global.jamm_sleep_types = ds_map_create();
        global.jamm_sleep_quest_objs = ds_map_create();
        global.jamm_sleep_zone_n = 0;
        global.jamm_sleep_hyst = 500;
        global.jamm_sleep_min_x = -100000000;
        global.jamm_sleep_min_y = -100000000;
        global.jamm_sleep_max_x = 100000000;
        global.jamm_sleep_max_y = 100000000;
        global.jamm_sleep_view_x1 = 0;
        global.jamm_sleep_view_y1 = 0;
        global.jamm_sleep_view_x2 = 0;
        global.jamm_sleep_view_y2 = 0;
        global.jamm_sleep_tick = 0;
        global.jamm_sleep_asleep_count = 0;
        global.jamm_sleep_wake_count = 0;
        jamm_sleep_init_types();
        jamm_sleep_owner = true;
        {{(debug ? "jamm_sleep_log = current_time + 1000;" : "")}}
        """;

    private static string DestroyControllerGml() => """
        if (variable_instance_exists(id, "jamm_sleep_owner") && jamm_sleep_owner)
        {
            ds_map_destroy(global.jamm_sleep_types);
            ds_map_destroy(global.jamm_sleep_quest_objs);
        }
        """;

    private static string BeginStepControllerGml(bool debug) => $$"""
        global.jamm_sleep_tick += 1;
        jamm_sleep_begin_step();
        {{(debug ? "if (current_time >= jamm_sleep_log) { jamm_sleep_log = current_time + 1000; show_debug_message(\"FishSleep=\" + string(global.jamm_perf_sleep) + \" asleep=\" + string(global.jamm_sleep_asleep_count) + \" wakes=\" + string(global.jamm_sleep_wake_count)); }" : "")}}
        """;

    internal static string Step2Prefix() => """
        if (jamm_asleep)
        {
            if (!global.jamm_perf_sleep || state != jamm_sleep_state || life <= 0 || deactivated)
            {
                jamm_sleep_wake();
            }
            else if (x >= global.jamm_sleep_min_x && x <= global.jamm_sleep_max_x && y >= global.jamm_sleep_min_y && y <= global.jamm_sleep_max_y)
            {
                if (jamm_sleep_in_zone(x, y, 0))
                    jamm_sleep_wake();
                else
                    exit;
            }
            else
            {
                exit;
            }
        }
        else if (global.jamm_perf_sleep)
        {
            if (!(x >= global.jamm_sleep_view_x1 && x <= global.jamm_sleep_view_x2 && y >= global.jamm_sleep_view_y1 && y <= global.jamm_sleep_view_y2))
            {
                if (jamm_sleep_should_sleep())
                {
                    jamm_sleep_enter();
                    exit;
                }
            }
        }
        """;

    private static string InitTypesGml(IReadOnlyList<FishSleepDecision> decisions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("var m = global.jamm_sleep_types;");
        sb.AppendLine("ds_map_clear(m);");
        foreach (var d in decisions)
        {
            string quoted = "\"" + d.ObjectName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
            sb.AppendLine(FormattableString.Invariant($"ds_map_add(m, asset_get_index({quoted}), {(d.Covered ? 1 : 0)});"));
        }
        return sb.ToString();
    }

    private static string AddBoxGml() => """
        var n = global.jamm_sleep_zone_n;
        var x1 = argument0;
        var y1 = argument1;
        var x2 = argument2;
        var y2 = argument3;
        global.jamm_sleep_zx1[n] = x1;
        global.jamm_sleep_zy1[n] = y1;
        global.jamm_sleep_zx2[n] = x2;
        global.jamm_sleep_zy2[n] = y2;
        global.jamm_sleep_zone_n = n + 1;
        if (n == 0)
        {
            global.jamm_sleep_min_x = x1;
            global.jamm_sleep_min_y = y1;
            global.jamm_sleep_max_x = x2;
            global.jamm_sleep_max_y = y2;
        }
        else
        {
            if (x1 < global.jamm_sleep_min_x) global.jamm_sleep_min_x = x1;
            if (y1 < global.jamm_sleep_min_y) global.jamm_sleep_min_y = y1;
            if (x2 > global.jamm_sleep_max_x) global.jamm_sleep_max_x = x2;
            if (y2 > global.jamm_sleep_max_y) global.jamm_sleep_max_y = y2;
        }
        """;

    private static string BeginStepGml(UndertaleData data)
    {
        bool hasLure = data.GameObjects.ByName("Lure") is not null;
        bool hasGirl = data.GameObjects.ByName("Girl") is not null;
        bool hasBoat = data.GameObjects.ByName("Boat") is not null;
        bool hasCatSwim = data.GameObjects.ByName("CatSwim") is not null;
        bool hasNet = data.GameObjects.ByName("Net") is not null;
        bool hasSonar = data.GameObjects.ByName("SonarPanel") is not null;
        bool hasDeepSonar = data.GameObjects.ByName("SonarPanelAdvanced") is not null;
        bool hasCatchQuest = data.GameObjects.ByName("CatchQuestNewNew") is not null;
        bool hasQuest = data.GameObjects.ByName("Quest") is not null;

        var sb = new StringBuilder();
        sb.AppendLine("""
            if (!global.jamm_perf_sleep) exit;
            var vw = max(view_wview[0], 1200);
            var margin = max(global.jamm_perf_sleep_margin, vw + 200);
            global.jamm_sleep_hyst = max(400, min(800, round(margin * 0.25)));
            global.jamm_sleep_zone_n = 0;
            global.jamm_sleep_view_x1 = 0;
            global.jamm_sleep_view_y1 = 0;
            global.jamm_sleep_view_x2 = 0;
            global.jamm_sleep_view_y2 = 0;
            if (view_visible[0])
            {
                global.jamm_sleep_view_x1 = view_xview[0];
                global.jamm_sleep_view_y1 = view_yview[0];
                global.jamm_sleep_view_x2 = view_xview[0] + view_wview[0];
                global.jamm_sleep_view_y2 = view_yview[0] + view_hview[0];
            }
            for (var v = 0; v < 8; v += 1)
            {
                if (view_visible[v])
                    jamm_sleep_add_box(view_xview[v] - margin, view_yview[v] - margin, view_xview[v] + view_wview[v] + margin, view_yview[v] + view_hview[v] + margin);
            }
            """);

        if (hasSonar)
        {
            sb.AppendLine("""
                if (instance_exists(SonarPanel) && SonarPanel.active)
                {
                    var sw = max(view_wview[3], 3700);
                    var sh = max(view_hview[3], 3700);
                    jamm_sleep_add_box(view_xview[3] - margin, view_yview[3] - margin, view_xview[3] + sw + margin, view_yview[3] + sh + margin);
                }
                """);
        }
        if (hasDeepSonar && hasLure)
        {
            sb.AppendLine("""
                if (instance_exists(SonarPanelAdvanced) && (SonarPanelAdvanced.active || SonarPanelAdvanced.advancedDrawEnabled) && instance_exists(Lure))
                {
                    jamm_sleep_add_box(Lure.x - 1000 - margin, Lure.y - 1000 - margin, Lure.x + 1000 + margin, Lure.y + 1000 + margin);
                }
                """);
        }
        if (hasLure)
        {
            sb.AppendLine("""
                with (Lure)
                {
                    var lm = max(margin, abs(lunge_scale) * 1500 + 500);
                    jamm_sleep_add_box(x - lm, y - lm, x + lm, y + lm);
                }
                """);
        }
        if (hasGirl)
        {
            sb.AppendLine("""
                with (Girl)
                    jamm_sleep_add_box(x - margin, y - margin, x + margin, y + margin);
                """);
        }
        if (hasBoat)
        {
            sb.AppendLine("""
                with (Boat)
                    jamm_sleep_add_box(x - margin, y - margin, x + margin, y + margin);
                """);
        }
        if (hasCatSwim)
        {
            sb.AppendLine("""
                with (CatSwim)
                {
                    var cm = max(margin, 3000);
                    jamm_sleep_add_box(x - cm, y - cm, x + cm, y + cm);
                }
                """);
        }
        if (hasNet)
        {
            sb.AppendLine("""
                with (Net)
                {
                    var nm = max(margin, 3000);
                    jamm_sleep_add_box(x - nm, y - nm, x + nm, y + nm);
                }
                """);
        }

        sb.AppendLine("ds_map_clear(global.jamm_sleep_quest_objs);");
        if (hasCatchQuest)
        {
            sb.AppendLine("""
                with (CatchQuestNewNew)
                {
                    if (variable_instance_exists(id, "fish_object_1") && is_real(fish_object_1) && fish_object_1 > 0)
                    {
                        ds_map_replace(global.jamm_sleep_quest_objs, fish_object_1, 1);
                        if (instance_exists(fish_object_1))
                            jamm_sleep_add_box(fish_object_1.x - margin, fish_object_1.y - margin, fish_object_1.x + margin, fish_object_1.y + margin);
                    }
                    if (variable_instance_exists(id, "fish_object_2") && is_real(fish_object_2) && fish_object_2 > 0)
                    {
                        ds_map_replace(global.jamm_sleep_quest_objs, fish_object_2, 1);
                        if (instance_exists(fish_object_2))
                            jamm_sleep_add_box(fish_object_2.x - margin, fish_object_2.y - margin, fish_object_2.x + margin, fish_object_2.y + margin);
                    }
                }
                """);
        }
        if (hasQuest)
        {
            sb.AppendLine("""
                with (Quest)
                {
                    if (variable_instance_exists(id, "target_x") && variable_instance_exists(id, "target_y"))
                        jamm_sleep_add_box(target_x - margin, target_y - margin, target_x + margin, target_y + margin);
                }
                """);
        }

        sb.AppendLine("""
            if (global.jamm_sleep_zone_n == 0)
            {
                global.jamm_sleep_min_x = -100000000;
                global.jamm_sleep_min_y = -100000000;
                global.jamm_sleep_max_x = 100000000;
                global.jamm_sleep_max_y = 100000000;
            }
            """);
        return sb.ToString();
    }

    private static string InZoneGml() => """
        var px = argument0;
        var py = argument1;
        var expand = argument2;
        var n = global.jamm_sleep_zone_n;
        if (n <= 0) return true;
        if (px < global.jamm_sleep_min_x - expand || px > global.jamm_sleep_max_x + expand || py < global.jamm_sleep_min_y - expand || py > global.jamm_sleep_max_y + expand)
            return false;
        for (var i = 0; i < n; i += 1)
        {
            if (px >= global.jamm_sleep_zx1[i] - expand && px <= global.jamm_sleep_zx2[i] + expand
                && py >= global.jamm_sleep_zy1[i] - expand && py <= global.jamm_sleep_zy2[i] + expand)
                return true;
        }
        return false;
        """;

    private static string ShouldSleepGml(UndertaleData data)
    {
        bool hasLure = data.GameObjects.ByName("Lure") is not null;
        bool hasCatSwim = data.GameObjects.ByName("CatSwim") is not null;
        bool hasChunkStream = data.Scripts.ByName("jamm_chunk_stream") is not null;

        var sb = new StringBuilder();
        sb.AppendLine("""
            if (state != 0 && state != 12) return false;
            if (life <= 0 || deactivated || hollow || netherFish || always_zoned || isDragging || onBoat || thrall_override) return false;
            if (lungeCooldown > 0) return false;
            if (!ds_map_exists(global.jamm_sleep_types, object_index)) return false;
            if (!ds_map_find_value(global.jamm_sleep_types, object_index)) return false;
            if (ds_map_exists(global.jamm_sleep_quest_objs, object_index)) return false;
            if (variable_instance_exists(id, "jamm_area_fish") && jamm_area_fish) return false;
            if (variable_instance_exists(id, "jamm_weather_owner")) return false;
            """);
        if (hasChunkStream)
            sb.AppendLine("if (variable_instance_exists(id, \"jmk_endless_ocean_fish\")) return false;");
        if (hasLure)
            sb.AppendLine("if (instance_exists(Lure) && (Lure.fish == id || (variable_instance_exists(Lure.id, \"stolenBy\") && Lure.stolenBy == id))) return false;");
        if (hasCatSwim)
            sb.AppendLine("if (instance_exists(CatSwim) && variable_instance_exists(CatSwim.id, \"holding\") && CatSwim.holding == id) return false;");
        sb.AppendLine("""
            for (var ja = 0; ja < 12; ja += 1)
            {
                if (alarm[ja] >= 0) return false;
            }
            if (jamm_sleep_in_zone(x, y, global.jamm_sleep_hyst)) return false;
            return true;
            """);
        return sb.ToString();
    }

    private static string EnterGml() => """
        jamm_asleep = true;
        jamm_sleep_tick = global.jamm_sleep_tick;
        jamm_sleep_state = state;
        jamm_sleep_x = x;
        jamm_sleep_y = y;
        jamm_sleep_path = path_index;
        jamm_sleep_path_pos = path_position;
        jamm_sleep_path_spd = path_speed;
        jamm_sleep_path_end = path_endaction;
        jamm_sleep_spd = spd;
        jamm_sleep_speed = speed;
        jamm_sleep_dir = direction;
        jamm_sleep_visible = visible;
        if (path_index != -1) path_speed = 0;
        speed = 0;
        hspeed = 0;
        vspeed = 0;
        spd = 0;
        visible = false;
        global.jamm_sleep_asleep_count += 1;
        """;

    private static string WakeGml(UndertaleData data)
    {
        bool hasCollidable = data.GameObjects.ByName("Collidable") is not null;
        string collidableCheck = hasCollidable
            ? "if (jw_ok && collision_point(jw_nx, jw_ny, Collidable, true, true) != noone) jw_ok = false;"
            : "";

        return $$"""
            jamm_asleep = false;
            if (global.jamm_sleep_asleep_count > 0) global.jamm_sleep_asleep_count -= 1;
            global.jamm_sleep_wake_count += 1;
            var jw_elapsed = max(0, global.jamm_sleep_tick - jamm_sleep_tick);
            var jw_vx1 = global.jamm_sleep_view_x1;
            var jw_vy1 = global.jamm_sleep_view_y1;
            var jw_vx2 = global.jamm_sleep_view_x2;
            var jw_vy2 = global.jamm_sleep_view_y2;
            var jw_has_view = (jw_vx2 > jw_vx1) && (jw_vy2 > jw_vy1);
            if (state == jamm_sleep_state && life > 0 && !deactivated)
            {
                if (jamm_sleep_path != -1 && path_exists(jamm_sleep_path) && path_index == jamm_sleep_path)
                {
                    var jw_plen = max(1, path_get_length(jamm_sleep_path));
                    var jw_rate = abs(jamm_sleep_path_spd);
                    if (variable_instance_exists(id, "PATH_MIN_SPEED") && variable_instance_exists(id, "PATH_THRUST_SPEED"))
                        jw_rate = max(jw_rate, PATH_MIN_SPEED + (0.5 * PATH_THRUST_SPEED));
                    var jw_step = min(jw_elapsed * jw_rate, jw_plen);
                    var jw_pos = frac(jamm_sleep_path_pos + (jw_step / jw_plen));
                    var jw_dx = path_get_x(jamm_sleep_path, jw_pos) - path_get_x(jamm_sleep_path, jamm_sleep_path_pos);
                    var jw_dy = path_get_y(jamm_sleep_path, jw_pos) - path_get_y(jamm_sleep_path, jamm_sleep_path_pos);
                    var jw_nx = clamp(x + jw_dx, 740, room_width - 100);
                    var jw_ny = clamp(y + jw_dy, 1294, room_height - 100);
                    var jw_ok = true;
                    if (jw_has_view && jw_nx >= jw_vx1 - 64 && jw_nx <= jw_vx2 + 64 && jw_ny >= jw_vy1 - 64 && jw_ny <= jw_vy2 + 64) jw_ok = false;
                    {{collidableCheck}}
                    if (jw_ok)
                    {
                        path_position = jw_pos;
                        x = jw_nx;
                        y = jw_ny;
                        xprevious = jw_nx;
                        yprevious = jw_ny;
                    }
                    else
                    {
                        path_position = jamm_sleep_path_pos;
                    }
                    path_speed = jamm_sleep_path_spd;
                }
                else if (state == 0 && abs(jamm_sleep_spd) > 0.01)
                {
                    var jw_dist = min(jw_elapsed * clamp(abs(jamm_sleep_spd), 0, 8), 400);
                    var jw_nx = clamp(x + lengthdir_x(jw_dist, jamm_sleep_dir), 740, room_width - 100);
                    var jw_ny = clamp(y + lengthdir_y(jw_dist, jamm_sleep_dir), 1294, room_height - 100);
                    if (variable_instance_exists(id, "TRUE_START_X") && variable_instance_exists(id, "TRUE_START_Y"))
                    {
                        var jw_hdist = point_distance(TRUE_START_X, TRUE_START_Y, jw_nx, jw_ny);
                        if (jw_hdist > 600)
                        {
                            var jw_hdir = point_direction(TRUE_START_X, TRUE_START_Y, jw_nx, jw_ny);
                            jw_nx = clamp(TRUE_START_X + lengthdir_x(600, jw_hdir), 740, room_width - 100);
                            jw_ny = clamp(TRUE_START_Y + lengthdir_y(600, jw_hdir), 1294, room_height - 100);
                        }
                    }
                    var jw_ok = true;
                    if (jw_has_view && jw_nx >= jw_vx1 - 64 && jw_nx <= jw_vx2 + 64 && jw_ny >= jw_vy1 - 64 && jw_ny <= jw_vy2 + 64) jw_ok = false;
                    {{collidableCheck}}
                    if (jw_ok)
                    {
                        x = jw_nx;
                        y = jw_ny;
                        xprevious = jw_nx;
                        yprevious = jw_ny;
                    }
                }
                speed = jamm_sleep_speed;
                spd = jamm_sleep_spd;
                direction = jamm_sleep_dir;
                visible = jamm_sleep_visible;
                if (state == 0) visible = true;
            }
            else
            {
                if (jamm_sleep_path != -1 && path_exists(jamm_sleep_path) && path_index == jamm_sleep_path && state == 0)
                    path_speed = jamm_sleep_path_spd;
                if (state != 6 && state != 11 && state != 12) visible = true;
            }
            if (variable_instance_exists(id, "lungeCooldown")) lungeCooldown = 0;
            if (variable_instance_exists(id, "target_timer")) target_timer = 0;
            if (variable_instance_exists(id, "target") && is_real(target) && target >= 100000 && !instance_exists(target)) target = -1;
            if (variable_instance_exists(id, "nearest_big") && is_real(nearest_big) && nearest_big >= 100000 && !instance_exists(nearest_big)) nearest_big = -1;
            if (variable_instance_exists(id, "pal") && is_real(pal) && pal >= 100000 && !instance_exists(pal)) pal = -1;
            if (variable_instance_exists(id, "check_timer")) check_timer = 6;
            if (variable_instance_exists(id, "frames_since_last_flip")) frames_since_last_flip = 0;
            """;
    }
}
