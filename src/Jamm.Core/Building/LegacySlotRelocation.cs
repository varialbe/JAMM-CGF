using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>Relocates proven distinct legacy boat additions before ordinary conflict analysis.</summary>
internal static partial class LegacySlotRelocation
{
    internal sealed record Result(ModPatch[] Patches, IReadOnlyList<string> Decisions, IReadOnlyList<UpgradeIconRelocation> UpgradeIcons);
    private sealed record Boat(int Slot, string Identity, string DisplayName);

    internal static Result Prepare(IReadOnlyList<ModPatch> source)
    {
        var (patches, boatDecisions) = PrepareBoats(source);
        var upgrades = LegacyUpgradeRelocation.Prepare(patches);
        return new(upgrades.Patches, [.. boatDecisions, .. upgrades.Decisions], upgrades.IconRelocations);
    }

    private static (ModPatch[] Patches, IReadOnlyList<string> Decisions) PrepareBoats(IReadOnlyList<ModPatch> source)
    {
        ModPatch[] patches = source.ToArray();
        string? ancestor = patches.Select(p => Create(p)?.BaseCode?.Text).FirstOrDefault(t => t is not null);
        if (ancestor is null) return (patches, []);
        var baseBoats = ReadBoats(ancestor);
        if (baseBoats.Count == 0) return (patches, []);

        var claims = patches.Select(p => ReadBoats(Create(p)?.ModCode?.Text ?? ancestor).Values
            .Where(b => IsAddition(b, ReadBoats(Create(p)?.ModCode?.Text ?? ancestor), baseBoats))
            .ToArray()).ToArray();
        int next = Math.Max(baseBoats.Keys.DefaultIfEmpty(-1).Max(), claims.SelectMany(c => c).Select(b => b.Slot).DefaultIfEmpty(-1).Max()) + 1;
        List<string> decisions = [];
        for (int slot = 0; slot <= claims.SelectMany(c => c).Select(b => b.Slot).DefaultIfEmpty(-1).Max(); slot++)
        {
            var contenders = claims.SelectMany((items, mod) => items.Where(b => b.Slot == slot).Select(b => (Mod: mod, Boat: b)))
                .OrderBy(x => x.Mod).ToArray();
            if (contenders.Select(c => c.Boat.Identity).Distinct(StringComparer.Ordinal).Count() < 2) continue;
            // Keep the earliest claimant at the source slot. Later claimants move in load order.
            foreach (var contender in contenders.Skip(1))
            {
                int target = next;
                if (!TryRewriteMod(patches[contender.Mod], slot, target, out var rewrittenCount, out var warning))
                {
                    decisions.Add($"WARNING: boat slot {slot} in '{Name(patches[contender.Mod])}' was not relocated: {warning}");
                    continue;
                }
                next++;
                patches[contender.Mod] = rewrittenCount.Patch;
                decisions.Add($"[CGF-RELOCATE-001] Boat \"{contender.Boat.DisplayName}\" ({Name(patches[contender.Mod])}) moved from slot {slot} to {target}; rewrote {rewrittenCount.Count} typed references.");
            }
        }
        return (patches, decisions);
    }

    private static bool IsAddition(Boat boat, IReadOnlyDictionary<int, Boat> mod, IReadOnlyDictionary<int, Boat> baseline)
    {
        if (!baseline.TryGetValue(boat.Slot, out var old)) return true;
        return old.Identity != boat.Identity && !mod.Values.Any(other => other.Identity == old.Identity);
    }

    private static AssetPatch? Create(ModPatch patch) => patch.Changes.GetValueOrDefault("code")?.Added
        .Concat(patch.Changes.GetValueOrDefault("code")?.Changed ?? []).FirstOrDefault(a => a.Name == "gml_Object_BoatData_Create_0");

    private static Dictionary<int, Boat> ReadBoats(string text)
    {
        Dictionary<int, string> objects = [];
        Dictionary<int, string> names = [];
        foreach (Match match in BoatObjectWrite().Matches(text))
            if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot))
                objects[slot] = match.Groups[2].Value;
        foreach (Match match in BoatNameWrite().Matches(text))
            if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot))
                names[slot] = match.Groups[2].Value;
        return objects.Select(pair => new Boat(pair.Key, pair.Value,
                names.GetValueOrDefault(pair.Key) ?? pair.Value))
            .ToDictionary(b => b.Slot);
    }

    private static bool TryRewriteMod(ModPatch patch, int oldSlot, int newSlot, out (ModPatch Patch, int Count) result, out string warning)
    {
        result = (patch, 0); warning = "";
        var code = patch.Changes.GetValueOrDefault("code");
        if (code is null) { warning = "the patch has no code changes to inspect"; return false; }
        List<(AssetPatch Asset, string Text, int Count)> updates = [];
        foreach (var asset in code.Added.Concat(code.Changed))
        {
            if (asset.ModCode is not { Format: "gml" } gml) { warning = $"'{asset.Name}' is not available as GML"; return false; }
            string text = gml.Text;
            string rewritten = RewriteBoatReferences(text, asset.Name, oldSlot, newSlot, out int count, out bool uncertain);
            if (uncertain) { warning = $"'{asset.Name}' contains a boat-typed use that cannot be classified"; return false; }
            if (count > 0) updates.Add((asset, rewritten, count));
        }
        if (updates.Count == 0) { warning = "no typed references to the claimed slot were found"; return false; }
        AssetPatch Update(AssetPatch asset)
        {
            var update = updates.FirstOrDefault(u => ReferenceEquals(u.Asset, asset));
            return update.Asset is null ? asset : asset with { ModCode = asset.ModCode! with { Text = update.Text } };
        }
        var changes = new SortedDictionary<string, AssetChanges>(patch.Changes, StringComparer.Ordinal);
        changes["code"] = code with { Added = code.Added.Select(Update).ToArray(), Changed = code.Changed.Select(Update).ToArray() };
        result = (patch with { Changes = changes }, updates.Sum(u => u.Count));
        return true;
    }

    private static string RewriteBoatReferences(string text, string entry, int oldSlot, int newSlot, out int count, out bool uncertain)
    {
        count = 0; uncertain = false;
        var tokens = GmlReferences.Tokens(text);
        var edits = new Dictionary<(int Start, int Length), string>();
        bool boatDataEvent = entry.StartsWith("gml_Object_BoatData_", StringComparison.Ordinal);
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Value != "[" || tokens[i + 1].Value != oldSlot.ToString(CultureInfo.InvariantCulture) || tokens[i + 2].Value != "]") continue;
            bool qualified = i >= 2 && tokens[i - 1].Value == "." && tokens[i - 2].Value == "BoatData";
            bool localBoatData = boatDataEvent && (i < 2 || tokens[i - 1].Value != ".");
            if (!qualified && !localBoatData) continue;
            edits[(tokens[i + 1].Start, tokens[i + 1].Length)] = newSlot.ToString(CultureInfo.InvariantCulture);
        }
        // Boat selection indices are explicit semantic sinks in the legacy game.
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Value is not ("boat" or "boat_index" or "item_id") || tokens[i + 1].Value is not ("=" or "==" or "!=" or "<" or ">")
                || tokens[i + 2].Value != oldSlot.ToString(CultureInfo.InvariantCulture)) continue;
            edits[(tokens[i + 2].Start, tokens[i + 2].Length)] = newSlot.ToString(CultureInfo.InvariantCulture);
        }
        string literal = oldSlot.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Value != literal || edits.ContainsKey((tokens[i].Start, tokens[i].Length))) continue;
            int from = Math.Max(0, i - 5), to = Math.Min(tokens.Count - 1, i + 5);
            bool boatContext = Enumerable.Range(from, to - from + 1)
                .Any(j => tokens[j].Kind == 'i' && tokens[j].Value.Contains("boat", StringComparison.OrdinalIgnoreCase));
            if (boatContext) { uncertain = true; return text; }
        }
        foreach (var edit in edits.OrderByDescending(e => e.Key.Start))
            text = text.Remove(edit.Key.Start, edit.Key.Length).Insert(edit.Key.Start, edit.Value);
        count = edits.Count;
        return text;
    }

    /// <summary>
    /// Returns merged text for a conflicting BoatData or UpgradeData hunk whose versions differ only by writes to
    /// disjoint table slots or upgrade count bounds, or null when the hunk needs a real decision.
    /// </summary>
    internal static string? SlotUnion(string asset, MergeHunk hunk)
    {
        if (hunk.Status != "CONFLICT" || hunk.Versions.Count < 2) return null;
        if (asset.Contains("gml_Object_BoatData_", StringComparison.Ordinal))
        {
            if (hunk.Kind == "list" && hunk.KeepBoth is not null) return hunk.KeepBoth;
            return TryUnionDisjointSlots(hunk);
        }
        return LegacyUpgradeRelocation.UpgradeUnion(asset, hunk);
    }

    /// <summary>
    /// Unions versions whose non-shared lines are 1D table slot assignments to disjoint numeric indices.
    /// </summary>
    internal static string? TryUnionDisjointSlots(MergeHunk hunk)
    {
        var lines = hunk.Versions.Select(v => SplitLines(v.Text)).ToArray();
        var common = lines.Skip(1).Aggregate(new HashSet<string>(lines[0].Select(l => l.Trim()), StringComparer.Ordinal),
            (set, next) => { set.IntersectWith(next.Select(l => l.Trim())); return set; });
        var claimed = new HashSet<int>();
        var unique = new List<string>[lines.Length];
        for (int v = 0; v < lines.Length; v++)
        {
            unique[v] = lines[v].Where(l => !common.Contains(l.Trim())).ToList();
            var slots = new HashSet<int>();
            foreach (string line in unique[v].Where(l => l.Trim().Length > 0))
            {
                var match = SlotWrite().Match(line);
                if (!match.Success) return null;
                slots.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            if (slots.Overlaps(claimed)) return null;
            claimed.UnionWith(slots);
        }
        // Insert the other versions' slot writes right after the first version's own unique lines, so shared trailing
        // lines such as an nItems computation stay after every slot write.
        var result = new List<string>(lines[0]);
        int anchor = unique[0].Count == 0 ? -1 : result.FindLastIndex(l => !common.Contains(l.Trim()));
        if (anchor < 0) anchor = result.FindIndex(l => l.Contains("nItems", StringComparison.Ordinal)) - 1;
        if (anchor < -1) anchor = result.Count - 1;
        result.InsertRange(anchor + 1, unique.Skip(1).SelectMany(u => u));
        return string.Concat(result);
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') { lines.Add(text[start..(i + 1)]); start = i + 1; }
        if (start < text.Length) lines.Add(text[start..] + "\n");
        return lines;
    }

    [GeneratedRegex(@"^\s*(?:BoatData\.|UpgradeData\.)?[A-Za-z_][A-Za-z0-9_]*\s*\[\s*(\d+)\s*\]\s*=[^=]", RegexOptions.CultureInvariant)]
    private static partial Regex SlotWrite();

    private static string Name(ModPatch patch) => patch.Mod.Name ?? Path.GetFileNameWithoutExtension(patch.Mod.FileName);

    [GeneratedRegex(@"(?:BoatData\.)?object\s*\[\s*(\d+)\s*\]\s*=\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex BoatObjectWrite();
    [GeneratedRegex("(?:BoatData\\.)?name\\s*\\[\\s*(\\d+)\\s*\\]\\s*=\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex BoatNameWrite();
}
