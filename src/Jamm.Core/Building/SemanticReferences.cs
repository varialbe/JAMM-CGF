using System.Globalization;
using System.Text;
using Jamm.Core.Merging;
using UndertaleModLib;

namespace Jamm.Core.Building;

/// <summary>An asset identity at a code location, independent of its archive index.</summary>
public sealed record ResolvedCodeReference(string Container, string Family, string Name);

/// <summary>Lexical reference checks complementing Underanalyzer's typed asset-name emission.</summary>
public static class SemanticReferences
{
    internal static Dictionary<string, string[]> Names(UndertaleData data) => ResourceCatalog.Tables(data)
        .Where(t => CgfAssetTypes.IndexedFamilies.Contains(t.Key, StringComparer.Ordinal))
        .ToDictionary(t => t.Key, t => t.Value.Cast<object>().Select(ResourceCatalog.Name).ToArray(), StringComparer.Ordinal);

    /// <summary>Resolves literals in audited typed containers and asset-typed call arguments; other numbers are untouched.</summary>
    public static string NameTypedLiterals(string text, string entry, IReadOnlyDictionary<string, string[]> tables)
    {
        var tokens = GmlReferences.Tokens(text);
        List<(int Start, int Length, string Name)> replacements = [];
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == 'i' && i + 1 < tokens.Count && tokens[i + 1].Value == "("
                && (i == 0 || tokens[i - 1].Value != "."))
            {
                var arguments = Arguments(i + 2);
                string[] values = arguments.Select(a => string.Concat(tokens.Skip(a.Start).Take(a.End - a.Start).Select(t => t.Value))).ToArray();
                var typed = BuiltinAssetArguments.ForCall(tokens[i].Value, values).ToList();
                for (int argument = 0; argument < arguments.Count; argument++)
                    if (CgfAssetTypes.ArgumentFamily(tokens[i].Value, argument) is { } configuredFamily) typed.Add((argument, configuredFamily));
                foreach (var (argument, argumentFamily) in typed.Distinct())
                {
                    var (start, end) = arguments[argument];
                    // Only a complete literal is an index; expressions and negative sentinels stay intact.
                    while (end - start >= 3 && tokens[start].Value == "(" && tokens[end - 1].Value == ")") { start++; end--; }
                    if (end - start == 2 && tokens[start].Value == "+") start++;
                    if (end - start != 1 || tokens[start].Kind != 'n') continue;
                    if (!Number(tokens[start].Value, out decimal argumentIndex) || argumentIndex != decimal.Truncate(argumentIndex)
                        || !tables.TryGetValue(argumentFamily, out var argumentNames) || argumentIndex >= argumentNames.Length)
                        throw new InvalidDataException($"Code '{entry}', function '{tokens[i].Value}', argument {argument + 1}: invalid {argumentFamily} index {tokens[start].Value}. Fix the source mod.");
                    replacements.Add((tokens[start].Start, tokens[start].Length, AssetSymbols.SafeName(argumentFamily, argumentNames[(int)argumentIndex])));
                }
            }
            if (tokens[i].Kind != 'i' || CgfAssetTypes.Family(tokens[i].Value, entry) is not { } family) continue;
            int j = i + 1;
            while (j < tokens.Count && tokens[j].Value == "[")
            {
                int depth = 1; j++;
                while (j < tokens.Count && depth > 0)
                { if (tokens[j].Value == "[") depth++; if (tokens[j].Value == "]") depth--; j++; }
            }
            if (j >= tokens.Count || tokens[j].Value is not ("=" or "==" or "!=" or "<" or ">" or "<=" or ">=")) continue;
            j++;
            if (j >= tokens.Count || tokens[j].Kind != 'n' || !int.TryParse(tokens[j].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) continue;
            // Expressions such as object[i] = 100 + offset cannot be remapped safely.
            if (j + 1 < tokens.Count && tokens[j + 1].Value is not (";" or ")" or "," or "}" or "&&" or "||")) continue;
            if (!tables.TryGetValue(family, out var names) || index >= names.Length)
                throw new InvalidDataException($"Code '{entry}', container '{tokens[i].Value}': invalid {family} index {index}. Fix this reference in the source mod.");
            replacements.Add((tokens[j].Start, tokens[j].Length, AssetSymbols.SafeName(family, names[index])));
        }
        StringBuilder result = new(text);
        foreach (var r in replacements.Distinct().OrderByDescending(r => r.Start)) result.Remove(r.Start, r.Length).Insert(r.Start, r.Name);
        return result.ToString();

        List<(int Start, int End)> Arguments(int start)
        {
            List<(int Start, int End)> result = [];
            int depth = 0, first = start;
            for (int j = start; j < tokens.Count; j++)
            {
                if (tokens[j].Kind != 'p') continue;
                string value = tokens[j].Value;
                if (depth == 0 && value is "," or ")")
                {
                    if (j > first) result.Add((first, j));
                    if (value == ")") return result;
                    first = j + 1;
                }
                else if (value is "(" or "[" or "{") depth++;
                else if (value is ")" or "]" or "}") depth--;
            }
            return []; // Incomplete calls are left to the compiler.
        }
    }

    private static bool Number(string value, out decimal number)
    {
        int prefix = value.StartsWith('$') ? 1 : value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        if (prefix != 0)
        {
            bool valid = ulong.TryParse(value.AsSpan(prefix), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex);
            number = hex;
            return valid;
        }
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Resolves named references and direct numeric typed-container references to family/name identities.</summary>
    public static IReadOnlyList<ResolvedCodeReference> Resolve(string text, string entry, IReadOnlyDictionary<string, string[]> tables)
    {
        text = NameTypedLiterals(text, entry, tables);
        var lookup = tables.SelectMany(t => t.Value.Select(n => (Name: n, Family: t.Key)))
            .GroupBy(x => x.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(x => x.Family).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var tokens = GmlReferences.Tokens(text);
        List<ResolvedCodeReference> result = [];
        string container = "named reference";
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Value is ";" or "{") container = "named reference";
            if (token.Kind != 'i') continue;
            if (CgfAssetTypes.Family(token.Value, entry) is not null || CgfAssetTypes.ArgumentFamily(token.Value, 0) is not null) container = token.Value;
            if (i > 0 && tokens[i - 1].Value == ".") continue; // member names are not global asset symbols
            if (AssetSymbols.Parse(token.Value, out string symbolFamily, out string symbolName))
            { result.Add(new(container, symbolFamily, symbolName)); continue; }
            if (lookup.TryGetValue(token.Value, out var families))
            {
                string? typed = CgfAssetTypes.Family(container, entry);
                foreach (string family in families.Where(f => families.Length == 1 || typed is null || f == typed)) result.Add(new(container, family, token.Value));
            }
        }
        return result;
    }

    /// <summary>Fails with the code entry, container and both identities when selected references change meaning.</summary>
    public static void Verify(string entry, IReadOnlyList<ResolvedCodeReference> expected, IReadOnlyList<ResolvedCodeReference> actual)
    {
        for (int i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            var e = i < expected.Count ? expected[i] : null;
            var a = i < actual.Count ? actual[i] : null;
            if (e?.Family == a?.Family && e?.Name == a?.Name) continue;
            throw new InvalidDataException($"Semantic reference verification failed in code '{entry}', container '{e?.Container ?? a!.Container}': expected {e?.Family}/{e?.Name ?? "<absent>"}, actual {a?.Family}/{a?.Name ?? "<absent>"}. Correct the source reference or conflict decision; no output was published.");
        }
    }
}
