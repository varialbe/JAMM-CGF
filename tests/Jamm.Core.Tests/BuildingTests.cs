using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.Tests;

/// <summary>Build decisions, field application, compiler failures and immutable installation backups.</summary>
public sealed partial class BuildingTests
{
    [Fact]
    public void CodeResolutionsPreserveDisjointChangesAndSupportInsertionUnion()
    {
        var a = Patch("A", "start\nend\n", "start\na = 1;\nend\n");
        var b = Patch("B", "start\nend\n", "start\nb = 2;\nend\n");
        Assert.Equal("start\nb = 2;\nend\n", Assert.Single(BuildPlan.Create([a, b]).Assets).Gml);
        Assert.Equal("start\na = 1;\nb = 2;\nend\n", Assert.Single(BuildPlan.Create([a, b], new Dictionary<string, string> { ["test_code"] = "keep-both" }).Assets).Gml);
        Assert.Equal("start\na = 1;\nend\n", Assert.Single(BuildPlan.Create([a, b], new Dictionary<string, string> { ["test_code"] = "A" }).Assets).Gml);
        Assert.Throws<ArgumentException>(() => BuildPlan.Create([a, b], new Dictionary<string, string> { ["test_code"] = "unknown" }));
        b = Patch("B", "start\nend\n", "changed\nend\n");
        Assert.Equal("changed\na = 1;\nend\n", Assert.Single(BuildPlan.Create([a, b]).Assets).Gml);
    }

    [Fact]
    public void BuildAppliesFieldsUnionsRoomInstancesAndRelinksCollisionObjects()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(basePath, _ => { });
        WriteModel(aPath, d => { d.GameObjects[0].Depth = 12; d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = d.GameObjects[0], X = 50, Y = 50 }); });
        WriteModel(bPath, d => { d.GameObjects[0].Visible = false; d.Rooms[0].GameObjects[0].ScaleX = 2; });
        using var original = GameFileLoader.Load(basePath);
        using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        string output = temp.PathFor("merged.win");
        var result = ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], output);
        Assert.NotNull(result.Verification);
        using var merged = GameFileLoader.Load(output);
        Assert.Equal(12, merged.Data.GameObjects[0].Depth);
        Assert.False(merged.Data.GameObjects[0].Visible);
        Assert.Equal(2, merged.Data.Rooms[0].GameObjects.Count);
        Assert.Equal(2, merged.Data.Rooms[0].GameObjects.Single(i => i.X == 10).ScaleX);
        Assert.Equal(42u, merged.Data.Rooms[0].GameObjects.Single(i => i.X == 10).InstanceID);
        Assert.NotEqual(42u, merged.Data.Rooms[0].GameObjects.Single(i => i.X == 50).InstanceID);
        Assert.Equal(original.Inspection.Sha256, BuildFiles.Hash(basePath));
        Assert.Throws<IOException>(() => ArchiveBuilder.Build(basePath, [new(pa, aPath)], basePath));
        Assert.Throws<IOException>(() => ArchiveBuilder.Build(basePath, [new(pa, aPath)], aPath));
        Assert.NotNull(ArchiveBuilder.Build(basePath, [new(pa, aPath)], output).Verification);
        Assert.Throws<InvalidDataException>(() => ArchiveBuilder.Build(basePath, [new(pa with { Mod = pa.Mod with { Sha256 = "wrong" } }, aPath)], temp.PathFor("wrong.win")));
    }

    [Fact]
    public void DryRunAndCompilationFailureNeverWriteOutputOrCreateItsDirectory()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win"), output = temp.PathFor("absent/output.win");
        WriteModel(basePath, _ => { }); WriteModel(modPath, d => d.GameObjects[0].Depth = 7);
        using var original = GameFileLoader.Load(basePath); using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod);
        Assert.Null(ArchiveBuilder.Build(basePath, [new(patch, modPath)], output, dryRun: true).Verification);
        Assert.False(Directory.Exists(System.IO.Path.GetDirectoryName(output)));
        var code = new AssetPatch("test_code", JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [],
            new("", "gml", 0, 0, null, []), new("if (", "gml", 0, 0, null, []));
        patch.Changes["code"] = new([], [code], []);
        var ex = Assert.Throws<InvalidDataException>(() => ArchiveBuilder.Build(basePath, [new(patch, modPath)], output));
        Assert.Contains("test_code", ex.Message);
        Assert.Contains("Compilation failed", ex.Message);
        Assert.False(File.Exists(output));
        Assert.False(Directory.Exists(System.IO.Path.GetDirectoryName(output)));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "previous verified output");
        Assert.Throws<InvalidDataException>(() => ArchiveBuilder.Build(basePath, [new(patch, modPath)], output));
        Assert.Equal("previous verified output", File.ReadAllText(output));
    }

    [Fact]
    public void BackupIsVerifiedPermanentAndUnknownInstallationsAreRefused()
    {
        using var temp = new TemporaryDirectory();
        string game = temp.PathFor("game"); Directory.CreateDirectory(game);
        string current = System.IO.Path.Combine(game, "data.win"), backup = current + ".cgfmm-original";
        string build = temp.PathFor("merged.win"); File.WriteAllText(current, "original"); File.WriteAllText(build, "build one");
        string baseHash = BuildFiles.Hash(current);
        GameInstaller.Install(build, game, baseHash);
        Assert.Equal("original", File.ReadAllText(backup)); Assert.Equal("build one", File.ReadAllText(current));
        File.WriteAllText(build, "build two"); GameInstaller.Install(build, game, baseHash);
        Assert.Equal("original", File.ReadAllText(backup));
        Assert.Equal(baseHash, GameInstaller.Restore(game)); Assert.Equal("original", File.ReadAllText(current));
        Assert.True(File.Exists(backup));
        File.WriteAllText(current, "unrecognized mod");
        Assert.Throws<IOException>(() => GameInstaller.Install(build, game, baseHash));
        Assert.Equal("unrecognized mod", File.ReadAllText(current));
        File.WriteAllText(current, "original");
        Assert.Throws<IOException>(() => GameInstaller.Validate(game, baseHash, [current]));
        GameInstaller.Install(build, game, baseHash);
        File.WriteAllText(backup, "corrupted");
        Assert.Throws<IOException>(() => GameInstaller.Restore(game));
        Assert.Equal("build two", File.ReadAllText(current));
    }

    [Fact]
    public void LinkedOutputsAndInstallFilesCannotAliasInputs()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TemporaryDirectory();
        string original = temp.PathFor("original.win"); File.WriteAllText(original, "base");
        string alias = temp.PathFor("alias.win"); File.CreateSymbolicLink(alias, original);
        Assert.Throws<IOException>(() => BuildFiles.ValidateNewOutput(alias, [original]));
        string game = temp.PathFor("game"); Directory.CreateDirectory(game);
        File.CreateSymbolicLink(System.IO.Path.Combine(game, "data.win"), original);
        Assert.Throws<IOException>(() => GameInstaller.Validate(game, BuildFiles.Hash(original)));
        Assert.Equal("base", File.ReadAllText(original));
    }

    [Fact]
    public void StructuredFamiliesCombineFieldsAndDefinitionsAreRetained()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(basePath, d => d.Paths.Add(new() { Name = d.Strings.MakeString("route"), Precision = 4 }));
        WriteModel(aPath, d =>
        {
            d.Paths.Add(new() { Name = d.Strings.MakeString("route"), Precision = 8 });
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("z_first") });
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("a_second") });
            d.GameObjects[0].Events[4].Add(new() { EventSubtype = 2, Actions = [new() { CodeId = d.Code[0], ActionName = d.Strings.MakeString("") }] });
        });
        WriteModel(bPath, d => d.Paths.Add(new() { Name = d.Strings.MakeString("route"), Precision = 4, IsClosed = true }));
        using var original = GameFileLoader.Load(basePath); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        // Definition removals are informational even if a patch requests them explicitly.
        pa.Changes["strings"] = new([], [], [original.Data.Strings[0].Content]);
        string output = temp.PathFor("result.win");
        var result = ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], output);
        using var merged = GameFileLoader.Load(output);
        Assert.Equal(8u, merged.Data.Paths[0].Precision); Assert.True(merged.Data.Paths[0].IsClosed);
        Assert.Equal(new[] { "fish", "z_first", "a_second" }, merged.Data.GameObjects.Select(o => o.Name.Content));
        uint other = Assert.Single(merged.Data.GameObjects[0].Events[4]).EventSubtype;
        Assert.Equal("a_second", merged.Data.GameObjects[(int)other].Name.Content);
        Assert.Contains(merged.Data.Strings, x => x.Content == original.Data.Strings[0].Content);
        Assert.Contains(result.Plan.Decisions, d => d.Contains("retain base definition", StringComparison.Ordinal));
    }

    [Fact]
    public void LaterSurvivingReferencesWinOverEarlierAssetDeletions()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(basePath, _ => { });
        WriteModel(aPath, d => { d.Rooms[0].GameObjects.Clear(); d.GameObjects.Clear(); });
        WriteModel(bPath, d => d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = d.GameObjects[0], X = 100, Y = 100 }));
        using var original = GameFileLoader.Load(basePath); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        var result = ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], temp.PathFor("result.win"));
        Assert.Contains(result.Plan.Decisions, d => d.Contains("retained objects/fish", StringComparison.Ordinal));
        using var merged = GameFileLoader.Load(temp.PathFor("result.win"));
        Assert.Single(merged.Data.GameObjects); Assert.Single(merged.Data.Rooms[0].GameObjects);
        var chosen = BuildPlan.Create([pb, pa], new Dictionary<string, string> { ["fish"] = "B" });
        Assert.DoesNotContain(chosen.Assets, x => x.Family == "objects" && x.Name == "fish");
    }

    [Fact]
    public void BinaryImportsKeepEachModsPixelsAndPayloadReferences()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), aPath = temp.PathFor("a.win"), bPath = temp.PathFor("b.win");
        WriteModel(basePath, _ => { });
        WriteModel(aPath, d => AddSprite(d, "a_sprite", 20));
        WriteModel(bPath, d => AddSprite(d, "b_sprite", 80));
        using var original = GameFileLoader.Load(basePath); using var a = GameFileLoader.Load(aPath); using var b = GameFileLoader.Load(bPath);
        var pa = PatchBuilder.Create(original, a, "A"); var pb = PatchBuilder.Create(original, b, "B");
        string output = temp.PathFor("result.win");
        ArchiveBuilder.Build(basePath, [new(pa, aPath), new(pb, bPath)], output);
        using var merged = GameFileLoader.Load(output);
        Assert.Equal(2, merged.Data.EmbeddedTextures.Count);
        Assert.Equal(2, merged.Data.TexturePageItems.Count);
        Assert.Single(merged.Data.Strings, s => s.Content == "shared display name");
        foreach (var source in new[] { a, b })
        {
            var expected = source.Data.Sprites.Single();
            var actual = merged.Data.Sprites.Single(s => s.Name.Content == expected.Name.Content);
            Assert.Equal(new ContentFingerprints().Region(expected.Textures[0].Texture), new ContentFingerprints().Region(actual.Textures[0].Texture));
            Assert.Contains(actual.Textures[0].Texture, merged.Data.TexturePageItems);
            Assert.Contains(actual.Textures[0].Texture.TexturePage, merged.Data.EmbeddedTextures);
        }
        static void AddSprite(UndertaleData data, string name, byte colour)
        {
            var image = new GMImage(1, 1); image.GetRawImageData()[0] = colour;
            var page = new UndertaleEmbeddedTexture { Name = new("Texture 0"), TextureData = new() { Image = image.ConvertToPng() } };
            var item = new UndertaleTexturePageItem { Name = new("PageItem 0"), TexturePage = page,
                SourceWidth = 1, SourceHeight = 1, TargetWidth = 1, TargetHeight = 1, BoundingWidth = 1, BoundingHeight = 1 };
            data.EmbeddedTextures.Add(page); data.TexturePageItems.Add(item);
            data.Sprites.Add(new() { Name = data.Strings.MakeString(name), Width = 1, Height = 1, Textures = [new() { Texture = item }] });
            data.Fonts.Add(new() { Name = data.Strings.MakeString(name + "_font"), DisplayName = data.Strings.MakeString("shared display name"), Texture = item });
            data.EmbeddedTextures.Add(new() { Name = new("unused"), TextureData = new() { Image = image.ConvertToPng() } });
        }
    }

    [Fact]
    public void CliDryRunWritesNothingAndInstallCannotUseItsBackupAsOutput()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win"), output = temp.PathFor("new/output.win");
        WriteModel(basePath, _ => { }); WriteModel(modPath, d => d.GameObjects[0].Depth = 8);
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("build", ["--base", basePath, "--order", modPath, "--out", output, "--dry-run"]));
        Assert.False(Directory.Exists(System.IO.Path.GetDirectoryName(output)));
        string game = temp.PathFor("game"); Directory.CreateDirectory(game); File.Copy(basePath, System.IO.Path.Combine(game, "data.win"));
        string backup = System.IO.Path.Combine(game, "data.win.cgfmm-original");
        Assert.Throws<IOException>(() => Jamm.Cli.BuildCommand.Run("build", ["--base", basePath, "--order", modPath, "--out", backup, "--install", game]));
        Assert.False(File.Exists(backup)); Assert.False(Directory.Exists(System.IO.Path.Combine(game, "patches")));
    }

    [Fact]
    public void CustomUiSpritesHonorOverwriteOrderingExplicitChoicesAndHybridUpgradeIcons()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string customAPath = temp.PathFor("custom-a.win");
        string customBPath = temp.PathFor("custom-b.win");
        string hybridPath = temp.PathFor("hybrid.win");

        WriteSpriteModel(basePath, rodColour: 10, upgradeColours: [20, 30], includeUpgradeRef: false);
        WriteSpriteModel(customAPath, rodColour: 110, upgradeColours: [120, 130], includeUpgradeRef: false);
        WriteSpriteModel(customBPath, rodColour: 210, upgradeColours: [220, 230], includeUpgradeRef: false);
        WriteSpriteModel(hybridPath, rodColour: 50, upgradeColours: [60, 70, 80], includeUpgradeRef: true);

        using var orig = GameFileLoader.Load(basePath);
        using var customA = GameFileLoader.Load(customAPath);
        using var customB = GameFileLoader.Load(customBPath);
        using var hybrid = GameFileLoader.Load(hybridPath);

        var patchA = PatchBuilder.Create(orig, customA, "CustomA");
        var patchB = PatchBuilder.Create(orig, customB, "CustomB");
        var rawHybrid = PatchBuilder.Create(orig, hybrid, "HybridMod");
        var sanitizedHybrid = ModSanitizer.Sanitize(rawHybrid).Patch;

        // 1. Overwrite ordering and explicit choices on custom UI sprites (spr_rodcard).
        string outAB = temp.PathFor("ui-ab.win");
        ArchiveBuilder.Build(basePath, [new(patchA, customAPath), new(patchB, customBPath)], outAB);
        using (var merged = GameFileLoader.Load(outAB))
            Assert.Equal(RegionHash(customB, "spr_rodcard", 0), RegionHash(merged, "spr_rodcard", 0));

        string outBA = temp.PathFor("ui-ba.win");
        ArchiveBuilder.Build(basePath, [new(patchB, customBPath), new(patchA, customAPath)], outBA);
        using (var merged = GameFileLoader.Load(outBA))
            Assert.Equal(RegionHash(customA, "spr_rodcard", 0), RegionHash(merged, "spr_rodcard", 0));

        string outResolved = temp.PathFor("ui-resolved.win");
        ArchiveBuilder.Build(basePath, [new(patchA, customAPath), new(patchB, customBPath)], outResolved,
            resolutions: new Dictionary<string, string>(StringComparer.Ordinal) { ["sprites/spr_rodcard"] = "CustomA" });
        using (var merged = GameFileLoader.Load(outResolved))
            Assert.Equal(RegionHash(customA, "spr_rodcard", 0), RegionHash(merged, "spr_rodcard", 0));

        // 2. Custom UI mod (CustomA) + sanitized hybrid spr_upgradeicons appender (HybridMod) in BOTH load orders
        // preserves CustomA's spr_rodcard + custom spr_upgradeicons base frames 0..1 AND HybridMod's appended frame 2.
        foreach (var (orderName, inputs) in new[]
        {
            ("custom-first", new[] { new BuildInput(patchA, customAPath), new BuildInput(sanitizedHybrid, hybridPath) }),
            ("hybrid-first", new[] { new BuildInput(sanitizedHybrid, hybridPath), new BuildInput(patchA, customAPath) })
        })
        {
            string outMerged = temp.PathFor($"hybrid-{orderName}.win");
            var res = ArchiveBuilder.Build(basePath, inputs, outMerged);
            Assert.NotNull(res.Verification);
            using var merged = GameFileLoader.Load(outMerged);
            Assert.Equal(RegionHash(customA, "spr_rodcard", 0), RegionHash(merged, "spr_rodcard", 0));
            var upg = merged.Data.Sprites.ByName("spr_upgradeicons");
            Assert.NotNull(upg);
            Assert.Equal(3, upg.Textures.Count);
            Assert.Equal(RegionHash(customA, "spr_upgradeicons", 0), RegionHash(merged, "spr_upgradeicons", 0));
            Assert.Equal(RegionHash(customA, "spr_upgradeicons", 1), RegionHash(merged, "spr_upgradeicons", 1));
            Assert.Equal(RegionHash(hybrid, "spr_upgradeicons", 2), RegionHash(merged, "spr_upgradeicons", 2));
        }

        static string RegionHash(LoadedGameFile file, string spriteName, int frame)
            => new ContentFingerprints().Region(file.Data.Sprites.ByName(spriteName).Textures[frame].Texture);

        static void WriteSpriteModel(string path, byte rodColour, byte[] upgradeColours, bool includeUpgradeRef)
        {
            using var data = UndertaleData.CreateNew();
            data.GeneralInfo.Major = 1;
            data.GeneralInfo.Build = 1773;
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Object_UpgradeData_Create_0");
            var compiler = new CompileGroup(data);
            compiler.QueueCodeReplace(code, includeUpgradeRef
                ? "unlocks[0] = 0;\nunlocks[1] = 1;\nname[0] = \"Base\";\nname[1] = \"Added\";\n"
                : "unlocks[0] = 0;\nname[0] = \"Base\";\n");
            Assert.True(compiler.Compile().Successful);
            AddSprite(data, "spr_rodcard", [rodColour]);
            AddSprite(data, "spr_upgradeicons", upgradeColours);
            using var stream = File.Create(path);
            UndertaleIO.Write(stream, data);
        }

        static void AddSprite(UndertaleData data, string name, byte[] colours)
        {
            var spr = new UndertaleSprite { Name = data.Strings.MakeString(name), Width = 4, Height = 4, MarginRight = 3, MarginBottom = 3 };
            for (int i = 0; i < colours.Length; i++)
            {
                var image = new GMImage(4, 4);
                image.GetRawImageData()[0] = colours[i];
                var page = new UndertaleEmbeddedTexture { Name = data.Strings.MakeString($"Page_{name}_{i}"), TextureData = new() { Image = image.ConvertToPng() } };
                var item = new UndertaleTexturePageItem
                {
                    Name = data.Strings.MakeString($"Item_{name}_{i}"),
                    TexturePage = page,
                    SourceWidth = 4,
                    SourceHeight = 4,
                    TargetWidth = 4,
                    TargetHeight = 4,
                    BoundingWidth = 4,
                    BoundingHeight = 4
                };
                data.EmbeddedTextures.Add(page);
                data.TexturePageItems.Add(item);
                spr.Textures.Add(new() { Texture = item });
            }
            data.Sprites.Add(spr);
        }
    }

    private static ModPatch Patch(string name, string ancestor, string text)
    {
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
        foreach (string family in new[] { "code", "scripts", "objects", "rooms", "sprites", "texturePages", "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions" }) changes[family] = new([], [], []);
        changes["code"] = new([], [new("test_code", JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [],
            new(ancestor, "gml", 0, 0, null, []), new(text, "gml", 0, 0, null, []))], []);
        return new(2, "test", DateTimeOffset.UnixEpoch, new(name + ".win", name, 1, "1.4", 16, name), new("base.win", "base", 1, "1.4", 16), [], changes, new(), []);
    }

    private static void WriteModel(string path, Action<UndertaleData> edit)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1; data.GeneralInfo.Build = 1773;
        var code = UndertaleCode.CreateEmptyEntry(data, "test_code");
        var compiler = new CompileGroup(data); compiler.QueueCodeReplace(code, "x = 1;\ny = 2;\n");
        Assert.True(compiler.Compile().Successful);
        data.GameObjects.Add(new() { Name = data.Strings.MakeString("fish") });
        data.Rooms[0].GameObjects.Add(new() { ObjectDefinition = data.GameObjects[0], X = 10, Y = 20, InstanceID = 42 });
        edit(data);
        using var stream = File.Create(path); UndertaleIO.Write(stream, data);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgfmm-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => System.IO.Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
