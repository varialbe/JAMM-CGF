using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;

namespace Jamm.Core.Patching;

/// <summary>A single removed or unclassified performance-related change recorded by <see cref="ModSanitizer"/>.</summary>
public sealed record SanitizedChange(string Category, string Entry, string Summary, int BaseLine = 0, int ModLine = 0);

/// <summary>A classified code diff hunk produced by <see cref="ModSanitizer"/>.</summary>
public sealed record ClassifiedHunk(string Category, int BaseStart, int BaseLength, int ModStart, int ModLength,
    string BaseText, string ModText, string RevertedText, string Summary);

/// <summary>Persistent report of what <see cref="ModSanitizer"/> removed or flagged as unclassified for a mod patch.</summary>
public sealed record ModSanitizerReport(int SanitizerVersion, string ModSha256, string BaseSha256, bool IsJammMod,
    IReadOnlyList<SanitizedChange> Removed, IReadOnlyList<SanitizedChange> Unclassified)
{
    /// <summary>Count of removed third-party performance hacks (all categories except <see cref="ModSanitizer.HudStyle"/>).</summary>
    public int RemovedPerformanceCount => Removed.Count(r => r.Category != ModSanitizer.HudStyle);

    /// <summary>Count of reverted third-party HUD/UI asset changes (<see cref="ModSanitizer.HudStyle"/>).</summary>
    public int RemovedHudCount => Removed.Count(r => r.Category == ModSanitizer.HudStyle);

    /// <summary>Formats a concise human-readable multi-line summary for UI flyouts and CLI output.</summary>
    public string FormatDetails()
    {
        if (IsJammMod || Removed.Count == 0 && Unclassified.Count == 0)
            return "No third-party performance or HUD changes were removed.";
        StringBuilder sb = new();
        foreach (var item in Removed)
            sb.Append('[').Append(item.Category).Append("] ").Append(item.Entry).Append(": ").AppendLine(item.Summary);
        foreach (var item in Unclassified)
            sb.Append("[unclassified - kept] ").Append(item.Entry).Append(": ").AppendLine(item.Summary);
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Result of running <see cref="ModSanitizer"/> on a <see cref="ModPatch"/>.</summary>
public sealed record ModSanitizerResult(ModPatch Patch, ModSanitizerReport Report);

/// <summary>
/// Deterministic hunk-level and HUD-asset sanitizer over portable <see cref="ModPatch"/> documents.
/// Classifies code hunks into <c>timing</c>, <c>tick-throttle</c>, <c>distance-cull</c>, <c>fps-adaptive</c>,
/// <c>debug-overlay</c>, <c>content</c>, or <c>unclassified</c>; reverts non-content performance hacks and
/// third-party HUD/UI replacements (<c>hud-style</c>) to the base version while preserving content (including
/// code-referenced appended frames on HUD sprites such as <c>spr_upgradeicons</c>); and removes unreferenced helpers.
/// </summary>
public static partial class ModSanitizer
{
    /// <summary>Current deterministic sanitizer rule revision used to invalidate cached sanitized patches and compatibility checks.</summary>
    public const int Version = 2;

    /// <summary>Category for delta/time_scalar/DELTA_INSTANT/room_speed/game_set_speed/frame-limiter edits.</summary>
    public const string Timing = "timing";
    /// <summary>Category for modulo or counter step gates (<c>&gt;= N</c>, <c>== N</c>, <c>mod N</c>) around logic.</summary>
    public const string TickThrottle = "tick-throttle";
    /// <summary>Category for Camera/view <c>point_distance</c> cut-offs and <c>FishThinner.do_zoning</c> toggles.</summary>
    public const string DistanceCull = "distance-cull";
    /// <summary>Category for <c>fps</c>/<c>fps_real</c>-driven behaviour or <c>cull_trigger</c> rewrites.</summary>
    public const string FpsAdaptive = "fps-adaptive";
    /// <summary>Category for <c>draw_text*</c> overlays displaying FPS or camera/instance coordinates.</summary>
    public const string DebugOverlay = "debug-overlay";
    /// <summary>Category for third-party replacements of base HUD/UI sprites, fonts, or backgrounds.</summary>
    public const string HudStyle = "hud-style";
    /// <summary>Category for gameplay, fish, item, catalog, room, or UI content kept intact.</summary>
    public const string Content = "content";
    /// <summary>Category for ambiguous hunks kept intact without guessing.</summary>
    public const string Unclassified = "unclassified";

    /// <summary>
    /// Evidence-based set of base HUD/UI sprites drawn or assigned on HUD/UI objects:
    /// wallet (<c>Wallet</c>), sonar panel &amp; radar (<c>SonarPanel</c>, <c>SonarPanelAdvanced</c>, <c>RadarButton</c>),
    /// catalog &amp; hatalog UI (<c>Catalog*</c>, <c>Fishalog</c>, <c>Hatalog*</c>, <c>icon_Catalog</c>),
    /// shop/save cards &amp; menus (<c>RodCard</c>, <c>LureCard</c>, <c>BoatCard</c>, <c>ToolCard</c>, <c>SaveCard</c>,
    /// <c>GearCard*</c>, <c>SCB*</c>, <c>Controls</c>, <c>GM_Classic</c>, <c>GM_Realism</c>),
    /// upgrade menu (<c>UpgradeCard</c>, <c>UpgradeSlot</c>, <c>EmblemMaster</c>),
    /// buttons &amp; HUD icons (<c>Icons*</c>, <c>icon_*</c>), level/breath bar (<c>ExperienceBar</c>, <c>BreathBar</c>),
    /// and HUD/UI data/controller icons (<c>SaveData</c>, <c>ToolData</c>, <c>RodData</c>, <c>LureData</c>, <c>SaveManager</c>).
    /// </summary>
    public static readonly IReadOnlySet<string> HudSprites = new HashSet<string>(StringComparer.Ordinal)
    {
        // Wallet
        "spr_wallet",
        // Sonar panel & radar
        "spr_sonarpanel", "spr_sonarnoise", "spr_sonarblip", "spr_sonarhat", "spr_lureblip", "spr_radarbutton",
        // Catalog & Hatalog UI
        "spr_catalog_arrow", "spr_catalog_bar", "spr_catalog_btn", "spr_catalog_itembg", "spr_catalog_tab",
        "spr_hatalog_itembg", "spr_pixel",
        // Cards, shop/save menus, controls, game mode buttons
        "spr_rodcard", "spr_lurecard", "spr_rowcard", "spr_blankcard", "spr_savecard_btn",
        "spr_lock", "spr_tools", "spr_noboat", "spr_controls", "spr_gm_classic", "spr_gm_realism", "spr_button_icons",
        // Upgrade menu & emblems
        "spr_upgradeicon_bg", "spr_upgradeicons", "spr_emblemholder", "spr_emblems", "spr_crankbait_emblem",
        // Buttons & HUD icons
        "spr_icon", "spr_iconsmall", "spr_icon_labels",
        // Level / Experience / Breath bar
        "spr_xpbar",
        // UI data & save controller icons
        "spr_huddata", "spr_roddata", "spr_luredata", "spr_floppy"
    };

    /// <summary>
    /// Evidence-based set of base HUD/UI fonts drawn by <c>Wallet</c>, <c>ExperienceBar</c>, <c>BreathBar</c>,
    /// <c>Icons</c>, <c>Card</c>s, <c>Menu</c>s, <c>Catalog</c>, <c>Hatalog</c>, <c>Quest</c>, <c>Message*</c>, and <c>Credits*</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> HudFonts = new HashSet<string>(StringComparer.Ordinal)
    {
        "fnt_babyBig", "fnt_babyBigOld",
        "fnt_babyMedium", "fnt_babyMediumOld",
        "fnt_babyMoney", "fnt_babyQuest",
        "fnt_babySmall", "fnt_babySmallOld",
        "fnt_babyTiny", "fnt_babyVerySmall"
    };

    /// <summary>
    /// Evidence-based set of base HUD/UI backgrounds (empty in base CGF, where <c>bg_mountains*</c> are world backgrounds).
    /// </summary>
    public static readonly IReadOnlySet<string> HudBackgrounds = new HashSet<string>(StringComparer.Ordinal);

    // Explicit GML line patterns for each category:
    // 1. timing: writes to delta, delta_fixed, delta_previous, time_scalar, time_scalar_last, DELTA_INSTANT, room_speed;
    //    or calls to game_set_speed/game_get_speed; or busy-wait frame pacing with get_timer()/target_fps/target_frame_time.
    [GeneratedRegex(@"^(?:var\s+)?(?:(?:self|other|global|Girl)\.)?(?:delta|delta_fixed|delta_previous|time_scalar|time_scalar_last|DELTA_INSTANT|room_speed)\s*(?:=|\+=|-=|\*=|/=)", RegexOptions.CultureInvariant)]
    private static partial Regex TimingWriteRegex();

    [GeneratedRegex(@"\b(?:game_set_speed|game_get_speed)\s*\(|\b(?:target_fps|target_frame_time|frame_duration)\b|\bget_timer\s*\(\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex TimingFrameLimiterRegex();

    [GeneratedRegex(@"^(?:var\s+now\s*=|var\s+frame_duration\s*=|now\s*=\s*get_timer\s*\(\s*\)|frame_duration\s*=\s*now\s*-\s*last_frame_time|while\s*\(\s*frame_duration\s*<\s*target_frame_time\s*\)|last_frame_time\s*=\s*(?:now|get_timer\s*\(\s*\)))", RegexOptions.CultureInvariant)]
    private static partial Regex TimingLimiterLineRegex();

    // 2. tick-throttle: counter increments (x++, x += 1), counter/modulo gates (if (x >= N), if (x == N), if ((x mod N) == 0)), and counter resets (x = 0).
    [GeneratedRegex(@"^(?:var\s+)?([A-Za-z_]\w*)\s*(?:\+\+|\+=\s*1|=\s*\1\s*\+\s*1)\s*;?$", RegexOptions.CultureInvariant)]
    private static partial Regex CounterIncrementRegex();

    [GeneratedRegex(@"^(?:else\s+)?if\s*\(\s*(?:\(?\s*([A-Za-z_]\w*(?:\s*\+\s*[A-Za-z_]\w*)?)\s*(?:%|mod)\s*([2-9]|\d{2,})\s*\)?\s*(?:==|!=)\s*0|([A-Za-z_]\w*)\s*(?:>=|==|>)\s*([2-9]|\d{2,}))\s*\)$", RegexOptions.CultureInvariant)]
    private static partial Regex CounterGateRegex();

    [GeneratedRegex(@"^(?:var\s+)?([A-Za-z_]\w*)\s*=\s*0\s*;?$", RegexOptions.CultureInvariant)]
    private static partial Regex CounterResetRegex();

    [GeneratedRegex(@"^(?:check_?timer|tick_?timer|step_?timer|throttle_?timer|frame_?timer|cull_?timer|skip_?timer)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KnownThrottleNameRegex();

    // 3. distance-cull: point_distance to Camera or view_xview/view_yview, or FishThinner do_zoning toggles.
    [GeneratedRegex(@"\bpoint_distance\s*\([^;]*\b(?:Camera\b|view_xview\b|view_yview\b)", RegexOptions.CultureInvariant)]
    private static partial Regex DistanceCameraRegex();

    [GeneratedRegex(@"^(?:self\.)?do_zoning\s*=\s*(?:1|true|0|false)\s*;?$", RegexOptions.CultureInvariant)]
    private static partial Regex DoZoningRegex();

    // 4. fps-adaptive: reads of fps or fps_real for behaviour, and cull_trigger writes.
    [GeneratedRegex(@"\b(?:fps|fps_real)\b", RegexOptions.CultureInvariant)]
    private static partial Regex FpsReadRegex();

    [GeneratedRegex(@"^(?:(?:else\s+)?if\s*\(\s*(?:fps|fps_real)\s*(?:<|<=|>|>=|==|!=)\s*[\d.]+\s*\)|cull_trigger\s*(?:=|\+=|-=)\s*[\d.]+\s*;?)$", RegexOptions.CultureInvariant)]
    private static partial Regex FpsAdaptiveLineRegex();

    // 5. debug-overlay: draw_text* of FPS or Camera/instance X/Y coordinates, plus preceding draw_set_*/draw_x/draw_y setup.
    [GeneratedRegex(@"\bdraw_text\w*\s*\([^;]*(?:""FPS\s*:?\s*""|""X\s*:?\s*""|""Y\s*:?\s*""|\bstring\s*\(\s*(?:fps|fps_real|Camera\.x|Camera\.y)\s*\))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DebugDrawTextRegex();

    [GeneratedRegex(@"^(?:draw_set_(?:color|colour|font|halign|valign|alpha)\s*\(|draw_[xy]\s*=\s*)", RegexOptions.CultureInvariant)]
    private static partial Regex DrawSetupLineRegex();

    // Suspicious performance primitives that must not be silently guessed as content if used in unknown ways.
    [GeneratedRegex(@"\b(?:fps|fps_real|game_set_speed|instance_deactivate_all|instance_deactivate_object|instance_deactivate_region|cull_trigger)\b", RegexOptions.CultureInvariant)]
    private static partial Regex SuspiciousPerfTokenRegex();

    /// <summary>Returns true when <paramref name="patch"/> was produced by JAMM's own ModMaker (<c>obj_jamm_*</c> marker).</summary>
    public static bool IsJammMod(ModPatch patch) =>
        patch.Changes.TryGetValue("objects", out var objects)
        && objects.Added.Any(o => o.Name.StartsWith("obj_jamm_", StringComparison.Ordinal));

    /// <summary>Classifies every changed hunk in a single decompiled GML entry without modifying a patch.</summary>
    public static IReadOnlyList<ClassifiedHunk> ClassifyEntry(string entryName, string baseGml, string modGml, IReadOnlySet<string>? throttleVariables = null)
    {
        throttleVariables ??= DiscoverThrottleVariables([("", baseGml, modGml)]);
        string[] baseLines = SplitLines(LineDiff.Normalize(baseGml));
        string[] modLines = SplitLines(LineDiff.Normalize(modGml));
        var spans = ExtractHunkSpans(baseLines, modLines, entryName, throttleVariables);
        List<ClassifiedHunk> result = new(spans.Count);
        foreach (var (b0, b1, m0, m1) in spans)
        {
            string[] bSlice = baseLines[b0..b1];
            string[] mSlice = modLines[m0..m1];
            var (category, revertedSlice, summary) = ClassifyAndRevertSlice(entryName, bSlice, mSlice, throttleVariables);
            result.Add(new(
                category,
                b0 + 1,
                b1 - b0,
                m0 + 1,
                m1 - m0,
                JoinLines(bSlice),
                JoinLines(mSlice),
                JoinLines(revertedSlice),
                summary));
        }
        return result;
    }

    /// <summary>Sanitizes a portable <see cref="ModPatch"/> deterministically without touching game archives.</summary>
    public static ModSanitizerResult Sanitize(ModPatch patch, bool keepPerformanceChanges = false, bool keepHudDesign = false)
    {
        if (IsJammMod(patch))
        {
            var jammReport = new ModSanitizerReport(Version, patch.Mod.Sha256, patch.Base.Sha256, true, [], []);
            return new(patch, jammReport);
        }

        List<SanitizedChange> removed = [];
        List<SanitizedChange> unclassified = [];
        HashSet<string> removedTokens = new(StringComparer.Ordinal);

        var codeEntries = patch.Changes["code"];
        var objectChanges = patch.Changes["objects"];
        var roomChanges = patch.Changes["rooms"];
        var variableChanges = patch.Changes["variables"];
        var functionChanges = patch.Changes["functions"];

        List<AssetPatch> keptAddedCode = [.. codeEntries.Added];
        List<AssetPatch> keptChangedCode = [.. codeEntries.Changed];
        List<AssetPatch> keptAddedObjects = [.. objectChanges.Added];
        List<AssetPatch> keptChangedObjects = [.. objectChanges.Changed];
        List<AssetPatch> keptChangedRooms = [.. roomChanges.Changed];
        List<AssetPatch> keptAddedVariables = [.. variableChanges.Added];
        List<AssetPatch> keptAddedFunctions = [.. functionChanges.Added];
        HashSet<string> candidateHelperObjects = new(StringComparer.Ordinal);
        HashSet<string> candidateHelperCode = new(StringComparer.Ordinal);

        if (!keepPerformanceChanges)
        {
            var throttleVars = DiscoverThrottleVariables(
                codeEntries.Changed.Where(c => c.BaseCode?.Format == "gml" && c.ModCode?.Format == "gml")
                    .Select(c => (c.Name, c.BaseCode!.Text, c.ModCode!.Text))
                    .Concat(codeEntries.Added.Where(a => a.ModCode?.Format == "gml")
                        .Select(a => (a.Name, "", a.ModCode!.Text))));

            // 1. Classify and sanitize changed code entries at hunk granularity.
            keptChangedCode = [];
            foreach (var entry in codeEntries.Changed)
            {
                if (entry.BaseCode is not { Format: "gml" } baseCode || entry.ModCode is not { Format: "gml" } modCode)
                {
                    keptChangedCode.Add(entry);
                    continue;
                }
                string[] baseLines = SplitLines(LineDiff.Normalize(baseCode.Text));
                string[] modLines = SplitLines(LineDiff.Normalize(modCode.Text));
                var hunks = ClassifyEntry(entry.Name, baseCode.Text, modCode.Text, throttleVars);
                if (hunks.Count == 0)
                {
                    keptChangedCode.Add(entry);
                    continue;
                }

                bool anyReverted = false;
                List<string> rebuilt = [];
                int baseCursor = 0;
                foreach (var hunk in hunks)
                {
                    int bStart = hunk.BaseStart - 1;
                    int bEnd = bStart + hunk.BaseLength;
                    rebuilt.AddRange(baseLines[baseCursor..bStart]);
                    if (hunk.Category is Timing or TickThrottle or DistanceCull or FpsAdaptive or DebugOverlay)
                    {
                        anyReverted = true;
                        rebuilt.AddRange(SplitLines(hunk.RevertedText));
                        removed.Add(new(hunk.Category, entry.Name, hunk.Summary, hunk.BaseStart, hunk.ModStart));
                        foreach (string id in GmlReferences.Identifiers(hunk.ModText))
                            removedTokens.Add(id);
                    }
                    else
                    {
                        if (hunk.Category == Unclassified)
                            unclassified.Add(new(Unclassified, entry.Name, hunk.Summary, hunk.BaseStart, hunk.ModStart));
                        rebuilt.AddRange(SplitLines(hunk.ModText));
                    }
                    baseCursor = bEnd;
                }
                rebuilt.AddRange(baseLines[baseCursor..]);

                if (!anyReverted)
                {
                    keptChangedCode.Add(entry);
                    continue;
                }

                string newText = ReconstructText(rebuilt, baseCode.Text);
                if (LineDiff.Normalize(newText) == LineDiff.Normalize(baseCode.Text)
                    && entry.Base is { }
                    && baseCode.Parent == modCode.Parent
                    && baseCode.Children.SequenceEqual(modCode.Children, StringComparer.Ordinal))
                {
                    // Entire code entry reverted to base; omit it from Changed so it causes no false conflicts.
                    continue;
                }

                var updatedModCode = modCode with { Text = newText };
                keptChangedCode.Add(entry with { ModCode = updatedModCode });
            }

            // 2. Identify added helper objects & added helper code entries that only serve performance hacks.
            Dictionary<string, (string Category, string Summary)> addedCodeClassifications = new(StringComparer.Ordinal);
            foreach (var addedCode in codeEntries.Added)
            {
                if (addedCode.ModCode is not { Format: "gml" } modCode) continue;
                string trimmed = modCode.Text.Trim();
                if (trimmed.Length == 0)
                {
                    addedCodeClassifications[addedCode.Name] = ("empty", "empty event");
                    continue;
                }
                string[] lines = SplitLines(LineDiff.Normalize(modCode.Text));
                var (cat, _, summary) = ClassifyAndRevertSlice(addedCode.Name, [], lines, throttleVars);
                addedCodeClassifications[addedCode.Name] = (cat, summary);
            }

            Dictionary<string, (string Category, string Summary)> helperObjectInfo = new(StringComparer.Ordinal);
            foreach (var obj in objectChanges.Added)
            {
                var eventCodes = ExtractObjectEventCodes(obj.Mod);
                string prefix = $"gml_Object_{obj.Name}_";
                foreach (var c in codeEntries.Added.Where(a => a.Name.StartsWith(prefix, StringComparison.Ordinal)))
                    eventCodes.Add(c.Name);
                if (eventCodes.Count == 0) continue;
                var cats = eventCodes
                    .Select(c => addedCodeClassifications.TryGetValue(c, out var info) ? info : (Category: Content, Summary: ""))
                    .Where(x => x.Category != "empty")
                    .ToArray();
                if (cats.Length > 0 && cats.All(x => x.Category is Timing or TickThrottle or DistanceCull or FpsAdaptive or DebugOverlay))
                {
                    candidateHelperObjects.Add(obj.Name);
                    helperObjectInfo[obj.Name] = (cats[0].Category, $"object {obj.Name} ({string.Join("; ", cats.Select(c => c.Summary).Distinct())})");
                }
            }

            foreach (var addedCode in codeEntries.Added)
            {
                if (candidateHelperObjects.Any(o => addedCode.Name.StartsWith($"gml_Object_{o}_", StringComparison.Ordinal))
                    || objectChanges.Added.Where(o => candidateHelperObjects.Contains(o.Name))
                        .SelectMany(o => ExtractObjectEventCodes(o.Mod)).Contains(addedCode.Name, StringComparer.Ordinal))
                {
                    candidateHelperCode.Add(addedCode.Name);
                    continue;
                }
                if (addedCodeClassifications.TryGetValue(addedCode.Name, out var info)
                    && info.Category is Timing or TickThrottle or DistanceCull or FpsAdaptive or DebugOverlay)
                {
                    candidateHelperCode.Add(addedCode.Name);
                }
            }

            HashSet<string> keptContentIdentifiers = CollectKeptContentIdentifiers(
                codeEntries.Added.Where(a => !candidateHelperCode.Contains(a.Name)),
                keptChangedCode,
                objectChanges.Added.Where(o => !candidateHelperObjects.Contains(o.Name)));

            candidateHelperObjects.RemoveWhere(keptContentIdentifiers.Contains);
            candidateHelperCode.RemoveWhere(keptContentIdentifiers.Contains);
            foreach (var keptObj in objectChanges.Added.Where(o => !candidateHelperObjects.Contains(o.Name)))
                foreach (string codeName in ExtractObjectEventCodes(keptObj.Mod))
                    candidateHelperCode.Remove(codeName);

            keptAddedCode = [];
            foreach (var addedCode in codeEntries.Added)
            {
                if (candidateHelperCode.Contains(addedCode.Name))
                {
                    if (addedCode.ModCode is { } mc)
                        foreach (string id in GmlReferences.Identifiers(mc.Text))
                            removedTokens.Add(id);
                    bool belongsToHelperObject = candidateHelperObjects.Any(o =>
                        addedCode.Name.StartsWith($"gml_Object_{o}_", StringComparison.Ordinal));
                    if (!belongsToHelperObject && addedCodeClassifications.TryGetValue(addedCode.Name, out var info) && info.Category != "empty")
                        removed.Add(new(info.Category, addedCode.Name, info.Summary, 0, 1));
                    continue;
                }
                keptAddedCode.Add(addedCode);
            }

            keptAddedObjects = [];
            foreach (var obj in objectChanges.Added)
            {
                if (candidateHelperObjects.Contains(obj.Name))
                {
                    var info = helperObjectInfo[obj.Name];
                    removed.Add(new(info.Category, $"objects/{obj.Name}", info.Summary));
                    removedTokens.Add(obj.Name);
                    continue;
                }
                keptAddedObjects.Add(obj);
            }

            keptChangedObjects = [];
            foreach (var obj in objectChanges.Changed)
            {
                var filteredFields = obj.Fields.Where(f => !FieldReferencesRemovedCode(f, candidateHelperCode)).ToArray();
                if (filteredFields.Length == obj.Fields.Count)
                {
                    keptChangedObjects.Add(obj);
                    continue;
                }
                if (obj.Base is not { } baseObj || filteredFields.Length == 0)
                    continue;
                var updatedMod = FieldMergeEngine.Apply(baseObj, filteredFields, obj.Mod);
                var recomputedFields = FieldDiff.Compare(baseObj, updatedMod);
                if (recomputedFields.Count > 0 && !JsonElement.DeepEquals(baseObj, updatedMod))
                    keptChangedObjects.Add(obj with { Mod = updatedMod, Fields = recomputedFields });
            }

            // 3. Sanitize rooms.Changed: revert /speed timing changes and remove instances of removed helper objects.
            keptChangedRooms = [];
            foreach (var room in roomChanges.Changed)
            {
                List<FieldChange> keptFields = [];
                bool roomModifiedBySanitizer = false;
                foreach (var field in room.Fields)
                {
                    if (field.Path == "/speed" && field.Base is { } baseSpeed && field.Mod is { } modSpeed)
                    {
                        removed.Add(new(Timing, $"rooms/{room.Name}", $"room speed {baseSpeed.GetRawText()} -> {modSpeed.GetRawText()} reverted to {baseSpeed.GetRawText()}"));
                        roomModifiedBySanitizer = true;
                        continue;
                    }
                    if (FieldReferencesRemovedObjectInstance(field, candidateHelperObjects))
                    {
                        roomModifiedBySanitizer = true;
                        continue;
                    }
                    keptFields.Add(field);
                }
                if (!roomModifiedBySanitizer)
                {
                    keptChangedRooms.Add(room);
                    continue;
                }
                if (room.Base is not { } baseRoom || keptFields.Count == 0)
                    continue;
                var updatedMod = FieldMergeEngine.Apply(baseRoom, keptFields, room.Mod);
                var recomputedFields = FieldDiff.Compare(baseRoom, updatedMod);
                if (recomputedFields.Count > 0 && !JsonElement.DeepEquals(baseRoom, updatedMod))
                    keptChangedRooms.Add(room with { Mod = updatedMod, Fields = recomputedFields });
            }

            // 4. Recompute identifiers still used anywhere in kept code/objects/rooms so helper variables/functions can be pruned safely.
            var survivingIdentifiers = CollectKeptContentIdentifiers(keptAddedCode, keptChangedCode, keptAddedObjects);
            keptAddedVariables = [];
            foreach (var variable in variableChanges.Added)
            {
                string? varName = TryParseVariableName(variable.Name);
                if (varName is not null
                    && (removedTokens.Contains(varName) || throttleVars.Contains(varName))
                    && !survivingIdentifiers.Contains(varName))
                {
                    continue;
                }
                keptAddedVariables.Add(variable);
            }

            keptAddedFunctions = [];
            foreach (var fn in functionChanges.Added)
            {
                if (removedTokens.Contains(fn.Name) && !survivingIdentifiers.Contains(fn.Name))
                    continue;
                keptAddedFunctions.Add(fn);
            }
        }

        // 5. Sanitize HUD/UI assets (sprites, fonts, backgrounds) unless keepHudDesign is set.
        var spriteChanges = patch.Changes["sprites"];
        var fontChanges = patch.Changes["fonts"];
        var backgroundChanges = patch.Changes["backgrounds"];
        List<AssetPatch> keptChangedSprites = [.. spriteChanges.Changed];
        List<AssetPatch> keptChangedFonts = [.. fontChanges.Changed];
        List<AssetPatch> keptChangedBackgrounds = [.. backgroundChanges.Changed];

        if (!keepHudDesign)
        {
            keptChangedSprites = SanitizeHudSprites(spriteChanges.Changed, keptAddedCode, keptChangedCode, removed);
            keptChangedFonts = [];
            foreach (var font in fontChanges.Changed)
            {
                if (HudFonts.Contains(font.Name))
                {
                    removed.Add(new(HudStyle, $"fonts/{font.Name}", $"reverted HUD font {font.Name} to base"));
                    continue;
                }
                keptChangedFonts.Add(font);
            }
            keptChangedBackgrounds = [];
            foreach (var bg in backgroundChanges.Changed)
            {
                if (HudBackgrounds.Contains(bg.Name))
                {
                    removed.Add(new(HudStyle, $"backgrounds/{bg.Name}", $"reverted HUD background {bg.Name} to base"));
                    continue;
                }
                keptChangedBackgrounds.Add(bg);
            }
        }

        // 6. Filter References to drop removed helper objects/code.
        var updatedReferences = patch.References?
            .Where(r => !IsRemovedReference(r, candidateHelperObjects, candidateHelperCode))
            .ToArray();

        var updatedChanges = new SortedDictionary<string, AssetChanges>(patch.Changes, StringComparer.Ordinal)
        {
            ["code"] = codeEntries with { Added = keptAddedCode, Changed = keptChangedCode },
            ["objects"] = objectChanges with { Added = keptAddedObjects, Changed = keptChangedObjects },
            ["rooms"] = roomChanges with { Changed = keptChangedRooms },
            ["sprites"] = spriteChanges with { Changed = keptChangedSprites },
            ["fonts"] = fontChanges with { Changed = keptChangedFonts },
            ["backgrounds"] = backgroundChanges with { Changed = keptChangedBackgrounds },
            ["variables"] = variableChanges with { Added = keptAddedVariables },
            ["functions"] = functionChanges with { Added = keptAddedFunctions }
        };

        var sanitizedPatch = patch with { Changes = updatedChanges, References = updatedReferences };
        var report = new ModSanitizerReport(Version, patch.Mod.Sha256, patch.Base.Sha256, false, removed, unclassified);
        return new(sanitizedPatch, report);
    }

    private static List<AssetPatch> SanitizeHudSprites(
        IReadOnlyList<AssetPatch> changedSprites,
        IReadOnlyList<AssetPatch> keptAddedCode,
        IReadOnlyList<AssetPatch> keptChangedCode,
        List<SanitizedChange> removed)
    {
        List<AssetPatch> result = [];
        foreach (var entry in changedSprites)
        {
            if (!HudSprites.Contains(entry.Name))
            {
                result.Add(entry);
                continue;
            }
            if (entry.Base is not { ValueKind: JsonValueKind.Object } baseSnap
                || entry.Mod.ValueKind != JsonValueKind.Object
                || !baseSnap.TryGetProperty("frames", out var baseFramesElem)
                || !entry.Mod.TryGetProperty("frames", out var modFramesElem)
                || baseFramesElem.ValueKind != JsonValueKind.Array
                || modFramesElem.ValueKind != JsonValueKind.Array)
            {
                removed.Add(new(HudStyle, $"sprites/{entry.Name}", $"reverted HUD sprite {entry.Name} to base"));
                continue;
            }

            var baseFrames = baseFramesElem.EnumerateArray().ToArray();
            var modFrames = modFramesElem.EnumerateArray().ToArray();
            int baseCount = baseFrames.Length;
            int modCount = modFrames.Length;

            if (modCount <= baseCount)
            {
                removed.Add(new(HudStyle, $"sprites/{entry.Name}", $"reverted HUD sprite {entry.Name} to base"));
                continue;
            }

            // Mod has more frames than base: check which appended frames (baseCount .. modCount - 1) are referenced by kept mod code.
            int keptFrameCount = baseCount;
            for (int f = baseCount; f < modCount; f++)
            {
                if (IsAppendedHudFrameReferencedByCode(entry.Name, f, keptAddedCode, keptChangedCode))
                    keptFrameCount = f + 1;
            }

            if (keptFrameCount <= baseCount)
            {
                removed.Add(new(HudStyle, $"sprites/{entry.Name}", $"reverted HUD sprite {entry.Name} (appended frames {baseCount}..{modCount - 1} unreferenced in code) to base"));
                continue;
            }

            // Build a hybrid snapshot: keep all base properties and base frames 0..baseCount-1 unchanged,
            // and append only frames baseCount..keptFrameCount-1 from the mod.
            var combinedFrames = baseFrames.Concat(modFrames[baseCount..keptFrameCount]).ToArray();
            Dictionary<string, JsonElement> props = new(StringComparer.Ordinal);
            foreach (var prop in baseSnap.EnumerateObject())
                props[prop.Name] = prop.Value;
            props["frameCount"] = JsonSerializer.SerializeToElement(keptFrameCount);
            props["frames"] = JsonSerializer.SerializeToElement(combinedFrames);
            JsonElement updatedMod = JsonSerializer.SerializeToElement(props);
            var recomputedFields = FieldDiff.Compare(baseSnap, updatedMod);

            int modifiedBaseFrames = 0;
            for (int i = 0; i < baseCount; i++)
                if (!JsonElement.DeepEquals(baseFrames[i], modFrames[i]))
                    modifiedBaseFrames++;
            bool hadOtherBaseEdits = entry.Fields.Any(f =>
                f.Path != "/frameCount" && !f.Path.StartsWith("/frames/", StringComparison.Ordinal))
                || keptFrameCount < modCount;

            if (modifiedBaseFrames > 0 || hadOtherBaseEdits)
            {
                removed.Add(new(HudStyle, $"sprites/{entry.Name}",
                    $"reverted {modifiedBaseFrames} base frame(s) of {entry.Name} to original HUD art; kept appended frame(s) {baseCount}..{keptFrameCount - 1}"));
            }

            if (recomputedFields.Count > 0 && !JsonElement.DeepEquals(baseSnap, updatedMod))
                result.Add(entry with { Mod = updatedMod, Fields = recomputedFields });
        }
        return result;
    }

    private static bool IsAppendedHudFrameReferencedByCode(
        string spriteName,
        int frameIndex,
        IReadOnlyList<AssetPatch> keptAddedCode,
        IReadOnlyList<AssetPatch> keptChangedCode)
    {
        string frameToken = frameIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // UpgradeCard_Other_11 and UpgradeSlot_Other_11 index spr_upgradeicons at (1 + card_id),
        // so frame N corresponds to upgrade index (N - 1) in UpgradeData / UpgradeMenu / UpgradeCard.
        string? upgradeToken = spriteName == "spr_upgradeicons" && frameIndex >= 1
            ? (frameIndex - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

        foreach (var entry in keptAddedCode.Concat(keptChangedCode))
        {
            if (entry.ModCode is not { Format: "gml" } modCode) continue;
            string addedOrChangedText = entry.BaseCode is { Format: "gml" } baseCode
                ? ExtractAddedOrChangedLines(baseCode.Text, modCode.Text)
                : modCode.Text;
            if (addedOrChangedText.Length == 0) continue;

            var tokens = GmlReferences.Tokens(addedOrChangedText);
            bool mentionsSprite = tokens.Any(t => t.Kind == 'i' && t.Value == spriteName);
            bool hasFrameNumber = tokens.Any(t => t.Kind == 'n' && t.Value == frameToken);
            if (mentionsSprite && hasFrameNumber)
                return true;

            if (upgradeToken is not null
                && (entry.Name.Contains("Upgrade", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
                    || tokens.Any(t => t.Kind == 'i' && t.Value is "UpgradeData" or "UpgradeMenu" or "UpgradeCard" or "UnlockUpgrade" or "unlocks" or "spawn_order"))
                && tokens.Any(t => t.Kind == 'n' && (t.Value == upgradeToken || t.Value == frameToken)))
            {
                return true;
            }

            if (spriteName == "spr_icon_labels"
                && tokens.Any(t => t.Kind == 'i' && t.Value == "img_label")
                && hasFrameNumber)
            {
                return true;
            }

            if (spriteName == "spr_tools"
                && (entry.Name.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                    || tokens.Any(t => t.Kind == 'i' && t.Value is "ToolData" or "UnlockTool"))
                && hasFrameNumber)
            {
                return true;
            }
        }
        return false;
    }

    private static string ExtractAddedOrChangedLines(string baseGml, string modGml)
    {
        var edits = LineDiff.Compare(LineDiff.Normalize(baseGml), LineDiff.Normalize(modGml));
        StringBuilder sb = new();
        foreach (var edit in edits)
            if (edit.Kind == '+')
                sb.AppendLine(edit.Text);
        return sb.ToString();
    }

    private static HashSet<string> DiscoverThrottleVariables(IEnumerable<(string Name, string BaseGml, string ModGml)> entries)
    {
        HashSet<string> result = new(StringComparer.Ordinal) { "check_timer", "checkTimer" };
        foreach (var (_, baseGml, modGml) in entries)
        {
            string[] baseLines = SplitLines(LineDiff.Normalize(baseGml));
            string[] modLines = SplitLines(LineDiff.Normalize(modGml));
            var baseSet = baseLines.Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);
            HashSet<string> incremented = new(StringComparer.Ordinal);
            HashSet<string> gated = new(StringComparer.Ordinal);
            HashSet<string> reset = new(StringComparer.Ordinal);
            foreach (string raw in modLines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || baseSet.Contains(line)) continue;
                if (CounterIncrementRegex().Match(line) is { Success: true } inc)
                    incremented.Add(inc.Groups[1].Value);
                if (CounterGateRegex().Match(line) is { Success: true } gate)
                {
                    string expr = gate.Groups[1].Success ? gate.Groups[1].Value : gate.Groups[3].Value;
                    foreach (string id in GmlReferences.Identifiers(expr))
                        gated.Add(id);
                }
                if (CounterResetRegex().Match(line) is { Success: true } rst)
                {
                    string v = rst.Groups[1].Value;
                    reset.Add(v);
                    if (KnownThrottleNameRegex().IsMatch(v))
                        result.Add(v);
                }
            }
            foreach (string v in incremented.Intersect(gated, StringComparer.Ordinal))
                if (reset.Contains(v) || KnownThrottleNameRegex().IsMatch(v))
                    result.Add(v);
        }
        return result;
    }

    private static List<(int BaseStart, int BaseEnd, int ModStart, int ModEnd)> ExtractHunkSpans(
        string[] baseLines, string[] modLines, string entryName, IReadOnlySet<string> throttleVars)
    {
        var edits = LineDiff.Compare(JoinLines(baseLines), JoinLines(modLines));
        List<(int B0, int B1, int M0, int M1)> raw = [];
        int bPos = 0, mPos = 0;
        int bStart = -1, mStart = -1, bLen = 0, mLen = 0;
        foreach (var edit in edits)
        {
            if (edit.Kind == ' ')
            {
                Flush();
                bPos++;
                mPos++;
            }
            else
            {
                if (bStart < 0) { bStart = bPos; mStart = mPos; }
                if (edit.Kind == '-') { bLen++; bPos++; }
                else { mLen++; mPos++; }
            }
        }
        Flush();
        void Flush()
        {
            if (bStart < 0) return;
            raw.Add((bStart, bStart + bLen, mStart, mStart + mLen));
            bStart = -1; mStart = -1; bLen = 0; mLen = 0;
        }
        if (raw.Count == 0) return [];

        // Merge spans when braces are unbalanced or intervening lines are trivial structural tokens ({, }, else).
        List<(int B0, int B1, int M0, int M1)> merged = [raw[0]];
        for (int i = 1; i < raw.Count; i++)
        {
            var prev = merged[^1];
            var cur = raw[i];
            var between = baseLines[prev.B1..cur.B0];
            bool trivialBetween = between.All(l => l.Trim() is "" or "{" or "}" or "else");
            if (BraceDelta(baseLines[prev.B0..prev.B1]) != 0
                || BraceDelta(modLines[prev.M0..prev.M1]) != 0
                || trivialBetween)
            {
                merged[^1] = (prev.B0, cur.B1, prev.M0, cur.M1);
            }
            else
            {
                merged.Add(cur);
            }
        }

        // Balance any span where LineDiff matched a closing '}' or opening '{' as an equal line at the span boundary.
        for (int i = 0; i < merged.Count; i++)
        {
            var (sB0, sB1, sM0, sM1) = merged[i];
            int prevB1 = i > 0 ? merged[i - 1].B1 : 0;
            int prevM1 = i > 0 ? merged[i - 1].M1 : 0;
            int nextB0 = i + 1 < merged.Count ? merged[i + 1].B0 : baseLines.Length;
            int nextM0 = i + 1 < merged.Count ? merged[i + 1].M0 : modLines.Length;
            while ((BraceDelta(baseLines[sB0..sB1]) > 0 || BraceDelta(modLines[sM0..sM1]) > 0)
                && sB1 < nextB0 && sM1 < nextM0)
            {
                sB1++;
                sM1++;
            }
            while ((BraceDelta(baseLines[sB0..sB1]) < 0 || BraceDelta(modLines[sM0..sM1]) < 0)
                && sB0 > prevB1 && sM0 > prevM1)
            {
                sB0--;
                sM0--;
            }
            merged[i] = (sB0, sB1, sM0, sM1);
        }

        // Split spans if they contain adjacent independent statement blocks of different categories.
        List<(int B0, int B1, int M0, int M1)> refined = [];
        foreach (var span in merged)
        {
            refined.AddRange(TrySplitSpanByStatementCategory(baseLines, modLines, span, entryName, throttleVars));
        }
        return refined;
    }

    private static IEnumerable<(int B0, int B1, int M0, int M1)> TrySplitSpanByStatementCategory(
        string[] baseLines, string[] modLines, (int B0, int B1, int M0, int M1) span,
        string entryName, IReadOnlySet<string> throttleVars)
    {
        int bCount = span.B1 - span.B0;
        int mCount = span.M1 - span.M0;

        // Case A: 1-to-1 single-line statements with zero brace delta on every line.
        if (bCount > 1 && bCount == mCount)
        {
            bool allSimple = true;
            for (int k = 0; k < bCount; k++)
            {
                string bl = baseLines[span.B0 + k].Trim();
                string ml = modLines[span.M0 + k].Trim();
                if (BraceDelta([bl]) != 0 || BraceDelta([ml]) != 0 || IsControlHeader(bl) || IsControlHeader(ml))
                {
                    allSimple = false;
                    break;
                }
            }
            if (allSimple)
            {
                string[] cats = new string[bCount];
                for (int k = 0; k < bCount; k++)
                    cats[k] = ClassifyAndRevertSlice(entryName, [baseLines[span.B0 + k]], [modLines[span.M0 + k]], throttleVars).Category;
                if (cats.Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    int runStart = 0;
                    for (int k = 1; k <= bCount; k++)
                    {
                        if (k == bCount || cats[k] != cats[runStart])
                        {
                            yield return (span.B0 + runStart, span.B0 + k, span.M0 + runStart, span.M0 + k);
                            runStart = k;
                        }
                    }
                    yield break;
                }
            }
        }

        // Case B: Pure insertion (bCount == 0) with multiple brace-balanced statement blocks of different categories.
        if (bCount == 0 && mCount > 1)
        {
            var blocks = GroupIntoMinimalBlocks(modLines[span.M0..span.M1], throttleVars);
            if (blocks.Count > 1)
            {
                string[] cats = blocks.Select(b => ClassifyAndRevertSlice(entryName, [], modLines[(span.M0 + b.Start)..(span.M0 + b.End)], throttleVars).Category).ToArray();
                if (cats.Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    int runStart = 0;
                    for (int k = 1; k <= blocks.Count; k++)
                    {
                        if (k == blocks.Count || cats[k] != cats[runStart])
                        {
                            yield return (span.B0, span.B1, span.M0 + blocks[runStart].Start, span.M0 + blocks[k - 1].End);
                            runStart = k;
                        }
                    }
                    yield break;
                }
            }
        }

        // Case C: bCount > 0 and modLines has multiple brace-balanced top-level blocks where one block wraps/replaces bSlice
        // and adjacent blocks are independent insertions of a different category.
        if (bCount > 0 && mCount > 1)
        {
            var blocks = GroupIntoMinimalBlocks(modLines[span.M0..span.M1], throttleVars);
            if (blocks.Count > 1)
            {
                var bSlice = baseLines[span.B0..span.B1];
                var bTrimmed = bSlice.Select(l => l.Trim()).Where(l => !IsStructuralToken(l)).ToHashSet(StringComparer.Ordinal);
                var bLhs = bTrimmed.Select(TryExtractLhs).Where(s => s is not null).ToHashSet(StringComparer.Ordinal);
                int ownerIdx = -1;
                for (int k = 0; k < blocks.Count; k++)
                {
                    var blkLines = modLines[(span.M0 + blocks[k].Start)..(span.M0 + blocks[k].End)];
                    if (blkLines.Any(l => bTrimmed.Contains(l.Trim())))
                        ownerIdx = ownerIdx < 0 ? k : -2;
                }
                if (ownerIdx == -1)
                {
                    for (int k = 0; k < blocks.Count; k++)
                    {
                        var blkLines = modLines[(span.M0 + blocks[k].Start)..(span.M0 + blocks[k].End)];
                        if (blkLines.Select(l => TryExtractLhs(l.Trim())).Any(lhs => lhs is not null && bLhs.Contains(lhs)))
                            ownerIdx = ownerIdx < 0 ? k : -2;
                    }
                }
                if (ownerIdx >= 0)
                {
                    string ownerCat = ClassifyAndRevertSlice(entryName, bSlice, modLines[(span.M0 + blocks[ownerIdx].Start)..(span.M0 + blocks[ownerIdx].End)], throttleVars).Category;
                    string[] insertCats = blocks.Select((b, idx) => idx == ownerIdx ? ownerCat
                        : ClassifyAndRevertSlice(entryName, [], modLines[(span.M0 + b.Start)..(span.M0 + b.End)], throttleVars).Category).ToArray();
                    if (insertCats.Distinct(StringComparer.Ordinal).Count() > 1 && insertCats.All(c => c != Unclassified))
                    {
                        for (int k = 0; k < blocks.Count; k++)
                        {
                            var b = blocks[k];
                            if (k < ownerIdx)
                                yield return (span.B0, span.B0, span.M0 + b.Start, span.M0 + b.End);
                            else if (k == ownerIdx)
                                yield return (span.B0, span.B1, span.M0 + b.Start, span.M0 + b.End);
                            else
                                yield return (span.B1, span.B1, span.M0 + b.Start, span.M0 + b.End);
                        }
                        yield break;
                    }
                }
            }
        }

        yield return span;
    }

    private static string? TryExtractLhs(string trimmed)
    {
        int eq = trimmed.IndexOf('=');
        if (eq <= 0 || eq + 1 < trimmed.Length && trimmed[eq + 1] == '=') return null;
        string lhs = trimmed[..eq].TrimEnd('+', '-', '*', '/').Trim();
        return lhs.Length > 0 && lhs.All(c => char.IsLetterOrDigit(c) || c is '_' or '.') ? lhs : null;
    }

    private static List<(int Start, int End)> GroupIntoMinimalBlocks(string[] lines, IReadOnlySet<string> throttleVars)
    {
        List<(int Start, int End)> rawBlocks = [];
        int i = 0;
        while (i < lines.Length)
        {
            int start = i;
            int depth = 0;
            while (i < lines.Length)
            {
                string trimmed = lines[i].Trim();
                depth += BraceDelta([trimmed]);
                i++;
                if (depth == 0)
                {
                    if (IsControlHeader(trimmed)) continue;
                    // Attach trailing else block to the same if statement.
                    if (i < lines.Length && lines[i].Trim().StartsWith("else", StringComparison.Ordinal)) continue;
                    // Attach counter++ immediately preceding if (counter >= N).
                    if (CounterIncrementRegex().Match(trimmed) is { Success: true } inc
                        && throttleVars.Contains(inc.Groups[1].Value)
                        && i < lines.Length
                        && CounterGateRegex().IsMatch(lines[i].Trim()))
                        continue;
                    // Attach draw_set_* / draw_x / draw_y setup lines to following draw lines.
                    if (DrawSetupLineRegex().IsMatch(trimmed) && i < lines.Length)
                        continue;
                    break;
                }
            }
            rawBlocks.Add((start, i));
        }
        return rawBlocks;
    }

    private static bool IsControlHeader(string trimmed) =>
        trimmed.StartsWith("if ", StringComparison.Ordinal) || trimmed.StartsWith("if(", StringComparison.Ordinal)
        || trimmed.StartsWith("else", StringComparison.Ordinal)
        || trimmed.StartsWith("for ", StringComparison.Ordinal) || trimmed.StartsWith("for(", StringComparison.Ordinal)
        || trimmed.StartsWith("while ", StringComparison.Ordinal) || trimmed.StartsWith("while(", StringComparison.Ordinal)
        || trimmed.StartsWith("switch ", StringComparison.Ordinal) || trimmed.StartsWith("switch(", StringComparison.Ordinal)
        || trimmed.StartsWith("with ", StringComparison.Ordinal) || trimmed.StartsWith("with(", StringComparison.Ordinal);

    private static (string Category, string[] RevertedSlice, string Summary) ClassifyAndRevertSlice(
        string entryName, string[] bSlice, string[] mSlice, IReadOnlySet<string> throttleVars)
    {
        var bNorm = bSlice.Select(l => l.Trim()).Where(l => !IsStructuralToken(l)).ToHashSet(StringComparer.Ordinal);
        var bNormState12 = bSlice.Select(l => l.Trim())
            .Where(l => l.StartsWith("if (", StringComparison.Ordinal) && l.EndsWith(')'))
            .Select(l => l[..^1] + " && state != 12)")
            .ToHashSet(StringComparer.Ordinal);
        var mNorm = mSlice.Select(l => l.Trim()).Where(l => !IsStructuralToken(l)).ToHashSet(StringComparer.Ordinal);

        string[] effRem = bSlice.Select(l => l.Trim())
            .Where(l => !IsStructuralToken(l) && !mNorm.Contains(l) && !(l.StartsWith("if (", StringComparison.Ordinal) && l.EndsWith(')') && mNorm.Contains(l[..^1] + " && state != 12)")))
            .ToArray();
        string[] effAdd = mSlice.Select(l => l.Trim())
            .Where(l => !IsStructuralToken(l) && !bNorm.Contains(l) && !bNormState12.Contains(l))
            .ToArray();
        string[] allEff = [.. effRem, .. effAdd];

        if (allEff.Length == 0)
            return (Content, mSlice, "formatting only");

        // 1. Check debug-overlay: draw_text* of FPS/coordinates (+ optional draw_set_*/draw_x/draw_y setup).
        if (allEff.Any(l => DebugDrawTextRegex().IsMatch(l)))
        {
            if (effRem.Length == 0 && allEff.All(l => DebugDrawTextRegex().IsMatch(l) || DrawSetupLineRegex().IsMatch(l)))
                return (DebugOverlay, bSlice, Summarize(effRem, effAdd, "debug FPS/coordinate overlay"));
            return (Unclassified, mSlice, Summarize(effRem, effAdd, "mixed debug overlay and other statements"));
        }

        // 2. Check timing: writes to delta/delta_fixed/time_scalar/DELTA_INSTANT/room_speed, game_set_speed, or get_timer frame limiter.
        if (allEff.Any(IsTimingLine))
        {
            if (allEff.All(IsTimingLine))
                return (Timing, bSlice, Summarize(effRem, effAdd, "timing/delta modification"));
            return (Unclassified, mSlice, Summarize(effRem, effAdd, "mixed timing and other statements"));
        }

        // 3. Check fps-adaptive: reads of fps/fps_real for behaviour / cull_trigger.
        if (allEff.Any(l => FpsReadRegex().IsMatch(l)))
        {
            if (allEff.All(l => FpsAdaptiveLineRegex().IsMatch(l) || FpsReadRegex().IsMatch(l) && l.Contains("cull_trigger", StringComparison.Ordinal)))
                return (FpsAdaptive, bSlice, Summarize(effRem, effAdd, "fps-adaptive culling"));
            return (Unclassified, mSlice, Summarize(effRem, effAdd, "ambiguous fps read"));
        }

        // 4. Check tick-throttle and distance-cull.
        bool hasThrottle = allEff.Any(l => IsThrottleLine(l, throttleVars));
        bool hasDistanceCull = allEff.Any(l => IsDistanceCullCondition(entryName, l));

        if (hasThrottle || hasDistanceCull)
        {
            string primaryCategory = hasThrottle ? TickThrottle : DistanceCull;
            if (effRem.All(l => IsThrottleOrCullLine(entryName, l, throttleVars))
                && effAdd.All(l => IsThrottleOrCullLine(entryName, l, throttleVars)))
            {
                return (primaryCategory, bSlice, Summarize(effRem, effAdd, primaryCategory));
            }

            // Check if mSlice is an outer tick-throttle or distance-cull wrapper around both bSlice AND kept content lines.
            if (TryUnwrapGatePreservingContent(entryName, bSlice, mSlice, throttleVars, out var unwrapped, out string unwrapSummary))
            {
                return (primaryCategory, unwrapped, unwrapSummary);
            }

            return (Unclassified, mSlice, Summarize(effRem, effAdd, $"mixed {primaryCategory} and other statements"));
        }

        // 5. Check for any remaining suspicious performance primitives not matched by safe rules.
        if (allEff.Any(l => SuspiciousPerfTokenRegex().IsMatch(l)))
        {
            return (Unclassified, mSlice, Summarize(effRem, effAdd, "unclassified performance primitive"));
        }

        return (Content, mSlice, Summarize(effRem, effAdd, "content"));
    }

    private static bool TryUnwrapGatePreservingContent(
        string entryName, string[] bSlice, string[] mSlice, IReadOnlySet<string> throttleVars,
        out string[] unwrapped, out string summary)
    {
        unwrapped = [];
        summary = "";
        // Detect pattern: [counter++;] if (gate) { <inner> } [else { <cull-only> }]
        int idx = 0;
        List<string> removedGateLines = [];
        if (idx < mSlice.Length && CounterIncrementRegex().Match(mSlice[idx].Trim()) is { Success: true } inc
            && throttleVars.Contains(inc.Groups[1].Value))
        {
            removedGateLines.Add(mSlice[idx].Trim());
            idx++;
        }
        if (idx >= mSlice.Length) return false;
        string header = mSlice[idx].Trim();
        bool validHeader = (CounterGateRegex().Match(header) is { Success: true } gate
                && GmlReferences.Identifiers(gate.Groups[1].Success ? gate.Groups[1].Value : gate.Groups[3].Value).Any(throttleVars.Contains))
            || IsDistanceCullCondition(entryName, header);
        if (!validHeader) return false;
        removedGateLines.Add(header);
        idx++;
        if (idx >= mSlice.Length || mSlice[idx].Trim() != "{") return false;
        int bodyStart = idx + 1;
        int depth = 1;
        int bodyEnd = bodyStart;
        while (bodyEnd < mSlice.Length && depth > 0)
        {
            depth += BraceDelta([mSlice[bodyEnd]]);
            if (depth == 0) break;
            bodyEnd++;
        }
        if (depth != 0 || bodyEnd >= mSlice.Length) return false;

        // Check any trailing else block after bodyEnd: must be empty or pure cull lines (visible = false; exit;).
        int afterBody = bodyEnd + 1;
        if (afterBody < mSlice.Length)
        {
            var trailing = mSlice[afterBody..].Select(l => l.Trim()).Where(l => !IsStructuralToken(l)).ToArray();
            if (!trailing.All(l => IsThrottleOrCullLine(entryName, l, throttleVars)))
                return false;
        }

        // Filter out counter resets inside the body and un-indent by 4 spaces.
        List<string> inner = [];
        foreach (string line in mSlice[bodyStart..bodyEnd])
        {
            string t = line.Trim();
            if (CounterResetRegex().Match(t) is { Success: true } rst && throttleVars.Contains(rst.Groups[1].Value))
            {
                removedGateLines.Add(t);
                continue;
            }
            inner.Add(line.StartsWith("    ", StringComparison.Ordinal) ? line[4..] : line);
        }

        // Verify that non-base lines remaining inside inner are pure content (not unclassified/suspicious).
        var bSet = bSlice.Select(l => l.Trim()).Where(l => !IsStructuralToken(l)).ToHashSet(StringComparer.Ordinal);
        var remainingAdded = inner.Select(l => l.Trim()).Where(l => !IsStructuralToken(l) && !bSet.Contains(l)).ToArray();
        if (remainingAdded.Length == 0
            || remainingAdded.Any(l => IsTimingLine(l) || IsThrottleOrCullLine(entryName, l, throttleVars) || SuspiciousPerfTokenRegex().IsMatch(l)))
            return false;

        unwrapped = inner.ToArray();
        summary = string.Join(" ", removedGateLines);
        return true;
    }

    private static bool IsStructuralToken(string trimmed) => trimmed is "" or "{" or "}" or "else";

    private static bool IsTimingLine(string trimmed) =>
        TimingWriteRegex().IsMatch(trimmed)
        || TimingFrameLimiterRegex().IsMatch(trimmed)
        || TimingLimiterLineRegex().IsMatch(trimmed);

    private static bool IsThrottleLine(string trimmed, IReadOnlySet<string> throttleVars)
    {
        if (CounterIncrementRegex().Match(trimmed) is { Success: true } inc && throttleVars.Contains(inc.Groups[1].Value))
            return true;
        if (CounterResetRegex().Match(trimmed) is { Success: true } rst && throttleVars.Contains(rst.Groups[1].Value))
            return true;
        if (CounterGateRegex().Match(trimmed) is { Success: true } gate)
        {
            string expr = gate.Groups[1].Success ? gate.Groups[1].Value : gate.Groups[3].Value;
            if (gate.Groups[1].Success || GmlReferences.Identifiers(expr).Any(throttleVars.Contains))
                return true;
        }
        return false;
    }

    private static bool IsDistanceCullCondition(string entryName, string trimmed) =>
        DistanceCameraRegex().IsMatch(trimmed)
        || entryName.Contains("FishThinner", StringComparison.Ordinal) && DoZoningRegex().IsMatch(trimmed);

    private static bool IsThrottleOrCullLine(string entryName, string trimmed, IReadOnlySet<string> throttleVars) =>
        IsThrottleLine(trimmed, throttleVars)
        || IsDistanceCullCondition(entryName, trimmed)
        || trimmed is "visible = false;" or "visible = true;" or "visible = 0;" or "visible = 1;"
            or "exit;" or "break;" or "state = 12;" or "state = 0;"
            or "if (state == 12 || state == 6)" or "if (instance_exists(Girl))";

    private static string Summarize(string[] effRem, string[] effAdd, string fallback)
    {
        string[] pick = effAdd.Length > 0 ? effAdd : effRem;
        if (pick.Length == 0) return fallback;
        string joined = effRem.Length > 0 && effAdd.Length > 0 && effRem.Length <= 2 && effAdd.Length <= 2
            ? $"{string.Join(" ", effRem)} -> {string.Join(" ", effAdd)}"
            : string.Join(" ", pick.Take(3));
        return joined.Length > 120 ? joined[..117] + "..." : joined;
    }

    private static int BraceDelta(IEnumerable<string> lines)
    {
        int delta = 0;
        foreach (string line in lines)
            foreach (char c in line)
            {
                if (c == '{') delta++;
                else if (c == '}') delta--;
            }
        return delta;
    }

    private static string[] SplitLines(string normalized)
    {
        if (normalized.Length == 0) return [];
        var parts = normalized.Split('\n');
        return parts[^1].Length == 0 ? parts[..^1] : parts;
    }

    private static string JoinLines(IReadOnlyList<string> lines) =>
        lines.Count == 0 ? "" : string.Join('\n', lines) + "\n";

    private static string ReconstructText(IReadOnlyList<string> lines, string referenceText)
    {
        if (lines.Count == 0) return "";
        bool trailingNewline = LineDiff.Normalize(referenceText).EndsWith('\n');
        return string.Join('\n', lines) + (trailingNewline ? "\n" : "");
    }

    private static HashSet<string> ExtractObjectEventCodes(JsonElement objSnapshot)
    {
        HashSet<string> codes = new(StringComparer.Ordinal);
        if (objSnapshot.ValueKind == JsonValueKind.Object && objSnapshot.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Object)
        {
            foreach (var ev in events.EnumerateObject())
                if (ev.Value.ValueKind == JsonValueKind.Object && ev.Value.TryGetProperty("code", out var codeArr) && codeArr.ValueKind == JsonValueKind.Array)
                    foreach (var item in codeArr.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                            codes.Add(s);
        }
        return codes;
    }

    private static HashSet<string> CollectKeptContentIdentifiers(
        IEnumerable<AssetPatch> addedCode, IEnumerable<AssetPatch> changedCode, IEnumerable<AssetPatch> addedObjects)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var entry in addedCode.Concat(changedCode))
            if (entry.ModCode is { Format: "gml" } code)
                foreach (string id in GmlReferences.Identifiers(code.Text))
                    ids.Add(id);
        foreach (var obj in addedObjects)
            foreach (var r in GmlReferences.Structured("objects", obj.Name, obj.Mod))
                ids.Add(r.Target);
        return ids;
    }

    private static bool FieldReferencesRemovedCode(FieldChange field, IReadOnlySet<string> removedCode)
    {
        if (!field.Path.StartsWith("/events/", StringComparison.Ordinal) || field.Mod is not { ValueKind: JsonValueKind.Object } modEv)
            return false;
        if (!modEv.TryGetProperty("code", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;
        var names = arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
        return names.Length > 0 && names.All(removedCode.Contains);
    }

    private static bool FieldReferencesRemovedObjectInstance(FieldChange field, IReadOnlySet<string> removedObjects)
    {
        if (!field.Path.StartsWith("/instances/", StringComparison.Ordinal) || removedObjects.Count == 0)
            return false;
        foreach (string obj in removedObjects)
            if (field.Path.StartsWith($"/instances/[\"{obj}\",", StringComparison.Ordinal))
                return true;
        return false;
    }

    private static string? TryParseVariableName(string rawKey)
    {
        if (!rawKey.StartsWith('[')) return rawKey;
        try
        {
            var parts = JsonSerializer.Deserialize<string[]>(rawKey);
            return parts is { Length: > 0 } ? parts[0] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRemovedReference(AssetReference reference, IReadOnlySet<string> removedObjects, IReadOnlySet<string> removedCode) =>
        reference.Family == "objects" && removedObjects.Contains(reference.Asset)
        || reference.TargetFamily == "objects" && removedObjects.Contains(reference.Target)
        || reference.Family == "code" && removedCode.Contains(reference.Asset)
        || reference.TargetFamily == "code" && removedCode.Contains(reference.Target);
}
