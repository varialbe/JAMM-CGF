using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>Relocates proven distinct legacy boat, lure, and tool additions before ordinary conflict analysis.</summary>
internal static partial class LegacySlotRelocation
{
    internal sealed record Result(
        ModPatch[] Patches,
        IReadOnlyList<string> Decisions,
        IReadOnlyList<UpgradeIconRelocation> UpgradeIcons,
        IReadOnlyList<SharedUnlockHunk> SharedUnlocks);

    private sealed record TableDescriptor(
        string Kind,
        string LowerKind,
        string TableObject,
        string CreateEntry,
        string LocalEventPrefix,
        string[] IndexVariables,
        string[] ScopedItemIdPrefixes,
        string[] CallSinks,
        string[] IndexedArrays,
        string ContextKeyword);

    private sealed record TableItem(int Slot, string Identity, string DisplayName);

    private static readonly TableDescriptor BoatTable = new(
        Kind: "Boat",
        LowerKind: "boat",
        TableObject: "BoatData",
        CreateEntry: "gml_Object_BoatData_Create_0",
        LocalEventPrefix: "gml_Object_BoatData_",
        IndexVariables: ["boat", "boat_index"],
        ScopedItemIdPrefixes: ["gml_Object_BoatData_", "gml_Object_BoatCard_", "gml_Object_BoatMenu_"],
        CallSinks: ["boat_was_unlocked"],
        IndexedArrays: [],
        ContextKeyword: "boat");

    private static readonly TableDescriptor LureTable = new(
        Kind: "Lure",
        LowerKind: "lure",
        TableObject: "LureData",
        CreateEntry: "gml_Object_LureData_Create_0",
        LocalEventPrefix: "gml_Object_LureData_",
        IndexVariables: ["lure_index", "active_lure"],
        ScopedItemIdPrefixes: ["gml_Object_LureData_", "gml_Object_LureCard_", "gml_Object_LureMenu_"],
        CallSinks: ["buy_lure", "lure_was_purchased"],
        IndexedArrays: ["gear_lure_scalar", "lure_scalar"],
        ContextKeyword: "lure");

    private static readonly TableDescriptor ToolTable = new(
        Kind: "Tool",
        LowerKind: "tool",
        TableObject: "ToolData",
        CreateEntry: "gml_Object_ToolData_Create_0",
        LocalEventPrefix: "gml_Object_ToolData_",
        IndexVariables: ["tool_index", "active_tool"],
        ScopedItemIdPrefixes: ["gml_Object_ToolData_", "gml_Object_ToolCard_", "gml_Object_ToolMenu_"],
        CallSinks: ["tool_was_purchased"],
        IndexedArrays: [],
        ContextKeyword: "tool");

    private static readonly TableDescriptor[] Tables = [BoatTable, LureTable, ToolTable];

    internal static Result Prepare(IReadOnlyList<ModPatch> source)
    {
        ModPatch[] patches = source.ToArray();
        List<string> decisions = [];
        foreach (var table in Tables)
            PrepareTable(table, patches, decisions);
        var upgrades = LegacyUpgradeRelocation.Prepare(patches);
        return new(upgrades.Patches, [.. decisions, .. upgrades.Decisions], upgrades.IconRelocations, upgrades.SharedUnlocks);
    }

    private static void PrepareTable(TableDescriptor table, ModPatch[] patches, List<string> decisions)
    {
        string? ancestor = patches.Select(p => Create(table, p)?.BaseCode?.Text).FirstOrDefault(t => t is not null);
        if (ancestor is null) return;
        var baseItems = ReadItems(table, ancestor);
        if (baseItems.Count == 0) return;

        var modItems = patches.Select(p => ReadItems(table, Create(table, p)?.ModCode?.Text ?? ancestor)).ToArray();
        var claims = modItems.Select(items => items.Values
            .Where(item => IsAddition(item, items, baseItems))
            .ToArray()).ToArray();
        int maxSlot = Math.Max(
            baseItems.Keys.DefaultIfEmpty(-1).Max(),
            modItems.SelectMany(m => m.Keys).DefaultIfEmpty(-1).Max());
        int next = maxSlot + 1;
        int maxClaimSlot = claims.SelectMany(c => c).Select(item => item.Slot).DefaultIfEmpty(-1).Max();

        for (int slot = 0; slot <= maxClaimSlot; slot++)
        {
            var contenders = claims
                .SelectMany((items, mod) => items.Where(item => item.Slot == slot).Select(item => (Mod: mod, Item: item)))
                .OrderBy(x => x.Mod)
                .ToArray();
            if (contenders.Select(c => c.Item.Identity).Distinct(StringComparer.Ordinal).Count() < 2)
                continue;

            // Keep the earliest identity at the source slot; group later distinct identities in load order.
            string keptIdentity = contenders[0].Item.Identity;
            var movedGroups = contenders
                .Where(c => c.Item.Identity != keptIdentity)
                .GroupBy(c => c.Item.Identity, StringComparer.Ordinal)
                .OrderBy(g => g.Min(x => x.Mod))
                .ToArray();

            foreach (var group in movedGroups)
            {
                int target = next;
                List<(int Mod, ModPatch Patch, int Count, string DisplayName)> pending = [];
                bool failed = false;
                foreach (var contender in group.OrderBy(x => x.Mod))
                {
                    if (!TryRewriteMod(table, patches[contender.Mod], baseItems, slot, target, out var rewritten, out string warning))
                    {
                        decisions.Add($"WARNING: {table.LowerKind} slot {slot} in '{Name(patches[contender.Mod])}' was not relocated: {warning}");
                        failed = true;
                        break;
                    }
                    pending.Add((contender.Mod, rewritten.Patch, rewritten.Count, contender.Item.DisplayName));
                }
                if (failed) continue;
                next++;
                foreach (var item in pending)
                {
                    patches[item.Mod] = item.Patch;
                    decisions.Add($"[CGF-RELOCATE-001] {table.Kind} \"{item.DisplayName}\" ({Name(patches[item.Mod])}) moved from slot {slot} to {target}; rewrote {item.Count} typed references.");
                }
            }
        }
    }

    private static bool IsAddition(TableItem item, IReadOnlyDictionary<int, TableItem> mod, IReadOnlyDictionary<int, TableItem> baseline)
    {
        if (!baseline.TryGetValue(item.Slot, out var old)) return true;
        return old.Identity != item.Identity && !mod.Values.Any(other => other.Identity == old.Identity);
    }

    private static AssetPatch? Create(TableDescriptor table, ModPatch patch) =>
        patch.Changes.GetValueOrDefault("code")?.Added
            .Concat(patch.Changes.GetValueOrDefault("code")?.Changed ?? [])
            .FirstOrDefault(a => a.Name == table.CreateEntry);

    private static Dictionary<int, TableItem> ReadItems(TableDescriptor table, string text)
    {
        Dictionary<int, string> objects = [];
        Dictionary<int, string> names = [];
        var tokens = GmlReferences.Tokens(LineDiff.Normalize(text));
        for (int i = 0; i + 5 < tokens.Count; i++)
        {
            int fieldIdx = i;
            if (tokens[i].Kind == 'i' && tokens[i].Value == table.TableObject && tokens[i + 1].Value == ".")
                fieldIdx = i + 2;
            else if (i > 0 && tokens[i - 1].Value == ".")
                continue;

            if (fieldIdx + 5 >= tokens.Count) continue;
            string field = tokens[fieldIdx].Value;
            if (tokens[fieldIdx].Kind != 'i' || field is not ("object" or "name")) continue;
            if (tokens[fieldIdx + 1].Value != "[" || tokens[fieldIdx + 2].Kind != 'n' || tokens[fieldIdx + 3].Value != "]"
                || tokens[fieldIdx + 4].Value != "=")
                continue;
            if (!int.TryParse(tokens[fieldIdx + 2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot))
                continue;

            var rhs = tokens[fieldIdx + 5];
            if (field == "object" && rhs.Kind == 'i'
                && rhs.Value is not ("true" or "false" or "undefined" or "noone" or "self" or "other" or "all"))
            {
                objects[slot] = rhs.Value;
            }
            else if (field == "name" && rhs.Kind == 's' && rhs.Value.Length > 0)
            {
                names[slot] = rhs.Value;
            }
        }

        if (objects.Count > 0)
        {
            return objects.ToDictionary(
                pair => pair.Key,
                pair => new TableItem(pair.Key, pair.Value, names.GetValueOrDefault(pair.Key) ?? pair.Value));
        }

        return names.ToDictionary(
            pair => pair.Key,
            pair => new TableItem(pair.Key, pair.Value, pair.Value));
    }

    private static bool TryRewriteMod(
        TableDescriptor table,
        ModPatch patch,
        IReadOnlyDictionary<int, TableItem> baseItems,
        int oldSlot,
        int newSlot,
        out (ModPatch Patch, int Count) result,
        out string warning)
    {
        result = (patch, 0);
        warning = "";
        var code = patch.Changes.GetValueOrDefault("code");
        if (code is null) { warning = "the patch has no code changes to inspect"; return false; }

        List<(AssetPatch Asset, string Text, int Count)> updates = [];
        foreach (var asset in code.Added.Concat(code.Changed))
        {
            if (asset.ModCode is not { Format: "gml" } gml) { warning = $"'{asset.Name}' is not available as GML"; return false; }
            string text = LineDiff.Normalize(gml.Text);
            string? baseText = asset.BaseCode is { Format: "gml" } baseGml ? LineDiff.Normalize(baseGml.Text) : null;
            int placeholderCount = 0;
            if (asset.Name == table.CreateEntry && baseText is not null)
            {
                text = RelocateCreatePlaceholders(table, baseText, text, baseItems, oldSlot, newSlot, out placeholderCount);
            }

            HashSet<int>? changedLines = baseText is null ? null : CollectChangedLines(baseText, text);
            string rewritten = RewriteTableReferences(table, text, asset.Name, changedLines, oldSlot, newSlot, out int count, out bool uncertain);
            if (uncertain) { warning = $"'{asset.Name}' contains a {table.LowerKind}-typed use that cannot be classified"; return false; }

            if (asset.Name == table.CreateEntry)
                rewritten = BumpCreateCountAssignment(table, rewritten, newSlot + 1);

            int totalCount = count + placeholderCount;
            if (totalCount > 0 || !string.Equals(rewritten, text, StringComparison.Ordinal))
                updates.Add((asset, rewritten, totalCount));
        }

        if (updates.Sum(u => u.Count) == 0) { warning = "no typed references to the claimed slot were found"; return false; }

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

    private static string RelocateCreatePlaceholders(
        TableDescriptor table,
        string baseText,
        string modText,
        IReadOnlyDictionary<int, TableItem> baseItems,
        int oldSlot,
        int newSlot,
        out int movedCount)
    {
        movedCount = 0;
        if (baseItems.ContainsKey(oldSlot)) return modText;

        var baseLines = SplitLines(baseText);
        var modLines = SplitLines(modText);
        string prefixPattern = $@"^\s*(?:{Regex.Escape(table.TableObject)}\.)?unlocked\s*\[\s*";
        var oldPlaceholderRegex = new Regex(prefixPattern + oldSlot.ToString(CultureInfo.InvariantCulture) + @"\s*\]\s*=\s*-\d+\s*;?\s*$", RegexOptions.CultureInvariant);
        string? basePlaceholder = baseLines.FirstOrDefault(l => oldPlaceholderRegex.IsMatch(l));
        if (basePlaceholder is null) return modText;

        // Only move if the mod actually replaced the base placeholder line in-place (not an untouched base line).
        var changedLines = CollectChangedLines(baseText, modText);
        var oldAssignRegex = new Regex(prefixPattern + oldSlot.ToString(CultureInfo.InvariantCulture) + @"\s*\]\s*=[^=]", RegexOptions.CultureInvariant);
        int oldIdx = -1;
        for (int i = 0; i < modLines.Count; i++)
        {
            if (changedLines.Contains(i + 1) && oldAssignRegex.IsMatch(modLines[i]))
            {
                oldIdx = i;
                break;
            }
        }
        if (oldIdx < 0) return modText;
        // If an untouched base placeholder for oldSlot still exists, this was an appended write rather than an in-place edit.
        for (int i = 0; i < modLines.Count; i++)
        {
            if (!changedLines.Contains(i + 1) && oldPlaceholderRegex.IsMatch(modLines[i]))
                return modText;
        }

        string movedLine = new Regex(@"(\[\s*)" + oldSlot.ToString(CultureInfo.InvariantCulture) + @"(\s*\])", RegexOptions.CultureInvariant)
            .Replace(modLines[oldIdx], $"${{1}}{newSlot}${{2}}", 1);
        modLines[oldIdx] = basePlaceholder;

        var newPlaceholderRegex = new Regex(prefixPattern + newSlot.ToString(CultureInfo.InvariantCulture) + @"\s*\]\s*=\s*-\d+\s*;?\s*$", RegexOptions.CultureInvariant);
        int newIdx = -1;
        if (!baseItems.ContainsKey(newSlot))
        {
            for (int i = 0; i < modLines.Count; i++)
            {
                if (!changedLines.Contains(i + 1) && newPlaceholderRegex.IsMatch(modLines[i]))
                {
                    newIdx = i;
                    break;
                }
            }
        }

        if (newIdx >= 0)
        {
            modLines[newIdx] = movedLine;
        }
        else
        {
            var anyUnlockedRegex = new Regex(prefixPattern + @"\d+\s*\]\s*=[^=]", RegexOptions.CultureInvariant);
            int lastUnlocked = modLines.FindLastIndex(l => anyUnlockedRegex.IsMatch(l));
            modLines.Insert(lastUnlocked >= 0 ? lastUnlocked + 1 : oldIdx + 1, movedLine);
        }

        movedCount = 1;
        return string.Concat(modLines);
    }

    private static string BumpCreateCountAssignment(TableDescriptor table, string text, int minCount)
    {
        var tokens = GmlReferences.Tokens(text);
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            int countIdx = i;
            if (tokens[i].Kind == 'i' && tokens[i].Value == table.TableObject && i + 1 < tokens.Count && tokens[i + 1].Value == ".")
                countIdx = i + 2;
            else if (i > 0 && tokens[i - 1].Value == ".")
                continue;

            if (countIdx + 2 >= tokens.Count) continue;
            if (tokens[countIdx].Kind != 'i' || tokens[countIdx].Value != "nItems") continue;
            if (tokens[countIdx + 1].Value != "=" || tokens[countIdx + 2].Kind != 'n') continue;
            if (countIdx + 3 < tokens.Count && tokens[countIdx + 3].Value is not (";" or "\n") && tokens[countIdx + 3].Line == tokens[countIdx + 2].Line)
                continue;
            if (!int.TryParse(tokens[countIdx + 2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int current) || current >= minCount)
                continue;
            return text.Remove(tokens[countIdx + 2].Start, tokens[countIdx + 2].Length)
                .Insert(tokens[countIdx + 2].Start, minCount.ToString(CultureInfo.InvariantCulture));
        }
        return text;
    }

    private static HashSet<int> CollectChangedLines(string baseText, string modText)
    {
        HashSet<int> changed = [];
        int line = 1;
        foreach (var edit in LineDiff.Compare(baseText, modText))
        {
            if (edit.Kind == '+') changed.Add(line);
            if (edit.Kind != '-') line++;
        }
        return changed;
    }

    private static string RewriteTableReferences(
        TableDescriptor table,
        string text,
        string entry,
        HashSet<int>? changedLines,
        int oldSlot,
        int newSlot,
        out int count,
        out bool uncertain)
    {
        count = 0;
        uncertain = false;
        var tokens = GmlReferences.Tokens(text);
        var edits = new Dictionary<(int Start, int Length), string>();
        string literal = oldSlot.ToString(CultureInfo.InvariantCulture);
        string replacement = newSlot.ToString(CultureInfo.InvariantCulture);

        ComputeScopes(table, tokens, entry, out bool[] inTableScope, out bool[] inCardScope);

        bool LineOwned(GmlReferences.Token token) => changedLines is null || changedLines.Contains(token.Line);

        // 1. Array indexing: TableObject.field[N], local field[N] in TableObject scope, and dedicated slot-indexed arrays.
        for (int i = 1; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Value != "[" || tokens[i + 1].Kind != 'n' || tokens[i + 1].Value != literal || tokens[i + 2].Value != "]")
                continue;
            if (!LineOwned(tokens[i + 1]) || tokens[i - 1].Kind != 'i')
                continue;

            bool qualified = i >= 3 && tokens[i - 2].Value == "." && tokens[i - 3].Value == table.TableObject
                && (i == 3 || tokens[i - 4].Value != ".");
            bool localTable = inTableScope[i] && (i < 2 || tokens[i - 2].Value != ".");
            bool indexedArray = table.IndexedArrays.Contains(tokens[i - 1].Value, StringComparer.Ordinal);
            if (!qualified && !localTable && !indexedArray)
                continue;

            edits[(tokens[i + 1].Start, tokens[i + 1].Length)] = replacement;
        }

        // 2. Typed call sinks where arg 0 is the item slot (e.g. buy_lure(N), lure_was_purchased(N), tool_was_purchased(N)).
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || (i > 0 && tokens[i - 1].Value == ".")) continue;
            if (!table.CallSinks.Contains(tokens[i].Value, StringComparer.Ordinal)) continue;
            if (tokens[i + 1].Value != "(" || tokens[i + 2].Kind != 'n' || tokens[i + 2].Value != literal
                || tokens[i + 3].Value is not ("," or ")"))
                continue;
            if (!LineOwned(tokens[i + 2])) continue;
            edits[(tokens[i + 2].Start, tokens[i + 2].Length)] = replacement;
        }

        // 3. Explicit scalar selection/index variables.
        bool isLureQuestEntry = table == LureTable && entry.StartsWith("gml_Object_LureQuest_", StringComparison.Ordinal);
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i') continue;
            bool knownVar = table.IndexVariables.Contains(tokens[i].Value, StringComparer.Ordinal)
                || (tokens[i].Value == "item_id" && inCardScope[i])
                || (table == LureTable && tokens[i].Value is "purchased" or "type"
                    && (isLureQuestEntry || (i >= 2 && tokens[i - 1].Value == "." && tokens[i - 2].Value == "LureQuest")));
            if (!knownVar) continue;
            if (tokens[i + 1].Value is not ("=" or "==" or "!=" or "<" or ">")) continue;
            if (tokens[i + 2].Kind != 'n' || tokens[i + 2].Value != literal || !LineOwned(tokens[i + 2])) continue;
            if (i + 3 < tokens.Count && tokens[i + 3].Value is "+" or "-" or "*" or "/" or "div" or "mod") continue;
            edits[(tokens[i + 2].Start, tokens[i + 2].Length)] = replacement;
        }

        // 4. Fail closed on any remaining slot literal in table-keyword context.
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'n' || tokens[i].Value != literal
                || !LineOwned(tokens[i])
                || edits.ContainsKey((tokens[i].Start, tokens[i].Length)))
                continue;
            int from = Math.Max(0, i - 5), to = Math.Min(tokens.Count - 1, i + 5);
            bool context = Enumerable.Range(from, to - from + 1)
                .Any(j => tokens[j].Kind == 'i' && tokens[j].Value.Contains(table.ContextKeyword, StringComparison.OrdinalIgnoreCase));
            if (context) { uncertain = true; return text; }
        }

        foreach (var edit in edits.OrderByDescending(e => e.Key.Start))
            text = text.Remove(edit.Key.Start, edit.Key.Length).Insert(edit.Key.Start, edit.Value);
        count = edits.Count;
        return text;
    }

    private static void ComputeScopes(
        TableDescriptor table,
        IReadOnlyList<GmlReferences.Token> tokens,
        string entry,
        out bool[] inTableScope,
        out bool[] inCardScope)
    {
        bool baseTable = entry.StartsWith(table.LocalEventPrefix, StringComparison.Ordinal);
        bool baseCard = table.ScopedItemIdPrefixes.Any(p => entry.StartsWith(p, StringComparison.Ordinal));
        inTableScope = Enumerable.Repeat(baseTable, tokens.Count).ToArray();
        inCardScope = Enumerable.Repeat(baseCard, tokens.Count).ToArray();

        var matchingParen = MatchDelimiters(tokens, "(", ")");
        var matchingBrace = MatchDelimiters(tokens, "{", "}");
        string cardObj = $"{table.Kind}Card";
        string menuObj = $"{table.Kind}Menu";

        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != "with" || tokens[i + 1].Value != "(") continue;
            if (!matchingParen.TryGetValue(i + 1, out int closeParen) || closeParen + 1 >= tokens.Count) continue;
            string? target = closeParen == i + 3 && tokens[i + 2].Kind == 'i' ? tokens[i + 2].Value : null;

            int end = closeParen + 1;
            if (tokens[end].Value == "{")
            {
                if (!matchingBrace.TryGetValue(end, out end)) continue;
            }
            else
            {
                while (end < tokens.Count && tokens[end].Value != ";") end++;
            }

            bool targetIsTable = target == table.TableObject;
            bool targetIsCard = target == table.TableObject || target == cardObj || target == menuObj;
            for (int j = closeParen + 1; j <= end && j < tokens.Count; j++)
            {
                inTableScope[j] = targetIsTable;
                inCardScope[j] = targetIsCard;
            }
        }
    }

    private static Dictionary<int, int> MatchDelimiters(IReadOnlyList<GmlReferences.Token> tokens, string open, string close)
    {
        Dictionary<int, int> map = [];
        Stack<int> stack = new();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Value == open) stack.Push(i);
            else if (tokens[i].Value == close && stack.TryPop(out int start)) map[start] = i;
        }
        return map;
    }

    /// <summary>
    /// Returns merged text for a conflicting BoatData, LureData, ToolData, or UpgradeData hunk whose versions differ
    /// only by writes to disjoint table slots, table count assignments, or upgrade count bounds, or null when the
    /// hunk needs a real decision.
    /// </summary>
    internal static string? SlotUnion(string asset, MergeHunk hunk)
    {
        if (hunk.Status != "CONFLICT" || hunk.Versions.Count < 2) return null;
        if (Tables.Any(t => asset.Contains(t.LocalEventPrefix, StringComparison.Ordinal)))
        {
            if (hunk.Kind == "list" && hunk.KeepBoth is not null) return hunk.KeepBoth;
            return TryUnionDisjointSlots(hunk);
        }
        return LegacyUpgradeRelocation.UpgradeUnion(asset, hunk);
    }

    /// <summary>
    /// Unions versions whose non-shared lines are 1D table slot assignments to disjoint numeric indices
    /// (plus an optional literal <c>nItems</c> count assignment, merged to the maximum).
    /// </summary>
    internal static string? TryUnionDisjointSlots(MergeHunk hunk)
    {
        var lines = hunk.Versions.Select(v => SplitLines(v.Text)).ToArray();
        var common = lines.Skip(1).Aggregate(new HashSet<string>(lines[0].Select(l => l.Trim()), StringComparer.Ordinal),
            (set, next) => { set.IntersectWith(next.Select(l => l.Trim())); return set; });
        var claimed = new HashSet<int>();
        var unique = new List<string>[lines.Length];
        string? bestCountLine = null;
        int bestCount = int.MinValue;
        string? firstCountLine = null;

        for (int v = 0; v < lines.Length; v++)
        {
            unique[v] = [];
            var slots = new HashSet<int>();
            bool seenCountInVersion = false;
            foreach (string line in lines[v].Where(l => !common.Contains(l.Trim())))
            {
                if (line.Trim().Length == 0)
                {
                    unique[v].Add(line);
                    continue;
                }
                var countMatch = CountAssignment().Match(line);
                if (countMatch.Success)
                {
                    if (seenCountInVersion || !int.TryParse(countMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                        return null;
                    seenCountInVersion = true;
                    if (v == 0) firstCountLine = line;
                    if (n > bestCount) { bestCount = n; bestCountLine = line; }
                    continue;
                }
                var match = SlotWrite().Match(line);
                if (!match.Success) return null;
                slots.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
                unique[v].Add(line);
            }
            if (slots.Overlaps(claimed)) return null;
            claimed.UnionWith(slots);
        }

        // Insert the other versions' slot writes right after the first version's own unique lines, so shared trailing
        // lines such as an nItems computation stay after every slot write.
        var result = new List<string>(lines[0]);
        if (firstCountLine is not null)
        {
            int countIdx = result.FindIndex(l => string.Equals(l, firstCountLine, StringComparison.Ordinal));
            if (countIdx >= 0) result.RemoveAt(countIdx);
        }
        int anchor = unique[0].Count == 0 ? -1 : result.FindLastIndex(l => !common.Contains(l.Trim()));
        if (anchor < 0) anchor = result.FindIndex(l => l.Contains("nItems", StringComparison.Ordinal)) - 1;
        if (anchor < -1) anchor = result.Count - 1;
        var extraSlots = unique.Skip(1).SelectMany(u => u).ToList();
        result.InsertRange(anchor + 1, extraSlots);
        if (bestCountLine is not null)
            result.Insert(anchor + 1 + extraSlots.Count, bestCountLine);
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

    [GeneratedRegex(@"^\s*(?:BoatData\.|LureData\.|ToolData\.|RodData\.|UpgradeData\.|Rod\.)?[A-Za-z_][A-Za-z0-9_]*\s*\[\s*(\d+)\s*\]\s*=[^=]", RegexOptions.CultureInvariant)]
    private static partial Regex SlotWrite();

    [GeneratedRegex(@"^\s*(?:BoatData\.|LureData\.|ToolData\.|RodData\.)?nItems\s*=\s*(\d+)\s*;?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CountAssignment();

    private static string Name(ModPatch patch) => patch.Mod.Name ?? Path.GetFileNameWithoutExtension(patch.Mod.FileName);
}
