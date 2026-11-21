using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Patching;
using Jamm.ModMaker;
using UndertaleModLib;

namespace Jamm.Core.Building;

internal static partial class ConsoleUpgradeGuard
{
    private const string ReadScript = "jamm_rod_effect_at";
    private const string RodCreate = "gml_Object_Rod_Create_0";
    private const string RodRefresh = "gml_Object_Rod_Other_10";
    internal const string ConsoleStep = "gml_Object_" + ConsoleMod.ObjectName + "_Step_1";

    internal static void Apply(UndertaleData data, IReadOnlyDictionary<string, CodeText> baseline,
        Dictionary<string, string> recompile, Dictionary<string, string> generated, List<string> decisions)
    {
        var guarded = new List<string>();
        int knownLength = Math.Min(InitializedLength(RodCreate), InitializedLength(RodRefresh));
        if (knownLength == 0) knownLength = InitializedLength(RodRefresh);
        var entries = data.Code.Select(c => c.Name.Content).ToArray();
        foreach (string entry in entries)
        {
            string? text = Current(entry);
            if (text is null) continue;
            if (entry == ConsoleStep)
            {
                // Also update the legacy binary bundled before these loop bounds were fixed.
                string safe = ConsoleBounds().Replace(text, match => match.Groups[1].Value +
                    "min(" + match.Groups[2].Value + ", array_length_1d(" + match.Groups[3].Value + ".unlocked));");
                safe = ToolBounds().Replace(safe, "if ($1 < array_length_1d(ToolData.item)) $0");
                if (safe != text) Save(entry, safe);
                continue;
            }
            if (!text.Contains("upgrades", StringComparison.Ordinal)) continue;
            bool rod = entry.StartsWith("gml_Object_Rod_", StringComparison.Ordinal);
            var code = new GuardGml(text);
            var edits = new List<(int Start, int End, string Value)>();
            for (int i = 0; i < code.Tokens.Count; i++)
            {
                if (!code.Is(i, "upgrades") || !code.Is(i + 1, "[") || code.Close(i + 1) is not int close) continue;
                string? qualifier = code.Is(i - 1, ".") && code.Identifier(i - 2) ? code.Tokens[i - 2].Value : null;
                int target = qualifier is null ? i : i - 2;
                if (code.Is(close + 1, "=", "+=", "-=", "*=", "/=") || Increment(target - 2) || Increment(close + 1)) continue;
                if (code.Resolve(i, qualifier, rod ? "Rod" : null) != "Rod") continue;
                var index = new GuardGml.Span(i + 2, close);
                double? slot = code.Number(index);
                // Dynamic reads in existing vanilla code already have their own loop bounds.
                if (slot is null && (!recompile.ContainsKey(entry) || entry == RodRefresh)) continue;
                if (slot >= 0 && slot < knownLength) continue;
                if (edits.Any(edit => edit.End > code.Tokens[i].Start)) continue;
                int start = code.Tokens[target].Start;
                int end = code.End(new(i, close + 1));
                string receiver = qualifier is null or "self" or "id" ? "id" : qualifier;
                edits.Add((start, end, $"{ReadScript}({receiver}, {code.Source(index)})"));
                i = close;
            }
            if (edits.Count == 0) continue;
            StringBuilder updated = new(text);
            foreach (var (start, end, value) in edits.AsEnumerable().Reverse())
                updated.Remove(start, end - start).Insert(start, value);
            Save(entry, updated.ToString());
            guarded.Add(entry);

            bool Increment(int start) => code.Is(start, "+") && code.Is(start + 1, "+")
                || code.Is(start, "-") && code.Is(start + 1, "-");
        }

        if (guarded.Count == 0) return;
        ValueGuard.PutScript(data, ReadScript, """
            var jamm_effect;
            jamm_effect = 0;
            if (argument1 < 0 || floor(argument1) != argument1 || !instance_exists(argument0)) return 0;
            with (argument0)
            {
                if (variable_instance_exists(id, "upgrades"))
                {
                    if (argument1 < array_length_1d(upgrades)) jamm_effect = upgrades[argument1];
                }
            }
            return jamm_effect;
            """, recompile, generated);
        decisions.Add($"[CGF-CONSOLE-001] Guarded Rod upgrade effect reads in {string.Join(", ", guarded)}. Missing slots use effect 0.");

        string? Current(string entry) => recompile.GetValueOrDefault(entry) ?? baseline.GetValueOrDefault(entry)?.Text;
        void Save(string entry, string text) => generated[entry] = recompile[entry] = text;

        int InitializedLength(string entry)
        {
            if (Current(entry) is not { } text) return 0;
            // The unconditional initialization is the prefix before the first block.
            var gml = new GuardGml(text);
            int conditional = gml.Tokens.FindIndex(t => t.Kind == 'p' && t.Value == "{");
            var slots = gml.Writes().Where(w => w.Field == "upgrades" && w.Index is not null
                && (conditional < 0 || w.At < conditional)).Select(w => gml.Number(w.Index!.Value)).ToHashSet();
            int length = 0;
            while (slots.Contains(length)) length++;
            return length;
        }
    }

    [GeneratedRegex(@"(\bjamm_(?:rod|lure|boat|tool|hat)_total\s*=\s*)((RodData|LureData|BoatData|ToolData|GearData)\.(?:nItems|n_gear));")]
    private static partial Regex ConsoleBounds();

    [GeneratedRegex(@"if\s*\(\s*ToolData\.unlocked\[(jamm_i\d*)\]\s*==\s*1\s*&&\s*instance_exists\(ToolData\.item\[\1\]\)\s*\)")]
    private static partial Regex ToolBounds();
}
