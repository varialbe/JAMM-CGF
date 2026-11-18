using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jamm.Core.Building;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;
using UndertaleModLib;

namespace Jamm.Core.Patching;

/// <summary>Vanilla sonar unlock and visibility condition derived from the base archive.</summary>
public sealed record VanillaSonarCondition(int SonarToolSlot, int DeepSonarToolSlot, string UnlockExpression)
{
    /// <summary>Vanilla legacy CGF condition when the base scripts are not present in a standalone patch.</summary>
    public static readonly VanillaSonarCondition Default = new(1, 2, "ToolData.unlocked[1] == 1");

    /// <summary>Unlock condition for Deep Sonar (<c>SonarPanelAdvanced</c>) derived from <see cref="DeepSonarToolSlot"/>.</summary>
    public string DeepUnlockExpression => $"ToolData.unlocked[{DeepSonarToolSlot}] == 1";

    private static readonly Regex SonarSlotRegex = new(
        @"\bitem\[(\d+)\]\s*=\s*SonarPanel\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex DeepSonarSlotRegex = new(
        @"\bitem\[(\d+)\]\s*=\s*SonarPanelAdvanced\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex RadarUnlockSlotRegex = new(
        @"\bToolData\.unlocked\[(\d+)\]\s*==\s*1\b",
        RegexOptions.CultureInvariant);

    /// <summary>Derives the vanilla sonar condition from decompiled baseline code entries.</summary>
    public static VanillaSonarCondition Derive(IReadOnlyDictionary<string, CodeText> baseline)
    {
        baseline.TryGetValue("gml_Object_ToolData_Create_0", out var toolData);
        baseline.TryGetValue("gml_Object_RadarButton_Step_0", out var radarStep);
        return DeriveFromGml(toolData?.Text, radarStep?.Text);
    }

    /// <summary>Derives the vanilla sonar condition by reading only the sonar and tool scripts from an open base archive.</summary>
    public static VanillaSonarCondition FromData(UndertaleData data)
    {
        var decompiler = new CodeDecompiler(data, "base", _ => { });
        string? DecompileIfPresent(string name)
        {
            var entry = data.Code.FirstOrDefault(c => c.Name?.Content == name && c.ParentEntry is null);
            return entry is null ? null : decompiler.Read(entry).Text;
        }

        return DeriveFromGml(
            DecompileIfPresent("gml_Object_ToolData_Create_0"),
            DecompileIfPresent("gml_Object_RadarButton_Step_0"));
    }

    /// <summary>Derives the vanilla sonar condition from base snapshots inside <paramref name="patch"/> when available.</summary>
    public static VanillaSonarCondition FromPatch(ModPatch patch)
    {
        if (!patch.Changes.TryGetValue("code", out var code)) return Default;
        string? toolData = code.Changed.FirstOrDefault(c => c.Name == "gml_Object_ToolData_Create_0")?.BaseCode?.Text;
        string? radarStep = code.Changed.FirstOrDefault(c => c.Name == "gml_Object_RadarButton_Step_0")?.BaseCode?.Text;
        return DeriveFromGml(toolData, radarStep);
    }

    private static VanillaSonarCondition DeriveFromGml(string? toolDataCreateGml, string? radarButtonStepGml)
    {
        int sonarSlot = 1;
        int deepSlot = 2;
        bool hasToolSonar = false;

        if (!string.IsNullOrEmpty(toolDataCreateGml))
        {
            var mSonar = SonarSlotRegex.Match(toolDataCreateGml);
            if (mSonar.Success && int.TryParse(mSonar.Groups[1].Value, out int parsedSonar))
            {
                sonarSlot = parsedSonar;
                hasToolSonar = true;
            }

            var mDeep = DeepSonarSlotRegex.Match(toolDataCreateGml);
            if (mDeep.Success && int.TryParse(mDeep.Groups[1].Value, out int parsedDeep))
                deepSlot = parsedDeep;
        }

        if (!string.IsNullOrEmpty(radarButtonStepGml))
        {
            var mUnlock = RadarUnlockSlotRegex.Match(radarButtonStepGml);
            if (mUnlock.Success && int.TryParse(mUnlock.Groups[1].Value, out int radarSlot) && (!hasToolSonar || radarSlot == sonarSlot))
                sonarSlot = radarSlot;
        }

        return new VanillaSonarCondition(sonarSlot, deepSlot, $"ToolData.unlocked[{sonarSlot}] == 1");
    }
}

public static partial class ModSanitizer
{
    /// <summary>Category for third-party gameplay changes that force sonar visibility or bypass progression unlocks.</summary>
    public const string GameplaySonar = "gameplay-sonar";

    private static readonly Regex SonarActiveAssignRegex = new(
        @"\b(?<target>SonarPanel(?:Advanced)?)\.active\s*=(?!=)\s*(?<rhs>[^;\r\n]+?)(?:\s*;|\s*(?=\r?$))",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex SonarDrawAssignRegex = new(
        @"\bSonarPanel\.(?:shouldDraw|drawOverride)\s*=(?!=)\s*(?<rhs>[^;\r\n]+?)(?:\s*;|\s*(?=\r?$))",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex SonarViewVisibleAssignRegex = new(
        @"\bview_visible\s*\[\s*3\s*\]\s*=(?!=)\s*(?<rhs>[^;\r\n]+?)(?:\s*;|\s*(?=\r?$))",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex RadarVisibleAssignRegex = new(
        @"\bRadarButton\.visible\s*=(?!=)\s*(?<rhs>[^;\r\n]+?)(?:\s*;|\s*(?=\r?$))",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex ToolUnlockedQualifiedAssignRegex = new(
        @"\bToolData\.unlocked\s*\[\s*(?<slot>\d+)\s*\]\s*=(?!=)\s*(?<rhs>[^;\r\n]+?)(?:\s*;|\s*(?=\r?$))",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex ToolUnlockedBareAssignRegex = new(
        @"(?<!\.)\bunlocked\s*\[\s*(?<slot>\d+)\s*\]\s*=(?!=)\s*(?<rhs>[^;\r\n]+?)(?:\s*;|\s*(?=\r?$))",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex DirectRadarLocalVisibleRegex = new(
        @"^\s*visible\s*=(?!=)\s*(?<rhs>[^;\r\n]+?)\s*;?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex DirectSonarLocalAssignRegex = new(
        @"^\s*(?:active|shouldDraw|drawOverride)\s*=(?!=)\s*(?<rhs>[^;\r\n]+?)\s*;?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static bool IsInsideComment(string text, int index)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        string prefix = text[lineStart..index];
        return prefix.Contains("//", StringComparison.Ordinal);
    }

    private static bool IsTruthyLiteral(string rhs)
    {
        string trimmed = rhs.Trim();
        if (trimmed is "0" or "false") return false;
        if (trimmed is "1" or "true") return true;
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double num))
            return num != 0.0;
        return false;
    }

    private static bool IsZeroOrFalse(string rhs)
    {
        string trimmed = rhs.Trim();
        return trimmed is "0" or "false";
    }

    private static bool IsUnlockGatePresent(string text, string unlockExpr) =>
        text.Contains(unlockExpr, StringComparison.Ordinal)
        || Regex.IsMatch(text, $@"\bToolData\.unlocked\[\s*{Regex.Escape(ExtractSlot(unlockExpr))}\s*\]\s*>=\s*1\b", RegexOptions.CultureInvariant);

    private static string ExtractSlot(string unlockExpr)
    {
        var m = Regex.Match(unlockExpr, @"\[(\d+)\]");
        return m.Success ? m.Groups[1].Value : "1";
    }

    private static bool IsVanillaSonarOrToolScript(string entryName) =>
        entryName.StartsWith("gml_Object_SonarPanel_", StringComparison.Ordinal)
        || entryName.StartsWith("gml_Object_SonarPanelAdvanced_", StringComparison.Ordinal)
        || entryName.StartsWith("gml_Object_RadarButton_", StringComparison.Ordinal)
        || entryName == "gml_Object_ToolData_Create_0"
        || entryName == "gml_Object_Girl_Step_0";

    private static (bool HasAny, bool HasSonarDisable, bool HasSonarTruthy, bool HasDeepDisable, bool HasDeepTruthy) ScanSonarActiveWrites(string text)
    {
        bool hasAny = false, sOff = false, sOn = false, dOff = false, dOn = false;
        foreach (Match m in SonarActiveAssignRegex.Matches(text))
        {
            if (IsInsideComment(text, m.Index)) continue;
            hasAny = true;
            bool isDeep = m.Groups["target"].Value == "SonarPanelAdvanced";
            bool zero = IsZeroOrFalse(m.Groups["rhs"].Value);
            if (isDeep) { if (zero) dOff = true; else dOn = true; }
            else { if (zero) sOff = true; else sOn = true; }
        }
        return (hasAny, sOff, sOn, dOff, dOn);
    }

    private static bool HasToolUnlockBypass(string text, VanillaSonarCondition sonar, bool allowBareUnlocked)
    {
        foreach (Match m in ToolUnlockedQualifiedAssignRegex.Matches(text))
        {
            if (!int.TryParse(m.Groups["slot"].Value, out int slot)) continue;
            string rhs = m.Groups["rhs"].Value.Trim();
            if (slot == sonar.SonarToolSlot && IsTruthyLiteral(rhs)) return true;
            if (slot == sonar.DeepSonarToolSlot && !rhs.StartsWith("-1", StringComparison.Ordinal) && IsTruthyLiteral(rhs)) return true;
        }
        if (allowBareUnlocked)
        {
            foreach (Match m in ToolUnlockedBareAssignRegex.Matches(text))
            {
                if (!int.TryParse(m.Groups["slot"].Value, out int slot)) continue;
                string rhs = m.Groups["rhs"].Value.Trim();
                if (slot == sonar.SonarToolSlot && IsTruthyLiteral(rhs)) return true;
                if (slot == sonar.DeepSonarToolSlot && !rhs.StartsWith("-1", StringComparison.Ordinal) && IsTruthyLiteral(rhs)) return true;
            }
        }
        return false;
    }

    private static string RewriteAddedOrMixedSonarGml(
        string text,
        string entryName,
        VanillaSonarCondition sonar,
        out List<string> descriptions)
    {
        List<string> notes = [];
        string updated = text;
        var activeScan = ScanSonarActiveWrites(updated);
        bool jamPairSonar = activeScan.HasSonarDisable && activeScan.HasSonarTruthy;
        bool jamPairDeep = activeScan.HasDeepDisable && activeScan.HasDeepTruthy;

        // 1. Neutralize forced SonarPanel.shouldDraw / drawOverride truthy writes (vanilla SonarPanel_Step_0 manages shouldDraw).
        updated = SonarDrawAssignRegex.Replace(updated, m =>
        {
            if (IsInsideComment(updated, m.Index) || IsZeroOrFalse(m.Groups["rhs"].Value)) return m.Value;
            notes.Add("removed forced SonarPanel visibility draw override");
            return "{}";
        });

        // 2. Neutralize direct view_visible[3] = 1 / RadarButton.visible = 1 outside vanilla objects.
        updated = SonarViewVisibleAssignRegex.Replace(updated, m =>
        {
            if (IsInsideComment(updated, m.Index) || IsZeroOrFalse(m.Groups["rhs"].Value)) return m.Value;
            notes.Add("removed forced sonar view_visible[3] assignment");
            return "{}";
        });
        updated = RadarVisibleAssignRegex.Replace(updated, m =>
        {
            if (IsInsideComment(updated, m.Index) || IsZeroOrFalse(m.Groups["rhs"].Value)) return m.Value;
            notes.Add("removed forced RadarButton.visible assignment");
            return "{}";
        });

        // 3. Neutralize literal sonar slot unlocks (ToolData.unlocked[1/2] = 1).
        updated = ToolUnlockedQualifiedAssignRegex.Replace(updated, m =>
        {
            if (IsInsideComment(updated, m.Index)) return m.Value;
            if (!int.TryParse(m.Groups["slot"].Value, out int slot)) return m.Value;
            if ((slot == sonar.SonarToolSlot || slot == sonar.DeepSonarToolSlot) && IsTruthyLiteral(m.Groups["rhs"].Value))
            {
                notes.Add($"removed early ToolData.unlocked[{slot}] unlock");
                return "{}";
            }
            return m.Value;
        });

        // 4. Handle SonarPanel.active / SonarPanelAdvanced.active writes.
        updated = SonarActiveAssignRegex.Replace(updated, m =>
        {
            if (IsInsideComment(updated, m.Index)) return m.Value;
            string target = m.Groups["target"].Value;
            string rhs = m.Groups["rhs"].Value;
            bool isDeep = target == "SonarPanelAdvanced";
            string unlockExpr = isDeep ? sonar.DeepUnlockExpression : sonar.UnlockExpression;
            string jamVar = isDeep ? "jamm_deep_sonar_jammed" : "jamm_sonar_jammed";
            bool hasJamPair = isDeep ? jamPairDeep : jamPairSonar;

            if (IsZeroOrFalse(rhs))
            {
                if (!hasJamPair) return m.Value;
                return $"{{ if (instance_exists(ToolData) && {unlockExpr} && instance_exists({target}) && {target}.active) {{ {target}.{jamVar} = 1; }} {target}.active = {rhs.Trim()}; }}";
            }

            if (hasJamPair)
            {
                notes.Add($"guarded {target}.active restoration behind {unlockExpr} and prior jammed state");
                return $"{{ if (instance_exists(ToolData) && {unlockExpr} && instance_exists({target}) && variable_instance_exists({target}.id, \"{jamVar}\") && {target}.{jamVar}) {{ {target}.active = {rhs.Trim()}; {target}.{jamVar} = 0; }} }}";
            }

            // Force-enable without a jam/disable pair: keep if explicitly gated on the unlock condition, otherwise drop.
            if (IsUnlockGatePresent(text, unlockExpr))
                return m.Value;

            notes.Add($"removed unguarded {target}.active bypass before {unlockExpr}");
            return "{}";
        });

        descriptions = notes.Distinct(StringComparer.Ordinal).ToList();
        return updated;
    }

    private static List<AssetPatch> SanitizeAddedSonarCode(
        IReadOnlyList<AssetPatch> addedCode,
        VanillaSonarCondition sonar,
        List<SanitizedChange> removed)
    {
        List<AssetPatch> result = new(addedCode.Count);
        foreach (var entry in addedCode)
        {
            if (entry.ModCode is not { Format: "gml" } modCode)
            {
                result.Add(entry);
                continue;
            }

            string originalText = modCode.Text;
            string sanitizedText = RewriteAddedOrMixedSonarGml(originalText, entry.Name, sonar, out var notes);
            if (notes.Count == 0 || sanitizedText == originalText)
            {
                result.Add(entry);
                continue;
            }

            removed.Add(new(GameplaySonar, $"code/{entry.Name}", string.Join("; ", notes)));
            result.Add(entry with { ModCode = modCode with { Text = sanitizedText } });
        }
        return result;
    }

    private static List<AssetPatch> SanitizeChangedSonarCode(
        IReadOnlyList<AssetPatch> changedCode,
        VanillaSonarCondition sonar,
        List<SanitizedChange> removed)
    {
        List<AssetPatch> result = [];
        foreach (var entry in changedCode)
        {
            if (entry.BaseCode is not { Format: "gml" } baseCode || entry.ModCode is not { Format: "gml" } modCode)
            {
                result.Add(entry);
                continue;
            }

            string normalizedBase = LineDiff.Normalize(baseCode.Text);
            string normalizedMod = LineDiff.Normalize(modCode.Text);
            if (normalizedBase == normalizedMod)
            {
                result.Add(entry);
                continue;
            }

            string[] baseLines = SplitLines(normalizedBase);
            string[] modLines = SplitLines(normalizedMod);
            var hunks = ExtractHunkSpans(baseLines, modLines, entry.Name, new HashSet<string>(StringComparer.Ordinal));

            bool modified = false;
            List<string> rebuilt = [];
            int baseCursor = 0;
            bool isSonarScript = IsVanillaSonarOrToolScript(entry.Name);

            foreach (var (b0, b1, m0, m1) in hunks)
            {
                rebuilt.AddRange(baseLines[baseCursor..b0]);
                string bText = JoinLines(baseLines[b0..b1]);
                string mText = JoinLines(modLines[m0..m1]);

                if (HunkBypassesSonar(entry.Name, bText, mText, sonar, out string reason, out string? rewrittenHunk))
                {
                    modified = true;
                    removed.Add(new(GameplaySonar, $"code/{entry.Name}", reason, b0 + 1, m0 + 1));
                    string replacement = rewrittenHunk ?? (isSonarScript || b1 > b0 ? bText : "");
                    if (replacement.Length > 0)
                        rebuilt.AddRange(SplitLines(replacement));
                }
                else
                {
                    rebuilt.AddRange(modLines[m0..m1]);
                }

                baseCursor = b1;
            }
            rebuilt.AddRange(baseLines[baseCursor..]);

            if (!modified)
            {
                result.Add(entry);
                continue;
            }

            string finalText = ReconstructText(rebuilt, modCode.Text);
            if (LineDiff.Normalize(finalText) == normalizedBase
                && entry.Base is { }
                && baseCode.Parent == modCode.Parent
                && baseCode.Children.SequenceEqual(modCode.Children, StringComparer.Ordinal))
                continue;

            result.Add(entry with { ModCode = modCode with { Text = finalText } });
        }
        return result;
    }

    private static bool HunkBypassesSonar(
        string entryName,
        string baseText,
        string modText,
        VanillaSonarCondition sonar,
        out string reason,
        out string? rewrittenHunk)
    {
        reason = "";
        rewrittenHunk = null;

        bool isToolDataCreate = entryName == "gml_Object_ToolData_Create_0";
        bool isRadar = entryName.StartsWith("gml_Object_RadarButton_", StringComparison.Ordinal);
        bool isSonarPanel = entryName.StartsWith("gml_Object_SonarPanel", StringComparison.Ordinal);

        // 1. Check if vanilla unlock gate was weakened or removed on RadarButton / SonarPanel / Girl.
        if ((isRadar || isSonarPanel || entryName == "gml_Object_Girl_Step_0")
            && baseText.Contains(sonar.UnlockExpression, StringComparison.Ordinal)
            && !modText.Contains(sonar.UnlockExpression, StringComparison.Ordinal))
        {
            reason = $"restored vanilla unlock gate ({sonar.UnlockExpression}) in {entryName}";
            return true;
        }

        // 2. Check ToolData_Create_0 or any script unlocking the sonar/deep-sonar slot early.
        if (HasToolUnlockBypass(modText, sonar, allowBareUnlocked: isToolDataCreate)
            && !HasToolUnlockBypass(baseText, sonar, allowBareUnlocked: isToolDataCreate))
        {
            reason = $"reverted early sonar unlock in {entryName}";
            if (isToolDataCreate || baseText.Length > 0) return true;
        }

        // 3. Check direct local assignments on RadarButton / SonarPanel / SonarPanelAdvanced.
        if (isRadar)
        {
            foreach (Match m in DirectRadarLocalVisibleRegex.Matches(modText))
            {
                if (IsTruthyLiteral(m.Groups["rhs"].Value) && !DirectRadarLocalVisibleRegex.IsMatch(baseText))
                {
                    reason = $"reverted forced RadarButton visibility in {entryName}";
                    return true;
                }
            }
        }
        if (isSonarPanel)
        {
            foreach (Match m in DirectSonarLocalAssignRegex.Matches(modText))
            {
                if (IsTruthyLiteral(m.Groups["rhs"].Value) && !baseText.Contains(m.Value.Trim(), StringComparison.Ordinal))
                {
                    reason = $"reverted forced sonar panel activation in {entryName}";
                    return true;
                }
            }
            if (SonarViewVisibleAssignRegex.IsMatch(modText) && !SonarViewVisibleAssignRegex.IsMatch(baseText))
            {
                reason = $"reverted forced view_visible[3] in {entryName}";
                return true;
            }
        }

        // 4. For any other changed script hunk, sanitize SonarPanel / RadarButton / view_visible[3] / ToolData.unlocked writes
        //    while keeping unrelated mod gameplay changes in the same hunk intact.
        string sanitizedMod = RewriteAddedOrMixedSonarGml(modText, entryName, sonar, out var notes);
        if (notes.Count > 0 && sanitizedMod != modText)
        {
            // Only flag if the base text did not already have the exact same sonar write.
            string sanitizedBase = RewriteAddedOrMixedSonarGml(baseText, entryName, sonar, out var baseNotes);
            if (notes.Except(baseNotes, StringComparer.Ordinal).Any() || sanitizedBase == baseText)
            {
                reason = string.Join("; ", notes);
                if (!IsVanillaSonarOrToolScript(entryName))
                    rewrittenHunk = sanitizedMod;
                return true;
            }
        }

        return false;
    }

    private static List<AssetPatch> SanitizeChangedSonarObjects(
        IReadOnlyList<AssetPatch> changedObjects,
        List<SanitizedChange> removed)
    {
        List<AssetPatch> result = [];
        foreach (var obj in changedObjects)
        {
            if (obj.Name is not ("RadarButton" or "SonarPanelAdvanced"))
            {
                result.Add(obj);
                continue;
            }

            List<FieldChange> keptFields = [];
            bool modified = false;
            foreach (var field in obj.Fields)
            {
                if (field.Path == "/visible"
                    && field.Base is { ValueKind: JsonValueKind.False }
                    && field.Mod is { ValueKind: JsonValueKind.True })
                {
                    removed.Add(new(GameplaySonar, $"objects/{obj.Name}", $"reverted {obj.Name}.visible true -> false to match base"));
                    modified = true;
                    continue;
                }
                keptFields.Add(field);
            }

            if (!modified)
            {
                result.Add(obj);
                continue;
            }
            if (obj.Base is not { } baseObj || keptFields.Count == 0)
                continue;
            var updatedMod = FieldMergeEngine.Apply(baseObj, keptFields, obj.Mod);
            var recomputed = FieldDiff.Compare(baseObj, updatedMod);
            if (recomputed.Count > 0 && !JsonElement.DeepEquals(baseObj, updatedMod))
                result.Add(obj with { Mod = updatedMod, Fields = recomputed });
        }
        return result;
    }

    private static List<AssetPatch> SanitizeChangedSonarRooms(
        IReadOnlyList<AssetPatch> changedRooms,
        List<SanitizedChange> removed)
    {
        List<AssetPatch> result = [];
        foreach (var room in changedRooms)
        {
            List<FieldChange> keptFields = [];
            bool modified = false;
            foreach (var field in room.Fields)
            {
                if (field.Path == "/views/3/enabled"
                    && field.Base is { ValueKind: JsonValueKind.False }
                    && field.Mod is { ValueKind: JsonValueKind.True })
                {
                    removed.Add(new(GameplaySonar, $"rooms/{room.Name}", "reverted sonar viewport (views[3].enabled) to base"));
                    modified = true;
                    continue;
                }
                keptFields.Add(field);
            }

            if (!modified)
            {
                result.Add(room);
                continue;
            }
            if (room.Base is not { } baseRoom || keptFields.Count == 0)
                continue;
            var updatedMod = FieldMergeEngine.Apply(baseRoom, keptFields, room.Mod);
            var recomputed = FieldDiff.Compare(baseRoom, updatedMod);
            if (recomputed.Count > 0 && !JsonElement.DeepEquals(baseRoom, updatedMod))
                result.Add(room with { Mod = updatedMod, Fields = recomputed });
        }
        return result;
    }
}
