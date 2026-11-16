using System.Globalization;
using System.Text;
using Jamm.Core.Merging;

namespace Jamm.Core.Building;

// Deliberately a small lexical adapter, not a general coordinate/data-flow evaluator.
internal static class HudTextAdapter
{
    internal static string Rewrite(string name, string text, string baseline, out int count)
    {
        count = 0;
        bool quest = name == HudRuntime.Quest;
        bool experience = name == HudRuntime.Experience && HasStandardAnchors(text);
        var originals = Calls(baseline).GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.Count());
        List<(int Start, int Length, string Text)> edits = [];
        foreach (var call in Calls(text))
        {
            bool added = !originals.TryGetValue(call.Key, out int remaining) || remaining == 0;
            if (!added) originals[call.Key] = remaining - 1;
            if (!quest && !added) continue;
            var args = call.Args;
            if (args.Count < 3) continue;
            bool region = quest && Compact(args[0]) == "draw_x" && Compact(args[1]) == "draw_y"
                || experience && HasStandardAnchors(text[..call.Start]) && Offset(args[0], "draw_x", 380, 455) && Offset(args[1], "draw_y", -160, -1)
                || Offset(args[0], "view_xview[view_current]+view_wview[view_current]", -60, -5)
                    && Offset(args[1], "view_yview[view_current]", 292, 451);
            if (!region) continue;
            string sx = "1", sy = "1", angle = "0";
            if (call.Function == "draw_text_transformed")
            {
                if (args.Count != 6 || !Positive(args[3]) || !Positive(args[4]) || Compact(args[5]) != "0") continue;
                sx = args[3]; sy = args[4]; angle = args[5];
            }
            else if (call.Function is not ("draw_text" or "draw_text_shadowed") || args.Count != 3) continue;
            edits.Add((call.Start, call.Length, $"jamm_hud_text({(quest ? 2 : 1)}, {(call.Function == "draw_text_shadowed" ? 1 : 0)}, {args[2]}, {sx}, {sy}, {angle})"));
        }
        StringBuilder result = new(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start)) result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        count = edits.Count;
        return result.ToString();
    }

    private static bool Positive(string expression) => double.TryParse(Compact(expression), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) && value > 0;
    private static string Compact(string expression) => string.Concat(GmlReferences.Tokens(expression).Select(t => t.Kind == 's' ? "\"" + t.Value + "\"" : t.Value));
    private static bool Offset(string expression, string anchor, int min, int max)
    {
        string compact = Compact(expression).Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal);
        if (!compact.StartsWith(anchor, StringComparison.Ordinal)) return false;
        string tail = compact[anchor.Length..];
        return tail.Length > 1 && tail[0] is '+' or '-' && int.TryParse(tail, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int offset) && offset >= min && offset <= max;
    }

    private static bool HasStandardAnchors(string text)
    {
        var tokens = GmlReferences.Tokens(text);
        int x = 0, y = 0;
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value is not ("draw_x" or "draw_y")) continue;
            // Reject mutation, qualification and unknown assignments rather than guessing.
            if (tokens[i + 1].Value is "++" or "--" or "+=" or "-=" or "*=" or "/=") return false;
            if (tokens[i + 1].Value != "=") continue;
            int end = i + 2;
            while (end < tokens.Count && tokens[end].Value != ";") end++;
            if (end == tokens.Count) return false;
            string rhs = Compact(text[tokens[i + 2].Start..tokens[end].Start]);
            bool horizontal = tokens[i].Value == "draw_x";
            if (rhs != (horizontal ? "round(view_xview[view_current]+offset_x)" : "round(view_yview[view_current]+offset_y)")) return false;
            if (horizontal) x++; else y++;
        }
        return x > 0 && y > 0;
    }

    private sealed record Call(string Function, int Start, int Length, List<string> Args, string Key);
    private static IEnumerable<Call> Calls(string text)
    {
        var t = GmlReferences.Tokens(text);
        for (int i = 0; i + 1 < t.Count; i++)
        {
            if (t[i].Kind != 'i' || !t[i].Value.StartsWith("draw_text", StringComparison.Ordinal) || t[i + 1].Value != "(" || i > 0 && t[i - 1].Value == ".") continue;
            int depth = 0, start = i + 2, end = start;
            List<string> args = [];
            for (; end < t.Count; end++)
            {
                if (t[end].Kind == 'p')
                {
                    if (t[end].Value == ")" && depth == 0) { Add(end); break; }
                    if (t[end].Value == "," && depth == 0) { Add(end); start = end + 1; }
                    else if (t[end].Value is "(" or "[" or "{") depth++;
                    else if (t[end].Value is ")" or "]" or "}") depth--;
                }
            }
            if (end == t.Count) continue;
            int length = t[end].Start + t[end].Length - t[i].Start;
            yield return new(t[i].Value, t[i].Start, length, args, Compact(text.Substring(t[i].Start, length)));
            i = end;
            void Add(int stop) { if (start < stop) args.Add(text[t[start].Start..t[stop].Start].Trim()); }
        }
    }
}
