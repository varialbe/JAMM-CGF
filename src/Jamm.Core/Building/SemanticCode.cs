using Jamm.Core.Patching;
using UndertaleModLib;

namespace Jamm.Core.Building;

// Decompile each source once, before mixing any code. Cached v2 patches may predate CGF typing;
// refresh their code from the hash-verified archives, preserving their non-code patch data.
internal sealed class SemanticCode
{
    internal Dictionary<string, CodeText> Base { get; }
    internal IReadOnlyList<Dictionary<string, CodeText>> Mods { get; }
    internal IReadOnlyDictionary<string, string[]> BaseNames => baseNames;
    private readonly Dictionary<string, string[]> baseNames;
    private readonly Dictionary<string, string[]>[] modNames;
    private readonly HashSet<string> ambiguous;
    private readonly bool legacy;
    private readonly AnalysisOptions options;
    private readonly Action<string>? progress;

    internal SemanticCode(UndertaleData original, IReadOnlyList<UndertaleData> sources, Action<string>? progress, AnalysisOptions options, string baseHash)
    {
        this.options = options;
        this.progress = progress;
        legacy = !original.IsVersionAtLeast(2);
        baseNames = SemanticReferences.Names(original);
        modNames = sources.Select(SemanticReferences.Names).ToArray();
        ambiguous = AssetSymbols.Ambiguous([baseNames, .. modNames]);
        Base = Read(original, "base", progress, ambiguous, options, baseHash);
        Mods = sources.Select((s, i) => Read(s, $"mod {i + 1}", progress, ambiguous, options)).ToArray();
    }

    internal static Dictionary<string, CodeText> Read(UndertaleData data, string label, Action<string>? progress, IReadOnlySet<string> ambiguous, AnalysisOptions options, string? baseHash = null)
    {
        progress?.Invoke($"Resolving asset references in {label} code");
        var entries = CodeDecompiler.ReadAll(data, label, m => progress?.Invoke(m), ambiguous, options, progress, baseHash);
        var names = SemanticReferences.Names(data);
        var codes = data.Code.ToArray();
        var normalized = new CodeText[codes.Length];
        AnalysisWork.For(codes.Length, options, i =>
        {
            var code = codes[i];
            var text = entries[code.Name.Content];
            if (text.Format != "gml") throw new InvalidDataException($"Cannot verify semantic references in {label}, code '{code.Name.Content}': decompilation failed. Use a mod with decompilable legacy GML.");
            normalized[i] = text with { Text = SemanticReferences.NameTypedLiterals(text.Text, code.Name.Content, names) };
        });
        var result = codes.Select((code, i) => (code.Name.Content, normalized[i])).ToDictionary(x => x.Content, x => x.Item2, StringComparer.Ordinal);
        return result;
    }

    internal ModPatch[] Prepare(IReadOnlyList<ModPatch> patches)
    {
        var result = new ModPatch[patches.Count];
        AnalysisWork.For(patches.Count, options, i =>
        {
            var patch = patches[i];
            var changes = new SortedDictionary<string, AssetChanges>(patch.Changes, StringComparer.Ordinal);
            var existing = changes["code"].Added.Concat(changes["code"].Changed).ToDictionary(a => a.Name, StringComparer.Ordinal);
            List<AssetPatch> added = [], changed = [];
            int index = 0;
            foreach (var (name, text) in Mods[i])
            {
                Base.TryGetValue(name, out var before);
                var asset = existing.GetValueOrDefault(name);
                var effective = asset?.ModCode is { } patchCode
                    ? patchCode with { Text = AssetSymbols.Qualify(SemanticReferences.NameAddedAssetLiterals(
                        SemanticReferences.NameTypedLiterals(legacy ? AssetSymbols.QualifyLegacyNames(patchCode.Text, name, modNames[i]) : patchCode.Text, name, modNames[i]),
                        name, baseNames, modNames[i], message => progress?.Invoke($"WARNING: {message}")), name, modNames[i], ambiguous) }
                    : text with { Text = AssetSymbols.Qualify(SemanticReferences.NameAddedAssetLiterals(
                        SemanticReferences.NameTypedLiterals(text.Text, name, modNames[i]), name, baseNames, modNames[i], message => progress?.Invoke($"WARNING: {message}")), name, modNames[i], ambiguous) };
                if (asset?.BaseCode is { } patchBase)
                    before = patchBase with { Text = AssetSymbols.Qualify(SemanticReferences.NameTypedLiterals(legacy ? AssetSymbols.QualifyLegacyNames(patchBase.Text, name, baseNames) : patchBase.Text, name, baseNames), name, baseNames, ambiguous) };
                bool same = before is not null && before.Text == effective.Text && before.LocalsCount == effective.LocalsCount
                    && before.ArgumentsCount == effective.ArgumentsCount && before.Parent == effective.Parent && before.Children.SequenceEqual(effective.Children);
                if ((same || patch.AssetTypesVersion == PatchJson.AssetTypesVersion) && asset is null) { index++; continue; }
                var snapshot = asset?.Mod ?? AssetFields.Json(new { text.LocalsCount, text.ArgumentsCount, Offset = 0, WeirdLocalFlag = false, text.Parent, text.Children });
                System.Text.Json.JsonElement? baseline = before is null ? null : asset?.Base ?? AssetFields.Json(new { before.LocalsCount, before.ArgumentsCount, Offset = 0, WeirdLocalFlag = false, before.Parent, before.Children });
                var updated = new AssetPatch(name, baseline, snapshot, FieldDiff.Compare(baseline, snapshot), before, effective, before is null ? index : null);
                if (before is null) added.Add(updated); else if (!same || asset is not null) changed.Add(updated);
                index++;
            }
            changes["code"] = new(added.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray(), changed.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray(), changes["code"].Removed);
            result[i] = patch with { Changes = changes, AssetTypesVersion = PatchJson.AssetTypesVersion };
        });
        if (Base.TryGetValue(CatalogMerge.Create, out var catalogBase)
            && !result.Any(p => p.Changes["code"].Changed.Any(a => a.Name == CatalogMerge.Create)))
        {
            var snapshot = AssetFields.Json(new { catalogBase.LocalsCount, catalogBase.ArgumentsCount });
            var anchor = new AssetPatch(CatalogMerge.Create, snapshot, snapshot, [], catalogBase, catalogBase);
            var changes = new SortedDictionary<string, AssetChanges>(result[0].Changes, StringComparer.Ordinal);
            changes["code"] = changes["code"] with { Changed = [.. changes["code"].Changed, anchor] };
            result[0] = result[0] with { Changes = changes };
        }
        return result;
    }

    internal Dictionary<string, IReadOnlyList<ResolvedCodeReference>> Expected(BuildPlan plan, UndertaleData merged, IReadOnlyDictionary<string, string> generatedCode)
    {
        var selected = plan.Assets.Where(a => a.Family == "code").ToDictionary(a => a.Name, StringComparer.Ordinal);
        // A union catalog recognizes selected names even if a mistaken plan omitted their assets.
        var union = baseNames.Keys.ToDictionary(f => f, f => baseNames[f].Concat(modNames.SelectMany(m => m[f])).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var mergedNames = SemanticReferences.Names(merged);
        foreach (string family in union.Keys.ToArray())
            union[family] = union[family].Concat(mergedNames[family]).Distinct(StringComparer.Ordinal).ToArray();
        Dictionary<string, IReadOnlyList<ResolvedCodeReference>> expected = new(StringComparer.Ordinal);
        foreach (var code in merged.Code.Where(c => c.ParentEntry is null))
        {
            string name = code.Name.Content;
            string text = generatedCode.TryGetValue(name, out var generated) ? generated
                : selected.TryGetValue(name, out var choice) ? choice.Gml!
                : name.StartsWith("gml_Script_", StringComparison.Ordinal) && CatalogRuntime.Scripts.TryGetValue(name[11..], out var runtime) ? runtime
                : name.StartsWith("gml_Script_", StringComparison.Ordinal) && HudRuntime.Scripts.TryGetValue(name[11..], out var hud) ? hud
                : name.StartsWith("gml_Script_", StringComparison.Ordinal) && ItemsRuntimeScripts.DefaultScripts.TryGetValue(name[11..], out var items) ? items
                : Base[name].Text;
            var references = SemanticReferences.Resolve(text, name, union);
            foreach (var reference in references)
                if (!mergedNames[reference.Family].Contains(reference.Name, StringComparer.Ordinal))
                    throw new InvalidDataException($"Dangling reference in code '{name}', container '{reference.Container}': {reference.Family}/{reference.Name} is missing from the merged build. Keep the asset or change --resolve.");
            expected[name] = references;
        }
        return expected;
    }

    internal IReadOnlyDictionary<string, int> Remapped(IReadOnlyList<ModPatch> patches, UndertaleData merged,
        IReadOnlyDictionary<(int Mod, string Family, string Original), string>? renames = null)
    {
        var target = SemanticReferences.Names(merged);
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        for (int i = 0; i < patches.Count; i++)
        {
            int count = 0;
            // Count all changed/added source references requiring relocation, including alternatives
            // intentionally dropped by conflict decisions. The selected stream is verified separately.
            foreach (var asset in patches[i].Changes["code"].Added.Concat(patches[i].Changes["code"].Changed))
                foreach (var r in SemanticReferences.Resolve(asset.ModCode?.Text ?? Mods[i][asset.Name].Text, asset.Name, modNames[i]))
                {
                    int index = Array.IndexOf(modNames[i][r.Family], r.Name);
                    // Dropped conflict alternatives may legitimately name an omitted added asset.
                    string destinationName = renames?.GetValueOrDefault((i, r.Family, r.Name)) ?? r.Name;
                    int destinationIndex = Array.IndexOf(target[r.Family], destinationName);
                    if (destinationIndex < 0) continue;
                    if (destinationIndex != index) count++;
                }
            counts[patches[i].Mod.Name ?? patches[i].Mod.FileName] = count;
        }
        return counts;
    }
}
