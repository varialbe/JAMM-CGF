using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

/// <summary>One code location that reads an object's instance variables without proof that an instance exists.</summary>
/// <param name="Code">Code entry name.</param>
/// <param name="Line">1-based line in the decompiled text.</param>
/// <param name="Kind"><c>object-index</c>, <c>fish-read</c>, <c>fish-write</c>, <c>fish-array</c> or <c>unchecked-lookup</c>.</param>
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
    internal const string Revision = "instance-guard-1";
    internal const string AccessorPrefix = "jamm_iv_";
    private static readonly Regex Member = new(@"(?<![\w.$])([A-Za-z_]\w*)\s*\.\s*([A-Za-z_]\w*)", RegexOptions.Compiled);
    private static readonly Regex Lookup = new(@"(?<![\w.])([A-Za-z_]\w*)\s*=\s*instance_(?:nearest|find|place|position)\s*\(", RegexOptions.Compiled);

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
    public static IReadOnlyList<InstanceReference> Audit(string archivePath, Action<string>? progress = null)
    {
        using var loaded = GameFileLoader.Load(archivePath, progress);
        var facts = ObjectFacts.From(loaded.Data);
        var texts = CodeDecompiler.ReadAll(loaded.Data, Path.GetFileName(archivePath), _ => { },
            AssetSymbols.Ambiguous([SemanticReferences.Names(loaded.Data)]), AnalysisOptions.Default, progress);
        List<InstanceReference> result = [];
        foreach (var (name, text) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            Rewrite(name, text.Text, facts, result, out _);
        }
        return result;
    }

    /// <summary>Rewrites guardable references in all final code and injects the accessors. Final build layer only.</summary>
    internal static void Apply(UndertaleData data, UndertaleData ancestor, IReadOnlyDictionary<string, CodeText> baseline,
        Dictionary<string, string> recompile, Dictionary<string, string> generated, List<string> decisions)
    {
        var facts = ObjectFacts.From(data, ancestor);
        List<InstanceReference> found = [];
        HashSet<string> accessors = new(StringComparer.Ordinal);
        foreach (var code in data.Code.Select(c => c.Name.Content).ToArray())
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
        if (guarded.Length == 0) return;
        var byKind = guarded.GroupBy(f => f.Kind).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        if (byKind.TryGetValue("object-index", out var constants))
            decisions.Add($"[CGF-INSTANCE-004] Replaced {constants.Length} '<Object>.object_index' read(s) with the object constant so they no longer need an active instance: {Summary(constants)}.");
        if (byKind.TryGetValue("fish-read", out var reads))
            decisions.Add($"[CGF-INSTANCE-005] Guarded {reads.Length} fish/mod-object variable read(s) that crash when no instance is active: {Summary(reads)}.");
        var reported = found.Where(f => !f.Guarded).ToArray();
        if (reported.Length > 0)
            decisions.Add($"INFO [CGF-INSTANCE-006] {reported.Length} other unguarded instance reference(s) left unchanged (writes, arrays, lookups); run 'cgfmm audit instances' for the list.");
    }

    internal static string AccessorGml(string variable)
    {
        string fallback = variable is "object_index" or "id" ? "noone" : "0";
        return $$"""
            if (instance_exists(argument0)) return argument0.{{variable}};
            return {{fallback}};
            """;
    }

    /// <summary>Returns the guarded text and records every risky location found in it.</summary>
    internal static string Rewrite(string code, string text, ObjectFacts facts, List<InstanceReference> found, out HashSet<string> accessors)
    {
        accessors = new(StringComparer.Ordinal);
        string masked = Mask(text);
        var lineStarts = LineStarts(text);
        List<(int Start, int Length, string Replacement)> edits = [];
        HashSet<int> guardedLines = [];
        foreach (Match match in Member.Matches(masked))
        {
            string obj = match.Groups[1].Value, member = match.Groups[2].Value;
            if (!facts.Names.Contains(obj)) continue;
            int end = match.Index + match.Length;
            string after = masked[end..Math.Min(masked.Length, end + 4)].TrimStart();
            string before = masked[Math.Max(0, match.Index - 2)..match.Index];
            bool write = Regex.IsMatch(after, @"^(=(?!=)|\+=|-=|\*=|/=|\+\+|--)") || before.EndsWith("++", StringComparison.Ordinal) || before.EndsWith("--", StringComparison.Ordinal);
            bool array = after.StartsWith('[');
            int line = LineOf(lineStarts, match.Index);
            string snippet = Line(text, lineStarts, line);
            if (member == "object_index" && !write && !facts.WithChildren.Contains(obj))
            {
                edits.Add((match.Index, match.Length, obj));
                found.Add(new(code, line, "object-index", obj, snippet, true));
                continue;
            }
            if (!facts.Risky.Contains(obj)) continue;
            if (write && SimpleStatement(text, lineStarts, line, obj) is { } statement)
            {
                if (guardedLines.Add(line))
                {
                    // Two insertions, so reads inside the statement can still be rewritten independently.
                    edits.Add((statement.Start + statement.Length, 0, " }"));
                    edits.Add((statement.Start, 0, $"if (instance_exists({obj})) {{ "));
                }
                found.Add(new(code, line, "fish-write", obj, snippet, true));
                continue;
            }
            if (write || array)
            {
                found.Add(new(code, line, write ? "fish-write" : "fish-array", obj, snippet, false));
                continue;
            }
            accessors.Add(member);
            edits.Add((match.Index, match.Length, $"{AccessorPrefix}{member}({obj})"));
            found.Add(new(code, line, "fish-read", obj, snippet, true));
        }
        foreach (Match match in Lookup.Matches(masked))
        {
            string variable = match.Groups[1].Value;
            int from = match.Index + match.Length;
            var deref = new Regex($@"(?<![\w.]){Regex.Escape(variable)}\s*\.\s*[A-Za-z_]");
            var use = deref.Match(masked, from);
            if (!use.Success) continue;
            string between = masked[from..use.Index];
            if (Regex.IsMatch(between, $@"instance_exists\s*\(\s*{Regex.Escape(variable)}\s*\)|{Regex.Escape(variable)}\s*(!=|==|>=|<=|>|<)|noone"))
                continue;
            int line = LineOf(lineStarts, use.Index);
            found.Add(new(code, line, "unchecked-lookup", variable, Line(text, lineStarts, line), false));
        }
        if (edits.Count == 0) return text;
        var builder = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
            builder.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        return builder.ToString();
    }

    /// <summary>
    /// Returns the statement span when a line is exactly one simple write to <paramref name="obj"/>
    /// (<c>Obj.var = expr;</c>, <c>Obj.var += expr;</c>, <c>Obj.var++;</c>) and the next code line is not an
    /// <c>else</c>, so wrapping it in an <c>if</c> cannot change which branch an <c>else</c> belongs to.
    /// </summary>
    private static (int Start, int Length)? SimpleStatement(string text, List<int> starts, int line, string obj)
    {
        int start = starts[line - 1];
        int end = line < starts.Count ? starts[line] - 1 : text.Length;
        string raw = text[start..end];
        string masked = Mask(raw);
        var statement = Regex.Match(masked, $@"^(\s*)({Regex.Escape(obj)}\s*\.\s*[A-Za-z_]\w*\s*(?:(?:=(?!=)|\+=|-=|\*=|/=)[^;{{}}]*|\+\+|--)\s*;)\s*$");
        if (!statement.Success || masked.Count(c => c == ';') != 1) return null;
        for (int next = line; next < starts.Count; next++)
        {
            int ns = starts[next];
            int ne = next + 1 < starts.Count ? starts[next + 1] - 1 : text.Length;
            string following = Mask(text[ns..ne]).Trim();
            if (following.Length == 0) continue;
            if (following.StartsWith("else", StringComparison.Ordinal)) return null;
            break;
        }
        return (start + statement.Groups[2].Index, statement.Groups[2].Length);
    }

    private static string Summary(IEnumerable<InstanceReference> refs)
    {
        var groups = refs.GroupBy(r => r.Code).Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.Object).Distinct())})").ToArray();
        return groups.Length <= 12 ? string.Join("; ", groups) : string.Join("; ", groups.Take(12)) + $"; and {groups.Length - 12} more entries";
    }

    /// <summary>Blanks string literals and comments so pattern matches only see code.</summary>
    internal static string Mask(string text)
    {
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c == '"' || c == '\'')
            {
                int j = i + 1;
                while (j < chars.Length && chars[j] != c && chars[j] != '\n') j++;
                for (int k = i + 1; k < j && k < chars.Length; k++) chars[k] = ' ';
                i = j;
            }
            else if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                int j = i;
                while (j < chars.Length && chars[j] != '\n') chars[j++] = ' ';
                i = j;
            }
            else if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                int j = i;
                while (j < chars.Length && !(chars[j] == '*' && j + 1 < chars.Length && chars[j + 1] == '/'))
                {
                    if (chars[j] != '\n') chars[j] = ' ';
                    j++;
                }
                if (j < chars.Length) { chars[j] = ' '; if (j + 1 < chars.Length) chars[j + 1] = ' '; }
                i = j + 1;
            }
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
