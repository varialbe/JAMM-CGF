using System.Globalization;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>
/// Rewrites hardcoded upgrade_scalar loops in Girl and Gear to dynamic UpgradeData.unlocks bounds
/// when the merged UpgradeData table grows past 18 entries in non-Items-API modded builds.
/// </summary>
internal static class UpgradeLoopBounds
{
    private const int VanillaUpgradeCount = 18;
    private const string UpgradeDataCreate = "gml_Object_UpgradeData_Create_0";
    private const string GirlGearRefresh = "gml_Object_Girl_Other_10";
    private const string GearStats = "gml_Object_Gear_Other_15";

    private static readonly HashSet<string> UpgradeLengthFields = new(StringComparer.Ordinal)
    {
        "name", "desc", "desc2", "cost", "unlocks", "disp"
    };

    internal static BuildPlan PreparePlan(BuildPlan plan, IReadOnlyDictionary<string, CodeText> baseline)
    {
        string? upgradeCreate = Current(plan.Assets, baseline, UpgradeDataCreate);
        if (string.IsNullOrWhiteSpace(upgradeCreate)) return plan;

        int entryCount = CountUpgradeEntries(upgradeCreate);
        if (entryCount <= VanillaUpgradeCount) return plan;

        List<PlannedAsset>? assets = null;
        List<string> rewritten = [];

        if (Current(plan.Assets, baseline, GirlGearRefresh) is { } girlText)
        {
            string updated = ItemsRuntimePatches.PatchGirlUpgradeScalarLoops(girlText, requireAnchors: false);
            if (updated != girlText)
            {
                assets ??= [.. plan.Assets];
                Put(assets, baseline, GirlGearRefresh, updated);
                rewritten.Add(GirlGearRefresh);
            }
        }

        if (Current(plan.Assets, baseline, GearStats) is { } gearText)
        {
            string updated = ItemsRuntimePatches.PatchGearUpgradeScalarLoop(gearText, requireAnchors: false);
            if (updated != gearText)
            {
                assets ??= [.. plan.Assets];
                Put(assets, baseline, GearStats, updated);
                rewritten.Add(GearStats);
            }
        }

        if (assets is null) return plan;

        string detail = rewritten.Contains(GirlGearRefresh, StringComparer.Ordinal)
            ? "array_length_1d(UpgradeData.unlocks) with array_length_1d(gear.upgrade_scalar) bounds check"
            : "array_length_1d(UpgradeData.unlocks)";
        return plan with
        {
            Assets = assets,
            Decisions =
            [
                .. plan.Decisions,
                $"[CGF-UPGRADE-LOOP-001] Rewrote hardcoded upgrade_scalar loops for {entryCount}-entry UpgradeData to {detail} in {string.Join(", ", rewritten)}."
            ]
        };
    }

    internal static int CountUpgradeEntries(string gml)
    {
        int maxCount = 0;
        foreach (var write in GmlReferences.IndexWrites(gml))
        {
            if (write.Kind != "array" || !int.TryParse(write.Index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) || idx < 0)
                continue;
            string field = write.Container.StartsWith("UpgradeData.", StringComparison.Ordinal)
                ? write.Container["UpgradeData.".Length..]
                : write.Container;
            if (UpgradeLengthFields.Contains(field))
                maxCount = Math.Max(maxCount, idx + 1);
        }

        var tokens = GmlReferences.Tokens(gml);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == 'i' && tokens[i].Value == "for"
                && LegacyUpgradeRelocation.TryMatchCountBoundLoop(tokens, i, out string? loopVar, out int boundTokenIdx, out bool inclusive, out int bodyEnd)
                && int.TryParse(tokens[boundTokenIdx].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int bound))
            {
                bool touchesUpgradeArray = false;
                for (int j = boundTokenIdx + 1; j <= bodyEnd && j < tokens.Count; j++)
                {
                    if (tokens[j].Kind == 'i' && UpgradeLengthFields.Contains(tokens[j].Value)
                        && j + 3 < tokens.Count && tokens[j + 1].Value == "[" && tokens[j + 2].Value == loopVar && tokens[j + 3].Value == "]")
                    {
                        touchesUpgradeArray = true;
                        break;
                    }
                }
                if (touchesUpgradeArray)
                    maxCount = Math.Max(maxCount, inclusive ? bound + 1 : bound);
            }
            else if (i + 3 < tokens.Count
                && tokens[i].Kind == 'i' && tokens[i].Value == "nItems"
                && tokens[i + 1].Value == "="
                && tokens[i + 2].Kind == 'n'
                && tokens[i + 3].Value == ";"
                && int.TryParse(tokens[i + 2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int nItems))
            {
                maxCount = Math.Max(maxCount, nItems);
            }
        }
        return maxCount;
    }

    private static string? Current(IReadOnlyList<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name)
    {
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i].Family == "code" && assets[i].Name == name)
                return assets[i].Patch is null ? null : assets[i].Gml;
        }
        return baseline.TryGetValue(name, out var before) ? before.Text : null;
    }

    private static void Put(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name, string text)
    {
        int idx = assets.FindIndex(a => a.Family == "code" && a.Name == name);
        if (idx >= 0) { assets[idx] = assets[idx] with { Gml = text }; return; }
        var before = baseline.GetValueOrDefault(name);
        var after = before is null ? new CodeText(text, "gml", 0, 0, null, []) : before with { Text = text };
        var snapshot = AssetFields.Json(new { });
        assets.Add(new("code", name, 0, new AssetPatch(name, before is null ? null : snapshot, snapshot, [], before, after), snapshot, text));
    }
}
