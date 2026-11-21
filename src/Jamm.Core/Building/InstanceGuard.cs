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
        string masked = Mask(text);
        var lineStarts = LineStarts(text);
        var shadowed = ShadowedObjects(masked, facts);
        List<(int Start, int Length, string Replacement)> edits = [];
        HashSet<int> guardedLines = [];
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
            bool write = IsWrite(before, after);
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
            if (InExistenceGuard(masked, match.Index, token, Symbol(obj))) continue;
            // A call around a typed member (object_index, sprite_index, ...) would untype the asset
            // constant it is compared with, so those reads stay as they are and are only reported.
            bool typedMember = CgfAssetTypes.Family(member, code) is not null;
            if (write && SimpleStatement(masked, lineStarts, line, token) is { } statement)
            {
                if (guardedLines.Add(line))
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
        foreach (Match match in Lookup.Matches(masked))
        {
            string variable = match.Groups[1].Value;
            // JAMM's own runtimes (jamm_* variables) validate their lookups in their own way.
            if (facts.Names.Contains(variable) || variable.StartsWith("jamm_", StringComparison.Ordinal)) continue;
            int from = match.Index + match.Length;
            // Guard dereferences until the variable is checked or assigned again.
            var reassigned = new Regex($@"(?<![\w.]){Regex.Escape(variable)}\s*=(?!=)");
            var stop = reassigned.Match(masked, from);
            int limit = stop.Success ? stop.Index : masked.Length;
            string value = Regex.Escape(variable);
            var check = new Regex($@"\bif\s*\(\s*(?:!?\s*instance_exists\s*\(\s*{value}\s*\)|!?\s*{value}\s*(?=[)&|])|{value}\s*(?:(?:!=|==)\s*(?:noone\b|-1\b)|(?:>=|>|<|<=)\s*0\b))");
            var checkedAt = check.Match(masked, from);
            if (checkedAt.Success && checkedAt.Index < limit) limit = checkedAt.Index;
            var deref = new Regex($@"(?<![\w.$]){Regex.Escape(variable)}\s*\.\s*([A-Za-z_]\w*)");
            for (var use = deref.Match(masked, from); use.Success && use.Index < limit; use = use.NextMatch())
            {
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
                bool write = IsWrite(head, tail);
                int line = LineOf(lineStarts, use.Index);
                if (write && SimpleStatement(masked, lineStarts, line, variable) is { } statement)
                {
                    if (guardedLines.Add(line))
                    {
                        edits.Add((statement.Start + statement.Length, 0, " } }"));
                        edits.Add((statement.Start, 0, $"{{ if ({variable} >= 0 && instance_exists({variable})) {{ "));
                    }
                    found.Add(new(code, line, "unchecked-lookup", variable, Line(text, lineStarts, line), true));
                    continue;
                }
                if (write || tail.StartsWith("[", StringComparison.Ordinal) || tail.StartsWith(".", StringComparison.Ordinal)
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
    }

    private static bool InExistenceGuard(string masked, int use, string target, string symbol)
    {
        // A previously generated guard or an existing explicit existence check can
        // protect an entire body. Look only at positive final condition terms.
        string check = $@"\bif\s*\(([^;{{}}]*?&&\s*)?instance_exists\(\s*(?:{Regex.Escape(target)}|{Regex.Escape(symbol)})\s*\)\s*\)\s*";
        foreach (Match guard in Regex.Matches(masked[..use], check))
        {
            var prefix = guard.Groups[1].ValueSpan;
            if (prefix.Contains("||", StringComparison.Ordinal) || prefix.Count('(') != prefix.Count(')')) continue;
            int start = guard.Index + guard.Length;
            if (start >= use) return true;
            if (masked[start] != '{')
            {
                if (!masked.AsSpan(start, use - start).Contains(';')) return true;
                continue;
            }
            int depth = 1;
            for (int i = start + 1; i < use && depth > 0; i++)
            {
                if (masked[i] == '{') depth++;
                if (masked[i] == '}') depth--;
            }
            if (depth > 0) return true;
        }
        return false;
    }

    private static bool IsWrite(ReadOnlySpan<char> before, ReadOnlySpan<char> after) =>
        before.EndsWith("++", StringComparison.Ordinal) || before.EndsWith("--", StringComparison.Ordinal)
        || (after.StartsWith("=", StringComparison.Ordinal) && !after.StartsWith("==", StringComparison.Ordinal))
        || (after.Length >= 2 && ((after[1] == '=' && "+-*/%&|^".Contains(after[0]))
            || (after[0] is '+' or '-' && after[0] == after[1])))
        || after.StartsWith("<<=", StringComparison.Ordinal) || after.StartsWith(">>=", StringComparison.Ordinal);

    private static HashSet<string> ShadowedObjects(string masked, ObjectFacts facts)
    {
        var shadowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match declaration in Regex.Matches(masked, @"\b(?:var|globalvar)\s+([A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)"))
            foreach (Match name in Regex.Matches(declaration.Groups[1].Value, @"\w+"))
                if (facts.Names.Contains(name.Value)) shadowed.Add(name.Value);
        foreach (Match assignment in Regex.Matches(masked, @"(?<![\w.])([A-Za-z_]\w*)\s*=(?!=)"))
            if (facts.Names.Contains(assignment.Groups[1].Value)) shadowed.Add(assignment.Groups[1].Value);
        return shadowed;
    }

    /// <summary>
    /// Returns the statement span when a line is exactly one simple write to <paramref name="obj"/>
    /// (<c>Obj.var = expr;</c>, <c>Obj.var += expr;</c>, <c>Obj.var++;</c>).
    /// A block around the guard preserves an enclosing if/else or loop body.
    /// </summary>
    private static (int Start, int Length)? SimpleStatement(string text, List<int> starts, int line, string obj)
    {
        int start = starts[line - 1];
        int end = line < starts.Count ? starts[line] - 1 : text.Length;
        string masked = text[start..end];
        var statement = Regex.Match(masked, $@"^(\s*)({Regex.Escape(obj)}\s*\.\s*[A-Za-z_]\w*\s*(?:(?:=(?!=)|\+=|-=|\*=|/=)[^;{{}}]*|\+\+|--)\s*;)\s*$");
        if (!statement.Success) return null;
        return (start + statement.Groups[2].Index, statement.Groups[2].Length);
    }

    /// <summary>Typed object symbol; linking turns it into the object index even when a sprite has the same name.</summary>
    internal static string Symbol(string obj) => AssetSymbols.Symbol("objects", obj);

    private static string Summary(IEnumerable<InstanceReference> refs)
    {
        var groups = refs.GroupBy(r => r.Code).Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.Object).Distinct())})").ToArray();
        return groups.Length <= 12 ? string.Join("; ", groups) : string.Join("; ", groups.Take(12)) + $"; and {groups.Length - 12} more entries";
    }

    /// <summary>Blanks string literals and comments so pattern matches only see code.</summary>
    internal static string Mask(string text)
    {
        var chars = text.Select(c => c == '\n' ? '\n' : ' ').ToArray();
        foreach (var token in GmlReferences.Tokens(text))
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
