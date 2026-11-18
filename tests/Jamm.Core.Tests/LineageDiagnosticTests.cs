using Jamm.Core;

namespace Jamm.Core.Tests;

/// <summary>Lineage classifications using synthetic name scoped fingerprints only.</summary>
public class LineageDiagnosticTests
{
    [Fact]
    public void ClassifiesExactMatchesRestorationsIndependentAndOverlappingEdits()
    {
        var vanilla = File("vanilla", AssetKind.Code,
            ("inherited", "base"), ("restored", "base"), ("independent", "base"), ("overlap", "base"));
        var foundation = File("foundation", AssetKind.Code,
            ("inherited", "foundation"), ("restored", "foundation"), ("independent", "base"), ("overlap", "foundation"));
        var legacy = File("legacy", AssetKind.Code,
            ("inherited", "foundation"), ("restored", "base"), ("independent", "legacy"), ("overlap", "legacy"));

        var report = LineageDiagnostic.Compare(vanilla, foundation, legacy);

        Assert.Collection(report.Items.OrderBy(x => x.Name),
            item => Assert.Equal(("independent", LineageClassification.IndependentModEdit), (item.Name, item.Classification)),
            item => Assert.Equal(("inherited", LineageClassification.ExactFoundationMatch), (item.Name, item.Classification)),
            item => Assert.Equal(("overlap", LineageClassification.OverlappingEditNeedsReview), (item.Name, item.Classification)),
            item => Assert.Equal(("restored", LineageClassification.VanillaRestoration), (item.Name, item.Classification)));
    }

    [Fact]
    public void ReportsCodeAndResourceGranularityAndVanillaRestorations()
    {
        var vanilla = new FileAnalysis("vanilla", null, [], null, new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>
        {
            [AssetKind.Code] = [new("scr_fish", "base")],
            [AssetKind.Sprites] = [new("spr_fish", "base"), new("spr_vanilla", "base")]
        });
        var foundation = new FileAnalysis("foundation", null, [], null, new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>
        {
            [AssetKind.Code] = [new("scr_fish", "base")],
            [AssetKind.Sprites] = [new("spr_fish", "foundation"), new("spr_vanilla", "foundation")]
        });
        var legacy = new FileAnalysis("legacy", null, [], null, new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>
        {
            [AssetKind.Code] = [new("scr_fish", "legacy")],
            [AssetKind.Sprites] = [new("spr_fish", "foundation"), new("spr_vanilla", "base")]
        });

        var report = LineageDiagnostic.Compare(vanilla, foundation, legacy);

        Assert.Collection(report.Items,
            item => Assert.Equal((AssetKind.Code, "scr_fish", LineageClassification.IndependentModEdit),
                (item.AssetType, item.Name, item.Classification)),
            item => Assert.Equal((AssetKind.Sprites, "spr_fish", LineageClassification.ExactFoundationMatch),
                (item.AssetType, item.Name, item.Classification)),
            item => Assert.Equal((AssetKind.Sprites, "spr_vanilla", LineageClassification.VanillaRestoration),
                (item.AssetType, item.Name, item.Classification)));
    }

    private static FileAnalysis File(string name, AssetKind kind, params (string Name, string Fingerprint)[] values) =>
        new(name, null, [], null, new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>
        {
            [kind] = values.Select(x => new AssetFingerprint(x.Name, x.Fingerprint)).ToArray()
        });
}
