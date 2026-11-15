using System.Globalization;
using System.Text.Json;
using Jamm.Core;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.Tests;

/// <summary>Semantic fingerprint regressions using synthetic UTML assets.</summary>
public class FingerprintTests
{
    [Fact]
    public void TinyInMemoryDataHasStableFingerprintsAndTracksSemanticEdits()
    {
        using var data = UndertaleData.CreateNew();
        var code = new UndertaleCode { Name = new UndertaleString("test_code") };
        code.Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.PushI,
            Type1 = UndertaleInstruction.DataType.Int16, ValueShort = 7 });
        data.Code.Add(code);
        var first = new ContentFingerprints().Snapshot(data);
        var second = new ContentFingerprints().Snapshot(data);
        Assert.Equal(14, first.Count);
        foreach (var type in first.Keys) Assert.Equal(first[type], second[type]);
        code.Instructions[0].ValueShort = 8;
        Assert.NotEqual(first[AssetKind.Code][0].Fingerprint, new ContentFingerprints().Snapshot(data)[AssetKind.Code][0].Fingerprint);
    }

    [Fact]
    public void CodeFingerprintIgnoresEntryNameAndCultureButDetectsChildOffset()
    {
        var code = new UndertaleCode { Name = new UndertaleString("first") };
        code.Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Push,
            Type1 = UndertaleInstruction.DataType.Double, ValueDouble = 1.25 });
        string hash = ContentFingerprints.Code(code);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            code.Name = new UndertaleString("second");
            Assert.Equal(hash, ContentFingerprints.Code(code));
        }
        finally { CultureInfo.CurrentCulture = previous; }
        var child = new UndertaleCode { ParentEntry = code, Offset = 4 };
        Assert.NotEqual(hash, ContentFingerprints.Code(child));
    }

    [Fact]
    public void StringTableReorderingDoesNotChangeCodeFingerprint()
    {
        var code = new UndertaleCode();
        var reference = new UndertaleResourceById<UndertaleString, UndertaleChunkSTRG>(new UndertaleString("synthetic"), 1);
        code.Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Push,
            Type1 = UndertaleInstruction.DataType.String, ValueString = reference });
        string before = ContentFingerprints.Code(code);
        reference.CachedId = 1234;
        Assert.Equal(before, ContentFingerprints.Code(code));
        reference.Resource = new("changed");
        Assert.NotEqual(before, ContentFingerprints.Code(code));
    }

    [Fact]
    public void FrameFingerprintIgnoresOtherAtlasPixelsAndAtlasLocation()
    {
        var image = new GMImage(2, 1);
        image.GetRawImageData()[0] = 42;
        var item = Item(image, 0);
        string original = new ContentFingerprints().Region(item);
        image.GetRawImageData()[4] = 99;
        Assert.Equal(original, new ContentFingerprints().Region(item));
        image.GetRawImageData()[0] = 43;
        Assert.NotEqual(original, new ContentFingerprints().Region(item));
        image.GetRawImageData()[4] = 42;
        Assert.Equal(original, new ContentFingerprints().Region(Item(image, 1)));
        item.TargetX = 1;
        Assert.NotEqual(original, new ContentFingerprints().Region(item));
    }

    [Fact]
    public void OutOfBoundsRegionWarnsAndUsesDeterministicMarker()
    {
        var image = new GMImage(1, 1);
        var item = Item(image, 0);
        item.SourceWidth = 2;
        List<string> warnings = [];
        string first = new ContentFingerprints(warnings.Add).Region(item);
        Assert.Contains("out-of-range region", Assert.Single(warnings));
        Assert.Equal("unsupported:out-of-range region", first);
        image.GetRawImageData()[0] = 10;
        string second = new ContentFingerprints().Region(item);
        Assert.Equal(first, second);
    }

    [Fact]
    public void UnsupportedSpritesAndExternalTexturesDoNotAbortSnapshot()
    {
        using var data = UndertaleData.CreateNew();
        data.Sprites.Add(new() { Name = new("spine"), IsSpecialType = true, SSpriteType = UndertaleSprite.SpriteType.Spine });
        data.Sprites.Add(new() { Name = new("swf"), IsSpecialType = true, SSpriteType = UndertaleSprite.SpriteType.SWF });
        var external = new UndertaleEmbeddedTexture { TextureExternal = true, Name = new("external") };
        data.EmbeddedTextures.Add(external);
        var sprite = new UndertaleSprite { Name = new("external sprite") };
        sprite.Textures.Add(new() { Texture = new() { TexturePage = external } });
        data.Sprites.Add(sprite);
        var outside = new UndertaleSprite { Name = new("outside") };
        var item = Item(new GMImage(1, 1), 0);
        item.SourceHeight = 2;
        outside.Textures.Add(new() { Texture = item });
        data.Sprites.Add(outside);
        data.Sprites.Add(new() { Name = new("normal") });
        List<string> warnings = [];
        var snapshot = new ContentFingerprints(warnings.Add).Snapshot(data);
        Assert.Equal(new[] { "unsupported:Spine sprite", "unsupported:SWF sprite", "unsupported:external texture", "unsupported:out-of-range region" },
            snapshot[AssetKind.Sprites].Take(4).Select(x => x.Fingerprint));
        Assert.DoesNotContain("unsupported:", snapshot[AssetKind.Sprites][4].Fingerprint);
        Assert.Equal("unsupported:external texture", Assert.Single(snapshot[AssetKind.TexturePages]).Fingerprint);
        Assert.Equal(5, warnings.Count);
        Assert.Equal(snapshot[AssetKind.Sprites], new ContentFingerprints().Snapshot(data)[AssetKind.Sprites]);
    }

    [Fact]
    public void ScriptReferencingCodeOutsideTableUsesStableMissingCodeMarker()
    {
        using var data = UndertaleData.CreateNew();
        data.Code.Add(new() { Name = new("present") });
        var script = new UndertaleScript { Name = new("script"), Code = new() { Name = new("absent") } };
        data.Scripts.Add(script);
        List<string> warnings = [];
        var before = new ContentFingerprints(warnings.Add).Snapshot(data)[AssetKind.Scripts];
        Assert.Contains("missing from the CODE table", Assert.Single(warnings));
        Assert.Equal(before, new ContentFingerprints().Snapshot(data)[AssetKind.Scripts]);
        data.Code.Add(script.Code);
        Assert.NotEqual(before[0].Fingerprint, new ContentFingerprints().Snapshot(data)[AssetKind.Scripts][0].Fingerprint);
    }

    [Fact]
    public void Gms2RoomLayerInstanceTileAndBackgroundEditsChangeFingerprint()
    {
        using var data = UndertaleData.CreateNew();
        var instance = new UndertaleRoom.GameObject { X = 10 };
        var instances = new UndertaleRoom.Layer.LayerInstancesData();
        instances.Instances.Add(instance);
        var tiles = new UndertaleRoom.Layer.LayerTilesData { TilesX = 1, TilesY = 1, TileData = [[1]] };
        var background = new UndertaleRoom.Layer.LayerBackgroundData();
        data.Rooms[0].Layers.Add(new() { LayerType = UndertaleRoom.LayerType.Instances, Data = instances });
        data.Rooms[0].Layers.Add(new() { LayerType = UndertaleRoom.LayerType.Tiles, Data = tiles });
        data.Rooms[0].Layers.Add(new() { LayerType = UndertaleRoom.LayerType.Background, Data = background });
        string Fingerprint() => new ContentFingerprints().Snapshot(data)[AssetKind.Rooms][0].Fingerprint;
        string before = Fingerprint();
        instance.X++;
        Assert.NotEqual(before, Fingerprint());
        before = Fingerprint();
        tiles.TileData[0][0]++;
        Assert.NotEqual(before, Fingerprint());
        before = Fingerprint();
        background.Color++;
        Assert.NotEqual(before, Fingerprint());
    }

    [Fact]
    public void Bz2QoiPageHashUsesPublicWriterWithoutClosedStreamFailure()
    {
        using var data = UndertaleData.CreateNew();
        data.EmbeddedTextures.Add(new() { TextureData = new()
            { Image = GMImage.FromBz2Qoi([1, 2, 3], 1, 1, 4) } });
        string first = new ContentFingerprints().Snapshot(data)[AssetKind.TexturePages][0].Fingerprint;
        Assert.Equal(first, new ContentFingerprints().Snapshot(data)[AssetKind.TexturePages][0].Fingerprint);
        data.EmbeddedTextures[0].TextureData.Image = GMImage.FromBz2Qoi([1, 2, 4], 1, 1, 4);
        Assert.NotEqual(first, new ContentFingerprints().Snapshot(data)[AssetKind.TexturePages][0].Fingerprint);
    }

    [Fact]
    public void StringComparisonUsesValuesAndDoesNotExposeDialogue()
    {
        using var original = UndertaleData.CreateNew();
        using var mod = UndertaleData.CreateNew();
        original.Strings.Clear();
        mod.Strings.Clear();
        original.Strings.Add(new("synthetic dialogue"));
        original.Strings.Add(new("repeated"));
        original.Strings.Add(new("repeated"));
        mod.Strings.Add(new("repeated"));
        mod.Strings.Add(new("synthetic dialogue"));
        mod.Strings.Add(new("replacement dialogue"));
        var result = AssetComparer.CompareAssets(AssetKind.Strings,
            new ContentFingerprints().Snapshot(original)[AssetKind.Strings], new ContentFingerprints().Snapshot(mod)[AssetKind.Strings]);
        Assert.Equal((2, 0, 1, 1), (result.Identical, result.Changed, result.Added, result.Removed));
        Assert.DoesNotContain("dialogue", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void ObjectRoomAndSoundEditsChangeFingerprints()
    {
        using var data = UndertaleData.CreateNew();
        data.GameObjects.Add(new() { Name = new("obj") });
        data.Sounds.Add(new() { Name = new("sound"), AudioFile = new() { Data = [1, 2] } });
        var before = new ContentFingerprints().Snapshot(data);
        data.GameObjects[0].Solid = true;
        data.Rooms[0].GameObjects.Add(new() { ObjectDefinition = data.GameObjects[0], X = 12, Y = 34 });
        data.Sounds[0].AudioFile.Data[0] = 3;
        var after = new ContentFingerprints().Snapshot(data);
        foreach (AssetKind type in new[] { AssetKind.Objects, AssetKind.Rooms, AssetKind.Sounds })
            Assert.NotEqual(before[type][0].Fingerprint, after[type][0].Fingerprint);
    }

    private static UndertaleTexturePageItem Item(GMImage image, ushort x) => new()
    {
        TexturePage = new() { TextureData = new() { Image = image } },
        SourceX = x, SourceWidth = 1, SourceHeight = 1, TargetWidth = 1, TargetHeight = 1,
        BoundingWidth = 1, BoundingHeight = 1
    };
}
