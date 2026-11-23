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
        replacements.AddRange(NameTypedLocalLiterals(tokens, entry, tables));
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

    /// <summary>Names untyped integer literals in asset-named assignments when they identify an asset added by this mod.</summary>
    public static string NameAddedAssetLiterals(string text, string entry,
        IReadOnlyDictionary<string, string[]> baseTables, IReadOnlyDictionary<string, string[]> modTables,
        Action<string>? warning = null)
    {
        var tokens = GmlReferences.Tokens(text);
        List<(int Start, int Length, string Name)> replacements = [];
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'n' || !int.TryParse(tokens[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0) continue;
            string? familyHint = null;
            // Recognize assignments such as harbor_sprite[i] = 488. Only a complete literal is rewritten.
            int equals = i - 1;
            if (equals >= 0 && tokens[equals].Value == "=")
            {
                int lhs = equals - 1;
                while (lhs >= 0 && tokens[lhs].Value == "]")
                {
                    int depth = 1; lhs--;
                    while (lhs >= 0 && depth > 0) { if (tokens[lhs].Value == "]") depth++; if (tokens[lhs].Value == "[") depth--; lhs--; }
                }
                if (lhs >= 0 && tokens[lhs].Kind == 'i') familyHint = FamilyHint(tokens[lhs].Value);
            }
            bool assetLike = familyHint is not null || i >= 2 && tokens[i - 2].Kind == 'i'
                && tokens[i - 2].Value.Contains("asset", StringComparison.OrdinalIgnoreCase);
            if (!assetLike) continue;
            if (i + 1 < tokens.Count && tokens[i + 1].Value is not (";" or ")" or "," or "}")) continue;
            if (i > 0 && tokens[i - 1].Value == "-") continue;
            var candidates = modTables.Where(t => t.Value.Length > baseTables.GetValueOrDefault(t.Key, []).Length
                    && value >= baseTables.GetValueOrDefault(t.Key, []).Length && value < t.Value.Length)
                .Select(t => (Family: t.Key, Name: t.Value[value])).Where(x => familyHint is null || x.Family == familyHint).ToArray();
            if (candidates.Length == 1 && familyHint is not null)
                replacements.Add((tokens[i].Start, tokens[i].Length, AssetSymbols.SafeName(candidates[0].Family, candidates[0].Name)));
            else if (candidates.Length > 1)
                warning?.Invoke($"Code '{entry}' has ambiguous asset-like integer {value}; candidates: {string.Join(", ", candidates.Select(c => c.Family + "/" + c.Name))}.");
        }
        StringBuilder result = new(text);
        foreach (var replacement in replacements.Distinct().OrderByDescending(r => r.Start))
            result.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Name);
        return result.ToString();

        static string? FamilyHint(string identifier)
        {
            string value = identifier.ToLowerInvariant();
            if (value.Contains("sprite", StringComparison.Ordinal) || value.StartsWith("spr", StringComparison.Ordinal)) return "sprites";
            if (value.Contains("sound", StringComparison.Ordinal) || value.StartsWith("snd", StringComparison.Ordinal)) return "sounds";
            if (value.Contains("object", StringComparison.Ordinal) || value.StartsWith("obj", StringComparison.Ordinal)) return "objects";
            if (value.Contains("background", StringComparison.Ordinal) || value.StartsWith("bg", StringComparison.Ordinal)) return "backgrounds";
            return null;
        }
    }

    private static List<(int Start, int Length, string Name)> NameTypedLocalLiterals(
        IReadOnlyList<GmlReferences.Token> tokens, string entry, IReadOnlyDictionary<string, string[]> tables)
    {
        Dictionary<int, HashSet<string>> typedReads = [];
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i + 1].Value != "(" || (i > 0 && tokens[i - 1].Value == ".")) continue;
            var args = CallArguments(i + 2);
            string[] values = args.Select(a => string.Concat(tokens.Skip(a.Start).Take(a.End - a.Start).Select(t => t.Value))).ToArray();
            var typed = BuiltinAssetArguments.ForCall(tokens[i].Value, values).ToList();
            for (int a = 0; a < args.Count; a++)
                if (CgfAssetTypes.ArgumentFamily(tokens[i].Value, a) is { } family) typed.Add((a, family));
            foreach (var (argument, family) in typed.Distinct())
            {
                if (argument >= args.Count) continue;
                var (start, end) = args[argument];
                while (end - start >= 3 && tokens[start].Value == "(" && tokens[end - 1].Value == ")") { start++; end--; }
                if (end - start == 1 && tokens[start].Kind == 'i')
                {
                    if (!typedReads.TryGetValue(start, out var families)) typedReads[start] = families = new(StringComparer.Ordinal);
                    families.Add(family);
                }
            }
        }

        List<(int Start, int Length, string Name)> result = [];
        var declarations = Enumerable.Range(0, tokens.Count)
            .Where(i => tokens[i].Value == "var" && i + 1 < tokens.Count && tokens[i + 1].Kind == 'i')
            .Select(i => i + 1).GroupBy(i => tokens[i].Value, StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            string name = declaration.Key;
            int[] declaredAt = declaration.ToArray();
            if (declaredAt.Length != 1 || IsFunctionParameter(name)) continue; // Shadowed locals and parameters are ambiguous.
            int declarationIndex = declaredAt[0];
            List<(int Index, int ValueIndex)> writes = [];
            List<(int Index, string Family)> reads = [];
            bool safe = true;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != 'i' || tokens[i].Value != name || (i > 0 && tokens[i - 1].Value == ".")) continue;
                if (i < declarationIndex) { safe = false; continue; }
                if (i == declarationIndex)
                {
                    if (i + 1 < tokens.Count && tokens[i + 1].Value == "=") writes.Add((i, i + 2));
                    else if (i + 1 < tokens.Count && tokens[i + 1].Value is "+=" or "-=" or "*=" or "/=") safe = false;
                    else safe = false; // An uninitialized declaration leaves the selected value unknown.
                    continue;
                }
                if (i + 1 < tokens.Count && tokens[i + 1].Value == "=") { writes.Add((i, i + 2)); continue; }
                if (i + 1 < tokens.Count && tokens[i + 1].Value is "+=" or "-=" or "*=" or "/=" or "++" or "--") { safe = false; continue; }
                if (i > 0 && tokens[i - 1].Value is "++" or "--") { safe = false; continue; }
                if (typedReads.TryGetValue(i, out var families) && families.Count == 1) reads.Add((i, families.Single()));
                else safe = false; // Escapes, arithmetic, tests and other untyped uses are ambiguous.
            }
            if (!safe || reads.Count == 0 || reads.Select(r => r.Family).Distinct(StringComparer.Ordinal).Count() != 1) continue;
            string selectedFamily = reads[0].Family;
            if (!tables.TryGetValue(selectedFamily, out string[]? names)) continue;
            List<(int Start, int Length, string Name)> localReplacements = [];
            foreach (var (_, valueIndex) in writes)
            {
                if (valueIndex >= tokens.Count) { safe = false; break; }
                int valueEnd = valueIndex + 1;
                bool negative = tokens[valueIndex].Value == "-";
                if (negative) valueEnd++;
                if (valueIndex >= tokens.Count || (negative && valueEnd > tokens.Count)) { safe = false; break; }
                if (valueEnd < tokens.Count && tokens[valueEnd].Value is not (";" or "," or ")" or "}" or "break")) { safe = false; break; }
                var value = negative ? tokens[valueIndex + 1] : tokens[valueIndex];
                if (value.Kind == 'n' && Number((negative ? "-" : "") + value.Value, out decimal numeric)
                    && numeric == decimal.Truncate(numeric))
                {
                    if (numeric == -1) continue;
                    if (numeric < 0 || numeric >= names.Length) { safe = false; break; }
                    localReplacements.Add((value.Start, value.Length, AssetSymbols.SafeName(selectedFamily, names[(int)numeric])));
                }
                else if (!negative && value.Kind == 'i' && KnownName(value.Value, selectedFamily, names)) { }
                else { safe = false; break; }
            }
            if (safe && writes.Count > 0) result.AddRange(localReplacements);
        }
        return result;

        List<(int Start, int End)> CallArguments(int start)
        {
            List<(int Start, int End)> args = [];
            int depth = 0, first = start;
            for (int j = start; j < tokens.Count; j++)
            {
                if (tokens[j].Kind != 'p') continue;
                string value = tokens[j].Value;
                if (depth == 0 && value is "," or ")")
                {
                    if (j > first) args.Add((first, j));
                    if (value == ")") return args;
                    first = j + 1;
                }
                else if (value is "(" or "[" or "{") depth++;
                else if (value is ")" or "]" or "}") depth--;
            }
            return [];
        }

        static bool KnownName(string value, string family, string[] names) =>
            AssetSymbols.Parse(value, out string symbolFamily, out string symbolName)
                ? symbolFamily == family && names.Contains(symbolName, StringComparer.Ordinal)
                : names.Contains(value, StringComparer.Ordinal);

        bool IsFunctionParameter(string name)
        {
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (tokens[i].Value != "function") continue;
                int open = i + 1;
                while (open < tokens.Count && tokens[open].Value != "(" && tokens[open].Value is not (";" or "{")) open++;
                if (open >= tokens.Count || tokens[open].Value != "(") continue;
                int depth = 1;
                for (int j = open + 1; j < tokens.Count && depth > 0; j++)
                {
                    if (tokens[j].Value == "(") depth++;
                    else if (tokens[j].Value == ")") depth--;
                    else if (depth == 1 && tokens[j].Kind == 'i' && tokens[j].Value == name) return true;
                }
            }
            return false;
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
        // After compilation, untyped asset-array literals are numeric again. Resolve them against
        // the merged table so semantic verification can compare their asset identity.
        text = NameAddedAssetLiterals(text, entry,
            new Dictionary<string, string[]>(StringComparer.Ordinal), tables);
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
            if (CgfAssetTypes.Family(token.Value, entry) is not null || CgfAssetTypes.HasTypedArguments(token.Value)) container = token.Value;
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
