using System.Globalization;
using System.Text.RegularExpressions;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>Makes selected legacy rows visible to the existing shop and save code.</summary>
internal static class LegacyShopRepair
{
    internal static void Apply(List<PlannedAsset> assets, List<string> decisions, IReadOnlyList<ModPatch> patches,
        IReadOnlyDictionary<string, CodeText>? baseline, IReadOnlySet<int> optedIn)
    {
        var findings = patches.Select((patch, i) => LegacyItemUnlocks.Analyze(patch)
            .Select(f => (Index: i, Finding: f))).SelectMany(f => f).ToArray();
        HashSet<(string Table, int Slot)> repaired = [];
        foreach (var (index, finding) in findings.Where(f => optedIn.Contains(f.Index) && f.Finding.Status == "missing"))
        {
            var (kind, table, create, item, _, _, _) = finding;
            string? text = Read(create);
            if (text is null || LegacySlotRelocation.UnlockItem(kind, text, item.Slot) != item)
                continue;
            // A competing mod can already supply an unlock path for the same merged item.
            if (findings.Any(f => f.Finding.Table == table && f.Finding.Item.Slot == item.Slot
                && f.Finding.Status != "missing")) continue;
            if (!repaired.Add((table, item.Slot))) continue;

            var state = LegacyItemUnlocks.ReadTable(text, table, create);
            string mod = patches[index].Mod.Name ?? Path.GetFileNameWithoutExtension(patches[index].Mod.FileName);
            string label = $"{mod}: {kind} '{item.DisplayName}' (slot {item.Slot})";
            if (!LegacyItemUnlocks.ShopAvailable(table, Read(LegacyItemUnlocks.ClickEntry(kind)),
                    Read("gml_Object_CardHandler_Alarm_0"))
                || state.Count is not int count
                || state.ComputedSlots.Contains(item.Slot)
                || !state.CountFromDefaults && item.Slot > count)
            {
                decisions.Add($"Legacy shop unlock repair skipped for {label}. The merged shop layout could not be verified.");
                continue;
            }

            double price = state.Prices.GetValueOrDefault(item.Slot, -1);
            double unlocked = state.Defaults.GetValueOrDefault(item.Slot, -1);
            if (item.Slot < count && (unlocked >= 1 || unlocked == 0 && price >= 0))
                continue;

            if (kind == "rod")
            {
                // Reuse relocation's matrix, save-loop and consumer edits for an exposed legacy rod.
                LegacyRodRelocation.CompleteCode(assets, patches, baseline, Math.Max(count, item.Slot + 1));
                text = Read(create)!;
            }
            string slot = item.Slot.ToString(CultureInfo.InvariantCulture);
            string additions = $"\nunlocked[{slot}] = 0;\n";
            if (price < 0)
            {
                price = SuggestedPrice(state.Prices, item.Slot);
                additions += $"cost[{slot}] = {price.ToString("0.######", CultureInfo.InvariantCulture)};\n";
            }
            if (state.CountFromDefaults)
                additions += "nItems = array_length_1d(unlocked);\n";
            else if (kind != "rod" && count <= item.Slot)
                additions += $"nItems = {(item.Slot + 1).ToString(CultureInfo.InvariantCulture)};\n";
            SetCode(create, text + additions);

            string? load = Read(LegacyItemUnlocks.SaveLoad);
            if (load is not null)
            {
                // The stock -1 fallback hides newly added rows on older saves. Preserve actual ownership.
                string marker = $@"unlocked\s*\[i\]\s*=\s*ini_read_real\(\s*""Unlocks""\s*,\s*""{kind}""\s*\+\s*string\(i\)\s*,\s*-1\s*\)\s*;";
                var match = Regex.Match(load, marker, RegexOptions.CultureInvariant);
                if (match.Success)
                    SetCode(LegacyItemUnlocks.SaveLoad, load.Insert(match.Index + match.Length,
                        $"\n            if (i == {slot} && unlocked[i] < 0) unlocked[i] = 0;"));
            }

            decisions.Add($"Legacy shop unlock repair made {label} buyable for {LegacyItemUnlocks.FormatPrice(price)}.");
        }

        string? Read(string name)
        {
            var asset = assets.FirstOrDefault(a => a.Family == "code" && a.Name == name);
            return asset is null ? baseline?.GetValueOrDefault(name)?.Text : asset.Gml ?? "";
        }

        void SetCode(string name, string text)
        {
            int index = assets.FindIndex(a => a.Family == "code" && a.Name == name);
            if (index >= 0)
            {
                assets[index] = assets[index] with { Gml = text };
                return;
            }
            var before = baseline![name];
            var snapshot = AssetFields.Json(new { });
            assets.Add(new("code", name, 0, new(name, snapshot, snapshot, [], before, before with { Text = text }), snapshot, text));
        }
    }

    private static double SuggestedPrice(IReadOnlyDictionary<int, double> prices, int slot)
    {
        var before = prices.Where(p => p.Key < slot && p.Value > 0).OrderByDescending(p => p.Key).FirstOrDefault();
        var after = prices.Where(p => p.Key > slot && p.Value > 0).OrderBy(p => p.Key).FirstOrDefault();
        if (before.Value == 0 && after.Value == 0) return 1000;
        if (before.Value == 0) return Math.Max(1, Math.Floor(after.Value / 2));
        if (after.Value == 0) return before.Value;
        return Math.Max(1, Math.Round(before.Value + (after.Value - before.Value) * ((double)(slot - before.Key) / (after.Key - before.Key))));
    }
}
