using System.Text;

namespace Jamm.Core.Diffing;

/// <summary>A line edit suitable for display and future three-way merging.</summary>
public sealed record LineEdit(char Kind, string Text, bool HasNewline = true);

/// <summary>Myers shortest edit scripts over normalized lines.</summary>
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
        if (a.Length == 0) return b.Select(line => Edit('+', line)).ToArray();
        if (b.Length == 0) return a.Select(line => Edit('-', line)).ToArray();
        int max = a.Length + b.Length, offset = max + 1;
        int[] v = new int[2 * max + 3];
        List<int[]> trace = [];
        for (int d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1] : v[offset + k - 1] + 1;
                int y = x - k;
                while (x < a.Length && y < b.Length && a[x] == b[y]) { x++; y++; }
                v[offset + k] = x;
                if (x < a.Length || y < b.Length) continue;
                List<LineEdit> edits = [];
                for (int depth = d; depth >= 0; depth--)
                {
                    int[] previous = trace[depth];
                    int diagonal = x - y;
                    int prevK = diagonal == -depth || (diagonal != depth && previous[offset + diagonal - 1] < previous[offset + diagonal + 1])
                        ? diagonal + 1 : diagonal - 1;
                    int prevX = previous[offset + prevK], prevY = prevX - prevK;
                    while (x > prevX && y > prevY) { edits.Add(Edit(' ', a[--x])); y--; }
                    if (depth == 0) break;
                    if (x == prevX) edits.Add(Edit('+', b[--y]));
                    else edits.Add(Edit('-', a[--x]));
                }
                edits.Reverse();
                return edits;
            }
        }
        throw new InvalidOperationException("Could not calculate line differences.");
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
