using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Fact]
    public void CatalogSyntheticArchivesCompileRelocateSaveAndRecordMapping()
    {
        using var temp = new TemporaryDirectory();
        const string createName = "gml_Object_CatalogData_Create_0";
        const string saveName = "gml_Object_SaveManager_Other_13";
        string baseline = "object[0,0] = fish; name[0,0] = \"Fish\"; map[0,0] = 0; desc[0,0] = \"Base fish\";";
        string basePath = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(basePath, d => Setup(d, null)); WriteModel(aPath, d => Setup(d, "Alpha")); WriteModel(bPath, d => Setup(d, "Beta"));
        using var original = GameFileLoader.Load(basePath); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], output);
        Assert.NotNull(build.Verification);
        string second = temp.PathFor("merged-again.win");
        ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], second);
        Assert.Equal(BuildFiles.Hash(output), BuildFiles.Hash(second));
        string create = build.Plan.Assets.Single(a => a.Name == createName).Gml!;
        Assert.Contains("object[0, 2] = Beta;", create);
        Assert.Contains("jamm_catalog_legacy[0, 0] = 0;", create);
        string save = build.Plan.Assets.Single(a => a.Name == saveName).Gml!;
        Assert.Contains("jamm_catalog_read(\"f\", i, j, -1)", save);
        using (var merged = GameFileLoader.Load(output))
        {
            Assert.Contains(merged.Data.Scripts, s => s.Name.Content == "jamm_catalog_register");
            Assert.Contains(merged.Data.Scripts, s => s.Name.Content == "jamm_catalog_read");
        }
        string game = temp.PathFor("game"); Directory.CreateDirectory(game); File.Copy(basePath, System.IO.Path.Combine(game, "data.win"));
        GameInstaller.Install(output, game, original.Inspection.Sha256, catalog: build.Plan.Catalog);
        string marker = File.ReadAllText(System.IO.Path.Combine(game, "data.win.cgfmm-build.json"));
        Assert.Contains("\"object\": \"Beta\"", marker);
        Assert.Contains("\"slot\": 2", marker);
        Assert.Equal(original.Inspection.Sha256, GameInstaller.Restore(game));

        void Setup(UndertaleData d, string? fish)
        {
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("CatalogData") });
            var create = UndertaleCode.CreateEmptyEntry(d, createName);
            string text = baseline;
            if (fish is not null)
            {
                d.GameObjects.Add(new() { Name = d.Strings.MakeString(fish) });
                text += $"object[0,1] = {fish}; name[0,1] = \"{fish}\"; map[0,1] = 1; desc[0,1] = \"Added\";";
            }
            var save = UndertaleCode.CreateEmptyEntry(d, saveName);
            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(create, text);
            compiler.QueueCodeReplace(save, "with (CatalogData) { i=0; j=0; count[i,j] = ini_read_real(\"Catalog\", \"f\" + string(i) + string(j), -1); ini_write_real(\"Catalog\", \"li\" + string(i) + string(j), life[i,j]); }");
            var result = compiler.Compile(); Assert.True(result.Successful);
        }
    }
}
