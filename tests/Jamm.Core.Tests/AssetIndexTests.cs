using UndertaleModLib;
using Jamm.Core.Building;
using Jamm.Core.Management;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using Jamm.Core.Patching;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Fact]
    public void IndexMapsHandleRemovedBaseEntriesAndOverlappingAdditionIndices()
    {
        var a = new AssetIndexMap(["base_a", "base_b", "new_a"], ["base_a", "base_b", "new_a", "new_b"]);
        var b = new AssetIndexMap(["base_b", "new_b"], ["base_a", "base_b", "new_a", "new_b"]);
        Assert.Equal(2, a.Resolve(2, "objects", "A"));
        Assert.Equal(1, b.Resolve(0, "objects", "B"));
        Assert.Equal(3, b.Resolve(1, "objects", "B"));
        var missing = new AssetIndexMap(["absent"], ["base_a"]);
        Assert.Contains("objects/absent", Assert.Throws<InvalidDataException>(() => missing.Resolve(0, "objects", "example code")).Message);
    }

    [Fact]
    public void TypedAssignmentsBecomeNamesWhileUntypedNumbersCommentsAndStringsStayUntouched()
    {
        var names = new Dictionary<string, string[]> { ["objects"] = ["base_fish", "mod_fish"] };
        const string input = "object[2, 58] = 1; cash = 1; // object[2, 58] = 1;\nlabel = \"object[2, 58] = 1;\";";
        string result = SemanticReferences.NameTypedLiterals(input, "catalog", names);
        Assert.Equal(input.Replace("object[2, 58] = 1; cash", "object[2, 58] = mod_fish; cash", StringComparison.Ordinal), result);
        Assert.Contains("invalid objects index 9", Assert.Throws<InvalidDataException>(() => SemanticReferences.NameTypedLiterals("object[0] = 9;", "catalog", names)).Message);
        Assert.Equal("object[0] = 1 + offset;", SemanticReferences.NameTypedLiterals("object[0] = 1 + offset;", "catalog", names));
        Assert.Equal("fish_target_nearest_fish(mod_fish);", SemanticReferences.NameTypedLiterals("fish_target_nearest_fish(1);", "spawn", names));
        Assert.Equal("target = 1;", SemanticReferences.NameTypedLiterals("target = 1;", "untyped_scope", names));
    }

    [Fact]
    public void SemanticVerifierCatchesWrongNumericIndexWithContainerAndBothNames()
    {
        var source = new Dictionary<string, string[]> { ["objects"] = ["Bombat", "Overbite"] };
        var merged = new Dictionary<string, string[]> { ["objects"] = ["Bombat", "other_fish", "Overbite"] };
        var expected = SemanticReferences.Resolve("object[2, 58] = 1;", "catalog", source);
        SemanticReferences.Verify("catalog", expected, SemanticReferences.Resolve("object[2, 58] = 2;", "catalog", merged));
        var error = Assert.Throws<InvalidDataException>(() => SemanticReferences.Verify("catalog", expected,
            SemanticReferences.Resolve("object[2, 58] = 1;", "catalog", merged)));
        foreach (string detail in new[] { "catalog", "object", "objects/Overbite", "objects/other_fish" }) Assert.Contains(detail, error.Message);
    }

    [Fact]
    public void BuildRetainsBaseIndicesAndRelinksBothModSpacesIncludingUnchangedBytecode()
    {
        using var temp = new TemporaryDirectory();
        string baseline = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(baseline, d =>
        {
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("Bombat") });
            Compile(d, "test_code", "object[0] = 1; cash = 1;");
        });
        WriteModel(aPath, d =>
        {
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("Bombat") });
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("Overbite") });
            Compile(d, "test_code", "object[0] = 1; cash = 1;");
            Compile(d, "added_a", "object[2, 58] = 2; cash = 2;");
        });
        WriteModel(bPath, d =>
        {
            d.Rooms[0].GameObjects.Clear(); d.GameObjects.Clear();
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("Bombat") });
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("CaveFish") });
            // Identical bytecode to the base, but index 1 now means CaveFish.
            Compile(d, "test_code", "object[0] = 1; cash = 1;");
            Compile(d, "added_b", "object[2, 59] = 1; cash = 1;");
        });
        using var original = GameFileLoader.Load(baseline); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        foreach (var inputs in new[] { new[] { new BuildInput(pa, aPath), new BuildInput(pb, bPath) }, new[] { new BuildInput(pb, bPath), new BuildInput(pa, aPath) } })
        {
            string output = temp.PathFor("merged.win");
            var result = ArchiveBuilder.Build(baseline, inputs, output);
            Assert.Equal(3, result.SemanticEntries); Assert.Equal(3, result.Verification!.SemanticEntries);
            Assert.Contains(result.Plan.Decisions, x => x.Contains("retained objects/fish", StringComparison.Ordinal));
            using var file = GameFileLoader.Load(output);
            Assert.Equal("fish", file.Data.GameObjects[0].Name.Content); Assert.Equal("Bombat", file.Data.GameObjects[1].Name.Content);
            Assert.Contains("Overbite", PatchBuilder.ReadCode(file, "added_a").Text);
            Assert.Contains("CaveFish", PatchBuilder.ReadCode(file, "added_b").Text);
            Assert.Contains("CaveFish", PatchBuilder.ReadCode(file, "test_code").Text);
            Assert.Contains("cash = 2", PatchBuilder.ReadCode(file, "added_a").Text);
        }
        static void Compile(UndertaleModLib.UndertaleData data, string name, string text)
        {
            var code = data.Code.FirstOrDefault(c => c.Name.Content == name) ?? UndertaleCode.CreateEmptyEntry(data, name);
            var compiler = new CompileGroup(data); compiler.QueueCodeReplace(code, text); Assert.True(compiler.Compile().Successful);
        }
    }

    [Fact]
    public void SameAssetNameInDifferentModFamiliesLinksToTheCorrectFamilyIndex()
    {
        using var temp = new TemporaryDirectory();
        string baseline = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(baseline, _ => { });
        WriteModel(aPath, d =>
        {
            d.Sprites.Add(new() { Name = d.Strings.MakeString("SharedAsset") });
            var c = UndertaleCode.CreateEmptyEntry(d, "sprite_code"); var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(c, "disp[0] = 0;"); Assert.True(compiler.Compile().Successful);
        });
        WriteModel(bPath, d =>
        {
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("SharedAsset") });
            var c = UndertaleCode.CreateEmptyEntry(d, "object_code"); var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(c, "object[0] = 1;"); Assert.True(compiler.Compile().Successful);
        });
        using var original = GameFileLoader.Load(baseline); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        string output = temp.PathFor("merged.win");
        var result = ArchiveBuilder.Build(baseline, [new(pa, aPath), new(pb, bPath)], output);
        Assert.Equal(3, result.Verification!.SemanticEntries);
        using var merged = GameFileLoader.Load(output);
        Assert.Contains("object[0] = SharedAsset;", PatchBuilder.ReadCode(merged, "object_code").Text);
        Assert.Contains("disp[0] = SharedAsset;", PatchBuilder.ReadCode(merged, "sprite_code").Text);
        Assert.DoesNotContain(merged.Data.Variables, v => v.Name.Content.StartsWith("__cgfmm_asset_", StringComparison.Ordinal));
    }

    [Fact]
    public void ManagedProfilesInvalidateInstallationSignaturesFromBeforeSemanticVerification()
    {
        using var temp = new TemporaryDirectory();
        string baseline = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win"), game = temp.PathFor("game");
        WriteModel(baseline, _ => { }); WriteModel(modPath, d => d.GameObjects[0].Depth = 7);
        Directory.CreateDirectory(game); File.Copy(baseline, System.IO.Path.Combine(game, "data.win"));
        var store = new ManagerStore(temp.PathFor("manager")); store.Set("base", baseline); store.Set("gameDir", game);
        var library = new ModLibrary(store); var mod = library.Add(modPath).Mod;
        var profile = new ProfileLibrary(store).Create("example", [mod.Id]);
        var installer = new ProfileInstaller(store);
        var prepared = installer.Prepare("example");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        string legacy = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            profile.Name, profile.Mods, Resolutions = profile.Resolutions.OrderBy(p => p.Key, StringComparer.Ordinal),
            Base = BuildFiles.Hash(baseline), Game = System.IO.Path.GetFullPath(game), Patches = new[] { library.Patch(mod) }
        }, options))));
        Assert.NotEqual(legacy, prepared.Signature);
        installer.Install(prepared); Assert.True(installer.IsInstalled("example"));
        File.WriteAllText(System.IO.Path.Combine(store.Root, "installed.json"), JsonSerializer.Serialize(new
        { signature = legacy, gameDirectory = game, buildSha256 = BuildFiles.Hash(System.IO.Path.Combine(game, "data.win")) }));
        Assert.False(installer.IsInstalled("example"));
    }

    [Fact]
    public void InstallAndRestoreAllowLinkedParentButRefuseLinkedFilesAndInputAliases()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TemporaryDirectory();
        string real = temp.PathFor("drive"), linked = temp.PathFor("linked"); Directory.CreateDirectory(real);
        string game = System.IO.Path.Combine(real, "game"); Directory.CreateDirectory(game);
        Directory.CreateSymbolicLink(linked, real);
        string aliasGame = System.IO.Path.Combine(linked, "game"), current = System.IO.Path.Combine(game, "data.win");
        File.WriteAllText(current, "original"); string hash = BuildFiles.Hash(current);
        string output = temp.PathFor("build.win"); File.WriteAllText(output, "merged");
        Assert.Throws<IOException>(() => GameInstaller.Validate(aliasGame, hash, [current]));
        GameInstaller.Install(output, aliasGame, hash);
        Assert.Equal("merged", File.ReadAllText(current)); Assert.Equal(hash, GameInstaller.Restore(aliasGame));
        string marker = current + ".cgfmm-build.json"; File.CreateSymbolicLink(marker, output);
        Assert.Throws<IOException>(() => GameInstaller.Validate(aliasGame, hash));
        Assert.Equal("original", File.ReadAllText(current));
    }
}
