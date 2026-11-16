using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Regressions for the Phase 1 findings fixed before conflict analysis.</summary>
public class PhaseOneReviewTests
{
    private static readonly PatchFile Base = new("base.win", "base", 1, "1.4", 16);
    private static readonly PatchFile Mod = new("mod.win", "mod", 1, "1.4", 16, "Mod");
    private static UndertaleData Model()
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        var code = UndertaleCode.CreateEmptyEntry(data, "entry");
        code.Instructions.Add(new() { Kind = UndertaleInstruction.Opcode.Exit, Type1 = UndertaleInstruction.DataType.Int32 });
        code.UpdateLength();
        data.GameObjects.Add(new() { Name = new("fish") });
        return data;
    }

    [Fact]
    public void DefinitionChangesAndAdditionsHaveUsefulFieldsAndSourceOrder()
    {
        using var a = Model(); using var b = Model();
        a.Functions.Add(new() { Name = new("fn"), Classification = 0 });
        b.Functions.Add(new() { Name = new("fn"), Classification = (UndertaleGeneralInfo.FunctionClassification)1 });
        b.Functions.Add(new() { Name = new("new_fn") });
        b.Variables.Add(new() { Name = new("new_var") });
        b.Strings.Add(new("new string"));
        b.Sprites.Add(new() { Name = new("z_first") }); b.Sprites.Add(new() { Name = new("a_second") });
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        Assert.Contains(Assert.Single(patch.Changes["functions"].Changed).Fields, f => f.Path == "/classification");
        foreach (string family in new[] { "functions", "variables", "strings" })
            Assert.All(patch.Changes[family].Added, asset => { Assert.NotEmpty(asset.Fields); Assert.DoesNotContain(asset.Fields, f => f.Path == ""); });
        Assert.Equal(new[] { "z_first", "a_second" }, patch.Changes["sprites"].Added.OrderBy(x => x.OriginalIndex).Select(x => x.Name));
    }

    [Fact]
    public void CollisionKeysUseNamesAndDuplicateEventsPreserveActions()
    {
        using var a = Model(); using var b = Model();
        a.GameObjects.Add(new() { Name = new("other") }); b.GameObjects.Insert(0, new() { Name = new("other") });
        a.GameObjects[0].Events[4].Add(new() { EventSubtype = 1, Actions = [new() { CodeId = a.Code[0] }] });
        b.GameObjects[1].Events[4].Add(new() { EventSubtype = 0, Actions = [new() { CodeId = b.Code[0] }] });
        Assert.Empty(PatchBuilder.Create(a, b, Base, Mod).Changes["objects"].Changed);
        b.GameObjects[1].Events[4].Add(new() { EventSubtype = 0, Actions = [new() { CodeId = b.Code[0] }] });
        var obj = Assert.Single(PatchBuilder.Create(a, b, Base, Mod).Changes["objects"].Changed);
        Assert.Equal(2, obj.Mod.GetProperty("events").GetProperty("4:other").GetProperty("code").GetArrayLength());
        Assert.Equal("other", obj.Mod.GetProperty("events").GetProperty("4:other").GetProperty("otherObject").GetString());
    }

    [Fact]
    public void LayersMomentsGlyphsAndTilesAreKeyedRatherThanPositional()
    {
        using var a = Model(); using var b = Model();
        b.Rooms[0].Layers.Add(new() { LayerName = new("new_layer") });
        b.Rooms[0].Tiles.Add(new() { X = 10, Y = 20, SourceX = 1, SourceY = 2, Width = 4, Height = 5, TileDepth = 6 });
        a.Timelines.Add(new() { Name = new("time") }); b.Timelines.Add(new() { Name = new("time"), Moments = [new() { Step = 42, Event = [] }] });
        a.Fonts.Add(new() { Name = new("font") }); b.Fonts.Add(new() { Name = new("font"), Glyphs = [new() { Character = 65 }] });
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        var room = Assert.Single(patch.Changes["rooms"].Changed);
        Assert.Contains(room.Fields, f => f.Path == "/layers/new_layer");
        Assert.Single(room.Mod.GetProperty("tiles").EnumerateObject());
        Assert.DoesNotContain(room.Mod.GetProperty("tiles").EnumerateObject(), p => p.Name is "count" or "contentHash");
        Assert.Contains(Assert.Single(patch.Changes["timelines"].Changed).Fields, f => f.Path == "/moments/42");
        Assert.Contains(Assert.Single(patch.Changes["fonts"].Changed).Fields, f => f.Path == "/glyphs/65");
    }

    [Fact]
    public void NullSpriteFramesAreSafeAndTexturePagesUseNumericSourceOrderAndContentAddresses()
    {
        using var a = Model(); using var b = Model();
        b.Sprites.Add(new() { Name = new("empty_frame"), Textures = [null!] });
        for (int i = 0; i < 12; i++)
        {
            var page = new UndertaleEmbeddedTexture();
            var image = new UndertaleModLib.Util.GMImage(1, 1);
            image.GetRawImageData()[0] = (byte)i;
            page.TextureData.Image = image;
            b.EmbeddedTextures.Add(page);
        }
        var patch = PatchBuilder.Create(a, b, Base, Mod);
        var sprite = Assert.Single(patch.Changes["sprites"].Added);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, sprite.Mod.GetProperty("frames")[0].GetProperty("geometry").ValueKind);
        Assert.Equal(Enumerable.Range(0, 12), patch.Changes["texturePages"].Added.Select(p => p.OriginalIndex!.Value));
        Assert.All(patch.Changes["texturePages"].Added, p => Assert.StartsWith("sha256:", p.Name));
        b.EmbeddedTextures.Add(new() { TextureExternal = true });
        b.EmbeddedTextures.Add(new() { TextureExternal = true });
        var unsupported = PatchBuilder.Create(a, b, Base, Mod).Changes["texturePages"].Added.Where(p => p.Name.StartsWith("unsupported:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, unsupported.Length); // Equal unsupported markers cannot establish page equality.
        Assert.NotEqual(unsupported[0].Name, unsupported[1].Name);
        b.Sprites[0].Textures.Clear(); // UTML's disposer assumes non-null entries.
    }

    [Fact]
    public void AudioGroupNameChangesAreDetectedWithoutDependingOnItsIndex()
    {
        using var a = Model(); using var b = Model();
        a.Sounds.Add(new() { Name = new("sound"), AudioGroup = new() { Name = new("old_group") } });
        b.Sounds.Add(new() { Name = new("sound"), AudioGroup = new() { Name = new("new_group") } });
        var sound = Assert.Single(PatchBuilder.Create(a, b, Base, Mod).Changes["sounds"].Changed);
        Assert.Contains(sound.Fields, f => f.Path == "/audioGroup");
    }

    [Fact]
    public void ScriptsWithOnlyChangedBytecodeDoNotEmitNoOpSnapshots()
    {
        using var a = Model(); using var b = Model();
        a.Scripts.Add(new() { Name = new("script"), Code = a.Code[0] });
        b.Scripts.Add(new() { Name = new("script"), Code = b.Code[0] });
        b.Code[0].ArgumentsCount = 1;
        Assert.Empty(PatchBuilder.Create(a, b, Base, Mod).Changes["scripts"].Changed);
    }
}
