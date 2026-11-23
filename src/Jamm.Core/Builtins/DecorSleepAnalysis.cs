using System.Globalization;
using System.Text.RegularExpressions;
using Jamm.Core.Building;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Classification of how an object's Step event behaves under DecorSleep.</summary>
internal enum DecorStepKind
{
    Empty,
    CameraCullOnly,
    PureParallax,
    TimeSway,
    AngleAccumPulse,
    LightSync,
    AlphaLerpLure
}

/// <summary>Parallax model used to compute world-space visibility bounds while sleeping.</summary>
internal enum DecorParallaxKind
{
    None,
    SceneItem,
    GrottoObject
}

/// <summary>Per-event sleep instrumentation plan for a single code entry.</summary>
internal sealed record DecorStepPatch(
    UndertaleCode Code,
    DecorStepKind Kind,
    double Rate = 0.0,
    bool HasCameraCheck = false);

/// <summary>Per-object sleep decision produced by build-time analysis.</summary>
internal sealed record DecorObjectPlan(
    UndertaleGameObject GameObject,
    bool Covered,
    string Reason,
    bool DeactivationSafe,
    bool ManageVisible,
    DecorParallaxKind Parallax,
    IReadOnlyList<DecorStepPatch> StepPatches,
    bool NeedsCreateInit,
    bool OwnerAnchored = false);

/// <summary>Result of analyzing an entire archive for static decoration sleep.</summary>
internal sealed record DecorSleepReport(
    IReadOnlyList<DecorObjectPlan> Objects,
    IReadOnlyList<UndertaleCode> SharedStepPatches,
    IReadOnlyList<UndertaleGameObject> RootSweepObjects,
    IReadOnlyList<UndertaleGameObject> DeactivatableObjects,
    UndertaleCode? LightAnchorCode,
    IReadOnlyList<string> LightAnchorVariables);

/// <summary>
/// Generic, pattern-based build-time analyzer for static decoration objects.
/// Inspects object hierarchy, room placements, external references, and event GML
/// to decide which objects can safely sleep off-view without affecting gameplay or saves.
/// </summary>
internal static partial class DecorSleepAnalysis
{
    private static readonly HashSet<string> NonDecorRoots = new(StringComparer.Ordinal)
    {
        "Fish", "FishPart", "Hookable", "PowerUp", "Boat", "Gear", "Buttons",
        "Handler", "Card", "Catalog", "Quest", "Tool", "Message", "PotEffect",
        "BuoyAbstract", "ThermalVent"
    };

    private static readonly HashSet<string> DecorRoots = new(StringComparer.Ordinal)
    {
        "CaveRock", "Rock", "Collidable", "SceneItem", "AbstractLight",
        "GrottoObject", "NoDrawCodeLit", "BackgroundLightBeam", "LandscapeAbs", "Bubble"
    };

    private static readonly HashSet<string> KnownControllerNames = new(StringComparer.Ordinal)
    {
        "Girl", "Rod", "Lure", "Camera", "Camera_Static", "SaveManager", "LightingSystem",
        "ParticleSystems", "AmbianceController", "FishThinner", "Zoner", "ZonerDeactivator",
        "NavNode", "NavEdge", "NavBrain", "ControlObjectSpawner", "ParallaxBackground",
        "Analytics", "CatSwim", "CatTracker", "CatAnchor", "ExperienceBar", "BreathBar",
        "Arcade", "SellHut", "SellHut2", "Louis", "LouisBase", "LouisQuestManager",
        "ObjLouisQuestUi", "LouisDialogue", "objLouisDialogue", "VentTransition",
        "Explosion", "Soda", "DiveCat", "ProceduralSpawner", "BulbfishSpawner",
        "FireflySpawner", "Firefly", "HypnoScreen", "Credits", "CreditsShort",
        "Skip_Room", "Node", "Mp3", "FlickerStar", "FlickerStar2", "RainbowStar",
        "SwalluskEye", "MagnapinnaTentacle4", "MagnapinnaTentacle5", "MagnapinnaTentacle6",
        "MagnapinnaTentacle7", "HumpbackFinBehind", "Minebombspawn", "MinebombHead",
        "MinebombChain", "Minebombdebris", "minebombexplosion", "Lilypad", "LilypadRipple",
        "WaterCurrent", "Stream", "ZoomBox", "LaunchPlatform", "NullNode", "CatalogHandle",
        "CaveClock", "CaveClockHour", "CaveClockMinute", "coraln6", "FrontgroundSand",
        "FrontgroundTree", "BackgroundTree1", "treeleaf", "ThermoSchool", "MissileSilo",
        "CrystalRubyHook", "Coral"
    };

    [GeneratedRegex(@"\b(?:collision_(?:point|line|circle|rectangle|ellipse)|place_meeting|position_meeting|instance_(?:place|position|nearest|furthest|exists|number|find|destroy))\s*\(([^)]*)\)")]
    private static partial Regex SpatialOrInstanceCall();

    [GeneratedRegex(@"\bwith\s*\(\s*([A-Za-z_]\w*|\d+)\s*\)")]
    private static partial Regex WithTarget();

    [GeneratedRegex(@"\b(?:var\s+)?([A-Za-z_]\w*)\s*=\s*instance_create(?:_depth|_layer)?\s*\([^,]*,[^,]*,\s*([A-Za-z_]\w*)\s*\)")]
    private static partial Regex CreatedInstanceAssignment();

    [GeneratedRegex(@"\b([A-Za-z_]\w*)\s*\.\s*(?:x|y|visible)\s*(?:=(?!=)|\+=|-=|\+\+|--)")]
    private static partial Regex InstancePropertyWrite();

    [GeneratedRegex(@"(?<![\w.$])([A-Za-z_]\w*)\s*\.\s*([A-Za-z_]\w*)")]
    private static partial Regex MemberAccess();

    [GeneratedRegex(@"\bimage_angle\s*\+=\s*([0-9]+(?:\.[0-9]+)?)\s*;")]
    private static partial Regex AngleAccum();

    [GeneratedRegex(@"\blerp\s*\(\s*image_alpha\s*,")]
    private static partial Regex AlphaLerp();

    [GeneratedRegex(@"\b(?:random|irandom|random_range|irandom_range|choose|randomize)\b")]
    private static partial Regex RngCall();

    [GeneratedRegex(@"\b(?:part_|effect_|audio_|ini_|file_|ds_|surface_|room_goto|game_|keyboard_|mouse_|physics_|mp_|path_)")]
    private static partial Regex GameplaySideEffect();

    [GeneratedRegex(@"\b(?:collision_|place_meeting|position_meeting|instance_place|instance_position|instance_nearest|instance_furthest)\b")]
    private static partial Regex CollisionQuery();

    [GeneratedRegex(@"\binstance_(?:create|create_depth|create_layer|destroy|change|copy|deactivate|activate)\b")]
    private static partial Regex LifecycleCall();

    [GeneratedRegex(@"\b(?:Girl|Lure|Rod|Boat|SaveManager|CatalogData|QuestEngine)\s*\.\s*[A-Za-z_]\w*\s*(?:=(?!=)|\+=|-=|\*=|/=|\+\+|--)")]
    private static partial Regex ExternalGameplayWrite();

    /// <summary>Analyzes all objects in <paramref name="data"/> and builds a DecorSleep plan.</summary>
    internal static DecorSleepReport Analyze(UndertaleData data)
    {
        var objectNames = data.GameObjects.Select(o => o.Name.Content).ToArray();
        var nameToObj = new Dictionary<string, UndertaleGameObject>(StringComparer.Ordinal);
        var idToObj = new Dictionary<int, UndertaleGameObject>();
        for (int i = 0; i < data.GameObjects.Count; i++)
        {
            var obj = data.GameObjects[i];
            nameToObj.TryAdd(obj.Name.Content, obj);
            idToObj[i] = obj;
        }

        var placedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (data.Rooms.ByName("NewWorld") is { } world)
        {
            foreach (var placement in world.GameObjects)
            {
                if (placement.ObjectDefinition?.Name.Content is { } n)
                    placedCounts[n] = placedCounts.GetValueOrDefault(n) + 1;
            }
        }

        // Cache decompiled code entries once.
        var decompiled = new Dictionary<UndertaleCode, string>();
        string Decompile(UndertaleCode code)
        {
            if (decompiled.TryGetValue(code, out string? cached)) return cached;
            try
            {
                string text = ModObjects.Decompile(data, code);
                if (text.Contains("DECOMPILE ERROR", StringComparison.Ordinal)) text = "";
                return decompiled[code] = text;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return decompiled[code] = "";
            }
        }

        // Build code-entry -> owner object map to distinguish self-references from external references.
        var codeOwners = new Dictionary<UndertaleCode, HashSet<UndertaleGameObject>>();
        foreach (var obj in data.GameObjects)
        {
            for (int et = 0; et < obj.Events.Count; et++)
            {
                foreach (var ev in obj.Events[et])
                {
                    foreach (var act in ev.Actions)
                    {
                        if (act.CodeId is null) continue;
                        if (!codeOwners.TryGetValue(act.CodeId, out var set))
                            codeOwners[act.CodeId] = set = [];
                        set.Add(obj);
                    }
                }
            }
        }

        // Global external reference & chunk/area ownership scan.
        var deactivationUnsafeRoots = new HashSet<UndertaleGameObject>();
        var externalWithGameplayRoots = new HashSet<UndertaleGameObject>();
        var chunkOrAreaObjects = new HashSet<UndertaleGameObject>();
        var externallyPositionedObjects = new HashSet<UndertaleGameObject>();
        var ownerAnchoredObjects = new HashSet<UndertaleGameObject>();
        var chunkScripts = CollectChunkCallbacks(data, Decompile);
        UndertaleCode? lightAnchorCode = null;
        var lightAnchorVariables = new HashSet<string>(StringComparer.Ordinal);
        var createdVariableTargets = new Dictionary<string, HashSet<UndertaleGameObject>>(StringComparer.Ordinal);

        foreach (var code in data.Code.Where(c => c.ParentEntry is null))
        {
            string masked = InstanceGuard.Mask(Decompile(code));
            foreach (Match m in CreatedInstanceAssignment().Matches(masked))
                if (nameToObj.TryGetValue(m.Groups[2].Value, out var target))
                {
                    if (!createdVariableTargets.TryGetValue(m.Groups[1].Value, out var targets))
                        createdVariableTargets[m.Groups[1].Value] = targets = [];
                    targets.Add(target);
                }
        }
        foreach (var code in data.Code.Where(c => c.ParentEntry is null))
        {
            string masked = InstanceGuard.Mask(Decompile(code));
            foreach (Match m in InstancePropertyWrite().Matches(masked))
                if (createdVariableTargets.TryGetValue(m.Groups[1].Value, out var targets))
                    externallyPositionedObjects.UnionWith(targets);
        }

        foreach (var code in data.Code.Where(c => c.ParentEntry is null))
        {
            string cname = code.Name.Content;
            if (cname.StartsWith("gml_Script_jamm_ds_", StringComparison.Ordinal)
                || cname.StartsWith("gml_Object_obj_jamm_", StringComparison.Ordinal))
                continue;

            string text = Decompile(code);
            if (string.IsNullOrEmpty(text)) continue;
            string masked = InstanceGuard.Mask(text);

            if (lightAnchorCode is null && cname.StartsWith("gml_Script_", StringComparison.Ordinal))
            {
                foreach (Match withMatch in WithTarget().Matches(masked))
                {
                    string withBody = ExtractBracedBlock(masked, withMatch.Index + withMatch.Length);
                    if (Regex.IsMatch(withBody, @"\b(?:x|y)\s*=\s*other\.(?:x|y)\s*;")
                        && Regex.IsMatch(withBody, @"\bvisible\s*=\s*other\.visible\s*;"))
                        lightAnchorVariables.Add(withMatch.Groups[1].Value);
                }
                if (lightAnchorVariables.Count > 0)
                    lightAnchorCode = code;
            }

            bool isSonarDraw = cname == "gml_Object_SonarPanel_Draw_0";
            bool isLightingDraw = cname == "gml_Object_LightingSystem_Draw_0";
            bool isChunkOrArea = cname.StartsWith("gml_RoomCC_jamm_area_", StringComparison.Ordinal)
                || (cname.StartsWith("gml_Script_", StringComparison.Ordinal)
                    && chunkScripts.Contains(cname["gml_Script_".Length..]));

            codeOwners.TryGetValue(code, out var owners);

            void MarkTarget(UndertaleGameObject target, bool fromWith, bool fromSonarWith, bool fromCreate)
            {
                if (owners is not null && owners.Any(o => Inherits(o, target) || Inherits(target, o)))
                    return;
                if (isChunkOrArea && fromCreate)
                {
                    chunkOrAreaObjects.Add(target);
                    deactivationUnsafeRoots.Add(target);
                    return;
                }
                // SonarPanel_Draw_0's `with (SceneItem)` only reads SceneItem within <8000 of Camera while Sonar is open;
                // jamm_ds_sweep keeps SceneItem awake in Sonar range when Sonar is active.
                if (!(fromSonarWith && !InheritsAny(target, ["CaveRock", "Rock", "Collidable"])))
                    deactivationUnsafeRoots.Add(target);
                if (fromWith && !isSonarDraw && !isLightingDraw)
                    externalWithGameplayRoots.Add(target);
            }

            foreach (Match m in WithTarget().Matches(masked))
            {
                string targetName = m.Groups[1].Value;
                IEnumerable<UndertaleGameObject> targets = createdVariableTargets.TryGetValue(targetName, out var variableTargets)
                    ? variableTargets
                    : ResolveObjectToken(targetName, nameToObj, idToObj) is { } resolved ? [resolved] : [];
                foreach (var target in targets)
                {
                    string withBody = ExtractBracedBlock(masked, m.Index + m.Length);
                    if (Regex.IsMatch(withBody, @"\b(?:x|y|visible)\s*(?:=(?!=)|\+=|-=|\+\+|--)"))
                        externallyPositionedObjects.Add(target);
                    bool readsVisibleOrNonDecor = false;
                    if (!isSonarDraw && !isLightingDraw)
                    {
                        string afterWith = ExtractBracedBlock(masked, m.Index + m.Length);
                        readsVisibleOrNonDecor = !InheritsAny(target, DecorRoots)
                            || Regex.IsMatch(afterWith, @"\bvisible\b");
                    }
                    MarkTarget(target, fromWith: readsVisibleOrNonDecor, fromSonarWith: isSonarDraw, fromCreate: false);
                }
            }

            foreach (Match m in SpatialOrInstanceCall().Matches(masked))
            {
                bool isCreate = m.Value.StartsWith("instance_create", StringComparison.Ordinal);
                foreach (string arg in m.Groups[1].Value.Split(','))
                {
                    string tok = arg.Trim();
                    if (ResolveObjectToken(tok, nameToObj, idToObj) is { } target)
                        MarkTarget(target, fromWith: false, fromSonarWith: false, fromCreate: isCreate);
                }
            }

            foreach (Match m in MemberAccess().Matches(masked))
            {
                string tok = m.Groups[1].Value;
                if (nameToObj.TryGetValue(tok, out var target))
                    MarkTarget(target, fromWith: false, fromSonarWith: false, fromCreate: false);
            }

            if (isChunkOrArea)
            {
                foreach (Match m in Regex.Matches(masked, @"\b([A-Za-z_]\w*)\b"))
                {
                    if (nameToObj.TryGetValue(m.Value, out var target))
                    {
                        chunkOrAreaObjects.Add(target);
                        deactivationUnsafeRoots.Add(target);
                    }
                }
            }
        }

        // light_anchor writes a child light's position and visibility from its owner every Draw Begin.
        // Keep those lights in the sleep tracker, but let the owner remain authoritative on wake.
        if (lightAnchorCode is not null)
        {
            foreach (string variable in lightAnchorVariables)
                if (createdVariableTargets.TryGetValue(variable, out var anchoredTargets))
                    ownerAnchoredObjects.UnionWith(anchoredTargets);
        }

        // Analyze every candidate object.
        var plans = new List<DecorObjectPlan>(data.GameObjects.Count);
        var coveredSet = new HashSet<UndertaleGameObject>();

        foreach (var obj in data.GameObjects)
        {
            var plan = AnalyzeObject(obj, data, placedCounts, Decompile,
                deactivationUnsafeRoots, externalWithGameplayRoots, chunkOrAreaObjects, externallyPositionedObjects, ownerAnchoredObjects);
            plans.Add(plan);
            if (plan.Covered) coveredSet.Add(obj);
        }

        // Collect unique step code patches (with consistent kind across covered objects).
        var stepPatches = plans
            .Where(p => p.Covered)
            .SelectMany(p => p.StepPatches)
            .Where(sp => sp.Kind != DecorStepKind.Empty)
            .GroupBy(sp => sp.Code)
            .Select(g => g.First())
            .ToArray();

        // Compute minimal root set for runtime sweeping (parent objects whose descendants include at least one covered object).
        // Runtime initialization marks covered objects with jamm_ds_managed = true and uncovered descendants with false.
        var sweepRoots = ComputeMinimalSweepSet(plans, coveredSet);
        var deactivatable = plans.Where(p => p.Covered && p.DeactivationSafe).Select(p => p.GameObject).ToArray();

        return new DecorSleepReport(plans, stepPatches.Select(s => s.Code).ToArray(), sweepRoots, deactivatable, lightAnchorCode,
            lightAnchorVariables.Order(StringComparer.Ordinal).ToArray());
    }

    private static DecorObjectPlan AnalyzeObject(
        UndertaleGameObject obj,
        UndertaleData data,
        Dictionary<string, int> placedCounts,
        Func<UndertaleCode, string> decompile,
        HashSet<UndertaleGameObject> deactivationUnsafeRoots,
        HashSet<UndertaleGameObject> externalWithGameplayRoots,
        HashSet<UndertaleGameObject> chunkOrAreaObjects,
        HashSet<UndertaleGameObject> externallyPositionedObjects,
        HashSet<UndertaleGameObject> ownerAnchoredObjects)
    {
        string name = obj.Name.Content;
        bool ownerAnchored = ownerAnchoredObjects.Any(r => Inherits(obj, r));
        DecorObjectPlan Skip(string reason) => new(obj, false, reason, false, false, DecorParallaxKind.None, [], false);

        if (name.StartsWith("obj_jamm_", StringComparison.Ordinal) || name.StartsWith("obj_jmk_", StringComparison.Ordinal))
            return Skip("JAMM runtime controller");
        if (KnownControllerNames.Contains(name))
            return Skip("gameplay/controller object");
        if (!ownerAnchored && externallyPositionedObjects.Any(r => Inherits(obj, r)))
            return Skip("position or visibility is controlled by another object's code");
        if (obj.Persistent)
            return Skip("persistent object");
        if (InheritsAny(obj, NonDecorRoots))
            return Skip("non-decoration parent hierarchy");

        bool hasDecorRoot = InheritsAny(obj, DecorRoots);
        int placed = placedCounts.GetValueOrDefault(name);

        if (!hasDecorRoot)
        {
            if (placed == 0)
                return Skip("unparented and not placed in NewWorld");
            if (!obj.Visible || obj.Sprite is null)
                return Skip("invisible or no sprite");
        }

        if (!ownerAnchored && externalWithGameplayRoots.Any(r => Inherits(obj, r)))
            return Skip("iterated by gameplay with-loop");
        if (chunkOrAreaObjects.Contains(obj) && name.StartsWith("jmk_", StringComparison.Ordinal))
            return Skip("chunk/area hook object");

        // Walk event chain (effective across inheritance so children of Alarm/Other/Input objects are skipped too).
        var alarmEvents = CollectEffectiveEvents(obj, EventType.Alarm);
        var collisionEvents = CollectEffectiveEvents(obj, EventType.Collision);
        var otherEvents = CollectEffectiveEvents(obj, EventType.Other);
        var inputEvents = CollectEffectiveEvents(obj, EventType.Keyboard)
            .Concat(CollectEffectiveEvents(obj, EventType.KeyPress))
            .Concat(CollectEffectiveEvents(obj, EventType.KeyRelease))
            .Concat(CollectEffectiveEvents(obj, EventType.Mouse));

        if (alarmEvents.Count > 0) return Skip("has Alarm event");
        if (collisionEvents.Count > 0) return Skip("has Collision event");
        if (otherEvents.Count > 0) return Skip("has Other event");
        if (inputEvents.Any()) return Skip("has input event");

        // Inspect Draw events.
        var drawEvents = CollectEffectiveEvents(obj, EventType.Draw);
        foreach (var (subtype, code, _) in drawEvents)
        {
            if (subtype == (uint)EventSubtypeDraw.DrawGUI || subtype == (uint)EventSubtypeDraw.DrawGUIEnd || subtype == (uint)EventSubtypeDraw.DrawGUIBegin)
                return Skip("has GUI Draw event");
            if (subtype is not ((uint)EventSubtypeDraw.Draw or (uint)EventSubtypeDraw.DrawBegin))
                return Skip($"has Draw_{subtype} event");

            string text = decompile(code);
            if (text.Length == 0 && code.Length > 0)
                return Skip($"Draw_{subtype} could not be decompiled");
            if (subtype == (uint)EventSubtypeDraw.Draw && !IsPureDraw(text))
                return Skip("Draw_0 has non-trivial side effects");
            if (subtype == (uint)EventSubtypeDraw.DrawBegin && !IsLightSyncDrawBegin(text))
                return Skip("Draw_72 has non-light-sync side effects");
        }

        // Inspect Create event (including inherited ancestors if action_inherited is called).
        var createEvents = CollectEffectiveEvents(obj, EventType.Create);
        bool createsChildren = false;
        foreach (var (_, code, owner) in createEvents)
        {
            string text = decompile(code);
            if (text.Length == 0 && code.Length > 0)
                return Skip("Create_0 could not be decompiled");
            if (!IsSafeDecorCreate(text, out bool childSpawn))
                return Skip("Create_0 has gameplay/system side effects");
            if (childSpawn) createsChildren = true;
            if (!createsChildren && (text.Contains("action_inherited()", StringComparison.Ordinal) || text.Contains("event_inherited()", StringComparison.Ordinal)))
            {
                for (var p = owner.ParentId; p is not null && !createsChildren; p = p.ParentId)
                {
                    foreach (var ev in p.Events[(int)EventType.Create].Where(e => e.EventSubtype == 0u))
                    {
                        foreach (var act in ev.Actions.Where(a => a.CodeId is not null))
                        {
                            IsSafeDecorCreate(decompile(act.CodeId!), out bool pChild);
                            if (pChild) createsChildren = true;
                        }
                    }
                }
            }
        }

        // Inspect Step events.
        var stepEvents = CollectEffectiveEvents(obj, EventType.Step);
        var stepPatches = new List<DecorStepPatch>();
        var parallax = DecorParallaxKind.None;
        bool writesGameplayVisible = false;

        foreach (var (subtype, code, owner) in stepEvents)
        {
            string text = decompile(code);
            if (text.Length == 0 && code.Length > 0)
                return Skip($"Step_{subtype} could not be decompiled");

            var classified = ClassifyStep(text, out double rate, out bool hasCameraCheck, out bool nonCameraVisibleWrite, out var stepParallax);
            if (Regex.IsMatch(InstanceGuard.Mask(text), @"\bvisible\s*=\s*instance_exists\s*\(\s*[A-Za-z_]\w*\s*\)"))
                ownerAnchored = true;
            if (nonCameraVisibleWrite) writesGameplayVisible = true;
            if (stepParallax != DecorParallaxKind.None) parallax = stepParallax;
            if (parallax == DecorParallaxKind.None && text.Contains("inherited()", StringComparison.Ordinal))
            {
                for (var p = owner.ParentId; p is not null && parallax == DecorParallaxKind.None; p = p.ParentId)
                {
                    foreach (var ev in p.Events[(int)EventType.Step].Where(e => e.EventSubtype == subtype))
                    {
                        foreach (var act in ev.Actions.Where(a => a.CodeId is not null))
                        {
                            ClassifyStep(decompile(act.CodeId!), out _, out _, out _, out var parentPara);
                            if (parentPara != DecorParallaxKind.None) parallax = parentPara;
                        }
                    }
                }
            }

            if (classified is null)
            {
                // If an object's Draw_0 is pure (e.g. Sea -> NoDrawCodeLit) and it does not write gameplay visible,
                // we can still cull Draw_0 via ManageVisible while keeping Step active — unless unparented.
                if (hasDecorRoot && !nonCameraVisibleWrite && drawEvents.Any(d => d.Subtype == (uint)EventSubtypeDraw.Draw))
                    continue;
                return Skip($"Step_{subtype} has stateful/gameplay logic");
            }

            if (classified.Value != DecorStepKind.Empty)
                stepPatches.Add(new DecorStepPatch(code, classified.Value, rate, hasCameraCheck));
        }

        if (writesGameplayVisible)
            return Skip("Step controls visible from gameplay state");

        // Must either have something to skip (Draw / default Draw or Step).
        bool hasVisualDraw = obj.Visible && (obj.Sprite is not null || drawEvents.Any(d => d.Subtype == (uint)EventSubtypeDraw.Draw));
        if (!hasVisualDraw && stepPatches.Count == 0)
            return Skip("nothing to draw or step");

        bool hasDrawBeginSync = drawEvents.Any(d => d.Subtype == (uint)EventSubtypeDraw.DrawBegin);
        bool deactivationSafe = !deactivationUnsafeRoots.Any(r => Inherits(obj, r))
            // Ordinary Rock descendants can be deactivated when the archive-wide reference scan
            // finds no gameplay queries. Keep CaveRock/Collidable excluded: collision and chunk
            // systems depend on their instances even while they are outside the camera margin.
            && !InheritsAny(obj, ["AbstractLight", "Collidable", "CaveRock"])
            && !createsChildren
            && !hasDrawBeginSync
            && !stepPatches.Any(s => s.Kind == DecorStepKind.LightSync);
        bool manageVisible = hasVisualDraw;
        bool needsCreateInit = stepPatches.Count > 0;

        string mode = stepPatches.Count > 0
            ? $"Step({string.Join("+", stepPatches.Select(s => s.Kind).Distinct())})" + (manageVisible ? "+Visible" : "")
            : "Visible";
        string scope = deactivationSafe ? "deactivatable" : "active-guarded";
        if (ownerAnchored)
            mode += "+owner-anchored";
        return new(obj, true, $"covered ({mode}, {scope})", deactivationSafe, manageVisible, parallax, stepPatches, needsCreateInit, ownerAnchored);
    }

    private static DecorStepKind? ClassifyStep(
        string text,
        out double rate,
        out bool hasCameraCheck,
        out bool nonCameraVisibleWrite,
        out DecorParallaxKind parallax)
    {
        rate = 0;
        hasCameraCheck = false;
        nonCameraVisibleWrite = false;
        parallax = DecorParallaxKind.None;

        string masked = InstanceGuard.Mask(text);
        string stripped = StripInherited(masked);
        if (string.IsNullOrWhiteSpace(stripped))
            return DecorStepKind.Empty;

        if (RngCall().IsMatch(masked) || GameplaySideEffect().IsMatch(masked)
            || CollisionQuery().IsMatch(masked) || LifecycleCall().IsMatch(masked)
            || ExternalGameplayWrite().IsMatch(masked))
            return null;

        if (Regex.IsMatch(masked, @"\bbackground_color\s*="))
            return null;

        // A few light objects derive visibility directly from an owner instance (for example
        // CrystalfishLight checks Lightbulb). Guard that Step while sleeping, then let it
        // recompute visibility on wake instead of restoring a cached value.
        if (Regex.IsMatch(masked, @"\bvisible\s*=\s*instance_exists\s*\(\s*[A-Za-z_]\w*\s*\)"))
            return DecorStepKind.LightSync;

        if (Regex.IsMatch(masked, @"\bvisible\s*="))
        {
            // Allowed only if Camera distance gating (e.g. CaveRock_Step_0, Dangler_Step_0).
            if (!masked.Contains("Camera", StringComparison.Ordinal))
            {
                nonCameraVisibleWrite = true;
                return null;
            }
            hasCameraCheck = true;
        }

        if (masked.Contains("checkTimer", StringComparison.Ordinal)
            && masked.Contains("Camera", StringComparison.Ordinal)
            && masked.Contains("visible", StringComparison.Ordinal)
            && !masked.Contains("image_angle", StringComparison.Ordinal))
            return DecorStepKind.CameraCullOnly;

        if (masked.Contains("home_x", StringComparison.Ordinal)
            && masked.Contains("Camera.x", StringComparison.Ordinal))
        {
            if (masked.Contains("scene_depth", StringComparison.Ordinal) && masked.Contains("para_scale", StringComparison.Ordinal))
            {
                parallax = DecorParallaxKind.SceneItem;
                return DecorStepKind.PureParallax;
            }
            if (masked.Contains("para_x", StringComparison.Ordinal) && masked.Contains("para_y", StringComparison.Ordinal))
            {
                parallax = DecorParallaxKind.GrottoObject;
                if (Regex.IsMatch(masked, @"\bimage_angle\s*=.*\bcurrent_time\b"))
                    return DecorStepKind.TimeSway;
                return DecorStepKind.PureParallax;
            }
        }

        if (Regex.IsMatch(masked, @"\bimage_angle\s*=.*\bcurrent_time\b")
            && !Regex.IsMatch(masked, @"\b(?:hspeed|vspeed|speed|x\s*[+-]?=|y\s*[+-]?=)\b"))
        {
            hasCameraCheck = masked.Contains("Camera", StringComparison.Ordinal);
            return DecorStepKind.TimeSway;
        }

        var accum = AngleAccum().Match(masked);
        if (accum.Success
            && masked.Contains("current_time", StringComparison.Ordinal)
            && double.TryParse(accum.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
            && !Regex.IsMatch(masked, @"\b(?:hspeed|vspeed|speed|x\s*[+-]?=|y\s*[+-]?=)\b"))
            return DecorStepKind.AngleAccumPulse;

        if (AlphaLerp().IsMatch(masked)
            && !Regex.IsMatch(masked, @"\b(?:hspeed|vspeed|speed|x\s*[+-]?=|y\s*[+-]?=)\b"))
            return DecorStepKind.AlphaLerpLure;

        if (Regex.IsMatch(masked, @"\blight\.x\s*=\s*x\b")
            && Regex.IsMatch(masked, @"\blight\.y\s*=\s*y\b")
            && !Regex.IsMatch(masked, @"\b(?:hspeed|vspeed|speed)\b"))
            return DecorStepKind.LightSync;

        return null;
    }

    private static bool IsPureDraw(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string masked = InstanceGuard.Mask(text);
        if (RngCall().IsMatch(masked) || GameplaySideEffect().IsMatch(masked)
            || CollisionQuery().IsMatch(masked) || LifecycleCall().IsMatch(masked)
            || ExternalGameplayWrite().IsMatch(masked))
            return false;
        if (Regex.IsMatch(masked, @"\b(?:x|y|hspeed|vspeed|speed|visible)\s*="))
            return false;
        return true;
    }

    private static bool IsLightSyncDrawBegin(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string masked = InstanceGuard.Mask(text);
        if (RngCall().IsMatch(masked) || GameplaySideEffect().IsMatch(masked)
            || CollisionQuery().IsMatch(masked) || LifecycleCall().IsMatch(masked))
            return false;
        return masked.Contains("with (light", StringComparison.Ordinal);
    }

    private static bool IsSafeDecorCreate(string text, out bool createsChildren)
    {
        createsChildren = false;
        if (string.IsNullOrWhiteSpace(text)) return true;
        string masked = InstanceGuard.Mask(text);
        if (Regex.IsMatch(masked, @"\b(?:part_|audio_|ini_|file_|ds_|surface_|room_goto|game_|keyboard_|mouse_|physics_)"))
            return false;
        if (ExternalGameplayWrite().IsMatch(masked))
            return false;
        createsChildren = LifecycleCall().IsMatch(masked);
        return true;
    }

    private static string StripInherited(string masked) =>
        Regex.Replace(masked, @"\b(?:action_inherited|event_inherited)\s*\(\s*\)\s*;?", "").Trim();

    private static HashSet<string> CollectChunkCallbacks(UndertaleData data, Func<UndertaleCode, string> decompile)
    {
        var callbacks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in data.Code.Where(c => c.ParentEntry is null))
        {
            string text = decompile(code);
            if (!text.Contains("jamm_chunk_stream", StringComparison.Ordinal)) continue;
            foreach (Match m in Regex.Matches(InstanceGuard.Mask(text), @"\bjamm_chunk_stream\s*\(([^)]*)\)"))
            {
                var args = m.Groups[1].Value.Split(',');
                for (int i = 3; i < Math.Min(5, args.Length); i++)
                {
                    string tok = args[i].Trim();
                    if (int.TryParse(tok, out int idx) && idx >= 0 && idx < data.Scripts.Count)
                        callbacks.Add(data.Scripts[idx].Name.Content);
                    else if (!string.IsNullOrEmpty(tok))
                        callbacks.Add(tok);
                }
            }
        }
        return callbacks;
    }

    private static UndertaleGameObject? ResolveObjectToken(
        string token,
        Dictionary<string, UndertaleGameObject> byName,
        Dictionary<int, UndertaleGameObject> byId)
    {
        if (byName.TryGetValue(token, out var obj)) return obj;
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            && byId.TryGetValue(id, out var byIdx))
            return byIdx;
        return null;
    }

    private static List<(uint Subtype, UndertaleCode Code, UndertaleGameObject Owner)> CollectEvents(
        UndertaleGameObject obj, EventType type)
    {
        var list = new List<(uint, UndertaleCode, UndertaleGameObject)>();
        foreach (var ev in obj.Events[(int)type])
        {
            foreach (var act in ev.Actions)
            {
                if (act.CodeId is not null)
                    list.Add((ev.EventSubtype, act.CodeId, obj));
            }
        }
        return list;
    }

    private static List<(uint Subtype, UndertaleCode Code, UndertaleGameObject Owner)> CollectEffectiveEvents(
        UndertaleGameObject obj, EventType type)
    {
        var seen = new HashSet<uint>();
        var list = new List<(uint, UndertaleCode, UndertaleGameObject)>();
        for (var cur = obj; cur is not null; cur = cur.ParentId)
        {
            foreach (var ev in cur.Events[(int)type])
            {
                if (!seen.Add(ev.EventSubtype)) continue;
                foreach (var act in ev.Actions)
                {
                    if (act.CodeId is not null)
                        list.Add((ev.EventSubtype, act.CodeId, cur));
                }
            }
        }
        return list;
    }

    private static string ExtractBracedBlock(string text, int from)
    {
        int brace = text.IndexOf('{', from);
        if (brace < 0) return "";
        int depth = 0;
        for (int i = brace; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(brace, i - brace + 1);
            }
        }
        return text[brace..];
    }

    private static UndertaleGameObject[] ComputeMinimalSweepSet(
        List<DecorObjectPlan> plans,
        HashSet<UndertaleGameObject> coveredSet)
    {
        // Sweep the highest covered ancestor of each covered object (never an uncovered ancestor like Collidable).
        // Uncovered descendants of a covered ancestor (e.g. MissileSilo < CaveRockBig) are tagged jamm_ds_managed = false.
        var result = new List<UndertaleGameObject>();
        foreach (var p in plans.Where(p => p.Covered))
        {
            bool hasCoveredAncestor = false;
            for (var cur = p.GameObject.ParentId; cur is not null; cur = cur.ParentId)
            {
                if (coveredSet.Contains(cur)) { hasCoveredAncestor = true; break; }
            }
            if (!hasCoveredAncestor)
                result.Add(p.GameObject);
        }
        return result.ToArray();
    }

    internal static bool Inherits(UndertaleGameObject obj, UndertaleGameObject target)
    {
        for (var cur = obj; cur is not null; cur = cur.ParentId)
            if (cur == target) return true;
        return false;
    }

    private static bool InheritsAny(UndertaleGameObject obj, HashSet<string> names)
    {
        for (var cur = obj; cur is not null; cur = cur.ParentId)
            if (names.Contains(cur.Name.Content)) return true;
        return false;
    }
}
