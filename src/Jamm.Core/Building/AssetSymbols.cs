using System.Globalization;
using System.Text;
using Jamm.Core.Merging;
using Underanalyzer;
using UndertaleModLib;
using UndertaleModLib.Decompiler;

namespace Jamm.Core.Building;

// GM1.4 has one compiler symbol namespace. Different mods can introduce the same name
// in different families. Carry the family through merging in private, temporary symbols,
// then link only those symbols to final indices immediately before compilation.
internal static class AssetSymbols
{
    private const string Prefix = "__cgfmm_asset_";
    internal static HashSet<string> Ambiguous(IEnumerable<IReadOnlyDictionary<string, string[]>> catalogs) => catalogs
        .SelectMany(c => c.SelectMany(t => t.Value.Select(n => (Name: n, Family: t.Key))))
        .GroupBy(x => x.Name, StringComparer.Ordinal).Where(g => g.Select(x => x.Family).Distinct().Count() > 1)
        .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
    internal static string Symbol(string family, string name) => Prefix + family + "_" + Convert.ToHexString(Encoding.UTF8.GetBytes(name));
    internal static bool Parse(string symbol, out string family, out string name)
    {
        family = name = "";
        if (!symbol.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int end = symbol.IndexOf('_', Prefix.Length);
        if (end < 0) return false;
        family = symbol[Prefix.Length..end];
        try { name = Encoding.UTF8.GetString(Convert.FromHexString(symbol[(end + 1)..])); return true; }
        catch (FormatException) { return false; }
    }
    internal static string Qualify(string text, string entry, IReadOnlyDictionary<string, string[]> source, IReadOnlySet<string> ambiguous)
    {
        var tokens = GmlReferences.Tokens(text);
        string? typed = null;
        List<(GmlReferences.Token Token, string Value)> edits = [];
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Value is ";" or "{") typed = null;
            if (t.Kind != 'i') continue;
            typed = CgfAssetTypes.Family(t.Value, entry) ?? typed;
            if (!ambiguous.Contains(t.Value) || i > 0 && tokens[i - 1].Value == ".") continue;
            string[] families = source.Where(p => p.Value.Contains(t.Value, StringComparer.Ordinal)).Select(p => p.Key).ToArray();
            string? family = families.Length == 1 ? families[0] : families.Contains(typed, StringComparer.Ordinal) ? typed : null;
            if (family is null) throw new InvalidDataException($"Ambiguous asset '{t.Value}' in code '{entry}'. Add its typed container to cgf-asset-types.json before building.");
            edits.Add((t, Symbol(family, t.Value)));
        }
        return Replace(text, edits);
    }
    internal static string Link(string text, string entry, IReadOnlyDictionary<string, string[]> target)
    {
        List<(GmlReferences.Token Token, string Value)> edits = [];
        foreach (var token in GmlReferences.Tokens(text).Where(t => t.Kind == 'i'))
            if (Parse(token.Value, out string family, out string name))
            {
                int index = target.TryGetValue(family, out var names) ? Array.IndexOf(names, name) : -1;
                if (index < 0) throw new InvalidDataException($"Dangling reference in code '{entry}': {family}/{name} is missing. Keep the asset or select another --resolve choice.");
                edits.Add((token, index.ToString(CultureInfo.InvariantCulture)));
            }
        return Replace(text, edits);
    }
    private static string Replace(string text, List<(GmlReferences.Token Token, string Value)> edits)
    {
        StringBuilder result = new(text);
        foreach (var (token, value) in edits.OrderByDescending(e => e.Token.Start)) result.Remove(token.Start, token.Length).Insert(token.Start, value);
        return result.ToString();
    }

    internal sealed class Context(UndertaleData data, IReadOnlySet<string> ambiguous) : GlobalDecompileContext(data), IGameContext
    {
        string? IGameContext.GetAssetName(AssetType type, int index)
        {
            string? name = GetAssetName(type, index);
            if (name is null || !ambiguous.Contains(name)) return name;
            string? family = type switch
            {
                AssetType.Object => "objects", AssetType.Sprite => "sprites", AssetType.Sound => "sounds",
                AssetType.Background => "backgrounds", AssetType.Path => "paths", AssetType.Script => "scripts",
                AssetType.Font => "fonts", AssetType.Timeline => "timelines", AssetType.Room => "rooms", AssetType.Shader => "shaders", _ => null
            };
            return family is null ? name : Symbol(family, name);
        }
    }
}
