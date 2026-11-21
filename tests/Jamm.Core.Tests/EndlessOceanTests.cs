using Jamm.Core.ModKit;
using Jamm.ModMaker;
using UndertaleModLib;

using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies the Endless Ocean recipe and its legacy archive integration.</summary>
public sealed class EndlessOceanTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("2147483646", 2147483646)]
    [InlineData("-1", null)]
    [InlineData("2147483647", null)]
    [InlineData("1.5", null)]
    [InlineData("\"123\"", null)]
    public void RecipeValidatesSeeds(string seed, int? expected)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, RecipePackage.ManifestFileName), $$"""
                { "id": "endless-ocean", "generator": "endless-ocean", "name": "Endless Ocean", "seed": {{seed}} }
                """);
            if (expected is int value)
                Assert.Equal(value, RecipePackage.Load(directory.FullName).Seed);
            else
                Assert.Contains("$.seed", Assert.Throws<InvalidDataException>(() => RecipePackage.Load(directory.FullName)).Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BlendsFromTheLastMaskAndReservesEasterlyArtwork()
    {
        using var data = UndertaleData.CreateNew();
        var world = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld"), Width = 172000, Height = 17000, Speed = 60 };
        data.Rooms.Add(world);
        var collidable = new UndertaleGameObject { Name = data.Strings.MakeString("Collidable") };
        var mask = new UndertaleSprite
        {
            Name = data.Strings.MakeString("rock_mask"), Width = 16, Height = 16,
            OriginX = 8, OriginY = 8, MarginRight = 15, MarginBottom = 15,
            SepMasks = UndertaleSprite.SepMaskType.Precise
        };
        var bits = mask.NewMaskEntry(data);
        bits.Data[2 * 6] = 0x10; // pixel (3, 6) is the eastmost solid pixel after the flip
        mask.CollisionMasks.Add(bits);
        var rock = new UndertaleGameObject { Name = data.Strings.MakeString("rock"), ParentId = collidable, Sprite = mask };
        world.GameObjects.Add(new UndertaleRoom.GameObject
        {
            X = 169000, Y = 13000, ScaleX = -2, ScaleY = 1, ObjectDefinition = rock
        });
        var artwork = new UndertaleGameObject
        {
            Name = data.Strings.MakeString("far_content"),
            Sprite = new UndertaleSprite { Width = 400, Height = 200, OriginX = 50 }
        };
        world.GameObjects.Add(new UndertaleRoom.GameObject { X = 173000, ScaleX = 2, ObjectDefinition = artwork });

        var edge = EndlessOceanEdge.Find(world, world.Width);

        Assert.Equal(169011, edge.X);
        Assert.Equal(12998, edge.Floor);
        Assert.Equal(173700u, EndlessOceanEdge.ProtectedEastEdge(world, world.Width));
        Assert.Equal(3, edge.ContentChunk);
        Assert.Equal(edge.X + (uint)(3 * EndlessOceanEdge.ChunkWidth + EndlessOceanMod.ExtensionWidth), edge.End);
        Assert.Equal(172000u, world.Width);
    }

    [Fact]
    public void MergedLayoutUsesTheResolvedRoomAndCanBeRecomputed()
    {
        using var data = UndertaleData.CreateNew();
        const string settings = "gml_Script_jmk_endless_ocean_setup";
        const string step = "gml_Object_obj_jmk_endless_ocean_Step_1";
        var world = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld"), Width = 100000, Height = 13000 };
        data.Rooms.Add(world);
        ModObjects.Create(data, EndlessOceanMod.Controller, persistent: false);
        UndertaleCode.CreateEmptyEntry(data, settings);
        UndertaleCode.CreateEmptyEntry(data, step);
        Dictionary<string, string> recompile = new() { [settings] = "ocean_end = 1100000;" };
        Dictionary<string, string> generated = new();
        List<string> decisions = [];

        EndlessOceanMod.RefreshMergedLayout(data, recompile, generated, decisions);
        string source = generated[settings];
        uint width = world.Width;
        Assert.Contains("ocean_start = 100000;", source);
        Assert.Contains($"ocean_end = {width};", source);
        Assert.Equal(source, recompile[settings]);
        Assert.Contains("jamm_chunk_stream(ocean_chunks, first, last, jmk_endless_ocean_load, jmk_endless_ocean_release)", generated[step]);
        Assert.Equal(generated[step], recompile[step]);
        Assert.Single(decisions);
        EndlessOceanMod.RefreshMergedLayout(data, recompile, generated, decisions);
        Assert.Equal(width, world.Width);

        // This fallback room has no terrain. In a real room the anchor comes from a mask
        // and is invariant when the recipe's own extension is stripped.
        var rock = new UndertaleGameObject
        {
            Name = data.Strings.MakeString("Collidable"),
            Sprite = new UndertaleSprite { Width = 512, Height = 512, MarginRight = 511, MarginBottom = 511 }
        };
        world.GameObjects.Add(new UndertaleRoom.GameObject { X = 90000, Y = 12500, ObjectDefinition = rock });
        EndlessOceanMod.RefreshMergedLayout(data, recompile, generated, decisions);
        Assert.Contains("ocean_start = 90512;", generated[settings]);
        width = world.Width;
        EndlessOceanMod.RefreshMergedLayout(data, recompile, generated, decisions);
        Assert.Equal(width, world.Width);
    }
}
