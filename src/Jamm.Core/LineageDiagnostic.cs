namespace Jamm.Core;

/// <summary>Classifies a legacy mod's content relative to vanilla and an explicit foundation reference.</summary>
public enum LineageClassification
{
    /// <summary>The legacy mod has the same changed content as the supplied foundation.</summary>
    ExactFoundationMatch,
    /// <summary>The foundation changed this asset, while the legacy mod retains vanilla content.</summary>
    VanillaRestoration,
    /// <summary>The legacy mod changed this asset while the foundation retains vanilla content.</summary>
    IndependentModEdit,
    /// <summary>Both sources changed this asset differently; inspect it before combining changes.</summary>
    OverlappingEditNeedsReview
}

/// <summary>A name scoped result exposing content fingerprints, never extracted game content.</summary>
public sealed record LineageItem(AssetKind AssetType, string Name, LineageClassification Classification);

/// <summary>Read-only three source comparison. Exact matches describe content equality, not historical ancestry.</summary>
public sealed record LineageReport(string VanillaName, string FoundationName, string LegacyModName,
    IReadOnlyList<LineageItem> Items);

/// <summary>Compares a vanilla reference, an explicitly selected foundation, and a legacy mod.</summary>
public static class LineageDiagnostic
{
    /// <summary>Classifies changed named assets. A foundation match is an exact fingerprint match, not proof of ancestry.</summary>
    public static LineageReport Compare(FileAnalysis vanilla, FileAnalysis foundation, FileAnalysis legacyMod)
    {
        ArgumentNullException.ThrowIfNull(vanilla);
        ArgumentNullException.ThrowIfNull(foundation);
        ArgumentNullException.ThrowIfNull(legacyMod);
        if (vanilla.Error is not null || foundation.Error is not null || legacyMod.Error is not null)
            throw new ArgumentException("All three source files must be successfully analyzed before comparing lineage.");

        var items = new List<LineageItem>();
        var kinds = vanilla.Assets.Keys.Union(foundation.Assets.Keys).Union(legacyMod.Assets.Keys).Distinct().Order();
        foreach (AssetKind kind in kinds)
        {
            var baseAssets = vanilla.Assets.GetValueOrDefault(kind) ?? [];
            var foundationAssets = foundation.Assets.GetValueOrDefault(kind) ?? [];
            var legacyAssets = legacyMod.Assets.GetValueOrDefault(kind) ?? [];
            // Reuse the same multiset comparison semantics as diff/compat reports, including duplicate assets.
            var baseToFoundation = AssetComparer.CompareAssets(kind, baseAssets, foundationAssets);
            var baseToLegacy = AssetComparer.CompareAssets(kind, baseAssets, legacyAssets);
            var foundationToLegacy = AssetComparer.CompareAssets(kind, foundationAssets, legacyAssets);
            var changedNames = baseToFoundation.Assets.Concat(baseToLegacy.Assets).Concat(foundationToLegacy.Assets)
                .Where(x => x.Classification != AssetClassification.Identical)
                .Select(x => x.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

            foreach (string name in changedNames)
            {
                string[] before = Values(baseAssets, name);
                string[] foundationValues = Values(foundationAssets, name);
                string[] legacyValues = Values(legacyAssets, name);
                bool foundationChanged = !before.SequenceEqual(foundationValues, StringComparer.Ordinal);
                bool legacyChanged = !before.SequenceEqual(legacyValues, StringComparer.Ordinal);
                if (!legacyChanged && !foundationChanged) continue;

                LineageClassification classification;
                if (foundationChanged && foundationValues.SequenceEqual(legacyValues, StringComparer.Ordinal))
                    classification = LineageClassification.ExactFoundationMatch;
                else if (!legacyChanged)
                    classification = LineageClassification.VanillaRestoration;
                else if (!foundationChanged)
                    classification = LineageClassification.IndependentModEdit;
                else
                    classification = LineageClassification.OverlappingEditNeedsReview;
                items.Add(new(kind, name, classification));
            }
        }
        return new(vanilla.FileName, foundation.FileName, legacyMod.FileName, items);
    }

    private static string[] Values(IReadOnlyList<AssetFingerprint> assets, string name) => assets
        .Where(x => string.Equals(x.Name, name, StringComparison.Ordinal))
        .Select(x => x.Fingerprint).Order(StringComparer.Ordinal).ToArray();
}
