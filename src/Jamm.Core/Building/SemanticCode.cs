using Jamm.Core.Patching;
using UndertaleModLib;

namespace Jamm.Core.Building;

// Decompile each source once, before mixing any code. Cached v2 patches may predate CGF typing;
// refresh their code from the hash-verified archives, preserving their non-code patch data.
internal sealed class SemanticCode
{
    internal Dictionary<string, CodeText> Base { get; }
    internal IReadOnlyList<Dictionary<string, CodeText>> Mods { get; }
    private readonly Dictionary<string, string[]> baseNames;
    private readonly Dictionary<string, string[]>[] modNames;
    private readonly HashSet<string> ambiguous;
    private readonly bool legacy;

    internal SemanticCode(UndertaleData original, IReadOnlyList<UndertaleData> sources, Action<string>? progress)
    {
        legacy = !original.IsVersionAtLeast(2);
        baseNames = SemanticReferences.Names(original);
        modNames = sources.Select(SemanticReferences.Names).ToArray();
        ambiguous = AssetSymbols.Ambiguous([baseNames, .. modNames]);
        Base = Read(original, "base", progress, ambiguous);
        Mods = sources.Select((s, i) => Read(s, $"mod {i + 1}", progress, ambiguous)).ToArray();
    }

    private static Dictionary<string, CodeText> Read(UndertaleData data, string label, Action<string>? progress, IReadOnlySet<string> ambiguous)
    {
        progress?.Invoke($"Resolving asset references in {label} code");
        var reader = new CodeDecompiler(data, label, m => progress?.Invoke(m), ambiguous);
        var names = SemanticReferences.Names(data);
        Dictionary<string, CodeText> result = new(StringComparer.Ordinal);
        foreach (var code in data.Code)
        {
            if (result.Count % 50 == 0) progress?.Invoke($"Resolving {label}: {result.Count}/{data.Code.Count} code entries");
            var text = reader.Read(code);
            if (text.Format != "gml") throw new InvalidDataException($"Cannot verify semantic references in {label}, code '{code.Name.Content}': decompilation failed. Use a mod with decompilable legacy GML.");
            result[code.Name.Content] = text with { Text = SemanticReferences.NameTypedLiterals(text.Text, code.Name.Content, names) };
        }
        return result;
    }

    internal ModPatch[] Prepare(IReadOnlyList<ModPatch> patches)
    {
        return patches.Select((patch, i) =>
        {
            var changes = new SortedDictionary<string, AssetChanges>(patch.Changes, StringComparer.Ordinal);
            var existing = changes["code"].Added.Concat(changes["code"].Changed).ToDictionary(a => a.Name, StringComparer.Ordinal);
            List<AssetPatch> added = [], changed = [];
            int index = 0;
            foreach (var (name, text) in Mods[i])
            {
                Base.TryGetValue(name, out var before);
                var asset = existing.GetValueOrDefault(name);
                var effective = asset?.ModCode is { } patchCode
                    ? patchCode with { Text = AssetSymbols.Qualify(SemanticReferences.NameTypedLiterals(legacy ? AssetSymbols.QualifyLegacyNames(patchCode.Text, name, modNames[i]) : patchCode.Text, name, modNames[i]), name, modNames[i], ambiguous) } : text;
                if (asset?.BaseCode is { } patchBase)
                    before = patchBase with { Text = AssetSymbols.Qualify(SemanticReferences.NameTypedLiterals(legacy ? AssetSymbols.QualifyLegacyNames(patchBase.Text, name, baseNames) : patchBase.Text, name, baseNames), name, baseNames, ambiguous) };
                bool same = before is not null && before.Text == effective.Text && before.LocalsCount == effective.LocalsCount
                    && before.ArgumentsCount == effective.ArgumentsCount && before.Parent == effective.Parent && before.Children.SequenceEqual(effective.Children);
                if (same && asset is null) { index++; continue; }
                var snapshot = asset?.Mod ?? AssetFields.Json(new { text.LocalsCount, text.ArgumentsCount, Offset = 0, WeirdLocalFlag = false, text.Parent, text.Children });
                System.Text.Json.JsonElement? baseline = before is null ? null : asset?.Base ?? AssetFields.Json(new { before.LocalsCount, before.ArgumentsCount, Offset = 0, WeirdLocalFlag = false, before.Parent, before.Children });
                var updated = new AssetPatch(name, baseline, snapshot, FieldDiff.Compare(baseline, snapshot), before, effective, before is null ? index : null);
                if (before is null) added.Add(updated); else if (!same || asset is not null) changed.Add(updated);
                index++;
            }
            changes["code"] = new(added.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray(), changed.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray(), changes["code"].Removed);
            return patch with { Changes = changes, AssetTypesVersion = PatchJson.AssetTypesVersion };
        }).ToArray();
    }

    internal Dictionary<string, IReadOnlyList<ResolvedCodeReference>> Expected(BuildPlan plan, UndertaleData merged)
    {
        var selected = plan.Assets.Where(a => a.Family == "code").ToDictionary(a => a.Name, StringComparer.Ordinal);
        // A union catalog recognizes selected names even if a mistaken plan omitted their assets.
        var union = baseNames.Keys.ToDictionary(f => f, f => baseNames[f].Concat(modNames.SelectMany(m => m[f])).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var mergedNames = SemanticReferences.Names(merged);
        Dictionary<string, IReadOnlyList<ResolvedCodeReference>> expected = new(StringComparer.Ordinal);
        foreach (var code in merged.Code.Where(c => c.ParentEntry is null))
        {
            string name = code.Name.Content;
            string text = selected.TryGetValue(name, out var choice) ? choice.Gml! : Base[name].Text;
            var references = SemanticReferences.Resolve(text, name, union);
            foreach (var reference in references)
                if (!mergedNames[reference.Family].Contains(reference.Name, StringComparer.Ordinal))
                    throw new InvalidDataException($"Dangling reference in code '{name}', container '{reference.Container}': {reference.Family}/{reference.Name} is missing from the merged build. Keep the asset or change --resolve.");
            expected[name] = references;
        }
        return expected;
    }

    internal IReadOnlyDictionary<string, int> Remapped(IReadOnlyList<ModPatch> patches, UndertaleData merged)
    {
        var target = SemanticReferences.Names(merged);
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        for (int i = 0; i < patches.Count; i++)
        {
            int count = 0;
            var maps = modNames[i].ToDictionary(t => t.Key, t => new AssetIndexMap(t.Value, target[t.Key]));
            // Count all changed/added source references requiring relocation, including alternatives
            // intentionally dropped by conflict decisions. The selected stream is verified separately.
            foreach (var asset in patches[i].Changes["code"].Added.Concat(patches[i].Changes["code"].Changed))
                foreach (var r in SemanticReferences.Resolve(Mods[i][asset.Name].Text, asset.Name, modNames[i]))
                {
                    int index = Array.IndexOf(modNames[i][r.Family], r.Name);
                    // Dropped conflict alternatives may legitimately name an omitted added asset.
                    if (!target[r.Family].Contains(r.Name, StringComparer.Ordinal)) continue;
                    if (maps[r.Family].Resolve(index, r.Family, $"mod '{patches[i].Mod.Name}', code '{asset.Name}'") != index) count++;
                }
            counts[patches[i].Mod.Name ?? patches[i].Mod.FileName] = count;
        }
        return counts;
    }
}
