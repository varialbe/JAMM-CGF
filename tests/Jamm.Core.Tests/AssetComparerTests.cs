using Jamm.Core;

namespace Jamm.Core.Tests;

/// <summary>Pure classification, metadata and feasibility threshold checks.</summary>
public class AssetComparerTests
{
    [Fact]
    public void ClassifiesAllFourKindsAndCountsOriginalAssets()
    {
        var result = AssetComparer.CompareAssets("code",
            [new("same", "1"), new("edit", "2"), new("gone", "3")],
            [new("same", "1"), new("edit", "4"), new("new", "5")]);
        Assert.Equal((1, 1, 1, 1, 3),
            (result.Identical, result.Changed, result.Added, result.Removed, result.OriginalCount));
        Assert.Equal(AssetClassification.Changed, result.Assets.Single(x => x.Name == "edit").Classification);
    }

    [Fact]
    public void PairsIdenticalDuplicatesFirstRegardlessOfOrder()
    {
        AssetFingerprint[] original = [new("v", "A"), new("v", "B"), new("v", "C")];
        AssetFingerprint[] mod = [new("v", "D"), new("v", "C"), new("v", "B"), new("v", "E")];
        var result = AssetComparer.CompareAssets("variables", original, mod);
        var reversed = AssetComparer.CompareAssets("variables", original.Reverse(), mod.Reverse());
        Assert.Equal((2, 1, 1, 0), (result.Identical, result.Changed, result.Added, result.Removed));
        Assert.Equal(result.Assets, reversed.Assets);
    }

    [Fact]
    public void NameMatchingIsOrdinalAndDoesNotMatchRenamesByHash()
    {
        var result = AssetComparer.CompareAssets("objects", [new("Obj", "A")], [new("obj", "A")]);
        Assert.Equal((0, 0, 1, 1), (result.Identical, result.Changed, result.Added, result.Removed));
    }

    [Theory]
    [InlineData(100, 100, "LIKELY SAME BASE")]
    [InlineData(80, 100, "LIKELY SAME BASE")]
    [InlineData(79, 100, "POSSIBLY DIFFERENT VERSION")]
    [InlineData(40, 100, "POSSIBLY DIFFERENT VERSION")]
    [InlineData(39, 100, "DIFFERENT BASE")]
    [InlineData(0, 100, "DIFFERENT BASE")]
    [InlineData(0, 0, "INDETERMINATE (no original bytecode)")]
    public void VerdictUsesExactThresholds(int same, int total, string expected) =>
        Assert.Equal(expected, AssetComparer.GetVerdict(same, total));

    [Fact]
    public void AdditionsDoNotDiluteOriginalUnchangedPercentAndMetadataIsIndependent()
    {
        var original = File("original", [new("A", "1"), new("B", "2")]);
        var mod = File("mod", [new("A", "1"), new("B", "3"), new("C", "4")]);
        mod = mod with { Inspection = mod.Inspection! with { GameMakerVersion = "2.0.0.0", BytecodeVersion = 17, GameName = "other" } };
        var result = AssetComparer.Compare(original, mod);
        Assert.Equal(50, result.OriginalAssetsUnchangedPercent);
        Assert.Equal(50, result.OriginalCodeUnchangedPercent);
        Assert.Equal("POSSIBLY DIFFERENT VERSION", result.Verdict);
        Assert.False(result.SameGameMakerVersion);
        Assert.False(result.SameBytecodeVersion);
        Assert.False(result.SameGameName);
    }

    [Fact]
    public void YycCannotProducePositiveBaseVerdict()
    {
        var original = File("original", [new("A", "1")]);
        var mod = File("mod", [new("A", "1")]);
        mod = mod with { Inspection = mod.Inspection! with { IsYyc = true } };
        var result = AssetComparer.Compare(original, mod);
        Assert.False(result.HasComparableBytecode);
        Assert.StartsWith("MERGING IMPOSSIBLE", result.Verdict);
    }

    private static FileAnalysis File(string path, AssetFingerprint[] code) => new(path,
        new(path, 0, "hash", "game", "game", "1.0.0.0", "1.0.0.0", "Pre2022_0", 16, 0, false, true,
            new Dictionary<string, int> { ["code"] = code.Length }), [], null,
        new Dictionary<string, IReadOnlyList<AssetFingerprint>> { ["code"] = code });
}
