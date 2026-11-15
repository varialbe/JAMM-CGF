namespace Jamm.Core;

/// <summary>Pure name-based, ordinal asset comparison with multiplicity preserved.</summary>
public static class AssetComparer
{
    /// <summary>Classifies a multiset of assets; identical duplicates are paired before changed ones.</summary>
    public static AssetComparison CompareAssets(string assetType,
        IEnumerable<AssetFingerprint> original, IEnumerable<AssetFingerprint> mod)
    {
        var left = original.GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var right = mod.GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        List<AssetDifference> differences = [];
        foreach (string name in left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var originals = left.GetValueOrDefault(name) ?? [];
            var mods = right.GetValueOrDefault(name)?.OrderBy(x => x.Fingerprint, StringComparer.Ordinal).ToList() ?? [];
            List<AssetFingerprint> unmatched = [];
            foreach (var asset in originals.OrderBy(x => x.Fingerprint, StringComparer.Ordinal))
            {
                int index = mods.FindIndex(x => x.Fingerprint == asset.Fingerprint);
                if (index < 0) unmatched.Add(asset);
                else
                {
                    differences.Add(new(name, AssetClassification.Identical, asset.Fingerprint, mods[index].Fingerprint));
                    mods.RemoveAt(index);
                }
            }
            for (int i = 0; i < Math.Max(unmatched.Count, mods.Count); i++)
            {
                string? before = i < unmatched.Count ? unmatched[i].Fingerprint : null;
                string? after = i < mods.Count ? mods[i].Fingerprint : null;
                var classification = before is null ? AssetClassification.Added
                    : after is null ? AssetClassification.Removed : AssetClassification.Changed;
                differences.Add(new(name, classification, before, after));
            }
        }
        return new(assetType,
            differences.Count(x => x.Classification == AssetClassification.Identical),
            differences.Count(x => x.Classification == AssetClassification.Changed),
            differences.Count(x => x.Classification == AssetClassification.Added),
            differences.Count(x => x.Classification == AssetClassification.Removed), differences);
    }

    /// <summary>Returns a conservative base verdict; no original code cannot establish a base.</summary>
    public static string GetVerdict(int identicalCode, int originalCode)
    {
        if (originalCode <= 0) return "INDETERMINATE (no original bytecode)";
        double ratio = (double)identicalCode / originalCode;
        return ratio >= 0.8 ? "LIKELY SAME BASE"
            : ratio >= 0.4 ? "POSSIBLY DIFFERENT VERSION" : "DIFFERENT BASE";
    }

    /// <summary>Compares loaded VM files, including metadata and percentages relative to the original.</summary>
    public static ModComparison Compare(FileAnalysis original, FileAnalysis mod)
    {
        var a = original.Inspection ?? throw new ArgumentException("The original could not be read.", nameof(original));
        var b = mod.Inspection ?? throw new ArgumentException("The mod could not be read.", nameof(mod));
        var types = !a.BytecodeSupported || !b.BytecodeSupported ? [] : original.Assets.Keys.Union(mod.Assets.Keys, StringComparer.Ordinal)
            .Select(type => CompareAssets(type, original.Assets.GetValueOrDefault(type) ?? [],
                mod.Assets.GetValueOrDefault(type) ?? [])).ToArray();
        var code = types.FirstOrDefault(x => x.AssetType == "code");
        int total = types.Sum(x => x.OriginalCount);
        bool comparable = !a.IsYyc && !b.IsYyc && a.BytecodeSupported && b.BytecodeSupported;
        return new(a.Path, b.Path,
            a.GameMakerVersion == b.GameMakerVersion && a.DetectedGameMakerVersion == b.DetectedGameMakerVersion
                && a.DetectedBranch == b.DetectedBranch,
            a.BytecodeVersion == b.BytecodeVersion, a.GameName == b.GameName, comparable,
            total == 0 ? 0 : 100.0 * types.Sum(x => x.Identical) / total,
            code is null || code.OriginalCount == 0 ? 0 : 100.0 * code.Identical / code.OriginalCount,
            !comparable ? a.IsYyc || b.IsYyc ? "MERGING IMPOSSIBLE (no bytecode in YYC file)"
                    : "MERGING IMPOSSIBLE (unsupported bytecode)"
                : GetVerdict(code?.Identical ?? 0, code?.OriginalCount ?? 0), types);
    }
}
