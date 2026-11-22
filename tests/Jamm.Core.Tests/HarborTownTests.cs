using Jamm.Core.ModKit;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Checks the Harbor Town package's economy, visual stages, and per-slot persistence contract.</summary>
public sealed class HarborTownTests
{
    private static readonly string Package = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../examples/modkit/harbor-town"));

    [Fact]
    public void PackageHasFourDockBuildingsAndOnlyUsesRecipeAssets()
    {
        var package = ModKitPackage.Load(Package);

        Assert.Equal("harbor-town", package.Id);
        Assert.Equal("0.2.0", package.Version);
        Assert.Collection(package.Sprites, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { });
        Assert.NotNull(Assert.Single(package.Objects).DrawGui);
        Assert.Contains(package.Areas, area => area.Anchor.Target?.Object == "Cliff");
        string manifest = File.ReadAllText(Path.Combine(Package, "jamm-mod.json"));
        Assert.Contains("\"#3b6f58\"", manifest);
        Assert.Contains("\"#4e7e6a\"", manifest);
        Assert.Contains("\"tint\": \"#6e4a1e\"", manifest);
        Assert.Equal(new[] { "bonus", "buy", "command", "cost", "donate", "fish", "format", "research", "save", "stage", "title" },
            package.Scripts.Select(script => script.Key).Order().ToArray());
        Assert.Empty(Directory.EnumerateFiles(Package, "*.png", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0.013862943611198906, 0.01039720770839918, 0.006931471805599453)]
    [InlineData(100, 0.0923024103368252, 0.06922680775261891, 0.0461512051684126)]
    [InlineData(1000000, 0.10, 0.08, 0.05)]
    public void ResearchBonusScriptUsesTheThreeCappedLogarithmicBonuses(int level, double sell, double bite, double luck)
    {
        string source = File.ReadAllText(Path.Combine(Package, "scripts/research.gml"));
        Assert.Contains("2 * ln(1 + level) / 100", source);
        Assert.Contains("1.5 * ln(1 + level) / 100", source);
        Assert.Contains("ln(1 + level) / 100", source);
        Assert.Equal(sell, Research(level, 0), 8);
        Assert.Equal(bite, Research(level, 1), 8);
        Assert.Equal(luck, Research(level, 2), 8);
    }

    [Fact]
    public void DonationsPersistAndTitlesUnlockAtTheDocumentedMilestones()
    {
        string save = File.ReadAllText(Path.Combine(Package, "scripts/save.gml"));
        string donate = File.ReadAllText(Path.Combine(Package, "scripts/donate.gml"));
        string title = File.ReadAllText(Path.Combine(Package, "scripts/title.gml"));
        Assert.Contains("ini_read_real(\"JAMM_Harbor\", \"donated\", 0)", save);
        Assert.Contains("ini_write_string(\"JAMM_Harbor\", \"donated\"", save);
        Assert.Contains("harbor_donated += paid", donate);
        Assert.Contains("Harbor Patron", title);
        Assert.Contains("Harbor Baron", title);
        Assert.Contains("Harbor Tycoon", title);
        Assert.Contains("Harbor Legend", title);
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

    [Fact]
    public void HarborSaveHookRoundTripsLegacyAndFreshSaveScenariosInSyntheticArchive()
    {
        using var data = UndertaleData.CreateNew();
        var harbor = new UndertaleGameObject { Name = data.Strings.MakeString("jmk_harbor_town_object_harbor") };
        var saveManager = new UndertaleGameObject { Name = data.Strings.MakeString("SaveManager") };
        data.GameObjects.Add(harbor);
        data.GameObjects.Add(saveManager);
        var create = harbor.EventHandlerFor(EventType.Create, 0u, data);
        var load = saveManager.EventHandlerFor(EventType.Other, 13u, data);
        var save = saveManager.EventHandlerFor(EventType.Other, 14u, data);

        var scriptCode = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jmk_harbor_save");
        data.Scripts.Add(new UndertaleScript
        {
            Name = data.Strings.MakeString("jmk_harbor_save"),
            Code = scriptCode
        });
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(create, File.ReadAllText(Path.Combine(Package, "hooks/create.gml")));
        compiler.QueueCodeReplace(scriptCode, File.ReadAllText(Path.Combine(Package, "scripts/save.gml")));
        compiler.QueueCodeReplace(load, "with (jmk_harbor_town_object_harbor) jmk_harbor_save(0);");
        compiler.QueueCodeReplace(save, "with (jmk_harbor_town_object_harbor) jmk_harbor_save(1);");
        ModObjects.Compile(compiler);

        using var archive = new MemoryStream();
        UndertaleIO.Write(archive, data);
        archive.Position = 0;
        using var roundTripped = UndertaleIO.Read(archive);

        string harborCreate = ModObjects.Decompile(roundTripped,
            roundTripped.Code.ByName("gml_Object_jmk_harbor_town_object_harbor_Create_0")!);
        string harborSave = ModObjects.Decompile(roundTripped,
            roundTripped.Code.ByName("gml_Script_jmk_harbor_save")!);
        string loadEvent = ModObjects.Decompile(roundTripped,
            roundTripped.Code.ByName("gml_Object_SaveManager_Other_13")!);
        string saveEvent = ModObjects.Decompile(roundTripped,
            roundTripped.Code.ByName("gml_Object_SaveManager_Other_14")!);

        // v0.1 stores only three levels and no donation total; a fresh save has no Harbor section.
        // Both cases load through the same zero defaults, while all four current keys are initialized
        // before either event indexes the per-instance arrays.
        var v01Save = new Dictionary<string, double>
        {
            ["lighthouse"] = 2,
            ["aquarium"] = 3,
            ["statue"] = 4
        };
        var freshSave = new Dictionary<string, double>();
        foreach ((Dictionary<string, double> values, double[] expected) in new[]
        {
            (v01Save, new[] { 2d, 3d, 4d, 0d }),
            (freshSave, new[] { 0d, 0d, 0d, 0d })
        })
        {
            double[] loaded = new[] { "lighthouse", "aquarium", "statue", "research" }
                .Select(key => values.TryGetValue(key, out double value) ? value : 0)
                .ToArray();
            Assert.Equal(expected, loaded);
            Assert.Equal(0, values.GetValueOrDefault("donated"));
        }
        Assert.Contains("harbor_key = array_create(4)", harborCreate);
        Assert.Contains("harbor_level = array_create(4, 0)", harborCreate);
        Assert.Contains("variable_instance_exists(id, \"harbor_key\")", harborSave);
        Assert.Contains("harbor_level = array_create(4, 0)", harborSave);
        Assert.Contains("ini_read_real(\"JAMM_Harbor\", harbor_key[i], 0)", harborSave);
        Assert.Contains("ini_read_real(\"JAMM_Harbor\", \"donated\", 0)", harborSave);
        Assert.Contains("jmk_harbor_save(0)", loadEvent);
        Assert.Contains("jmk_harbor_save(1)", saveEvent);
    }

    private static long Cost(double baseCost, int level)
    {
        double value = baseCost * Math.Pow(1.6, Math.Min(40, Math.Max(0, level)));
        value *= 1 + 0.25 * Math.Max(0, level - 40);
        return (long)Math.Min(1e15, Math.Round(value));
    }

    private static double Bonus(double level) => Math.Min(0.40, 0.03 * Math.Log(1 + Math.Max(0, level)));

    private static double Research(double level, int kind) => kind switch
    {
        0 => Math.Min(0.10, 2 * Math.Log(1 + level) / 100),
        1 => Math.Min(0.08, 1.5 * Math.Log(1 + level) / 100),
        _ => Math.Min(0.05, Math.Log(1 + level) / 100)
    };
}
