using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>A source frame to copy into the assembled legacy rod sprite.</summary>
internal sealed record RodFrame(int Mod, int SourceFrame, int TargetFrame);

/// <summary>Maintains the rod-indexed data that must grow alongside a relocated RodData entry.</summary>
internal static partial class LegacyRodRelocation
{
    internal const string RodCreate = "gml_Object_RodData_Create_0";
    internal const string UpgradeCreate = "gml_Object_UpgradeData_Create_0";
    internal const string Sprite = "spr_rod";

    private const string RocketCreate = "gml_Object_Rocket_Create_0";
    private const string RocketStep = "gml_Object_Rocket_Step_2";
    private const string QuestDistance = "gml_Script_start_quest_dist";

    private static readonly string[] MatrixEntries =
    [
        UpgradeCreate, "gml_Object_SaveManager_Other_13", "gml_Object_SaveManager_Other_14",
        "gml_Object_SaveManager_Other_15"
    ];

    private static readonly string[] SlotConsumers =
    [
        "gml_Object_UpgradeData_Draw_0", "gml_Object_Rod_Other_10", "gml_Object_UpgradeCard_Other_10"
    ];

    internal static bool IsRodArray(string entry, string field) =>
        entry is RocketCreate or RocketStep && field == "target_distance"
        || entry == QuestDistance && field is "dist" or "dpth";

    internal static bool HasCompatibleFrames(IReadOnlyList<ModPatch> patches, int kept, int moved, int slot)
    {
        var keptPatch = LegacyUpgradeRelocation.FindSpritePatch(patches[kept], Sprite);
        var movedPatch = LegacyUpgradeRelocation.FindSpritePatch(patches[moved], Sprite);
        var before = keptPatch?.Base ?? movedPatch?.Base;
        return (keptPatch?.Mod ?? before) is { } keptSprite
            && (movedPatch?.Mod ?? before) is { } movedSprite
            && LegacyUpgradeRelocation.TryReadSpriteDimensions(keptSprite, out int keptWidth, out int keptHeight, out int keptFrames)
            && LegacyUpgradeRelocation.TryReadSpriteDimensions(movedSprite, out int movedWidth, out int movedHeight, out int movedFrames)
            && keptWidth == movedWidth && keptHeight == movedHeight && slot < keptFrames && slot < movedFrames;
    }

    internal static void CompleteCode(List<PlannedAsset> assets, IReadOnlyList<ModPatch> patches,
        IReadOnlyDictionary<string, CodeText>? baseCode, int rodCount)
    {
        string[] entries = [.. MatrixEntries, .. SlotConsumers, RodCreate, RocketCreate, RocketStep, QuestDistance];
        var versions = patches.SelectMany(p => p.Changes.GetValueOrDefault("code") is { } code
            ? code.Changed.Concat(code.Added) : []).ToLookup(a => a.Name, StringComparer.Ordinal);
        int columns = MatrixEntries.Concat(SlotConsumers).SelectMany(entry => versions[entry]
                .SelectMany(a => new[] { a.BaseCode?.Text, a.ModCode?.Text })
                .Append(baseCode?.GetValueOrDefault(entry)?.Text)
                .OfType<string>()
                .Select(text => ColumnCount(entry, text)))
            .DefaultIfEmpty().Max();

        foreach (string entry in entries)
        {
            int index = assets.FindIndex(a => a.Family == "code" && a.Name == entry);
            var selected = index >= 0 ? assets[index] : null;
            var ancestor = baseCode?.GetValueOrDefault(entry) ?? versions[entry].Select(a => a.BaseCode).FirstOrDefault(c => c is not null);
            string? before = selected is null ? ancestor?.Text : selected.Gml;
            if (before is null) continue;

            string after = entry switch
            {
                RodCreate => CompleteRodData(before, rodCount),
                RocketCreate or RocketStep => ExtendArray(before, "target_distance", rodCount),
                QuestDistance => ExtendArray(ExtendArray(before, "dist", rodCount), "dpth", rodCount),
                UpgradeCreate => CompleteUpgradeData(RaiseBounds(entry, before, rodCount, columns), rodCount, columns),
                _ => RaiseBounds(entry, before, rodCount, columns)
            };
            if (after == before) continue;

            if (selected is not null)
                assets[index] = selected with { Gml = after };
            else
            {
                var snapshot = AssetFields.Json(new { });
                assets.Add(new("code", entry, 0, new(entry, snapshot, snapshot, [], ancestor, ancestor! with { Text = after }), snapshot, after));
            }
        }
    }

    private static string CompleteRodData(string text, int rodCount)
    {
        text = LegacySlotRelocation.BumpCreateCountAssignment(text, "RodData", rodCount);
        return ExtendArray(text, "unlocked", rodCount, (_, _) => "-2", "RodData");
    }

    private static string CompleteUpgradeData(string text, int rodCount, int columns)
    {
        // Vanilla derives each base cost from that rod's shop price. Preserve explicit mod values.
        text = ExtendArray(text, "cost_base", rodCount, (slot, _) => $"RodData.cost[{slot}]", "UpgradeData");
        var upg = new HashSet<(int Rod, int Column)>();
        var objects = new HashSet<(int Rod, int Column)>();
        foreach (Match match in MatrixWrite().Matches(text))
        {
            if (!TryIndex(match.Groups["rod"].Value, out int rod) || !TryIndex(match.Groups["col"].Value, out int column))
                continue;
            bool upgrade = match.Groups["field"].Value == "upg";
            (upgrade ? upg : objects).Add((rod, column));
        }

        var initializedRows = upg.Select(cell => cell.Rod).ToHashSet();
        // The usual nested loop initializes every object cell after its bounds have been raised.
        bool initializesObjects = ObjectInitializationLoop().IsMatch(text);
        StringBuilder defaults = new();
        for (int rod = 0; rod < rodCount; rod++)
        {
            // A missing rod row gets empty upgrade slots. Extra columns on existing rows stay disabled.
            int empty = initializedRows.Contains(rod) ? -2 : -1;
            for (int column = 0; column < columns; column++)
            {
                if (!upg.Contains((rod, column)))
                    defaults.Append(CultureInfo.InvariantCulture, $"upg[{rod}, {column}] = {empty};\n");
                if (!initializesObjects && !objects.Contains((rod, column)))
                    defaults.Append(CultureInfo.InvariantCulture, $"object[{rod}, {column}] = -1;\n");
            }
        }
        return defaults.Append(text).ToString();
    }

    private static string ExtendArray(string text, string field, int count,
        Func<int, int, string>? defaultValue = null, string? scope = null)
    {
        string prefix = scope is null ? "" : $@"(?:{scope}\s*\.\s*)?";
        var matches = Regex.Matches(text, $@"(?m)^[ \t]*{prefix}{field}\s*\[\s*(\d+)\s*\][ \t]*=[^=\r\n][^\r\n]*",
            RegexOptions.CultureInvariant);
        if (matches.Count == 0) return text;
        var assigned = matches.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToHashSet();
        int nearest = assigned.Min();
        StringBuilder additions = new();
        for (int slot = 0; slot < count; slot++)
        {
            if (assigned.Contains(slot)) nearest = slot;
            else
            {
                string value = defaultValue?.Invoke(slot, nearest) ?? $"{field}[{nearest}]";
                additions.Append(CultureInfo.InvariantCulture, $"\n{field}[{slot}] = {value};");
            }
        }
        var last = matches[^1];
        return text.Insert(last.Index + last.Length, additions.ToString());
    }

    private static int ColumnCount(string entry, string text)
    {
        int count = Bounds(entry, text).Where(b => !b.Rods).Select(b => b.Count).DefaultIfEmpty().Max();
        if (entry == UpgradeCreate)
            foreach (Match match in MatrixWrite().Matches(text))
                if (TryIndex(match.Groups["col"].Value, out int column))
                    count = Math.Max(count, column + 1);
        return count;
    }

    private static string RaiseBounds(string entry, string text, int rods, int columns)
    {
        StringBuilder result = new(text);
        foreach (var bound in Bounds(entry, text).OrderByDescending(b => b.Token.Start))
        {
            int required = bound.Rods ? rods : columns;
            if (bound.Count < required)
                result.Remove(bound.Token.Start, bound.Token.Length).Insert(bound.Token.Start,
                    (bound.Inclusive ? required - 1 : required).ToString(CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    private static IEnumerable<(GmlReferences.Token Token, bool Rods, bool Inclusive, int Count)> Bounds(string entry, string text)
    {
        var tokens = GmlReferences.Tokens(text);
        bool matrix = MatrixEntries.Contains(entry, StringComparer.Ordinal);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is not { Kind: 'i', Value: "for" }
                || !LegacyUpgradeRelocation.TryMatchCountBoundLoop(tokens, i, out string? variable, out int bound, out bool inclusive, out int end)
                || !TryIndex(tokens[bound].Value, out int count))
                continue;

            bool rods = matrix && variable == "ix";
            bool columns = matrix && variable == "iy"
                || SlotConsumers.Contains(entry, StringComparer.Ordinal)
                && UsesUpgradeColumn(tokens, bound + 1, end, variable);
            if (rods || columns)
                yield return (tokens[bound], rods, inclusive, inclusive ? count + 1 : count);
        }
    }

    private static bool UsesUpgradeColumn(IReadOnlyList<GmlReferences.Token> tokens, int start, int end, string? variable)
    {
        for (int i = start; i + 3 <= end; i++)
            if (tokens[i].Value == "upg" && tokens[i + 1].Value == "[")
                for (int j = i + 2; j + 2 <= end && tokens[j].Value != "]"; j++)
                    if (tokens[j].Value == "," && tokens[j + 1].Value == variable && tokens[j + 2].Value == "]")
                        return true;
        return false;
    }

    internal static string? UnionUpgradeRows(MergeHunk hunk)
    {
        if (hunk.Kind != "list") return null;
        Dictionary<(string Field, string Rod, string Column), string> writes = [];
        foreach (var version in hunk.Versions)
        {
            foreach (string line in version.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = MatrixWrite().Match(line);
                if (!match.Success) return null;
                var key = (match.Groups["field"].Value, match.Groups["rod"].Value, match.Groups["col"].Value);
                if (!writes.TryAdd(key, line) && writes[key] != line) return null;
            }
        }
        return string.Concat(writes.Values.Select(line => line + "\n"));
    }

    private static bool TryIndex(string value, out int index) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index < int.MaxValue;

    [GeneratedRegex(@"(?m)^[ \t]*(?:UpgradeData\s*\.\s*)?(?<field>upg|object)\s*\[\s*(?<rod>\d+)\s*,\s*(?<col>\d+)\s*\][ \t]*=[^=\r\n][^\r\n]*",
        RegexOptions.CultureInvariant)]
    private static partial Regex MatrixWrite();

    [GeneratedRegex(@"\bfor\s*\(\s*(?:var\s+)?ix\s*=\s*0\s*;[^)]*\)\s*\{\s*for\s*\(\s*(?:var\s+)?iy\s*=\s*0\s*;[^)]*\)\s*\{\s*object\s*\[\s*ix\s*,\s*iy\s*\]\s*=\s*-1\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex ObjectInitializationLoop();
}
