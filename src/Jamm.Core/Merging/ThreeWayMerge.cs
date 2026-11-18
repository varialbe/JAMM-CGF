using System.Text;
using Jamm.Core.Diffing;

namespace Jamm.Core.Merging;

/// <summary>A version in load order, always compared against the same ancestor.</summary>
public sealed record MergeVersion(string Name, string Text);

/// <summary>A source range and reusable resolution choices. Start is a zero-based ancestor line.</summary>
public sealed record MergeHunk(int Start, int Length, string Status, string Kind, string Base,
    IReadOnlyList<MergeVersion> Versions, string? Resolution, IReadOnlyList<string> Choices, string? KeepBoth);

/// <summary>Text with unresolved markers plus per-hunk choices for a future build.</summary>
public sealed record MergeResult(string Text, IReadOnlyList<MergeHunk> Hunks)
{
    /// <summary>Whether at least one hunk still requires a user decision.</summary>
    public bool HasConflicts => Hunks.Any(h => h.Status == "CONFLICT");
}

/// <summary>Line-based ancestor merge. Every version uses original coordinates, so unresolved markers never become input.</summary>
public static class ThreeWayMerge
{
    private sealed record Edit(int Start, int Length, string Text, int Version, bool LoopScope = false);
    private sealed record LoopScope(int Start, int End, int Header);

    /// <summary>Merges two descendants, preserving both sides of unresolved overlaps.</summary>
    public static MergeResult Merge(string ancestor, string earlier, string later, string earlierName = "A", string laterName = "B") =>
        Merge(ancestor, [new(earlierName, earlier), new(laterName, later)]);

    /// <summary>Applies any number of descendants in load order; disjoint edits commute, conflicting edits retain choices.</summary>
    public static MergeResult Merge(string ancestor, IReadOnlyList<MergeVersion> versions)
    {
        ancestor = LineDiff.Normalize(ancestor);
        string[] lines = Lines(ancestor);
        List<Edit> edits = [];
        for (int v = 0; v < versions.Count; v++)
        {
            int position = 0, start = -1, length = 0;
            StringBuilder replacement = new();
            foreach (var edit in LineDiff.Compare(ancestor, versions[v].Text))
            {
                if (edit.Kind == ' ') { Flush(); position++; }
                else
                {
                    if (start < 0) start = position;
                    if (edit.Kind == '-') { length++; position++; }
                    else replacement.Append(edit.Text).Append(edit.HasNewline ? "\n" : "");
                }
            }
            Flush();
            void Flush()
            {
                if (start < 0) return;
                edits.Add(new(start, length, replacement.ToString(), v));
                start = -1; length = 0; replacement.Clear();
            }
        }
        edits = CoalesceChangedLoops(lines, edits);
        List<List<Edit>> clusters = [];
        foreach (var edit in edits.OrderBy(e => e.Start).ThenBy(e => e.Length).ThenBy(e => e.Version))
        {
            var touching = clusters.Where(c => c.Any(e => Overlaps(e, edit))).ToArray();
            if (touching.Length == 0) clusters.Add([edit]);
            else
            {
                touching[0].Add(edit);
                foreach (var other in touching.Skip(1)) { touching[0].AddRange(other); clusters.Remove(other); }
            }
        }
        StringBuilder text = new();
        List<MergeHunk> hunks = [];
        int cursor = 0;
        foreach (var cluster in clusters.OrderBy(c => c.Min(e => e.Start)))
        {
            int start = cluster.Min(e => e.Start), end = cluster.Max(e => e.Start + e.Length);
            text.Append(string.Concat(lines[cursor..start]));
            string original = string.Concat(lines[start..end]);
            var alternatives = cluster.GroupBy(e => e.Version).OrderBy(g => g.Key).Select(g =>
            {
                StringBuilder value = new();
                int offset = start;
                foreach (var edit in g.OrderBy(e => e.Start))
                {
                    value.Append(string.Concat(lines[offset..edit.Start])).Append(edit.Text);
                    offset = edit.Start + edit.Length;
                }
                value.Append(string.Concat(lines[offset..end]));
                return new MergeVersion(versions[g.Key].Name, value.ToString());
            }).ToArray();
            bool identical = alternatives.Select(a => a.Text).Distinct(StringComparer.Ordinal).Count() == 1;
            string status = alternatives.Length == 1 ? "AUTO" : identical ? "INFO" : "CONFLICT";
            bool list = status == "CONFLICT" && cluster.All(e => e.Length == 0);
            string? keepBoth = list ? string.Concat(alternatives.Select(a => a.Text).Distinct(StringComparer.Ordinal)) : null;
            string[] choices = status == "CONFLICT" ? [.. alternatives.Select(a => a.Name), .. (list ? new[] { "keepBoth" } : [])] : [];
            hunks.Add(new(start, end - start, status, list ? "list" : "code", original, alternatives,
                status == "CONFLICT" ? null : alternatives[^1].Name, choices, keepBoth));
            text.Append(status == "CONFLICT" ? Markers(original, alternatives) : alternatives[^1].Text);
            cursor = end;
        }
        text.Append(string.Concat(lines[cursor..]));
        return new(text.ToString(), hunks);
    }

    // Keep a changed ancestor loop's header and body together. Line diff can
    // otherwise pair a newly inserted loop header with statements from a nearby loop.
    private static List<Edit> CoalesceChangedLoops(string[] lines, List<Edit> edits)
    {
        var loops = FindLoops(lines);
        var scopes = loops.Where(loop => edits.Any(edit =>
                edit.Start <= loop.Header && loop.Header < edit.Start + edit.Length))
            .OrderBy(loop => loop.Start).ToList();
        if (scopes.Count == 0) return edits;

        // Expand through every edit touched by a selected loop. An edit that crosses
        // a loop boundary brings its full range into the scope, never a clamped slice.
        bool changed;
        do
        {
            changed = false;
            scopes = MergeScopes(scopes);
            for (int i = 0; i < scopes.Count; i++)
            {
                var scope = scopes[i];
                int start = scope.Start, end = scope.End;
                foreach (var edit in edits.Where(edit => Intersects(edit, start, end)))
                {
                    start = Math.Min(start, edit.Start);
                    end = Math.Max(end, edit.Start + edit.Length);
                }
                if (start != scope.Start || end != scope.End)
                {
                    scopes[i] = scope with { Start = start, End = end };
                    changed = true;
                }
            }
        }
        while (changed);
        scopes = MergeScopes(scopes);

        foreach (var scope in scopes)
        {
            var absorbed = edits.Where(edit => Intersects(edit, scope.Start, scope.End)).ToArray();
            edits.RemoveAll(edit => absorbed.Contains(edit));
            foreach (var versionEdits in absorbed.GroupBy(edit => edit.Version))
            {
                int version = versionEdits.Key;
                edits.Add(new(scope.Start, scope.End - scope.Start,
                    ApplyWithin(lines, scope.Start, scope.End, versionEdits), version, LoopScope: true));
            }
        }
        return edits;
    }

    private static List<LoopScope> MergeScopes(IEnumerable<LoopScope> source)
    {
        List<LoopScope> merged = [];
        foreach (var scope in source.OrderBy(scope => scope.Start))
        {
            if (merged.Count > 0 && scope.Start < merged[^1].End)
            {
                var previous = merged[^1];
                merged[^1] = previous with { End = Math.Max(previous.End, scope.End) };
            }
            else merged.Add(scope);
        }
        return merged;
    }

    private static bool Intersects(Edit edit, int start, int end) => edit.Length == 0
        ? edit.Start > start && edit.Start < end
        : edit.Start < end && edit.Start + edit.Length > start;

    private static string ApplyWithin(string[] lines, int start, int end, IEnumerable<Edit> source)
    {
        var selected = source.OrderBy(e => e.Start).ThenBy(e => e.Length).ToArray();
        StringBuilder result = new();
        int cursor = start;
        foreach (var edit in selected)
        {
            if (edit.Start < cursor || edit.Start + edit.Length > end)
                throw new InvalidOperationException("A loop merge scope did not contain its complete edit range.");
            result.Append(string.Concat(lines[cursor..edit.Start])).Append(edit.Text);
            cursor = edit.Start + edit.Length;
        }
        result.Append(string.Concat(lines[cursor..end]));
        return result.ToString();
    }

    private static List<LoopScope> FindLoops(string[] lines)
    {
        var tokens = GmlReferences.Tokens(string.Concat(lines));
        Dictionary<int, int> matchingBraces = [];
        Stack<int> openBraces = new();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'p') continue;
            if (tokens[i].Value == "{") openBraces.Push(i);
            else if (tokens[i].Value == "}" && openBraces.TryPop(out int open))
                matchingBraces[open] = i;
        }

        List<LoopScope> loops = [];
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != "for" || i + 1 >= tokens.Count
                || tokens[i + 1].Value != "(") continue;

            int parenDepth = 0, closeHeader = -1;
            for (int j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Kind != 'p') continue;
                if (tokens[j].Value == "(") parenDepth++;
                else if (tokens[j].Value == ")" && --parenDepth == 0)
                {
                    closeHeader = j;
                    break;
                }
            }
            int openBrace = closeHeader + 1;
            if (closeHeader < 0 || openBrace >= tokens.Count || tokens[openBrace].Value != "{"
                || !matchingBraces.TryGetValue(openBrace, out int closeBrace)) continue;

            int start = tokens[i].Line - 1;
            int end = tokens[closeBrace].EndLine;
            if (start >= 0 && end > start) loops.Add(new(start, end, start));
        }
        return loops;
    }

    /// <summary>Creates a two-way conflict for incompatible same-name code additions.</summary>
    public static MergeResult Added(IReadOnlyList<MergeVersion> versions)
    {
        var normalized = versions.Select(v => v with { Text = LineDiff.Normalize(v.Text) }).ToArray();
        bool identical = normalized.Select(v => v.Text).Distinct(StringComparer.Ordinal).Count() == 1;
        var hunk = new MergeHunk(0, 0, identical ? "INFO" : "CONFLICT", "addition", "", normalized,
            identical ? normalized[^1].Name : null, identical ? [] : normalized.Select(v => v.Name).ToArray(), null);
        return new(identical ? normalized[^1].Text : Markers("", normalized), [hunk]);
    }

    /// <summary>Retains a whole-entry choice when one mod deletes code another edits, including metadata-only edits.</summary>
    public static MergeResult Removal(string ancestor, IReadOnlyList<MergeVersion> versions)
    {
        ancestor = LineDiff.Normalize(ancestor);
        var normalized = versions.Select(v => v with { Text = LineDiff.Normalize(v.Text) }).ToArray();
        var hunk = new MergeHunk(0, Lines(ancestor).Length, "CONFLICT", "removal", ancestor, normalized,
            null, normalized.Select(v => v.Name).ToArray(), null);
        return new(Markers(ancestor, normalized), [hunk]);
    }

    private static bool Overlaps(Edit a, Edit b)
    {
        if (a.LoopScope && b.Length == 0 && b.Start == a.Start || b.LoopScope && a.Length == 0 && a.Start == b.Start)
            return false;
        return JoinsLines(a, b) || JoinsLines(b, a) || (a.Length == 0 && b.Length == 0 ? a.Start == b.Start
        : a.Length == 0 ? a.Start >= b.Start && a.Start < b.Start + b.Length
        : b.Length == 0 ? b.Start >= a.Start && b.Start < a.Start + a.Length
        : a.Start < b.Start + b.Length && b.Start < a.Start + a.Length);
    }

    // Removing the final newline also changes the boundary used by an adjacent insertion.
    private static bool JoinsLines(Edit left, Edit right) => left.Start + left.Length == right.Start
        && left.Text.Length > 0 && !left.Text.EndsWith('\n');

    private static string[] Lines(string text)
    {
        if (text.Length == 0) return [];
        var split = text.Split('\n');
        return split.Select((line, i) => i < split.Length - 1 ? line + "\n" : line).Where(s => s.Length > 0).ToArray();
    }

    private static string Markers(string ancestor, IReadOnlyList<MergeVersion> versions)
    {
        StringBuilder text = new();
        text.Append("<<<<<<< ").AppendLine(versions[0].Name).Append(versions[0].Text);
        if (!versions[0].Text.EndsWith('\n')) text.AppendLine();
        text.AppendLine("||||||| base").Append(ancestor);
        if (ancestor.Length > 0 && !ancestor.EndsWith('\n')) text.AppendLine();
        foreach (var version in versions.Skip(1))
        {
            text.Append("======= ").AppendLine(version.Name).Append(version.Text);
            if (!version.Text.EndsWith('\n')) text.AppendLine();
        }
        return text.Append(">>>>>>> ").AppendLine(versions[^1].Name).ToString();
    }
}
