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
    private sealed record Edit(int Start, int Length, string Text, int Version);

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

    private static bool Overlaps(Edit a, Edit b) => JoinsLines(a, b) || JoinsLines(b, a) || (a.Length == 0 && b.Length == 0 ? a.Start == b.Start
        : a.Length == 0 ? a.Start >= b.Start && a.Start < b.Start + b.Length
        : b.Length == 0 ? b.Start >= a.Start && b.Start < a.Start + a.Length
        : a.Start < b.Start + b.Length && b.Start < a.Start + a.Length);

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
