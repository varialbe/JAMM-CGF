using System.Globalization;
using System.Text;
using System.Text.Json;
using Jamm.Core.Patching;

namespace Jamm.Core.Merging;

/// <summary>A write to a literal slot, with source lines for filtering to changed code.</summary>
public sealed record IndexWrite(string Container, string Index, string Kind, int Line, int EndLine);

/// <summary>A small lexical scanner for GML identifiers and literal container writes; strings/comments are never code.</summary>
public static class GmlReferences
{
    private sealed record Token(string Value, char Kind, int Line, int EndLine);

    /// <summary>Finds identifiers, excluding string literals and both comment forms.</summary>
    public static IReadOnlyList<string> Identifiers(string text) => Tokens(text).Where(t => t.Kind == 'i')
        .Select(t => t.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Finds array assignments, ds_list_set and ds_map_add with literal indices/keys.</summary>
    public static IReadOnlyList<IndexWrite> IndexWrites(string text)
    {
        var tokens = Tokens(text);
        List<IndexWrite> result = [];
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || (i > 0 && tokens[i - 1].Value == ".")) continue;
            int end = i + 1;
            string container = tokens[i].Value;
            if (container is "ds_list_set" or "ds_map_add" && At(end, "("))
            {
                string kind = container == "ds_list_set" ? "list" : "map";
                end++;
                if (end >= tokens.Count || tokens[end].Kind != 'i') continue;
                container = ReadContainer(ref end);
                if (!At(end++, ",")) continue;
                string? index = Literal(ref end, kind == "map");
                if (index is not null && At(end, ",")) result.Add(new(container, index, kind, tokens[i].Line, ValueEndLine(end + 1)));
            }
            else
            {
                end = i;
                container = ReadContainer(ref end);
                if (!At(end++, "[")) continue;
                string kind = "array";
                if (At(end, "|")) { kind = "list"; end++; }
                else if (At(end, "?")) { kind = "map"; end++; }
                else if (At(end, "#")) end++;
                string? index = Literal(ref end, kind == "map");
                if (index is not null && At(end++, "]") && end < tokens.Count
                    && tokens[end].Value is "=" or "+=" or "-=" or "*=" or "/=")
                    result.Add(new(container, index, kind, tokens[i].Line, ValueEndLine(end + 1)));
            }
        }
        return result.Distinct().ToArray();

        bool At(int index, string value) => index < tokens.Count && tokens[index].Value == value;
        int ValueEndLine(int start)
        {
            int depth = 0, last = tokens[start - 1].EndLine;
            for (int j = start; j < tokens.Count; j++)
            {
                var token = tokens[j];
                if (token.Kind == 'p')
                {
                    if (depth == 0 && token.Value is ";" or "," or ")" or "]" or "}") break;
                    if (token.Value is "(" or "[" or "{") depth++;
                    else if (token.Value is ")" or "]" or "}") depth--;
                }
                // GML also permits statements without semicolons. A newline between
                // two complete operands starts a new statement, unlike a continued RHS.
                if (depth == 0 && j > start && token.Line > last
                    && EndsOperand(tokens[j - 1]) && StartsOperand(token)) break;
                last = token.EndLine;
            }
            return last;
        }
        static bool EndsOperand(Token token) => token.Kind == 'i' ? !WordOperator(token.Value)
            : token.Kind is 'n' or 's' || token.Value is ")" or "]" or "}";
        static bool StartsOperand(Token token) => token.Kind == 'i' ? !WordOperator(token.Value) : token.Kind is 'n' or 's';
        static bool WordOperator(string value) => value is "and" or "or" or "xor" or "not" or "div" or "mod";
        string ReadContainer(ref int index)
        {
            StringBuilder name = new(tokens[index++].Value);
            while (At(index, ".") && index + 1 < tokens.Count && tokens[index + 1].Kind == 'i')
            { name.Append('.').Append(tokens[index + 1].Value); index += 2; }
            return name.ToString();
        }
        string? Literal(ref int index, bool allowString)
        {
            if (index >= tokens.Count) return null;
            if (allowString && tokens[index].Kind == 's') return "string:" + tokens[index++].Value;
            string sign = "";
            if (At(index, "-")) { sign = "-"; index++; }
            if (index >= tokens.Count || tokens[index].Kind != 'n') return null;
            string number = tokens[index++].Value;
            if (number.StartsWith('$') && ulong.TryParse(number.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                return sign + hex.ToString(CultureInfo.InvariantCulture);
            if (number.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(number.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hex))
                return sign + hex.ToString(CultureInfo.InvariantCulture);
            return decimal.TryParse(sign + number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value.ToString("G29", CultureInfo.InvariantCulture) : null;
        }
    }

    /// <summary>Extracts typed references from semantic snapshots, including unchanged structured assets.</summary>
    public static IReadOnlyList<AssetReference> Structured(string family, string asset, JsonElement snapshot)
    {
        List<AssetReference> result = [];
        Visit(snapshot, null);
        return result.Distinct().ToArray();
        void Visit(JsonElement value, string? property)
        {
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var item in value.EnumerateObject()) Visit(item.Value, item.Name);
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) Visit(item, property);
            else if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } name)
            {
                string? target = property switch
                {
                    "sprite" or "mask" => "sprites",
                    "parent" or "object" or "otherObject" => "objects",
                    "code" or "creationCode" or "preCreate" => "code",
                    "background" or "resource" => "backgrounds",
                    _ => null
                };
                if (target is not null) result.Add(new(family, asset, target, name));
            }
        }
    }

    private static List<Token> Tokens(string text)
    {
        List<Token> tokens = [];
        int line = 1;
        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { if (c == '\n') line++; i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                { if (text[i++] == '\n') line++; }
                i = Math.Min(text.Length, i + 2); continue;
            }
            int startLine = line;
            bool raw = c == '@' && i + 1 < text.Length && text[i + 1] is '\'' or '"';
            if (raw) c = text[++i];
            if (c is '\'' or '"')
            {
                char quote = c; i++;
                StringBuilder value = new();
                while (i < text.Length)
                {
                    c = text[i++];
                    if (c == quote) break;
                    if (c == '\n') line++;
                    if (!raw && c == '\\' && i < text.Length)
                    {
                        c = text[i++];
                        c = c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => c };
                    }
                    value.Append(c);
                }
                tokens.Add(new(value.ToString(), 's', startLine, line)); continue;
            }
            int start = i++;
            if (char.IsLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(new(text[start..i], 'i', line, line));
            }
            else if (char.IsDigit(c) || c == '$')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.')) i++;
                tokens.Add(new(text[start..i], 'n', line, line));
            }
            else
            {
                if (i < text.Length && text[i] == '=' && "=+-*/!<>".Contains(c)) i++;
                tokens.Add(new(text[start..i], 'p', line, line));
            }
        }
        return tokens;
    }
}
