using System.Globalization;
using Jamm.Core.Merging;
using Token = Jamm.Core.Merging.GmlReferences.Token;

namespace Jamm.Core.Building;

// Scans expression spans in decompiled GM1.4 GML, and keeps source offsets for precise edits.
internal sealed class GuardGml
{
    internal readonly record struct Span(int First, int End);
    internal sealed record Call(int At, IReadOnlyList<Span> Arguments);
    internal sealed record Write(int At, string? Qualifier, string Field, Span? Index, string Operator, Span Value);

    internal string Text { get; }
    internal List<Token> Tokens { get; }
    private readonly Dictionary<int, int> closes = [];
    private readonly List<(Span Body, string? Target)> scopes = [];
    private readonly List<(int At, string Name, string Target)> instances = [];

    internal GuardGml(string text)
    {
        Text = text;
        Tokens = GmlReferences.Tokens(text);
        Stack<int> pending = [];
        for (int i = 0; i < Tokens.Count; i++)
        {
            if (Tokens[i].Kind != 'p') continue;
            if (Is(i, "(", "[", "{")) pending.Push(i);
            else if (Is(i, ")", "]", "}") && pending.TryPop(out int open)) closes[open] = i;
        }
        for (int i = 0; i < Tokens.Count; i++)
        {
            if (Is(i, "with") && Is(i + 1, "(") && Close(i + 1) is int end)
            {
                string? target = ObjectName(new(i + 2, end));
                int body = end + 1;
                int last = StatementEnd(body);
                scopes.Add((new(body, last), target));
            }
            if (Identifier(i) && Is(i + 1, "=") && Is(i + 2, "instance_create")
                && CallAt(i + 2) is { Arguments.Count: 3 } call && ObjectName(call.Arguments[2]) is { } obj)
                instances.Add((i, Tokens[i].Value, obj));
        }
    }

    internal bool Is(int at, params string[] values) => at >= 0 && at < Tokens.Count
        && Tokens[at].Kind != 's' && values.Contains(Tokens[at].Value, StringComparer.Ordinal);

    internal bool Identifier(int at) => at >= 0 && at < Tokens.Count && Tokens[at].Kind == 'i';
    internal int? Close(int at) => closes.TryGetValue(at, out int close) ? close : null;
    internal int Start(Span span) => Tokens[span.First].Start;
    internal int End(Span span) => Tokens[span.End - 1].Start + Tokens[span.End - 1].Length;
    internal string Source(Span span) => Text[Start(span)..End(span)];
    internal string Key(Span span) => string.Join(" ", Tokens[span.First..span.End].Select(t => t.Kind == 's'
        ? System.Text.Json.JsonSerializer.Serialize(t.Value) : t.Value));

    internal string? Resolve(int at, string? qualifier, string? owner)
    {
        if (qualifier == "other")
        {
            var nesting = scopes.Where(s => at >= s.Body.First && at < s.Body.End).ToArray();
            return nesting.Length > 1 ? nesting[^2].Target : nesting.Length == 1 ? owner : null;
        }
        if (qualifier is not null and not ("self" or "id"))
            return instances.LastOrDefault(s => s.At < at && s.Name == qualifier).Target ?? AssetName(qualifier);
        var scope = scopes.FindLastIndex(s => at >= s.Body.First && at < s.Body.End);
        if (scope < 0) return owner;
        return scopes[scope].Target is { } target ? Resolve(scopes[scope].Body.First - 1, target, owner) : null;
    }

    internal string? ObjectName(Span span)
    {
        return span.End == span.First + 1 && Identifier(span.First) ? AssetName(Tokens[span.First].Value) : null;
    }

    private static string AssetName(string symbol) => AssetSymbols.Parse(symbol, out _, out string name) ? name : symbol;

    internal double? Number(Span span)
    {
        while (Is(span.First, "(") && Close(span.First) == span.End - 1)
            span = new(span.First + 1, span.End - 1);
        int at = span.First;
        bool minus = Is(at, "-");
        if (minus || Is(at, "+")) at++;
        if (at >= span.End) return null;
        string number = string.Concat(Tokens[at..span.End].Select(t => t.Value));
        if (span.End != at + 1 && !System.Text.RegularExpressions.Regex.IsMatch(number, @"^\d+(\.\d+)?[Ee][+-]\d+$"))
            return null;
        if (Tokens[at].Kind != 'n') return null;
        if (number.StartsWith('$') || number.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(number.AsSpan(number[0] == '$' ? 1 : 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out ulong hex) ? (minus ? -(double)hex : hex) : null;
        return double.TryParse((minus ? "-" : "") + number, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? result : null;
    }

    internal IEnumerable<Call> Calls()
    {
        for (int i = 0; i < Tokens.Count; i++)
            if (CallAt(i) is { } call) yield return call;
    }

    private Call? CallAt(int at)
    {
        if (!Identifier(at) || !Is(at + 1, "(") || Close(at + 1) is not int close) return null;
        List<Span> arguments = [];
        int start = at + 2;
        for (int i = start; i < close; i++)
        {
            if (Close(i) is int child) i = child;
            else if (Is(i, ","))
            {
                if (i == start) return null;
                arguments.Add(new(start, i));
                start = i + 1;
            }
        }
        if (start < close) arguments.Add(new(start, close));
        return new(at, arguments);
    }

    internal IEnumerable<Write> Writes()
    {
        for (int i = 0; i < Tokens.Count - 2; i++)
        {
            if (!Identifier(i)) continue;
            string? qualifier = null;
            if (Is(i - 1, "."))
            {
                if (!Identifier(i - 2) || Is(i - 3, ".")) continue;
                qualifier = Tokens[i - 2].Value;
            }
            Span? index = null;
            int op = i + 1;
            if (Is(op, "[") && Close(op) is int close)
            {
                index = new(op + 1, close);
                op = close + 1;
            }
            if (!Is(op, "=", "+=", "-=", "*=", "/=")) continue;
            int end = ExpressionEnd(op + 1);
            if (end > op + 1) yield return new(i, qualifier, Tokens[i].Value, index, Tokens[op].Value, new(op + 1, end));
        }
    }

    private int StatementEnd(int start)
    {
        if (Is(start, "{") && Close(start) is int block) return block;
        if (Is(start, "if", "while", "repeat", "with", "for") && Is(start + 1, "(") && Close(start + 1) is int condEnd)
        {
            int end = StatementEnd(condEnd + 1);
            if (Is(start, "if"))
            {
                int next = Is(end, ";") ? end + 1 : end;
                if (Is(next, "else")) return StatementEnd(next + 1);
            }
            return end;
        }
        return ExpressionEnd(start);
    }

    private int ExpressionEnd(int start)
    {
        int i = start;
        for (; i < Tokens.Count; i++)
        {
            if (Is(i, ";", ",", ")", "]", "}", "else", "case", "default")) break;
            if (i > start && Tokens[i].Line > Tokens[i - 1].EndLine && Identifier(i)
                && !Is(i, "and", "or", "xor", "div", "mod") && !Is(i - 1, "+", "-", "*", "/", "?", ":", "&&", "||", "&", "|", ".", "and", "or", "xor", "div", "mod"))
                break;
            if (Close(i) is int child) i = child;
        }
        return i;
    }
}
