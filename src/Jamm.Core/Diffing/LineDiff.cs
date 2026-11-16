using System.Text;

namespace Jamm.Core.Diffing;

/// <summary>A line edit suitable for display and future three-way merging.</summary>
public sealed record LineEdit(char Kind, string Text, bool HasNewline = true);

/// <summary>Linear-space shortest edit scripts over normalized lines (Hirschberg LCS).</summary>
public static class LineDiff
{
    /// <summary>Normalizes platform line endings to LF.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string[] Lines(string text)
    {
        text = Normalize(text);
        if (text.Length == 0) return [];
        var parts = text.Split('\n');
        return parts.Select((line, i) => i < parts.Length - 1 ? line + "\n" : line)
            .Where(line => line.Length > 0).ToArray();
    }

    private static LineEdit Edit(char kind, string token) => token.EndsWith('\n')
        ? new(kind, token[..^1]) : new(kind, token, false);

    /// <summary>Produces context, deletion and insertion lines (space, minus and plus).</summary>
    public static IReadOnlyList<LineEdit> Compare(string before, string after)
    {
        string[] a = Lines(before), b = Lines(after);
        List<LineEdit> edits = [];
        Visit(0, a.Length, 0, b.Length);
        return edits;

        void Visit(int a0, int a1, int b0, int b1)
        {
            while (a0 < a1 && b0 < b1 && a[a0] == b[b0])
            { edits.Add(Edit(' ', a[a0++])); b0++; }
            int suffix = 0;
            while (a0 < a1 && b0 < b1 && a[a1 - 1] == b[b1 - 1])
            { a1--; b1--; suffix++; }
            if (a0 == a1) for (int j = b0; j < b1; j++) edits.Add(Edit('+', b[j]));
            else if (b0 == b1) for (int i = a0; i < a1; i++) edits.Add(Edit('-', a[i]));
            else if (a1 - a0 == 1)
            {
                int match = Array.IndexOf(b, a[a0], b0, b1 - b0);
                if (match < 0) edits.Add(Edit('-', a[a0]));
                for (int j = b0; j < b1; j++) edits.Add(Edit(j == match ? ' ' : '+', b[j]));
            }
            else
            {
                int middle = (a0 + a1) / 2;
                int split = Split(a0, middle, a1, b0, b1);
                Visit(a0, middle, b0, split);
                Visit(middle, a1, split, b1);
            }
            for (int i = 0; i < suffix; i++) edits.Add(Edit(' ', a[a1 + i]));
        }

        int Split(int a0, int middle, int a1, int b0, int b1)
        {
            // Rows are released before recursion: peak auxiliary memory is linear.
            int width = b1 - b0;
            var forward = Row(a0, middle, false);
            var reverse = Row(middle, a1, true);
            int best = -1, split = 0;
            for (int j = 0; j <= width; j++)
                if (forward[j] + reverse[width - j] > best)
                { best = forward[j] + reverse[width - j]; split = j; }
            return b0 + split;

            int[] Row(int start, int end, bool backwards)
            {
                int[] row = new int[width + 1];
                for (int i = 0; i < end - start; i++)
                {
                    int diagonal = 0;
                    for (int j = 1; j <= width; j++)
                    {
                        int old = row[j];
                        row[j] = a[backwards ? end - i - 1 : start + i] == b[backwards ? b1 - j : b0 + j - 1]
                            ? diagonal + 1 : Math.Max(row[j], row[j - 1]);
                        diagonal = old;
                    }
                }
                return row;
            }
        }
    }

    /// <summary>Formats context-limited unified hunks, retaining reusable edits separately.</summary>
    public static string Unified(string before, string after, string name, int context = 3)
    {
        var edits = Compare(before, after);
        var changed = Enumerable.Range(0, edits.Count).Where(i => edits[i].Kind != ' ').ToArray();
        if (changed.Length == 0) return $"{name}: no text changes.\n";
        StringBuilder text = new($"--- base/{name}\n+++ mod/{name}\n");
        int cursor = 0, oldLine = 1, newLine = 1;
        while (cursor < edits.Count)
        {
            int next = Array.Find(changed, i => i >= cursor);
            if (next < cursor) break;
            int start = Math.Max(cursor, next - context), end = Math.Min(edits.Count, next + context + 1);
            foreach (int index in changed.Where(i => i > next))
            {
                if (index - context > end) break;
                end = Math.Min(edits.Count, index + context + 1);
            }
            for (int i = cursor; i < start; i++) { if (edits[i].Kind != '+') oldLine++; if (edits[i].Kind != '-') newLine++; }
            int oldCount = edits.Skip(start).Take(end - start).Count(x => x.Kind != '+');
            int newCount = edits.Skip(start).Take(end - start).Count(x => x.Kind != '-');
            text.AppendLine($"@@ -{(oldCount == 0 ? oldLine - 1 : oldLine)},{oldCount} +{(newCount == 0 ? newLine - 1 : newLine)},{newCount} @@");
            for (int i = start; i < end; i++)
            {
                text.Append(edits[i].Kind).AppendLine(edits[i].Text);
                if (!edits[i].HasNewline) text.AppendLine("\\ No newline at end of file");
                if (edits[i].Kind != '+') oldLine++;
                if (edits[i].Kind != '-') newLine++;
            }
            cursor = end;
        }
        return text.ToString();
    }
}
