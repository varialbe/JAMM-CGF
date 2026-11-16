using Jamm.Core.Building;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Fact]
    public void CancellationAfterSerializationPublishesNothingAndKeepsInputs()
    {
        using var temp = new TemporaryDirectory();
        string original = temp.PathFor("base.win"), mod = temp.PathFor("mod.win"), output = temp.PathFor("output.win");
        WriteModel(original, _ => { }); WriteModel(mod, d => d.GameObjects[0].Depth = 20);
        using var a = GameFileLoader.Load(original); using var b = GameFileLoader.Load(mod);
        var patch = Jamm.Core.Patching.PatchBuilder.Create(a, b);
        string hash = BuildFiles.Hash(original);
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => ArchiveBuilder.Build(original, [new(patch, mod)], output,
            progress: message => { if (message == "Writing temporary merged archive") cancellation.Cancel(); }, cancellationToken: cancellation.Token));
        Assert.False(File.Exists(output)); Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, ".cgfmm-*.tmp"));
        Assert.Equal(hash, BuildFiles.Hash(original));
    }

    [Fact]
    public void HunkChoicesCombineListsWhileSelectingOverlappingReplacements()
    {
        const string ancestor = "start\nanchor\nx = 0;\nend\n";
        var a = Patch("A", ancestor, "start\na = 1;\nanchor\nx = 1;\nend\n");
        var b = Patch("B", ancestor, "start\nb = 2;\nanchor\nx = 2;\nend\n");
        var choices = new Dictionary<string, string> { ["code/test_code#1"] = "keep-both", ["code/test_code#2"] = "A" };
        var plan = BuildPlan.Create([a, b], choices);
        Assert.Equal("start\na = 1;\nb = 2;\nanchor\nx = 1;\nend\n", Assert.Single(plan.Assets).Gml);
        Assert.Contains(plan.Decisions, d => d.Contains("code/test_code#1: explicit keep-both"));
        Assert.Throws<ArgumentException>(() => BuildPlan.Create([a, b], new Dictionary<string, string> { ["code/test_code"] = "keep-both" }));
        choices["code/test_code#2"] = "keep-both";
        Assert.Throws<ArgumentException>(() => BuildPlan.Create([a, b], choices));
        choices["code/test_code#2"] = "missing";
        Assert.Throws<ArgumentException>(() => BuildPlan.Create([a, b], choices));
        choices["code/test_code#2"] = "B"; choices["code/test_code#999"] = "A";
        Assert.Throws<ArgumentException>(() => BuildPlan.Create([a, b], choices));
        var wholeWithOverride = new Dictionary<string, string> { ["test_code"] = "keep-both", ["code/test_code#2"] = "A" };
        Assert.Equal(Assert.Single(plan.Assets).Gml, Assert.Single(BuildPlan.Create([a, b], wholeWithOverride).Assets).Gml);
    }
}
