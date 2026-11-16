using System.Globalization;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>An untyped integer that could denote an added or shifted source asset; candidates require human review.</summary>
public sealed record AssetLiteralFinding(string Mod, string Code, string Container, int Line, int Value, IReadOnlyList<string> CandidateFamilies);

/// <summary>Finds suspicious literal IDs without changing archives or guessing types from numeric ranges.</summary>
public static class AssetLiteralAudit
{
    /// <summary>Scans all root mod GML after known container and built-in argument types have been resolved.</summary>
    public static IReadOnlyList<AssetLiteralFinding> Analyze(LoadedGameFile original, LoadedGameFile mod)
    {
        PatchBuilder.Validate(original.Inspection, mod.Inspection);
        var baseline = SemanticReferences.Names(original.Data);
        var names = SemanticReferences.Names(mod.Data);
        var reader = new CodeDecompiler(mod.Data, mod.Inspection.FileName, _ => { });
        List<AssetLiteralFinding> result = [];
        foreach (var code in mod.Data.Code.Where(c => c.ParentEntry is null))
        {
            var source = reader.Read(code);
            if (source.Format != "gml") throw new InvalidDataException($"Cannot audit '{mod.Inspection.FileName}', code '{code.Name.Content}': GML decompilation failed.");
            string text = SemanticReferences.NameTypedLiterals(source.Text, code.Name.Content, names);
            string container = "expression";
            foreach (var token in GmlReferences.Tokens(text))
            {
                if (token.Value is ";" or "{") container = "expression";
                if (token.Kind == 'i') container = token.Value;
                if (token.Kind != 'n' || !int.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) continue;
                string[] families = names.Where(t => value >= 0 && value < t.Value.Length
                    && (value >= baseline[t.Key].Length || t.Value[value] != baseline[t.Key][value])).Select(t => t.Key).Order(StringComparer.Ordinal).ToArray();
                if (families.Length > 0) result.Add(new(mod.Inspection.FileName, code.Name.Content, container, token.Line, value, families));
            }
        }
        return result;
    }
}
