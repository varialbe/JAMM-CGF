using System.Globalization;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using Token = Jamm.Core.Merging.GmlReferences.Token;

namespace Jamm.Core.Building;

/// <summary>Finds evidence for additions to the legacy boat and rod tables.</summary>
internal static class LegacyItemUnlocks
{
    internal const string SaveLoad = "gml_Object_SaveManager_Other_13";
    private const string Cards = "gml_Object_CardHandler_Alarm_0";

    internal sealed record Finding(string Kind, string Table, string Create, LegacySlotRelocation.TableItem Item,
        string Status, string Evidence, IReadOnlyList<string> Sources)
    {
        internal ConflictItem ToConflict(string mod)
        {
            string label = $"{mod}: {Kind} '{Item.DisplayName}' (slot {Item.Slot})";
            string code = Kind == "boat" ? "CGF-UNLOCK-001" : "CGF-UNLOCK-002";
            string message = Status switch
            {
                "missing" => $"[{code}] {label} has no unlock path (not buyable, no quest, never unlocked in code). {Evidence}",
                "reachable" => $"{label} has an unlock path. {Evidence}",
                _ => $"{label} has an unverified unlock path. {Evidence}"
            };
            return new(Status == "missing" ? "WARNING" : "INFO", "unlock", Kind, $"{Item.DisplayName} [{Item.Slot}]",
                [mod], message, Sources: Sources);
        }
    }

    internal sealed record TableState(IReadOnlyDictionary<int, double> Prices, IReadOnlyDictionary<int, double> Defaults,
        int? Count, bool CountFromDefaults, IReadOnlySet<int> ComputedSlots);

    private sealed record Write(string Field, int? Slot, IReadOnlyList<Token> Value, int Line);

    internal static IEnumerable<Finding> Analyze(ModPatch patch)
    {
        var candidates = LegacySlotRelocation.UnlockCandidates(patch).ToArray();
        if (candidates.Length == 0) yield break;
        if (patch.Changes.GetValueOrDefault("code") is not { } code) yield break;

        var entries = code.Added.Concat(code.Changed).ToDictionary(a => a.Name, StringComparer.Ordinal);
        var tokenCache = new Dictionary<string, List<Token>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var (kind, table, create, item) = candidate;
            var state = ReadTable(Tokens(entries[create]), table, create);
            string click = ClickEntry(kind);
            bool knownShop = ShopAvailable(table, EntryState(click), EntryState(Cards),
                WindowCanShow(entries, code.Removed, kind));
            int slot = item.Slot;
            double? price = state.Prices.TryGetValue(slot, out double amount) ? amount : null;
            double? unlocked = state.Defaults.TryGetValue(slot, out double initial) ? initial : null;
            bool? inMenu = state.Count is int count ? slot < count : null;
            string? uncertainty = null;
            string? uncertainEntry = null;

            if (unlocked >= 1 && inMenu == true)
            {
                yield return Result("reachable", $"Starts owned in {table}.", create);
                continue;
            }
            if (unlocked == 0 && price >= 0 && inMenu == true && knownShop)
            {
                yield return Result("reachable", $"{table} exposes it for {FormatPrice(price.Value)} through the item card purchase event.", create, Cards, click);
                continue;
            }

            // The stock purchase event can reveal the next two -1 rows. Boats exclude the final row.
            if (unlocked == -1 && price >= 0 && knownShop && EntryState(click) is null
                && state.Count is int limit && slot < limit - (kind == "boat" ? 1 : 0)
                && HasPurchasablePredecessor(state, slot))
            {
                yield return Result("reachable", "Purchasing an earlier item reveals the next shop cards.", create, click);
                continue;
            }

            string? legacyWriter = inMenu == true && EntryState(SaveLoad) is null
                ? FindFlagWriter(("Unlocks", $"{kind}{slot}")) : null;
            if (legacyWriter is not null)
            {
                yield return Result("reachable", $"{legacyWriter} writes the ownership flag read by the legacy save loader.", legacyWriter, SaveLoad);
                continue;
            }

            Finding? grant = null;
            foreach (var entry in entries.Values.Where(e => e.Name != create && e.Name != click && e.Name != Cards))
            {
                if (entry.ModCode is not { Format: "gml" } changed)
                {
                    // We cannot prove absence when an executable change is unavailable as GML.
                    uncertainty ??= $"'{entry.Name}' could not be inspected as GML.";
                    uncertainEntry ??= entry.Name;
                    continue;
                }
                if (!changed.Text.Contains("unlocked", StringComparison.Ordinal)) continue;
                foreach (var write in Writes(Tokens(entry), table, entry.Name).Where(w => w.Field == "unlocked"))
                {
                    if (write.Slot is int target && target != slot) continue;
                    if (TryNumber(write.Value, out double value))
                    {
                        if (value < 0) continue;
                        if (write.Slot == slot && inMenu == true && (value >= 1 || price >= 0 && knownShop))
                        {
                            grant = Result("reachable", $"{entry.Name}:{write.Line} {(value >= 1 ? "grants ownership" : "reveals the priced shop card")}.", entry.Name);
                            break;
                        }
                    }
                    else if (IsIniRead(write.Value, out var key, out double fallback))
                    {
                        // Dynamic legacy reads mirror ownership. They are not independent unlock routes.
                        if (write.Slot != slot) continue;
                        string? writer = fallback > 0 || key is null ? null : FindFlagWriter(key.Value);
                        if (inMenu == true && (fallback > 0 || writer is not null))
                        {
                            grant = Result("reachable", $"{entry.Name}:{write.Line} grants ownership from a save flag.", writer is null ? [entry.Name] : [entry.Name, writer]);
                            break;
                        }
                        if (fallback < 0 && writer is null) continue;
                    }
                    uncertainty ??= $"{entry.Name}:{write.Line} writes a potentially relevant unlock state.";
                    uncertainEntry ??= entry.Name;
                }
                if (grant is not null) break;
            }
            if (grant is not null)
            {
                yield return grant;
                continue;
            }

            if (state.ComputedSlots.Contains(slot))
            {
                uncertainty ??= $"{table} computes the slot's price or unlock state at runtime.";
                uncertainEntry ??= create;
            }
            if (inMenu is null || (!knownShop && unlocked is 0 or -1))
            {
                uncertainty ??= inMenu is null ? $"{table}'s shop row count could not be established." : "The mod changes the shop code and its purchase rule could not be established.";
                uncertainEntry ??= inMenu is null ? create : click;
            }
            if (uncertainty is not null)
            {
                yield return Result("unknown", uncertainty, uncertainEntry!);
                continue;
            }

            string missing = inMenu == false ? $"{table}.nItems is {state.Count}, which excludes slot {slot}."
                : unlocked is < 0 ? $"{table} initializes the slot as hidden ({unlocked})."
                : price is null or < 0 ? $"{table} has no usable price for the slot."
                : $"{table} has no explicit available or owned state for the slot.";
            yield return Result("missing", missing, create, Cards, click);

            Finding Result(string status, string evidence, params string[] sources) =>
                new(kind, table, create, item, status, evidence, sources);
        }

        List<Token> Tokens(AssetPatch entry)
        {
            if (!tokenCache.TryGetValue(entry.Name, out var tokens))
                tokenCache.Add(entry.Name, tokens = GmlReferences.Tokens(entry.ModCode!.Text));
            return tokens;
        }

        // Null indicates the stock event is inherited. Empty text marks removed or unavailable code.
        string? EntryState(string name) => entries.TryGetValue(name, out var entry)
            ? entry.ModCode is { Format: "gml" } gml ? gml.Text : ""
            : code.Removed.Contains(name) ? "" : null;

        string? FindFlagWriter((string Section, string Key) flag) =>
            entries.Values.FirstOrDefault(entry => entry.ModCode is { Format: "gml" } gml
                && gml.Text.Contains("ini_write_real", StringComparison.Ordinal)
                && WritesSaveFlag(Tokens(entry), flag))?.Name;
    }

    internal static string ClickEntry(string kind) =>
        kind == "boat" ? "gml_Object_BoatCard_Other_10" : "gml_Object_RodCard_Other_10";

    internal static bool ShopAvailable(string table, string? click, string? cards, bool canShow = true) =>
        canShow && (click is null || ContainsCode(click, $"Girl.dollars >= {table}.cost[item_id]")
            && ContainsCode(click, $"{table}.unlocked[item_id] = 1"))
        && (cards is null || ContainsCode(cards, "iCard < data_object.nItems")
            && ContainsCode(cards, "data_object.unlocked[iCard] < 0"));

    private static bool WindowCanShow(Dictionary<string, AssetPatch> entries, IReadOnlyList<string> removed, string kind)
    {
        string menu = kind == "boat" ? "gml_Object_BoatMenu_Create_0" : "gml_Object_RodMenu_Create_0";
        string table = kind == "boat" ? "BoatData" : "RodData";
        return !removed.Contains(menu) && (!entries.TryGetValue(menu, out var change)
            || change.ModCode is { Format: "gml" } gml && ContainsCode(gml.Text, $"data_object = {table}"));
    }

    internal static TableState ReadTable(string text, string table, string entry) =>
        ReadTable(GmlReferences.Tokens(text), table, entry);

    private static TableState ReadTable(List<Token> tokens, string table, string entry)
    {
        Dictionary<int, double> prices = [], defaults = [];
        HashSet<(string Field, int Slot)> computed = [];
        int? count = null;
        bool countFromDefaults = false;
        foreach (var write in Writes(tokens, table, entry))
        {
            if (write.Field == "nItems")
            {
                count = TryNumber(write.Value, out double length) && length == Math.Floor(length) && length is >= 0 and <= int.MaxValue
                    ? (int)length : null;
                countFromDefaults = Matches(write.Value, "array_length_1d", "(", "unlocked", ")")
                    || Matches(write.Value, "array_length_1d", "(", table, ".", "unlocked", ")");
                continue;
            }
            if (write.Field is not ("cost" or "unlocked") || write.Slot is not int slot) continue;
            var field = write.Field == "cost" ? prices : defaults;
            if (TryNumber(write.Value, out double value))
            {
                field[slot] = value;
                computed.Remove((write.Field, slot));
            }
            else
            {
                field.Remove(slot);
                computed.Add((write.Field, slot));
            }
        }
        if (countFromDefaults)
        {
            int last = defaults.Keys.Concat(computed.Where(w => w.Field == "unlocked").Select(w => w.Slot)).DefaultIfEmpty(-1).Max();
            if (last >= 0) count = last + 1;
        }
        return new(prices, defaults, count, countFromDefaults, computed.Select(w => w.Slot).ToHashSet());
    }

    private static bool HasPurchasablePredecessor(TableState state, int slot)
    {
        int previous = -3;
        foreach (int i in state.Prices.Keys.Where(i => i < slot).Order())
        {
            if (state.Prices[i] < 0 || !state.Defaults.TryGetValue(i, out double unlocked)) continue;
            if (unlocked == 0 || unlocked == -1 && i - (long)previous <= 2)
                previous = i;
        }
        return slot - (long)previous <= 2;
    }

    private static IEnumerable<Write> Writes(IReadOnlyList<Token> tokens, string table, string entry)
    {
        bool localTable = entry.StartsWith($"gml_Object_{table}_", StringComparison.Ordinal);
        Stack<bool> localScopes = new();
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].Value == "{" && tokens[i].Kind == 'p')
            {
                localScopes.Push(At(i - 4, "with") && At(i - 3, "(") && At(i - 1, ")")
                    ? At(i - 2, table) : localTable);
                localTable = localScopes.Peek();
                continue;
            }
            if (tokens[i].Value == "}" && tokens[i].Kind == 'p' && localScopes.Count > 0)
            {
                localScopes.Pop();
                localTable = localScopes.TryPeek(out bool scope) ? scope : entry.StartsWith($"gml_Object_{table}_", StringComparison.Ordinal);
                continue;
            }
            string field = tokens[i].Value;
            if (tokens[i].Kind != 'i' || field is not ("unlocked" or "cost" or "nItems")) continue;
            bool qualified = At(i - 2, table) && At(i - 1, ".");
            if (!qualified && (!localTable || At(i - 1, "."))) continue;
            int op = i + 1;
            int? slot = null;
            if (field != "nItems")
            {
                if (!At(op++, "[")) continue;
                if (At(op + 1, "]") && tokens[op].Kind == 'n'
                    && int.TryParse(tokens[op].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int literal)
                    && literal < int.MaxValue)
                    slot = literal;
                while (op < tokens.Count && tokens[op].Value is not ("]" or ";" or "}")) op++;
                if (!At(op++, "]")) continue;
            }
            if (!At(op++, "=")) continue;
            int end = op, depth = 0;
            for (; end < tokens.Count; end++)
            {
                var token = tokens[end];
                if (token.Kind != 'p') continue;
                if (depth == 0 && token.Value is ";" or "}") break;
                if (token.Value is "(" or "[") depth++;
                if (token.Value is ")" or "]") depth--;
            }
            yield return new(field, slot, tokens.Skip(op).Take(end - op).ToArray(), tokens[i].Line);
        }

        bool At(int index, string value) => index >= 0 && index < tokens.Count && tokens[index].Value == value;
    }

    private static bool TryNumber(IReadOnlyList<Token> tokens, out double value)
    {
        value = 0;
        if (tokens.Count == 1 && tokens[0].Kind == 'i')
        {
            value = tokens[0].Value == "true" ? 1 : 0;
            return tokens[0].Value is "true" or "false";
        }
        if (!(tokens.Count == 1 && tokens[0].Kind == 'n'
            || tokens.Count == 2 && tokens[0].Value == "-" && tokens[1].Kind == 'n')) return false;
        return double.TryParse(string.Concat(tokens.Select(t => t.Value)), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    private static bool IsIniRead(IReadOnlyList<Token> tokens, out (string Section, string Key)? key, out double fallback)
    {
        key = null;
        fallback = -1;
        if (tokens.Count < 8 || !Matches(tokens.Take(2).ToArray(), "ini_read_real", "(")) return false;
        if (tokens[2].Kind == 's' && tokens[4].Kind == 's' && tokens[5].Value == ",")
        {
            key = (tokens[2].Value, tokens[4].Value);
            if (TryNumber(tokens.Skip(6).Take(tokens.Count - 7).ToArray(), out double number)) fallback = number;
        }
        return true;
    }

    private static bool WritesSaveFlag(IReadOnlyList<Token> tokens, (string Section, string Key) flag)
    {
        for (int i = 0; i + 7 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != "ini_write_real"
                || tokens[i + 2].Kind != 's' || tokens[i + 4].Kind != 's') continue;
            if (Matches(tokens.Skip(i).Take(6).ToArray(), "ini_write_real", "(", flag.Section, ",", flag.Key, ",")
                && tokens[i + 7].Value == ")" && TryNumber([tokens[i + 6]], out double value) && value > 0)
                return true;
        }
        return false;
    }

    private static bool Matches(IReadOnlyList<Token> tokens, params string[] values) =>
        tokens.Select(t => t.Value).SequenceEqual(values, StringComparer.Ordinal);

    internal static bool ContainsCode(string text, string pattern)
    {
        var source = GmlReferences.Tokens(text);
        var expected = GmlReferences.Tokens(pattern).Select(t => (t.Kind, t.Value)).ToArray();
        return Enumerable.Range(0, Math.Max(0, source.Count - expected.Length + 1))
            .Any(i => expected.Select((token, j) => token == (source[i + j].Kind, source[i + j].Value)).All(t => t));
    }

    internal static string FormatPrice(double price) => "$" + price.ToString("#,0.######", CultureInfo.InvariantCulture);
}
