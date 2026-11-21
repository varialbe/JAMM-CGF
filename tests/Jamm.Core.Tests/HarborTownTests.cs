using Jamm.Core.ModKit;

namespace Jamm.Core.Tests;

/// <summary>Checks the Harbor Town package's economy, visual stages, and per-slot persistence contract.</summary>
public sealed class HarborTownTests
{
    private static readonly string Package = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../examples/modkit/harbor-town"));

    [Fact]
    public void PackageHasThreeDockBuildingsAndOnlyUsesRecipeAssets()
    {
        var package = ModKitPackage.Load(Package);

        Assert.Equal("harbor-town", package.Id);
        Assert.Equal("0.1.0", package.Version);
        Assert.Collection(package.Sprites, _ => { }, _ => { }, _ => { }, _ => { });
        Assert.NotNull(Assert.Single(package.Objects).DrawGui);
        Assert.Contains(package.Areas, area => area.Anchor.Target?.Object == "Cliff");
        string manifest = File.ReadAllText(Path.Combine(Package, "jamm-mod.json"));
        Assert.Contains("\"#3b6f58\"", manifest);
        Assert.Contains("\"#4e7e6a\"", manifest);
        Assert.Contains("\"tint\": \"#6e4a1e\"", manifest);
        Assert.Equal(new[] { "bonus", "buy", "command", "cost", "fish", "format", "save", "stage" },
            package.Scripts.Select(script => script.Key).Order().ToArray());
        Assert.Empty(Directory.EnumerateFiles(Package, "*.png", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(0, 25000, 40000, 10000)]
    [InlineData(1, 40000, 64000, 16000)]
    [InlineData(40, 3653754093327, 5846006549324, 1461501637331)]
    [InlineData(50, 12788139326645, 20461022922633, 5115255730658)]
    public void CostScriptMatchesDocumentedCurve(int level, long lighthouse, long aquarium, long statue)
    {
        string source = File.ReadAllText(Path.Combine(Package, "scripts/cost.gml"));
        Assert.Contains("power(1.6, min(40, level))", source);
        Assert.Contains("0.25 * max(0, level - 40)", source);

        Assert.Equal(lighthouse, Cost(25000, level));
        Assert.Equal(aquarium, Cost(40000, level));
        Assert.Equal(statue, Cost(10000, level));
    }

    [Fact]
    public void StagesAndLighthouseBonusRemainCappedAndDiminishing()
    {
        string stage = File.ReadAllText(Path.Combine(Package, "scripts/stage.gml"));
        string bonus = File.ReadAllText(Path.Combine(Package, "scripts/bonus.gml"));

        Assert.Contains("level >= 5", stage);
        Assert.Contains("level >= 20", stage);
        Assert.Contains("level >= 100", stage);
        Assert.Contains("min(0.40, 0.03 * ln(1 + max(0, argument0)))", bonus);
        Assert.Equal(0, Bonus(0));
        Assert.True(Bonus(100) < 0.40);
        Assert.Equal(0.40, Bonus(1e12));
    }

    [Fact]
    public void SaveScriptReadsAndWritesOnlyTheHarborSection()
    {
        string source = File.ReadAllText(Path.Combine(Package, "scripts/save.gml"));

        Assert.Contains("ini_write_string(\"JAMM_Harbor\", harbor_key[i]", source);
        Assert.Contains("ini_read_real(\"JAMM_Harbor\", harbor_key[i], 0)", source);
        Assert.Contains("if (argument0 && !harbor_ready) exit", source);
        Assert.Contains("harbor_ready = true", source);
    }

    private static long Cost(double baseCost, int level)
    {
        double value = baseCost * Math.Pow(1.6, Math.Min(40, Math.Max(0, level)));
        value *= 1 + 0.25 * Math.Max(0, level - 40);
        return (long)Math.Min(1e15, Math.Round(value));
    }

    private static double Bonus(double level) => Math.Min(0.40, 0.03 * Math.Log(1 + Math.Max(0, level)));
}
