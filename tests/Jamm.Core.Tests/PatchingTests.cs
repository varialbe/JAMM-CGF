using System.Text.Json;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.Tests;

/// <summary>Semantic diffs, line edits, portable serialization and opt-in real archive comparisons.</summary>
public class PatchingTests
{
    private static readonly PatchFile Base = new("base.win", "base-hash", 1, "1.0.0.1773", 16);
    private static readonly PatchFile Mod = new("mod.win", "mod-hash", 1, "1.0.0.1773", 16, "Synthetic");

    private static UndertaleData Model()
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        var code = UndertaleCode.CreateEmptyEntry(data, "test_code");
        code.Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Exit, Type1 = UndertaleInstruction.DataType.Int32 });
        code.UpdateLength();
        data.GameObjects.Add(new() { Name = data.Strings.MakeString("fish") });
        data.Rooms[0].GameObjects.Add(new() { ObjectDefinition = data.GameObjects[0], X = 10, Y = 20, InstanceID = 42 });
        return data;
    }

    [Fact]
    public void ObjectEventsAndRoomInstancesUseSemanticKeysAndIgnoreIds()
    {
        using var a = Model(); using var b = Model();
        b.Rooms[0].GameObjects[0].InstanceID = 999;
        var unchanged = PatchBuilder.Create(a, b, Base, Mod);
        Assert.Empty(unchanged.Changes["rooms"].Changed);
        b.Rooms[0].GameObjects[0].Rotation = 45;
        b.Rooms[0].GameObjects.Add(new() { ObjectDefinition = b.GameObjects[0], X = 30, Y = 40 });
        b.GameObjects[0].Events[0].Add(new() { EventSubtype = 0, Actions = [new() { CodeId = b.Code[0] }] });
        b.GameObjects[0].Depth = 12;
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        var obj = Assert.Single(patch.Changes["objects"].Changed);
        Assert.Contains(obj.Fields, f => f.Path == "/depth");
        Assert.Contains(obj.Fields, f => f.Path == "/events/0:0");
        var room = Assert.Single(patch.Changes["rooms"].Changed);
        Assert.Contains(room.Fields, f => f.Path.EndsWith("/rotation", StringComparison.Ordinal));
        Assert.Contains(room.Fields, f => f.Base is null && f.Path.StartsWith("/instances/", StringComparison.Ordinal));
        b.GameObjects[0].Events[0].Clear();
        a.GameObjects[0].Events[0].Add(new() { EventSubtype = 0, Actions = [new() { CodeId = a.Code[0] }] });
        Assert.Contains(Assert.Single(PatchBuilder.Create(a, b, Base, Mod).Changes["objects"].Changed).Fields,
            f => f.Path == "/events/0:0" && f.Mod is null);
    }

    private static UndertaleSprite Swf(byte payload) => new()
    {
        Name = new("swf"), IsSpecialType = true, SSpriteType = UndertaleSprite.SpriteType.SWF, SWFVersion = 8,
        YYSWF = new() { Version = 8, JPEGTable = [payload], Timeline = new() { UsedItems = [], Frames = [], CollisionMasks = [] } }
    };

    [Fact]
    public void SwfPayloadChangesSurviveProjectionAndPortableRoundTrip()
    {
        using var a = Model(); using var b = Model(); using var c = Model();
        a.Sprites.Add(Swf(1)); b.Sprites.Add(Swf(2)); c.Sprites.Add(Swf(3));
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        var change = Assert.Single(patch.Changes["sprites"].Changed);
        Assert.Contains(change.Fields, f => f.Path == "/specialContentHash");
        var portable = PatchJson.Deserialize(PatchJson.Serialize(patch));
        Assert.Equal(3, ConflictAnalyzer.Analyze([portable, PatchBuilder.Create(a, c, Base, Mod with { Name = "C" })]).ExitCode);
        b.Sprites[0].YYSWF.JPEGTable = [1];
        Assert.Empty(PatchBuilder.Create(a, b, Base, Mod).Changes["sprites"].Changed);
        b.Sprites[0].YYSWF.Timeline.UsedItems.Add(new()
        {
            ItemType = UndertaleYYSWFItemType.ItemBitmap,
            BitmapData = new() { Width = 1, Height = 1, ImageData = [42] }
        });
        Assert.Single(PatchBuilder.Create(a, b, Base, Mod).Changes["sprites"].Changed);
    }

    [Fact]
    public void UnknownSpritePayloadsRemainExplicitUnresolvedChangesEvenWithEqualMetadata()
    {
        using var a = Model(); using var b = Model();
        var first = Swf(1); var second = Swf(2);
        first.YYSWF.Timeline.UsedItems.Add(new() { ItemType = UndertaleYYSWFItemType.ItemInvalid });
        second.YYSWF.Timeline.UsedItems.Add(new() { ItemType = UndertaleYYSWFItemType.ItemInvalid });
        a.Sprites.Add(first); b.Sprites.Add(second);
        var patch = PatchJson.Deserialize(PatchJson.Serialize(PatchBuilder.Create(a, b, Base, Mod)));
        var asset = Assert.Single(patch.Changes["sprites"].Changed);
        Assert.StartsWith("unsupported:", asset.Mod.GetProperty("specialContentHash").GetString());
        using var unchanged = Model();
        var empty = PatchBuilder.Create(unchanged, unchanged, Base, Mod with { Name = "Other" });
        var report = ConflictAnalyzer.Analyze([patch, empty]);
        Assert.Equal(3, report.ExitCode);
        Assert.Contains(report.Findings, f => f.Asset == "swf" && f.Kind == "binary" && f.Severity == "CONFLICT");
    }

    [Fact]
    public void SpinePayloadEditsAreHashedWithoutExportingTheirContent()
    {
        using var a = Model(); using var b = Model();
        foreach (var data in new[] { a, b })
            data.Sprites.Add(new() { Name = new("spine"), IsSpecialType = true, SSpriteType = UndertaleSprite.SpriteType.Spine,
                SpineVersion = 2, SpineJSON = "private-json", SpineAtlas = "private-atlas",
                SpineTextures = [new() { PageWidth = 1, PageHeight = 1, TexBlob = [1] }] });
        Assert.Empty(PatchBuilder.Create(a, b, Base, Mod).Changes["sprites"].Changed);
        b.Sprites[0].SpineTextures[0].TexBlob = [2];
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        Assert.Single(patch.Changes["sprites"].Changed);
        Assert.DoesNotContain("private-json", PatchJson.Serialize(patch));
        Assert.DoesNotContain("private-atlas", PatchJson.Serialize(patch));
    }

    [Fact]
    public void StringsAreValuesAndVariablesIncludeInstanceType()
    {
        using var a = Model(); using var b = Model();
        a.Strings.Add(new("removed")); b.Strings.Add(new("added")); b.Strings.Add(new("added"));
        a.Variables.Add(new() { Name = new("v"), InstanceType = UndertaleInstruction.InstanceType.Self });
        b.Variables.Add(new() { Name = new("v"), InstanceType = UndertaleInstruction.InstanceType.Global });
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        Assert.Equal("added", Assert.Single(patch.Changes["strings"].Added).Name);
        Assert.Equal("removed", Assert.Single(patch.Changes["strings"].Removed));
        Assert.Empty(patch.Changes["strings"].Changed);
        Assert.Contains("Self", Assert.Single(patch.Changes["variables"].Removed));
        Assert.Contains("Global", Assert.Single(patch.Changes["variables"].Added).Name);
    }

    [Fact]
    public void CodeIsDecompiledOnlyForCandidatesAndStoresBothTextsAndCounts()
    {
        using var a = Model(); using var b = Model();
        List<string> progress = [];
        Assert.Empty(PatchBuilder.Create(a, b, Base, Mod, progress.Add).Changes["code"].Changed);
        Assert.DoesNotContain(progress, p => p.StartsWith("Decompiling", StringComparison.Ordinal));
        b.Code[0].Instructions.Clear();
        b.Code[0].Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Push, Type1 = UndertaleInstruction.DataType.Int16, ValueShort = 7 });
        b.Code[0].Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Ret, Type1 = UndertaleInstruction.DataType.Variable });
        b.Code[0].UpdateLength();
        b.Code[0].ArgumentsCount = 2;
        var code = Assert.Single(PatchBuilder.Create(a, b, Base, Mod).Changes["code"].Changed);
        Assert.NotNull(code.BaseCode); Assert.NotNull(code.ModCode);
        Assert.Equal("gml", code.BaseCode.Format); Assert.Equal("gml", code.ModCode.Format);
        Assert.NotEqual(code.BaseCode.Text, code.ModCode.Text);
        Assert.Equal(2, code.ModCode.ArgumentsCount);
    }

    [Fact]
    public void PatchRoundTripsDeterministicallyAndRejectsFutureVersions()
    {
        using var a = Model(); using var b = Model();
        b.GameObjects[0].Visible = false;
        b.Sprites.Add(new() { Name = new("star") });
        b.GameObjects[0].Sprite = b.Sprites[0];
        var first = PatchBuilder.Create(a, b, Base, Mod);
        var second = PatchBuilder.Create(a, b, Base, Mod) with { CreatedUtc = first.CreatedUtc };
        string json = PatchJson.Serialize(first);
        Assert.Equal(json, PatchJson.Serialize(second));
        Assert.Equal(json, PatchJson.Serialize(PatchJson.Deserialize(json)));
        Assert.DoesNotContain("Undertale", json);
        Assert.Throws<InvalidDataException>(() => PatchJson.Deserialize(json.Replace($"\"formatVersion\": {PatchJson.FormatVersion}", "\"formatVersion\": 99")));
        Assert.Throws<InvalidDataException>(() => PatchJson.Deserialize("{}"));
    }

    [Theory]
    [InlineData("", "a\n")]
    [InlineData("a\n", "")]
    [InlineData("a\nb\nc\n", "a\nx\nc\n")]
    [InlineData("x\nx\ny\n", "x\ny\nx\n")]
    [InlineData("", "")]
    [InlineData("a", "a\n")]
    [InlineData("a\n\n", "a\n")]
    public void LineEditsReconstructBothInputs(string a, string b)
    {
        var edits = LineDiff.Compare(a, b);
        Assert.Equal(a, string.Concat(edits.Where(x => x.Kind != '+').Select(x => x.Text + (x.HasNewline ? "\n" : ""))));
        Assert.Equal(b, string.Concat(edits.Where(x => x.Kind != '-').Select(x => x.Text + (x.HasNewline ? "\n" : ""))));
    }

    [Fact]
    public void UnifiedDiffHasSeparatedContextHunksAndCorrectInsertRanges()
    {
        string before = string.Join('\n', Enumerable.Range(0, 30)) + "\n";
        string after = before.Replace("2\n", "two\n").Replace("25\n", "twenty-five\n");
        string diff = LineDiff.Unified(before, after, "synthetic", 1);
        Assert.Contains("--- base/synthetic\n+++ mod/synthetic\n", diff);
        Assert.Contains("-2\n+two\n", diff);
        Assert.Contains("@@ -0,0 +1,1 @@", LineDiff.Unified("", "a\n", "new"));
        Assert.Contains("no text changes", LineDiff.Unified("a\r\n", "a\n", "same"));
    }

    [Fact]
    public void FieldDiffDistinguishesNullFromAbsentAndEscapesPointers()
    {
        var before = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["a/b~"] = null });
        var after = JsonSerializer.SerializeToElement(new { });
        var change = Assert.Single(FieldDiff.Compare(before, after));
        Assert.Equal("/a~1b~0", change.Path);
        Assert.Equal(JsonValueKind.Null, change.Base!.Value.ValueKind); Assert.Null(change.Mod);
        var explicitNull = JsonSerializer.Deserialize<ModValue>("{\"Mod\":\"A\",\"Value\":null}")!;
        var absent = JsonSerializer.Deserialize<ModValue>("{\"Mod\":\"B\"}")!;
        Assert.Equal(JsonValueKind.Null, explicitNull.Value!.Value.ValueKind);
        Assert.Null(absent.Value);
    }

    [Fact]
    public void EquivalentGmlDoesNotBecomeAChangeJustBecauseInstructionEncodingDiffers()
    {
        using var a = Model(); using var b = Model();
        foreach (var model in new[] { a, b })
        {
            model.Code[0].Instructions.Clear();
            model.Code[0].Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Push, Type1 = UndertaleInstruction.DataType.Int16, ValueShort = 7 });
            model.Code[0].Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Ret, Type1 = UndertaleInstruction.DataType.Variable });
            model.Code[0].UpdateLength();
        }
        b.Code[0].Instructions[0].Type1 = UndertaleInstruction.DataType.Int32;
        b.Code[0].Instructions[0].ValueInt = 7;
        b.Code[0].UpdateLength();
        Assert.NotEqual(ContentFingerprints.Code(a.Code[0]), ContentFingerprints.Code(b.Code[0]));
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        Assert.Empty(patch.Warnings);
        Assert.Empty(patch.Changes["code"].Changed);
    }

    [Fact]
    public void ChildEntriesKeepNamesParentAndCountsInPortablePatch()
    {
        using var a = Model(); using var b = Model();
        var child = new UndertaleCode { Name = new("child_function"), ParentEntry = b.Code[0], Offset = 4, LocalsCount = 3, ArgumentsCount = 2 };
        b.Code[0].ChildEntries.Add(child);
        b.Code.Add(child);
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        var added = Assert.Single(patch.Changes["code"].Added);
        Assert.Equal("child_function", added.Name);
        Assert.Equal("test_code", added.ModCode!.Parent);
        Assert.Equal(3u, added.ModCode.LocalsCount);
        Assert.Equal((ushort)2, added.ModCode.ArgumentsCount);
        Assert.Contains("child_function", Assert.Single(patch.Changes["code"].Changed).ModCode!.Children);
        Assert.Equal(PatchJson.Serialize(patch), PatchJson.Serialize(PatchJson.Deserialize(PatchJson.Serialize(patch))));
    }

    [Fact]
    public void FailedDecompilationRetainsDisassemblyAndWarning()
    {
        using var a = Model(); using var b = Model();
        b.Code[0].Instructions.Insert(0, new() { Kind = UndertaleInstruction.Opcode.Push, Type1 = UndertaleInstruction.DataType.Int16, ValueShort = 7 });
        b.Code[0].Instructions.Insert(1, new() { Kind = UndertaleInstruction.Opcode.Popz, Type1 = UndertaleInstruction.DataType.Variable });
        b.Code[0].UpdateLength();
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        var code = Assert.Single(patch.Changes["code"].Changed);
        Assert.Equal("disassembly", code.ModCode!.Format);
        Assert.Contains(patch.Warnings, w => w.Contains("mod.win", StringComparison.Ordinal) && w.Contains("test_code", StringComparison.Ordinal));
        Assert.Contains("push", code.ModCode.Text);
    }

    [Fact]
    public void PathsTimelinesViewsAndBackgroundsHaveFieldDiffs()
    {
        using var a = Model(); using var b = Model();
        a.Paths.Add(new() { Name = new("path"), Points = [new() { X = 1 }] });
        b.Paths.Add(new() { Name = new("path"), Points = [new() { X = 2 }] });
        a.Timelines.Add(new() { Name = new("timeline"), Moments = [new() { Step = 1, Event = [] }] });
        b.Timelines.Add(new() { Name = new("timeline"), Moments = [new() { Step = 2, Event = [] }] });
        b.Rooms[0].Views[0].ViewWidth++;
        b.Rooms[0].Backgrounds[0].Enabled = true;
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        Assert.Contains(Assert.Single(patch.Changes["paths"].Changed).Fields, x => x.Path == "/points/0/x");
        Assert.Contains(Assert.Single(patch.Changes["timelines"].Changed).Fields, x => x.Path == "/moments/2");
        var room = Assert.Single(patch.Changes["rooms"].Changed);
        Assert.Contains(room.Fields, x => x.Path == "/views/0/viewWidth");
        Assert.Contains(room.Fields, x => x.Path == "/backgrounds/0/enabled");
    }

    [Fact]
    public void DuplicateNamesAreRejectedAndInputVersionsMustMatch()
    {
        using var a = Model(); using var b = Model();
        b.GameObjects.Add(new() { Name = new("fish") });
        Assert.Contains("Duplicate objects", Assert.Throws<InvalidDataException>(() => PatchBuilder.Create(a, b, Base, Mod)).Message);
        b.GameObjects.RemoveAt(1);
        Assert.Throws<InvalidDataException>(() => PatchBuilder.Create(a, b, Base, Mod with { Bytecode = 17 }));
        b.Code.Clear();
        Assert.Throws<NotComparableException>(() => PatchBuilder.Create(a, b, Base, Mod));
    }

    [Fact]
    public void CliCacheForceRelabelAndOutputProtectionWorkWithReadOnlyInputs()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"jamm-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string basePath = Path.Combine(directory, "base.win"), modPath = Path.Combine(directory, "mod.win");
            string patchPath = Path.Combine(directory, "mod.cgfmm-patch.json");
            using (var data = Model())
            {
                var removedCode = UndertaleCode.CreateEmptyEntry(data, "removed_code");
                removedCode.Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Push, Type1 = UndertaleInstruction.DataType.Int16, ValueShort = 7 });
                removedCode.Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Ret, Type1 = UndertaleInstruction.DataType.Variable });
                removedCode.UpdateLength();
                data.GameObjects.Add(new() { Name = data.Strings.MakeString("old_object") });
                using var stream = File.Create(basePath);
                UndertaleIO.Write(stream, data);
            }
            using (var data = Model())
            {
                data.GameObjects[0].Depth = 20;
                using var stream = File.Create(modPath);
                UndertaleIO.Write(stream, data);
            }
            byte[] original = File.ReadAllBytes(basePath), mod = File.ReadAllBytes(modPath);
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath]));
            string first = File.ReadAllText(patchPath);
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath, "--json-summary", "--show", "fish"]));
            Assert.Equal(first, File.ReadAllText(patchPath));
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath, "--show", "removed_code"]));
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath, "--show", "old_object"]));
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath, "--name", "Renamed"]));
            Assert.Equal("Renamed", PatchJson.Deserialize(File.ReadAllText(patchPath)).Mod.Name);
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath, "--verbose"]));
            Assert.Equal("mod", PatchJson.Deserialize(File.ReadAllText(patchPath)).Mod.Name);
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath, "--force"]));
            var forced = PatchJson.Deserialize(File.ReadAllText(patchPath));
            Assert.Equal(first, PatchJson.Serialize(forced with { CreatedUtc = PatchJson.Deserialize(first).CreatedUtc }));
            File.WriteAllText(patchPath, PatchJson.Serialize(forced with { FormatVersion = 1 }));
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath]));
            Assert.Equal(PatchJson.FormatVersion, PatchJson.Deserialize(File.ReadAllText(patchPath)).FormatVersion);
            File.WriteAllText(patchPath, "invalid JSON");
            Assert.Equal(0, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath]));
            Assert.Equal(original, File.ReadAllBytes(basePath)); Assert.Equal(mod, File.ReadAllBytes(modPath));
            Assert.Throws<ArgumentException>(() => Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", basePath]));
            Assert.Throws<ArgumentException>(() => Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", Path.Combine(directory, "other.win")]));
            using (var data = UndertaleData.CreateNew())
            using (var stream = File.Create(modPath)) UndertaleIO.Write(stream, data);
            Assert.Equal(2, Jamm.Cli.DiffCommand.Run([basePath, modPath, "--out", patchPath]));
            Assert.Equal(original, File.ReadAllBytes(basePath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SpritePatchIdentifiesFramePixelsWithoutEmbeddingBinaryData()
    {
        using var a = Model(); using var b = Model();
        void AddSprite(UndertaleData model, byte pixel)
        {
            var image = new GMImage(1, 1);
            image.GetRawImageData()[0] = pixel;
            var page = new UndertaleEmbeddedTexture();
            page.TextureData.Image = image;
            model.EmbeddedTextures.Add(page);
            var item = new UndertaleTexturePageItem { TexturePage = page, SourceWidth = 1, SourceHeight = 1,
                TargetWidth = 1, TargetHeight = 1, BoundingWidth = 1, BoundingHeight = 1 };
            model.Sprites.Add(new() { Name = new("star"), Width = 1, Height = 1, Textures = [new() { Texture = item }] });
        }
        AddSprite(a, 1); AddSprite(b, 2);
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        var sprite = Assert.Single(patch.Changes["sprites"].Changed);
        var pixels = Assert.Single(sprite.Fields, x => x.Path == "/frames/0/contentHash");
        Assert.Equal(64, pixels.Mod!.Value.GetString()!.Length);
        Assert.NotEqual(pixels.Base!.Value.GetString(), pixels.Mod.Value.GetString());
        Assert.Single(patch.Changes["texturePages"].Changed);
        Assert.DoesNotContain("textureData", PatchJson.Serialize(patch));
    }

    [Fact]
    public void StoredDetectedAndBytecodeVersionsAreValidated()
    {
        var a = new GameInspection("base.win", 0, "hash", "game", "game", "1.0.0.1773", "1.0.0.1773", "Default",
            16, 0, false, true, new Dictionary<AssetKind, int>());
        var b = a with { FileName = "mod.win" };
        PatchBuilder.Validate(a, b);
        Assert.Throws<InvalidDataException>(() => PatchBuilder.Validate(a, b with { GameMakerVersion = "other" }));
        Assert.Throws<InvalidDataException>(() => PatchBuilder.Validate(a, b with { DetectedGameMakerVersion = "other" }));
        Assert.Throws<InvalidDataException>(() => PatchBuilder.Validate(a, b with { BytecodeVersion = 17 }));
        Assert.Throws<NotComparableException>(() => PatchBuilder.Validate(a, b with { IsYyc = true }));
    }

    [RealFilesFact]
    public void RealPatchCountsMatchCompareForEachMod()
    {
        using var a = GameFileLoader.Load(Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!);
        var original = GameFileAnalyzer.Fingerprint(a);
        Assert.Null(original.Error);
        foreach (string path in Directory.GetFiles(Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!, "*.win").Order(StringComparer.Ordinal))
        {
            using var b = GameFileLoader.Load(path);
            var analysis = GameFileAnalyzer.Fingerprint(b);
            Assert.Null(analysis.Error);
            var compare = AssetComparer.Compare(original, analysis);
            var patch = PatchBuilder.Create(a, b);
            foreach (string kind in new[] { "code", "sprites", "objects", "rooms", "sounds" })
            {
                var expected = compare.AssetTypes.Single(x => x.AssetType.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase));
                var actual = patch.Changes[kind];
                Assert.Equal((expected.Added, expected.Changed, expected.Removed), (actual.Added.Count, actual.Changed.Count, actual.Removed.Count));
            }
            Assert.All(patch.Changes["code"].Added.Concat(patch.Changes["code"].Changed), x => Assert.NotNull(x.ModCode));
            string json = PatchJson.Serialize(patch);
            Assert.Equal(json, PatchJson.Serialize(PatchJson.Deserialize(json)));
        }
    }
}
