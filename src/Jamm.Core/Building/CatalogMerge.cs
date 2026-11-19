using System.Globalization;
using System.Text;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>A stable object identity and its source and merged catalog coordinates.</summary>
public sealed record CatalogMapping(string Mod, string Object, int Area, int SourceSlot, int Slot);

internal sealed record CatalogEntry(int Area, int Slot, string Object);

/// <summary>Statically relocates legacy fish additions before ordinary GML conflict resolution.</summary>
public static class CatalogMerge
{
    internal const string Create = "gml_Object_CatalogData_Create_0";
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    { "object", "name", "map", "exotic", "desc", "desc_caught", "desc_seen", "desc_hidden", "desc_n", "desc_e",
      "weight", "life", "count", "hide", "max_pop", "pop", "live", "extinct" };
    // These positions were reused by released Cave archives. An unmarked legacy save
    // may have come from Cave even when Cave is absent from today's profile.
    private static readonly HashSet<(int Area, int Slot)> LegacyReplacements =
        [(0, 23), (0, 25), (1, 39), (1, 43), (2, 46)];
    private sealed record Cell(string Field, int Area, int Slot, string Value, int Start, int End);

    internal static (ModPatch[] Patches, CatalogMapping[] Mapping) Prepare(IReadOnlyList<ModPatch> patches)
    {
        var creates = patches.Select(p => p.Changes.GetValueOrDefault("code")?.Changed.FirstOrDefault(a => a.Name == Create)).ToArray();
        string? ancestor = creates.FirstOrDefault(c => c?.BaseCode is not null)?.BaseCode?.Text;
        if (ancestor is null) return (patches.ToArray(), []);
        var baseCells = Cells(ancestor);
        var baseObjects = Objects(baseCells);
        if (baseObjects.Count == 0) return (patches.ToArray(), []);
        var occupied = new Dictionary<(int Area, int Slot), string>(baseObjects);
        var identity = baseObjects.ToDictionary(c => c.Value, c => c.Key, StringComparer.Ordinal);
        var maxima = baseObjects.Keys.GroupBy(k => k.Area).ToDictionary(g => g.Key, g => g.Max(k => k.Slot));
        List<CatalogMapping> mapping = [];
        var relocations = new Dictionary<(int Area, int Slot), int>[patches.Count];
        var additions = new List<(Cell[] Cells, int Area, int Slot)>();
        for (int m = 0; m < patches.Count; m++)
        {
            relocations[m] = [];
            var cells = Cells(creates[m]?.ModCode?.Text ?? ancestor);
            foreach (var (key, obj) in Objects(cells).OrderBy(c => c.Key.Area).ThenBy(c => c.Key.Slot))
            {
                if (baseObjects.TryGetValue(key, out string? baseObject) && baseObject == obj) continue;
                int slot;
                if (identity.TryGetValue(obj, out var previous))
                {
                    if (previous.Area != key.Area) throw new InvalidDataException($"Catalog fish '{obj}' changes size category across mods. Give the variants distinct object names.");
                    slot = previous.Slot;
                }
                else
                {
                    slot = occupied.ContainsKey(key) ? maxima.GetValueOrDefault(key.Area, -1) + 1 : key.Slot;
                    // Sparse arrays create phantom fish in GM1.4; additions must be contiguous.
                    if (slot > maxima.GetValueOrDefault(key.Area, -1) + 1)
                        throw new InvalidDataException($"Catalog in '{patches[m].Mod.Name}' has a gap before [{key.Area},{key.Slot}]. Append fish contiguously or use jamm_catalog_register.");
                    occupied[(key.Area, slot)] = obj;
                    identity[obj] = (key.Area, slot);
                    maxima[key.Area] = Math.Max(maxima.GetValueOrDefault(key.Area, -1), slot);
                    additions.Add((cells.Where(c => c.Area == key.Area && c.Slot == key.Slot && c.Field != "map").ToArray(), key.Area, slot));
                }
                relocations[m][key] = slot;
                mapping.Add(new(patches[m].Mod.Name ?? patches[m].Mod.FileName, ObjectName(obj), key.Area, key.Slot, slot));
            }
            // Some legacy mods overwrite their own additions. Recover earlier named objects
            // too; literal references retain the source's last-write meaning.
            foreach (var group in cells.Where(c => c.Field == "object").GroupBy(c => (c.Area, c.Slot)))
            {
                var versions = group.ToArray();
                if (versions.Select(c => c.Value).Distinct(StringComparer.Ordinal).Count() <= 1) continue;
                var labels = cells.Where(c => c.Field == "name" && c.Area == group.Key.Area && c.Slot == group.Key.Slot).ToArray();
                if (labels.Length != versions.Length)
                    throw new InvalidDataException($"Catalog in '{patches[m].Mod.Name}' overwrites a slot without matching names. Give each fish its own entry before merging.");
                for (int v = 0; v < versions.Length - 1; v++)
                {
                    var version = versions[v];
                    if (identity.ContainsKey(version.Value)) continue;
                    int slot = maxima.GetValueOrDefault(version.Area, -1) + 1;
                    maxima[version.Area] = slot;
                    occupied[(version.Area, slot)] = version.Value;
                    identity[version.Value] = (version.Area, slot);
                    // Description occurrences cannot reliably be paired after an author typo.
                    // Use the verified display label rather than another fish's description.
                    additions.Add(([version, labels[v], labels[v] with { Field = "desc" }], version.Area, slot));
                    mapping.Add(new(patches[m].Mod.Name ?? patches[m].Mod.FileName, ObjectName(version.Value), version.Area, version.Slot, slot));
                }
            }
        }
        // Keep the ancestor's complete parallel-array definitions and initialization order.
        // New entries are initialized explicitly after the ancestor, including all runtime arrays.
        StringBuilder create = new(ancestor.TrimEnd());
        create.AppendLine().AppendLine("jamm_catalog_notice = 0;");
        foreach (var (key, _) in baseObjects.OrderBy(c => c.Key.Area).ThenBy(c => c.Key.Slot))
            create.AppendLine($"jamm_catalog_legacy[{key.Area}, {key.Slot}] = {(LegacyReplacements.Contains(key) || mapping.Any(m => m.Area == key.Area && m.SourceSlot == key.Slot) ? -1 : key.Slot)};");
        foreach (var addition in additions)
        {
            var values = addition.Cells.GroupBy(c => c.Field).ToDictionary(g => g.Key, g => g.Last().Value);
            string at = $"[{addition.Area}, {addition.Slot}]";
            foreach (var (field, value) in Defaults(values)) create.AppendLine($"{field}{at} = {value};");
            create.AppendLine($"map{at} = {addition.Slot};");
            create.AppendLine($"jamm_catalog_legacy{at} = -1;");
        }
        // Preserve author calls to the new API, made after their literal definitions.
        foreach (string call in creates.Where(c => c?.ModCode is not null).SelectMany(c => Calls(c!.ModCode!.Text, "jamm_catalog_register")).Distinct(StringComparer.Ordinal))
            create.AppendLine(call + ";");
        var result = patches.Select((patch, m) =>
        {
            var changes = new SortedDictionary<string, AssetChanges>(patch.Changes, StringComparer.Ordinal);
            var code = changes["code"];
            AssetPatch Update(AssetPatch asset) => asset.ModCode is null ? asset : asset with
            { ModCode = asset.ModCode with { Text = asset.Name == Create ? create.ToString() : RewriteInstanceReferences(RewriteReferences(asset.ModCode.Text, asset.Name, relocations[m]), asset.Name, mapping.Where(v => v.Mod == (patch.Mod.Name ?? patch.Mod.FileName))) } };
            changes["code"] = code with { Added = code.Added.Select(Update).ToArray(), Changed = code.Changed.Select(Update).ToArray() };
            return patch with { Changes = changes };
        }).ToArray();
        return (result, mapping.ToArray());
    }

    private static IEnumerable<KeyValuePair<string, string>> Defaults(Dictionary<string, string> values)
    {
        string desc = values.GetValueOrDefault("desc", "\"\"");
        Dictionary<string, string> defaults = new(StringComparer.Ordinal)
        {
            ["object"] = values["object"], ["name"] = values.GetValueOrDefault("name") ?? throw new InvalidDataException("Catalog fish has no name. Define name and object together."),
            ["desc"] = desc, ["desc_caught"] = desc, ["desc_seen"] = desc, ["desc_hidden"] = "\"Little is known about this fish.\"",
            ["desc_n"] = desc, ["desc_e"] = desc, ["exotic"] = "0", ["weight"] = "1", ["life"] = "-1", ["count"] = "-1",
            ["hide"] = "0", ["max_pop"] = "instance_number(" + values["object"] + ")", ["pop"] = "0", ["live"] = "0", ["extinct"] = "0"
        };
        foreach (var pair in values) defaults[pair.Key] = pair.Value;
        return defaults;
    }

    internal static IReadOnlyList<CatalogEntry> ReadEntries(string text) =>
        Cells(text).Where(c => c.Field == "object")
            .Select(c => new CatalogEntry(c.Area, c.Slot, ObjectName(c.Value)))
            .ToArray();

    private static Dictionary<(int Area, int Slot), string> Objects(IEnumerable<Cell> cells) => cells.Where(c => c.Field == "object")
        .GroupBy(c => (c.Area, c.Slot)).ToDictionary(g => g.Key, g => g.Last().Value);
    private static string ObjectName(string value) => AssetSymbols.Parse(value, out _, out string name) ? name : value;

    // Lexical spans exclude strings/comments and preserve the original expression verbatim.
    private static List<Cell> Cells(string text)
    {
        var t = GmlReferences.Tokens(text);
        List<Cell> cells = [];
        for (int i = 0; i + 7 < t.Count; i++)
        {
            if (!Fields.Contains(t[i].Value) || t[i + 1].Value != "[" || !Number(t[i + 2].Value, out int a)
                || t[i + 3].Value != "," || !Number(t[i + 4].Value, out int s) || t[i + 5].Value != "]" || t[i + 6].Value != "=") continue;
            int end = i + 7;
            while (end < t.Count && t[end].Value != ";") end++;
            if (end == t.Count) throw new InvalidDataException("Catalog assignment has no semicolon. Regenerate the GML patch.");
            cells.Add(new(t[i].Value, a, s, text[t[i + 7].Start..t[end].Start].Trim(), t[i].Start, t[end].Start + t[end].Length));
        }
        return cells;
    }

    /// <summary>Rewrites literal catalog array coordinates, including qualified reads, without touching strings or other objects' arrays.</summary>
    public static string RewriteReferences(string text, string entry, IReadOnlyDictionary<(int Area, int Slot), int> relocation)
    {
        var t = GmlReferences.Tokens(text);
        List<(int Start, int Length, string Value)> edits = [];
        var contexts = CatalogContexts(t, entry);
        for (int i = 0; i + 5 < t.Count; i++)
        {
            if (!Fields.Contains(t[i].Value) || t[i + 1].Value != "[" || !Number(t[i + 2].Value, out int a)
                || t[i + 3].Value != "," || !Number(t[i + 4].Value, out int s) || t[i + 5].Value != "]") continue;
            bool qualified = i >= 2 && t[i - 1].Value == "." && t[i - 2].Value == "CatalogData";
            bool other = i > 0 && t[i - 1].Value == "." && !qualified;
            if (other || !(contexts[i] || qualified)) continue;
            if (relocation.TryGetValue((a, s), out int target) && target != s)
                edits.Add((t[i + 4].Start, t[i + 4].Length, target.ToString(CultureInfo.InvariantCulture)));
            if (t[i].Value == "map" && i + 8 < t.Count && t[i + 6].Value == "=" && Number(t[i + 7].Value, out int display)
                && t[i + 8].Value == ";" && relocation.TryGetValue((a, display), out int mapped) && mapped != display)
                edits.Add((t[i + 7].Start, t[i + 7].Length, mapped.ToString(CultureInfo.InvariantCulture)));
        }
        foreach (string function in new[] { "unhide_entry_for_values" })
            for (int i = 0; i + 5 < t.Count; i++)
                if (t[i].Value == function && t[i + 1].Value == "(" && Number(t[i + 2].Value, out int a) && t[i + 3].Value == ","
                    && Number(t[i + 4].Value, out int s) && t[i + 5].Value == ")" && relocation.TryGetValue((a, s), out int target) && target != s)
                    edits.Add((t[i + 4].Start, t[i + 4].Length, target.ToString(CultureInfo.InvariantCulture)));
        StringBuilder result = new(text);
        foreach (var edit in edits.Distinct().OrderByDescending(e => e.Start)) result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        return result.ToString();
    }

    private static string RewriteInstanceReferences(string text, string entry, IEnumerable<CatalogMapping> mappings)
    {
        if (!text.Contains("catalog_index", StringComparison.Ordinal)) return text;
        var tokens = GmlReferences.Tokens(text);
        List<(int Start, int Length, string Value)> edits = [];
        foreach (var mapping in mappings.Where(m => m.Slot != m.SourceSlot))
        {
            var contexts = CatalogContexts(tokens, entry, mapping.Object);
            for (int i = 0; i + 2 < tokens.Count; i++)
            {
                if (tokens[i].Value != "catalog_index" || tokens[i + 1].Value != "=" || !Number(tokens[i + 2].Value, out int slot) || slot != mapping.SourceSlot) continue;
                bool qualified = i >= 2 && tokens[i - 1].Value == "." && tokens[i - 2].Value == mapping.Object;
                if (!qualified && (!contexts[i] || i > 0 && tokens[i - 1].Value == ".")) continue;
                edits.Add((tokens[i + 2].Start, tokens[i + 2].Length, mapping.Slot.ToString(CultureInfo.InvariantCulture)));
            }
        }
        StringBuilder result = new(text);
        foreach (var edit in edits.Distinct().OrderByDescending(e => e.Start)) result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        return result.ToString();
    }

    internal static bool DisjointWrites(IEnumerable<string> alternatives)
    {
        HashSet<(string Field, int Area, int Slot)> seen = [];
        foreach (string text in alternatives)
        {
            var cells = Cells(text);
            if (cells.Count == 0) return false;
            StringBuilder remainder = new(text);
            foreach (var cell in cells.OrderByDescending(c => c.Start)) remainder.Remove(cell.Start, cell.End - cell.Start);
            if (GmlReferences.Tokens(remainder.ToString()).Count != 0) return false;
            foreach (var cell in cells)
                if (!seen.Add((cell.Field, cell.Area, cell.Slot))) return false;
        }
        return true;
    }

    private static bool[] CatalogContexts(IReadOnlyList<GmlReferences.Token> tokens, string entry, string objectName = "CatalogData")
    {
        var contexts = Enumerable.Repeat(entry.StartsWith("gml_Object_" + objectName + "_", StringComparison.Ordinal), tokens.Count).ToArray();
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Value != "with" || tokens[i + 1].Value != "(") continue;
            int close = i + 2, depth = 1;
            for (; close < tokens.Count; close++)
            {
                if (tokens[close].Value == "(") depth++;
                if (tokens[close].Value == ")" && IfClosing(ref depth)) break;
            }
            if (close + 1 >= tokens.Count) continue;
            bool catalog = close == i + 3 && tokens[i + 2].Value == objectName;
            int end = close + 1;
            if (tokens[end].Value == "{")
            {
                depth = 1;
                for (end++; end < tokens.Count; end++)
                {
                    if (tokens[end].Value == "{") depth++;
                    if (tokens[end].Value == "}" && IfClosing(ref depth)) break;
                }
            }
            else while (end < tokens.Count && tokens[end].Value != ";") end++;
            for (int j = close + 1; j <= end && j < contexts.Length; j++) contexts[j] = catalog;
        }
        return contexts;
    }

    /// <summary>Checks every expected name/object/description field and display permutation after compilation.</summary>
    public static int Validate(string expected, string actual)
    {
        var wanted = Cells(expected).Where(c => c.Field is "object" or "name" or "map" or "exotic" || c.Field.StartsWith("desc", StringComparison.Ordinal))
            .GroupBy(c => (c.Field, c.Area, c.Slot)).ToDictionary(g => g.Key, g => g.Last());
        var found = Cells(actual).GroupBy(c => (c.Field, c.Area, c.Slot)).ToDictionary(g => g.Key, g => g.Last());
        foreach (var (key, cell) in wanted)
        {
            if (!found.TryGetValue(key, out var value) || Canonical(cell.Value) != Canonical(value.Value))
                throw new InvalidDataException($"Catalog verification failed: {key.Field}[{key.Area},{key.Slot}] differs from its source fish. No output was published.");
        }
        var objects = Objects(Cells(actual));
        if (objects.Count != wanted.Keys.Count(k => k.Field == "object") || objects.Values.Distinct(StringComparer.Ordinal).Count() != objects.Count)
            throw new InvalidDataException("Catalog verification failed: missing, extra or duplicate fish objects. No output was published.");
        foreach (var area in objects.Keys.GroupBy(k => k.Area))
        {
            var order = area.Select(k => found.GetValueOrDefault(("map", k.Area, k.Slot))?.Value).ToArray();
            if (order.Any(v => v is null || !Number(v, out int target) || !objects.ContainsKey((area.Key, target))) || order.Distinct().Count() != area.Count())
                throw new InvalidDataException($"Catalog verification failed: display map for size {area.Key} is not a permutation. No output was published.");
        }
        return objects.Count;
    }

    private static string Canonical(string value) => string.Join("|", GmlReferences.Tokens(value).Select(t =>
        t.Kind == 'i' && AssetSymbols.Parse(t.Value, out _, out string name) ? "i:" + name : t.Kind + ":" + t.Value));

    private static bool Number(string value, out int number) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    private static IEnumerable<string> Calls(string text, string function)
    {
        var t = GmlReferences.Tokens(text);
        for (int i = 0; i + 1 < t.Count; i++)
        {
            if (t[i].Value != function || t[i + 1].Value != "(") continue;
            int depth = 0, j = i + 1;
            for (; j < t.Count; j++) { if (t[j].Value == "(") depth++; if (t[j].Value == ")" && IfClosing(ref depth)) break; }
            if (j < t.Count) yield return text[t[i].Start..(t[j].Start + t[j].Length)];
        }
    }
    private static bool IfClosing(ref int depth) { depth--; return depth == 0; }
}
