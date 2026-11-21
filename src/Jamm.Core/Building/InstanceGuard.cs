using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

/// <summary>One code location that reads an object's instance variables without proof that an instance exists.</summary>
/// <param name="Code">Code entry name.</param>
/// <param name="Line">1-based line in the decompiled text.</param>
/// <param name="Kind">Reference classification, including safe <c>with-object</c> selectors.</param>
/// <param name="Object">Object asset or local variable involved.</param>
/// <param name="Snippet">The trimmed source line.</param>
/// <param name="Guarded">Whether the build guard rewrites this location.</param>
public sealed record InstanceReference(string Code, int Line, string Kind, string Object, string Snippet, bool Guarded);

/// <summary>
/// Finds and guards GML that dereferences objects which may have no active instance.
/// <c>Bo.object_index</c> reads the first active Bo and raises "Unable to find any instance" when none
/// is active, although it means the constant <c>Bo</c>. Fish and mod objects are also often absent or
/// deactivated, so their variable reads go through generated <c>jamm_iv_*</c> accessors that return a
/// neutral value instead of stopping the game. Behaviour is unchanged while an instance exists.
/// </summary>
public static class InstanceGuard
{
    internal const string AccessorPrefix = "jamm_iv_";
    private static readonly Regex Member = new(@"(?<![\w.$])([A-Za-z_]\w*)\s*\.\s*([A-Za-z_]\w*)", RegexOptions.Compiled);
    private static readonly Regex Lookup = new(@"(?<![\w.])([A-Za-z_]\w*)\s*=\s*(instance_(?:nearest|find|place|position)|collision_(?:point|rectangle|circle|ellipse|line)|nav_get_random_connected_node)\s*\(", RegexOptions.Compiled);

    /// <summary>Object facts the analysis needs: names, parents, persistence and placement.</summary>
    internal sealed class ObjectFacts
    {
        internal HashSet<string> Names { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> WithChildren { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Risky { get; } = new(StringComparer.Ordinal);

        internal static ObjectFacts From(UndertaleData data, UndertaleData? ancestor = null)
        {
            var facts = new ObjectFacts();
            var fish = data.GameObjects.ByName("Fish");
            var placed = data.Rooms.SelectMany(r => r.GameObjects).Select(i => i.ObjectDefinition?.Name.Content)
                .Where(n => n is not null).ToHashSet(StringComparer.Ordinal);
            var baseNames = ancestor?.GameObjects.Select(o => o.Name.Content).ToHashSet(StringComparer.Ordinal);
            foreach (var obj in data.GameObjects)
            {
                string name = obj.Name.Content;
                facts.Names.Add(name);
                if (obj.ParentId is { } parent) facts.WithChildren.Add(parent.Name.Content);
                bool isFish = fish is not null && Inherits(obj, fish);
                bool modObject = baseNames is not null && !baseNames.Contains(name) && !obj.Persistent && !placed.Contains(name);
                if (isFish || modObject) facts.Risky.Add(name);
            }
            return facts;
        }

        private static bool Inherits(UndertaleGameObject obj, UndertaleGameObject parent)
        {
            int guard = 0;
            for (var cursor = obj; cursor is not null && guard++ < 256; cursor = cursor.ParentId)
                if (cursor == parent) return true;
            return false;
        }
    }

    /// <summary>Analyzes every code entry of an archive (read-only) and lists risky instance references.</summary>
    public static IReadOnlyList<InstanceReference> Audit(string archivePath, Action<string>? progress = null) =>
        InstanceReferenceAnalyzer.Analyze(archivePath, progress: progress);

    /// <summary>Rewrites guardable references in all final code and injects the accessors. Final build layer only.</summary>
    internal static void Apply(UndertaleData data, UndertaleData ancestor, IReadOnlyDictionary<string, CodeText> baseline,
        Dictionary<string, string> recompile, Dictionary<string, string> generated, List<string> decisions)
    {
        var facts = ObjectFacts.From(data, ancestor);
        List<InstanceReference> found = [];
        HashSet<string> accessors = new(StringComparer.Ordinal);
        foreach (var code in data.Code.Where(c => c.ParentEntry is null).Select(c => c.Name.Content).ToArray())
        {
            if (code.StartsWith("gml_Script_" + AccessorPrefix, StringComparison.Ordinal)) continue;
            string? text = recompile.GetValueOrDefault(code) ?? baseline.GetValueOrDefault(code)?.Text;
            if (text is null) continue;
            string rewritten = Rewrite(code, text, facts, found, out var used);
            if (rewritten == text) continue;
            recompile[code] = generated[code] = rewritten;
            accessors.UnionWith(used);
        }
        foreach (string variable in accessors.Order(StringComparer.Ordinal))
        {
            string script = AccessorPrefix + variable;
            string name = "gml_Script_" + script;
            var entry = data.Code.ByName(name) ?? UndertaleCode.CreateEmptyEntry(data, name);
            if (data.Scripts.ByName(script) is { } existing) existing.Code = entry;
            else data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(script), Code = entry });
            recompile[name] = generated[name] = AccessorGml(variable);
        }
        var guarded = found.Where(f => f.Guarded).ToArray();
        var byKind = guarded.GroupBy(f => f.Kind).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        if (byKind.TryGetValue("object-index", out var constants))
            decisions.Add($"[CGF-INSTANCE-004] Replaced {constants.Length} '<Object>.object_index' read(s) with the object constant so they no longer need an active instance: {Summary(constants)}.");
        if (byKind.TryGetValue("unchecked-lookup", out var lookups))
            decisions.Add($"[CGF-INSTANCE-007] Guarded {lookups.Length} read(s) of instance_nearest/find/place results that were used without a noone check: {Summary(lookups)}.");
        var members = guarded.Where(f => f.Kind is "fish-read" or "fish-write" or "fish-array").ToArray();
        if (members.Length > 0)
            decisions.Add($"[CGF-INSTANCE-005] Guarded {members.Length} fish/mod-object member access(es): {Summary(members)}.");
        if (byKind.TryGetValue("instance-repair", out var repairs))
            decisions.Add($"[CGF-INSTANCE-006] Repaired {repairs.Length} instance-dependent paths: {Summary(repairs)}.");
        var reported = found.Where(f => !f.Guarded && f.Kind != "with-object").ToArray();
        if (reported.Length > 0)
            decisions.Add($"INFO [CGF-INSTANCE-006] {reported.Length} other unguarded instance reference(s) left unchanged (writes, arrays, lookups); run 'cgfmm audit instances' for the list.");
    }

    internal static string AccessorGml(string variable)
    {
        string fallback = variable is "object_index" or "id" ? "noone" : "0";
        return $$"""
            if (argument0 >= 0 && instance_exists(argument0)) return argument0.{{variable}};
            return {{fallback}};
            """;
    }

    /// <summary>Returns the guarded text and records every risky location found in it.</summary>
    internal static string Rewrite(string code, string text, ObjectFacts facts, List<InstanceReference> found, out HashSet<string> accessors)
    {
        accessors = new(StringComparer.Ordinal);
        text = LegacyInstanceSites.Rewrite(code, text, found);
        var tokens = GmlReferences.Tokens(text);
        var info = TokenInfo.Analyze(tokens);
        string masked = Mask(text, tokens);
        var lineStarts = LineStarts(text);
        var shadowed = ShadowedObjects(tokens, facts);
        List<(int Start, int Length, string Replacement)> edits = [];
        HashSet<int> guardedStatements = [];
        Dictionary<string, List<(int Start, int End)>> guardCache = new(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(masked, @"\bwith\s*\(\s*([A-Za-z_]\w*)\s*\)"))
        {
            string token = match.Groups[1].Value;
            if (!TryObject(token, out string obj)) continue;
            int line = LineOf(lineStarts, match.Index);
            found.Add(new(code, line, "with-object", obj, Line(text, lineStarts, line), false));
        }
        foreach (Match match in Member.Matches(masked))
        {
            string token = match.Groups[1].Value, member = match.Groups[2].Value;
            if (!TryObject(token, out string obj)) continue;
            int end = match.Index + match.Length;
            var after = masked.AsSpan(end).TrimStart();
            var before = masked.AsSpan(0, match.Index).TrimEnd();
            if (before.EndsWith(".", StringComparison.Ordinal)) continue;
            bool prefixInc = IsPrefixIncrement(tokens, match.Index);
            bool write = IsWrite(prefixInc, after);
            bool array = after.StartsWith("[", StringComparison.Ordinal);
            bool chained = after.StartsWith(".", StringComparison.Ordinal);
            int line = LineOf(lineStarts, match.Index);
            string snippet = Line(text, lineStarts, line);
            // Only in comparisons: there the compiler and decompiler type the constant as an object.
            // In untyped positions (ind = Bulbfish.object_index) a bare name could resolve to a
            // same-named sprite, so those use the accessor below when the object is risky.
            bool comparison = before.EndsWith("==", StringComparison.Ordinal) || before.EndsWith("!=", StringComparison.Ordinal)
                || after.StartsWith("==", StringComparison.Ordinal) || after.StartsWith("!=", StringComparison.Ordinal);
            if (member == "object_index" && !write && !array && !chained && comparison && !facts.WithChildren.Contains(obj))
            {
                edits.Add((match.Index, match.Length, Symbol(obj)));
                found.Add(new(code, line, "object-index", obj, snippet, true));
                continue;
            }
            if (!facts.Risky.Contains(obj)) continue;
            if (InSafeRange(ObjectGuards(token, obj), match.Index)) continue;
            // A call around a typed member (object_index, sprite_index, ...) would untype the asset
            // constant it is compared with, so those reads stay as they are and are only reported.
            bool typedMember = CgfAssetTypes.Family(member, code) is not null;
            if (write && !array && SimpleWriteStatement(tokens, info, match.Index, prefixInc) is { } statement)
            {
                if (guardedStatements.Add(statement.Start))
                {
                    // Two insertions, so reads inside the statement can still be rewritten independently.
                    edits.Add((statement.Start + statement.Length, 0, " } }"));
                    edits.Add((statement.Start, 0, $"{{ if (instance_exists({Symbol(obj)})) {{ "));
                }
                found.Add(new(code, line, "fish-write", obj, snippet, true));
                continue;
            }
            if (write || array || typedMember || chained)
            {
                found.Add(new(code, line, write ? "fish-write" : array ? "fish-array" : "fish-read", obj, snippet, false));
                continue;
            }
            accessors.Add(member);
            edits.Add((match.Index, match.Length, $"{AccessorPrefix}{member}({Symbol(obj)})"));
            found.Add(new(code, line, "fish-read", obj, snippet, true));
        }
        var lookupMatches = Lookup.Matches(masked);
        var lookupStarts = lookupMatches.Select(m => m.Index).ToHashSet();
        foreach (Match match in lookupMatches)
        {
            string variable = match.Groups[1].Value;
            // JAMM's own runtimes (jamm_* variables) validate their lookups in their own way.
            if (variable.StartsWith("jamm_", StringComparison.Ordinal)) continue;
            int from = match.Index + match.Length;
            int limit = LookupLimit(tokens, info, from, variable, text.Length, lookupStarts);
            var safeRanges = SafeRanges(tokens, info, from, limit, new HashSet<string>(StringComparer.Ordinal) { variable }, allowLookupChecks: true);
            var deref = new Regex($@"(?<![\w.$]){Regex.Escape(variable)}\s*\.\s*([A-Za-z_]\w*)");
            for (var use = deref.Match(masked, from); use.Success && use.Index < limit; use = use.NextMatch())
            {
                if (InSafeRange(safeRanges, use.Index)) continue;
                // Collision and nav results can model control flow or shared instance fields.
                // Report unfamiliar paths for review instead of supplying invented coordinates.
                if (!match.Groups[2].Value.StartsWith("instance_", StringComparison.Ordinal))
                {
                    int lookupLine = LineOf(lineStarts, use.Index);
                    found.Add(new(code, lookupLine, "unchecked-lookup", variable, Line(text, lineStarts, lookupLine), false));
                    continue;
                }
                int end = use.Index + use.Length;
                var tail = masked.AsSpan(end).TrimStart();
                var head = masked.AsSpan(0, use.Index).TrimEnd();
                if (head.EndsWith(".", StringComparison.Ordinal)) continue;
                bool prefixInc = IsPrefixIncrement(tokens, use.Index);
                bool write = IsWrite(prefixInc, tail);
                bool array = tail.StartsWith("[", StringComparison.Ordinal);
                int line = LineOf(lineStarts, use.Index);
                if (write && !array && SimpleWriteStatement(tokens, info, use.Index, prefixInc) is { } statement)
                {
                    if (guardedStatements.Add(statement.Start))
                    {
                        edits.Add((statement.Start + statement.Length, 0, " } }"));
                        edits.Add((statement.Start, 0, $"{{ if ({variable} >= 0 && instance_exists({variable})) {{ "));
                    }
                    found.Add(new(code, line, "unchecked-lookup", variable, Line(text, lineStarts, line), true));
                    continue;
                }
                if (write || array || tail.StartsWith(".", StringComparison.Ordinal)
                    || CgfAssetTypes.Family(use.Groups[1].Value, code) is not null)
                {
                    found.Add(new(code, line, "unchecked-lookup", variable, Line(text, lineStarts, line), false));
                    continue;
                }
                string member = use.Groups[1].Value;
                accessors.Add(member);
                edits.Add((use.Index, use.Length, $"{AccessorPrefix}{member}({variable})"));
                found.Add(new(code, line, "unchecked-lookup", variable, Line(text, lineStarts, line), true));
            }
        }
        if (edits.Count == 0) return text;
        var builder = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
            builder.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        return builder.ToString();

        bool TryObject(string token, out string name)
        {
            if (AssetSymbols.Parse(token, out string family, out name))
                return family == "objects" && facts.Names.Contains(name);
            name = token;
            return facts.Names.Contains(token) && !shadowed.Contains(token);
        }

        List<(int Start, int End)> ObjectGuards(string token, string obj)
        {
            if (guardCache.TryGetValue(token, out var cached)) return cached;
            string symbol = Symbol(obj);
            var names = new HashSet<string>(StringComparer.Ordinal) { token, symbol };
            if (token != obj && !shadowed.Contains(obj)) names.Add(obj);
            return guardCache[token] = SafeRanges(tokens, info, 0, text.Length, names, allowLookupChecks: false);
        }
    }

    private sealed class TokenInfo
    {
        internal required int[] ParenDepth { get; init; }
        internal required int[] BraceDepth { get; init; }
        internal required bool[] IsStatementStart { get; init; }
        internal required bool[] IsDirectStatement { get; init; }

        internal static TokenInfo Analyze(IReadOnlyList<GmlReferences.Token> tokens)
        {
            int n = tokens.Count;
            int[] parenDepth = new int[n];
            int[] braceDepth = new int[n];
            bool[] isStatementStart = new bool[n];
            bool[] isDirectStatement = new bool[n];
            int pDepth = 0, bDepth = 0;
            bool inCaseLabel = false;
            Stack<(int Depth, bool ControlHeader)> parenStack = new();
            bool prevControlClose = false;
            for (int i = 0; i < n; i++)
            {
                var t = tokens[i];
                parenDepth[i] = pDepth;
                braceDepth[i] = bDepth;
                if (pDepth == 0)
                {
                    if (i == 0)
                    {
                        isStatementStart[i] = isDirectStatement[i] = true;
                    }
                    else
                    {
                        var prev = tokens[i - 1];
                        if (prev.Value is ";" or "{" or "}")
                        {
                            isStatementStart[i] = isDirectStatement[i] = true;
                        }
                        else if (prev.Value is "else" or "do" || prevControlClose || (prev.Value == ":" && inCaseLabel))
                        {
                            isStatementStart[i] = true;
                            inCaseLabel = false;
                        }
                        else if (t.Line > prev.EndLine && EndsOperand(prev) && StartsOperand(t))
                        {
                            isStatementStart[i] = isDirectStatement[i] = true;
                        }
                    }
                    if (t.Kind == 'i' && t.Value is "case" or "default") inCaseLabel = true;
                }
                prevControlClose = false;
                if (t.Value is "(" or "[")
                {
                    bool controlHeader = t.Value == "(" && i > 0 && tokens[i - 1].Kind == 'i'
                        && tokens[i - 1].Value is "if" or "while" or "for" or "with" or "repeat";
                    parenStack.Push((pDepth, controlHeader));
                    pDepth++;
                }
                else if (t.Value is ")" or "]")
                {
                    pDepth = Math.Max(0, pDepth - 1);
                    if (parenStack.Count > 0 && parenStack.Peek().Depth == pDepth)
                        prevControlClose = parenStack.Pop().ControlHeader && t.Value == ")";
                }
                else if (t.Value == "{")
                {
                    bDepth++;
                }
                else if (t.Value == "}")
                {
                    bDepth = Math.Max(0, bDepth - 1);
                }
            }
            return new TokenInfo { ParenDepth = parenDepth, BraceDepth = braceDepth, IsStatementStart = isStatementStart, IsDirectStatement = isDirectStatement };
        }
    }

    private static bool InSafeRange(IReadOnlyList<(int Start, int End)> ranges, int index)
    {
        foreach (var (start, end) in ranges)
            if (index >= start && index < end) return true;
        return false;
    }

    private static int LookupLimit(IReadOnlyList<GmlReferences.Token> tokens, TokenInfo info, int from, string variable, int textLength, HashSet<int> lookupStarts)
    {
        int startToken = FirstTokenAtOrAfter(tokens, from);
        for (int i = startToken; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != variable) continue;
            if (i > 0 && tokens[i - 1].Value == ".") continue;
            if (AssignmentWidth(tokens, i + 1) != 1 || tokens[i + 1].Value != "=") continue;
            if (lookupStarts.Contains(tokens[i].Start)) return tokens[i].Start;
            int endToken = StatementEnd(tokens, info, i);
            return tokens[endToken].Start + tokens[endToken].Length;
        }
        return textLength;
    }

    private static List<(int Start, int End)> SafeRanges(IReadOnlyList<GmlReferences.Token> tokens, TokenInfo info,
        int searchFromChar, int searchLimitChar, IReadOnlySet<string> names, bool allowLookupChecks)
    {
        List<(int Start, int End)> ranges = [];
        int first = FirstTokenAtOrAfter(tokens, searchFromChar);
        for (int i = first; i < tokens.Count && tokens[i].Start < searchLimitChar; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != "if") continue;
            if (i + 1 >= tokens.Count || tokens[i + 1].Value != "(") continue;
            int condEnd = MatchParen(tokens, i + 1);
            if (condEnd < 0 || condEnd + 1 >= tokens.Count) continue;
            int thenStart = condEnd + 1;
            int thenEnd = StatementEnd(tokens, info, thenStart);
            int? elseStart = thenEnd + 1 < tokens.Count && tokens[thenEnd + 1].Value == "else" && thenEnd + 2 < tokens.Count ? thenEnd + 2 : null;
            int? elseEnd = elseStart is { } es ? StatementEnd(tokens, info, es) : null;

            AnalyzeCondition(i + 2, condEnd, thenStart, thenEnd, elseStart, elseEnd);
        }
        return ranges;

        void AnalyzeCondition(int start, int end, int thenStart, int thenEnd, int? elseStart, int? elseEnd)
        {
            int depth = 0;
            List<(int Start, int End)> orBranches = [];
            List<string> orOps = [];
            int branchStart = start;
            for (int k = start; k < end; k++)
            {
                string val = tokens[k].Value;
                if (val is "(" or "[") { depth++; continue; }
                if (val is ")" or "]") { depth--; continue; }
                if (depth != 0) continue;
                if (val == "?") return;
                if (val is "or" or "xor")
                {
                    orBranches.Add((branchStart, k));
                    orOps.Add(val);
                    branchStart = k + 1;
                }
                else if (IsAdjacentPair(tokens, k, "|", "|") || IsAdjacentPair(tokens, k, "^", "^"))
                {
                    orBranches.Add((branchStart, k));
                    orOps.Add(val + val);
                    branchStart = k + 2;
                    k++;
                }
            }
            orBranches.Add((branchStart, end));

            if (orBranches.Count == 1)
            {
                var conjuncts = SplitAnd(start, end);
                foreach (var (cStart, cEnd) in conjuncts)
                {
                    if (IsConditionTerm(tokens, cStart, cEnd, names, allowLookupChecks, positive: true))
                    {
                        if (tokens[cEnd].Start < tokens[end].Start)
                            ranges.Add((tokens[cEnd].Start, tokens[end].Start));
                        ranges.Add((tokens[thenStart].Start, tokens[thenEnd].Start + tokens[thenEnd].Length));
                        return;
                    }
                }
                if (conjuncts.Count == 1 && IsConditionTerm(tokens, conjuncts[0].Start, conjuncts[0].End, names, allowLookupChecks, positive: false))
                    AddNegativeProtection(thenStart, thenEnd, elseStart, elseEnd, start - 2);
                return;
            }

            if (orOps.Any(op => op is "^^" or "xor")) return;
            foreach (var (bStart, bEnd) in orBranches)
            {
                var conjuncts = SplitAnd(bStart, bEnd);
                if (conjuncts.Count == 1 && IsConditionTerm(tokens, conjuncts[0].Start, conjuncts[0].End, names, allowLookupChecks, positive: false))
                {
                    AddNegativeProtection(thenStart, thenEnd, elseStart, elseEnd, start - 2);
                    return;
                }
            }
        }

        void AddNegativeProtection(int thenStart, int thenEnd, int? elseStart, int? elseEnd, int ifToken)
        {
            if (elseStart is { } es && elseEnd is { } ee)
                ranges.Add((tokens[es].Start, tokens[ee].Start + tokens[ee].Length));
            if (info.IsDirectStatement[ifToken] && UnconditionallyTerminates(tokens, info, thenStart, thenEnd))
            {
                int blockEnd = Math.Min(searchLimitChar, EnclosingBlockEndChar(tokens, ifToken, searchLimitChar));
                int afterIf = tokens[thenEnd].Start + tokens[thenEnd].Length;
                if (afterIf < blockEnd) ranges.Add((afterIf, blockEnd));
            }
        }

        List<(int Start, int End)> SplitAnd(int start, int end)
        {
            List<(int Start, int End)> parts = [];
            int depth = 0, partStart = start;
            for (int k = start; k < end; k++)
            {
                string val = tokens[k].Value;
                if (val is "(" or "[") { depth++; continue; }
                if (val is ")" or "]") { depth--; continue; }
                if (depth != 0) continue;
                if (val == "and")
                {
                    parts.Add((partStart, k));
                    partStart = k + 1;
                }
                else if (IsAdjacentPair(tokens, k, "&", "&"))
                {
                    parts.Add((partStart, k));
                    partStart = k + 2;
                    k++;
                }
            }
            parts.Add((partStart, end));
            return parts;
        }
    }

    private static bool IsConditionTerm(IReadOnlyList<GmlReferences.Token> tokens, int start, int end,
        IReadOnlySet<string> names, bool allowLookupChecks, bool positive)
    {
        while (end - start >= 2 && tokens[start].Value == "(" && MatchParen(tokens, start) == end - 1)
        {
            start++;
            end--;
        }
        int len = end - start;
        if (len <= 0) return false;
        if (positive)
        {
            if (len == 4 && tokens[start].Value == "instance_exists" && tokens[start + 1].Value == "("
                && tokens[end - 1].Value == ")" && IsTargetName(tokens, start + 2, names))
                return true;
            if (!allowLookupChecks) return false;
            if (len == 1 && IsTargetName(tokens, start, names)) return true;
            if (len >= 3)
            {
                if (IsTargetName(tokens, start, names))
                    return IsPositiveRightComparison(tokens, start + 1, end);
                if (IsTargetName(tokens, end - 1, names))
                    return IsPositiveLeftComparison(tokens, start, end - 1);
            }
            return false;
        }
        if (len == 5 && tokens[start].Value is "!" or "not" && tokens[start + 1].Value == "instance_exists"
            && tokens[start + 2].Value == "(" && tokens[end - 1].Value == ")" && IsTargetName(tokens, start + 3, names))
            return true;
        if (!allowLookupChecks) return false;
        if (len == 2 && tokens[start].Value is "!" or "not" && IsTargetName(tokens, start + 1, names)) return true;
        if (len >= 3)
        {
            if (IsTargetName(tokens, start, names))
                return IsNegativeRightComparison(tokens, start + 1, end);
            if (IsTargetName(tokens, end - 1, names))
                return IsNegativeLeftComparison(tokens, start, end - 1);
        }
        return false;

        static bool IsTargetName(IReadOnlyList<GmlReferences.Token> tokens, int idx, IReadOnlySet<string> names) =>
            tokens[idx].Kind == 'i' && names.Contains(tokens[idx].Value) && (idx == 0 || tokens[idx - 1].Value != ".");

        static bool IsMinusOne(IReadOnlyList<GmlReferences.Token> tokens, int start, int end) =>
            end - start == 2 && tokens[start].Value == "-" && tokens[start + 1].Value == "1";

        static bool IsZero(IReadOnlyList<GmlReferences.Token> tokens, int start, int end) =>
            end - start == 1 && tokens[start].Value == "0";

        static bool IsNoone(IReadOnlyList<GmlReferences.Token> tokens, int start, int end) =>
            end - start == 1 && tokens[start].Value == "noone";

        static bool IsPositiveRightComparison(IReadOnlyList<GmlReferences.Token> tokens, int op, int end) =>
            (tokens[op].Value == "!=" && (IsNoone(tokens, op + 1, end) || IsMinusOne(tokens, op + 1, end)))
            || (tokens[op].Value is ">=" or ">" && IsZero(tokens, op + 1, end))
            || (tokens[op].Value == ">" && IsMinusOne(tokens, op + 1, end));

        static bool IsPositiveLeftComparison(IReadOnlyList<GmlReferences.Token> tokens, int start, int op) =>
            (tokens[op - 1].Value == "!=" && (IsNoone(tokens, start, op - 1) || IsMinusOne(tokens, start, op - 1)))
            || (tokens[op - 1].Value is "<=" or "<" && IsZero(tokens, start, op - 1))
            || (tokens[op - 1].Value == "<" && IsMinusOne(tokens, start, op - 1));

        static bool IsNegativeRightComparison(IReadOnlyList<GmlReferences.Token> tokens, int op, int end) =>
            (tokens[op].Value == "==" && (IsNoone(tokens, op + 1, end) || IsMinusOne(tokens, op + 1, end)))
            || (tokens[op].Value == "<" && IsZero(tokens, op + 1, end))
            || (tokens[op].Value == "<=" && IsMinusOne(tokens, op + 1, end));

        static bool IsNegativeLeftComparison(IReadOnlyList<GmlReferences.Token> tokens, int start, int op) =>
            (tokens[op - 1].Value == "==" && (IsNoone(tokens, start, op - 1) || IsMinusOne(tokens, start, op - 1)))
            || (tokens[op - 1].Value == ">" && IsZero(tokens, start, op - 1))
            || (tokens[op - 1].Value == ">=" && IsMinusOne(tokens, start, op - 1));
    }

    private static bool UnconditionallyTerminates(IReadOnlyList<GmlReferences.Token> tokens, TokenInfo info, int start, int end)
    {
        if (tokens[start].Value != "{")
            return tokens[start].Value is "exit" or "return" or "break" or "continue";
        int targetBraceDepth = info.BraceDepth[start] + 1;
        for (int k = start + 1; k < end; k++)
        {
            if (info.BraceDepth[k] == targetBraceDepth && info.IsDirectStatement[k]
                && tokens[k].Value is "exit" or "return" or "break" or "continue")
                return true;
        }
        return false;
    }

    private static int EnclosingBlockEndChar(IReadOnlyList<GmlReferences.Token> tokens, int idx, int fallback)
    {
        int depth = 0;
        for (int k = idx - 1; k >= 0; k--)
        {
            if (tokens[k].Value == "}") depth++;
            else if (tokens[k].Value == "{")
            {
                if (depth == 0)
                {
                    int close = MatchBrace(tokens, k);
                    return close >= 0 ? tokens[close].Start : fallback;
                }
                depth--;
            }
        }
        return fallback;
    }

    private static int StatementEnd(IReadOnlyList<GmlReferences.Token> tokens, TokenInfo info, int start)
    {
        if (start >= tokens.Count) return tokens.Count - 1;
        string kw = tokens[start].Value;
        if (kw == "{")
        {
            int close = MatchBrace(tokens, start);
            return close >= 0 ? close : tokens.Count - 1;
        }
        if (kw == "if" && start + 1 < tokens.Count && tokens[start + 1].Value == "(")
        {
            int condEnd = MatchParen(tokens, start + 1);
            if (condEnd >= 0 && condEnd + 1 < tokens.Count)
            {
                int bodyEnd = StatementEnd(tokens, info, condEnd + 1);
                if (bodyEnd + 1 < tokens.Count && tokens[bodyEnd + 1].Value == "else" && bodyEnd + 2 < tokens.Count)
                    return StatementEnd(tokens, info, bodyEnd + 2);
                return bodyEnd;
            }
        }
        if (kw is "while" or "for" or "with" or "repeat" && start + 1 < tokens.Count && tokens[start + 1].Value == "(")
        {
            int headerEnd = MatchParen(tokens, start + 1);
            if (headerEnd >= 0 && headerEnd + 1 < tokens.Count)
                return StatementEnd(tokens, info, headerEnd + 1);
        }
        if (kw == "do" && start + 1 < tokens.Count)
        {
            int bodyEnd = StatementEnd(tokens, info, start + 1);
            if (bodyEnd + 1 < tokens.Count && tokens[bodyEnd + 1].Value == "until"
                && bodyEnd + 2 < tokens.Count && tokens[bodyEnd + 2].Value == "(")
            {
                int untilEnd = MatchParen(tokens, bodyEnd + 2);
                if (untilEnd >= 0)
                    return untilEnd + 1 < tokens.Count && tokens[untilEnd + 1].Value == ";" ? untilEnd + 1 : untilEnd;
            }
            return bodyEnd;
        }
        int baseDepth = info.ParenDepth[start];
        for (int k = start; k < tokens.Count; k++)
        {
            if (tokens[k].Value == "{" && k > start)
            {
                int close = MatchBrace(tokens, k);
                if (close >= 0) return close;
            }
            if (info.ParenDepth[k] == baseDepth)
            {
                if (tokens[k].Value == ";") return k;
                if (k > start && tokens[k].Line > tokens[k - 1].EndLine && EndsOperand(tokens[k - 1]) && StartsOperand(tokens[k]))
                    return k - 1;
            }
        }
        return tokens.Count - 1;
    }

    private static int MatchParen(IReadOnlyList<GmlReferences.Token> tokens, int openIdx)
    {
        int depth = 0;
        for (int k = openIdx; k < tokens.Count; k++)
        {
            if (tokens[k].Value == "(") depth++;
            else if (tokens[k].Value == ")")
            {
                depth--;
                if (depth == 0) return k;
            }
        }
        return -1;
    }

    private static int MatchBrace(IReadOnlyList<GmlReferences.Token> tokens, int openIdx)
    {
        int depth = 0;
        for (int k = openIdx; k < tokens.Count; k++)
        {
            if (tokens[k].Value == "{") depth++;
            else if (tokens[k].Value == "}")
            {
                depth--;
                if (depth == 0) return k;
            }
        }
        return -1;
    }

    private static int FirstTokenAtOrAfter(IReadOnlyList<GmlReferences.Token> tokens, int charIndex)
    {
        int lo = 0, hi = tokens.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (tokens[mid].Start < charIndex) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static bool IsPrefixIncrement(IReadOnlyList<GmlReferences.Token> tokens, int charIndex)
    {
        int t = FirstTokenAtOrAfter(tokens, charIndex);
        if (t < 2 || tokens[t].Start != charIndex) return false;
        return (IsAdjacentPair(tokens, t - 2, "+", "+") || IsAdjacentPair(tokens, t - 2, "-", "-"))
            && tokens[t - 2].Line == tokens[t].Line && (t < 3 || !EndsOperand(tokens[t - 3]));
    }

    private static bool IsWrite(bool prefixIncrement, ReadOnlySpan<char> after) =>
        prefixIncrement
        || (after.StartsWith("=", StringComparison.Ordinal) && !after.StartsWith("==", StringComparison.Ordinal))
        || (after.Length >= 2 && ((after[1] == '=' && "+-*/%&|^".Contains(after[0]))
            || (after[0] is '+' or '-' && after[0] == after[1])))
        || after.StartsWith("<<=", StringComparison.Ordinal) || after.StartsWith(">>=", StringComparison.Ordinal);

    private static HashSet<string> ShadowedObjects(IReadOnlyList<GmlReferences.Token> tokens, ObjectFacts facts)
    {
        var shadowed = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind != 'i') continue;
            if (t.Value is "var" or "globalvar")
            {
                int j = i + 1;
                while (j < tokens.Count && tokens[j].Kind == 'i')
                {
                    if (facts.Names.Contains(tokens[j].Value)) shadowed.Add(tokens[j].Value);
                    j++;
                    if (j < tokens.Count && tokens[j].Value == "=")
                    {
                        j++;
                        int depth = 0;
                        while (j < tokens.Count)
                        {
                            string v = tokens[j].Value;
                            if (depth == 0 && (v is "," or ";")) break;
                            if (depth == 0 && j > i + 1 && tokens[j].Line > tokens[j - 1].EndLine
                                && EndsOperand(tokens[j - 1]) && StartsOperand(tokens[j])) break;
                            if (v is "(" or "[" or "{") depth++;
                            else if (v is ")" or "]" or "}") { depth--; if (depth < 0) break; }
                            j++;
                        }
                    }
                    if (j < tokens.Count && tokens[j].Value == ",") j++;
                    else break;
                }
                continue;
            }
            if (t.Value == "function")
            {
                int j = i + 1;
                if (j < tokens.Count && tokens[j].Kind == 'i') j++;
                if (j < tokens.Count && tokens[j].Value == "(")
                {
                    j++;
                    int depth = 1;
                    bool expectParam = true;
                    while (j < tokens.Count && depth > 0)
                    {
                        string v = tokens[j].Value;
                        if (v is "(" or "[" or "{") { depth++; expectParam = false; }
                        else if (v is ")" or "]" or "}") { depth--; expectParam = false; }
                        else if (depth == 1 && v == ",") expectParam = true;
                        else if (depth == 1 && expectParam && tokens[j].Kind == 'i')
                        {
                            if (facts.Names.Contains(v)) shadowed.Add(v);
                            expectParam = false;
                        }
                        j++;
                    }
                }
                continue;
            }
            if ((i == 0 || tokens[i - 1].Value != ".") && (i + 1 >= tokens.Count || tokens[i + 1].Value != ".")
                && facts.Names.Contains(t.Value))
            {
                bool mutated = AssignmentWidth(tokens, i + 1) > 0
                    || IsAdjacentPair(tokens, i + 1, "+", "+") || IsAdjacentPair(tokens, i + 1, "-", "-")
                    || (i >= 2 && (IsAdjacentPair(tokens, i - 2, "+", "+") || IsAdjacentPair(tokens, i - 2, "-", "-"))
                        && tokens[i - 2].Line == t.Line && (i < 3 || !EndsOperand(tokens[i - 3])));
                if (mutated) shadowed.Add(t.Value);
            }
        }
        return shadowed;
    }

    private static (int Start, int Length)? SimpleWriteStatement(IReadOnlyList<GmlReferences.Token> tokens, TokenInfo info, int memberCharIndex, bool isPrefix)
    {
        int t = FirstTokenAtOrAfter(tokens, memberCharIndex);
        if (t + 2 >= tokens.Count || tokens[t].Start != memberCharIndex || tokens[t + 1].Value != ".") return null;
        int stmtStart = isPrefix ? t - 2 : t;
        if (stmtStart < 0 || !info.IsStatementStart[stmtStart] || info.ParenDepth[stmtStart] != 0) return null;
        if (isPrefix)
        {
            if (t + 3 >= tokens.Count || tokens[t + 3].Value != ";") return null;
            return (tokens[stmtStart].Start, tokens[t + 3].Start + 1 - tokens[stmtStart].Start);
        }
        if (IsAdjacentPair(tokens, t + 3, "+", "+") || IsAdjacentPair(tokens, t + 3, "-", "-"))
        {
            if (t + 5 >= tokens.Count || tokens[t + 5].Value != ";") return null;
            return (tokens[stmtStart].Start, tokens[t + 5].Start + 1 - tokens[stmtStart].Start);
        }
        int opWidth = AssignmentWidth(tokens, t + 3);
        if (opWidth == 0) return null;
        int rhsStart = t + 3 + opWidth;
        int depth = 0;
        for (int k = rhsStart; k < tokens.Count; k++)
        {
            string v = tokens[k].Value;
            if (v is "{" or "}") return null;
            if (depth == 0 && v == ";")
                return (tokens[stmtStart].Start, tokens[k].Start + 1 - tokens[stmtStart].Start);
            if (depth == 0 && k > rhsStart && tokens[k].Line > tokens[k - 1].EndLine
                && EndsOperand(tokens[k - 1]) && StartsOperand(tokens[k]))
                return null;
            if (v is "(" or "[") depth++;
            else if (v is ")" or "]") { depth--; if (depth < 0) return null; }
        }
        return null;
    }

    private static bool IsAdjacentPair(IReadOnlyList<GmlReferences.Token> tokens, int index, string first, string second) =>
        index >= 0 && index + 1 < tokens.Count
        && tokens[index].Value == first && tokens[index + 1].Value == second
        && tokens[index + 1].Start == tokens[index].Start + tokens[index].Length;

    private static int AssignmentWidth(IReadOnlyList<GmlReferences.Token> tokens, int index)
    {
        if (index >= tokens.Count) return 0;
        if (tokens[index].Value is "=" or "+=" or "-=" or "*=" or "/=") return 1;
        if (IsAdjacentPair(tokens, index, "%", "=") || IsAdjacentPair(tokens, index, "&", "=")
            || IsAdjacentPair(tokens, index, "|", "=") || IsAdjacentPair(tokens, index, "^", "=")
            || IsAdjacentPair(tokens, index, "<", "<=") || IsAdjacentPair(tokens, index, ">", ">="))
            return 2;
        return 0;
    }

    private static bool EndsOperand(GmlReferences.Token token) => token.Kind == 'i'
        ? token.Value is not ("and" or "or" or "xor" or "not" or "div" or "mod"
            or "if" or "else" or "while" or "for" or "with" or "repeat" or "switch" or "case" or "default"
            or "do" or "until" or "var" or "globalvar" or "function" or "return" or "then" or "begin" or "end")
        : token.Kind is 'n' or 's' || token.Value is ")" or "]" or "}";

    private static bool StartsOperand(GmlReferences.Token token) => token.Kind == 'i'
        ? token.Value is not ("and" or "or" or "xor" or "not" or "div" or "mod")
        : token.Kind is 'n' or 's';

    /// <summary>Typed object symbol; linking turns it into the object index even when a sprite has the same name.</summary>
    internal static string Symbol(string obj) => AssetSymbols.Symbol("objects", obj);

    private static string Summary(IEnumerable<InstanceReference> refs)
    {
        var groups = refs.GroupBy(r => r.Code).Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.Object).Distinct())})").ToArray();
        return groups.Length <= 12 ? string.Join("; ", groups) : string.Join("; ", groups.Take(12)) + $"; and {groups.Length - 12} more entries";
    }

    /// <summary>Blanks string literals and comments so pattern matches only see code.</summary>
    internal static string Mask(string text) => Mask(text, GmlReferences.Tokens(text));

    private static string Mask(string text, IReadOnlyList<GmlReferences.Token> tokens)
    {
        var chars = text.Select(c => c == '\n' ? '\n' : ' ').ToArray();
        foreach (var token in tokens)
        {
            if (token.Kind != 's')
                text.AsSpan(token.Start, token.Length).CopyTo(chars.AsSpan(token.Start));
            else
                chars[token.Start] = '"';
        }
        return new string(chars);
    }

    private static List<int> LineStarts(string text)
    {
        List<int> starts = [0];
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        return starts;
    }

    private static int LineOf(List<int> starts, int index)
    {
        int found = starts.BinarySearch(index);
        return (found >= 0 ? found : ~found - 1) + 1;
    }

    private static string Line(string text, List<int> starts, int line)
    {
        int start = starts[line - 1];
        int end = line < starts.Count ? starts[line] - 1 : text.Length;
        string value = text[start..end].Trim();
        return value.Length > 160 ? value[..157] + "..." : value;
    }
}
