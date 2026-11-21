using ImageMagick;
using Jamm.Core.ModKit;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

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

    [Fact]
    public void WangTilesAreDeterministicPaletteSafeAndSeamlessAcrossEdgesAndPadding()
    {
        const int patchSize = 132;
        byte[] MakePatch(int seed)
        {
            byte[] rgba = new byte[patchSize * patchSize * 4];
            for (int y = 0; y < patchSize; y++)
            {
                for (int x = 0; x < patchSize; x++)
                {
                    bool seam = ((x + y * 2 + seed * 7) % 37) < 3 || ((x * 3 - y + seed * 5) % 41 + 41) % 41 < 3;
                    int idx = (y * patchSize + x) * 4;
                    rgba[idx + 0] = (byte)(seam ? 82 : 104);
                    rgba[idx + 1] = (byte)(seam ? 94 : 117);
                    rgba[idx + 2] = (byte)(seam ? 105 : 129);
                    rgba[idx + 3] = 255;
                }
            }
            return rgba;
        }

        var patches = Enumerable.Range(0, 12).Select(MakePatch).ToList();
        var first = EndlessOceanSprites.BuildWangTiles(patches, EndlessOceanMod.DefaultSeed);
        var repeat = EndlessOceanSprites.BuildWangTiles(patches, EndlessOceanMod.DefaultSeed);
        var varied = EndlessOceanSprites.BuildWangTiles(patches, EndlessOceanMod.DefaultSeed + 1);

        Assert.Equal(EndlessOceanSprites.VariantCount, first.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], repeat[i]);
            for (int p = 0; p < first[i].Length; p += 4)
            {
                Assert.InRange(first[i][p + 0], 82, 104);
                Assert.InRange(first[i][p + 1], 94, 117);
                Assert.InRange(first[i][p + 2], 105, 129);
                Assert.Equal(255, first[i][p + 3]);
            }
        }
        Assert.Contains(Enumerable.Range(0, first.Count), i => !first[i].SequenceEqual(varied[i]));

        int pad = EndlessOceanSprites.WrapPad;
        int cell = EndlessOceanSprites.CellSize;
        int stride = (cell + 2 * pad) * 4;

        // Verify matching Wang edges share exact 2px neighbor padding byte-for-byte.
        for (int a = 0; a < first.Count; a++)
        {
            for (int b = 0; b < first.Count; b++)
            {
                if (((a >> 2) & 1) == (b & 1))
                {
                    for (int y = 0; y < cell + 2 * pad; y++)
                    {
                        for (int dx = 0; dx < pad; dx++)
                        {
                            int aPad = y * stride + (pad + cell + dx) * 4;
                            int bEdge = y * stride + (pad + dx) * 4;
                            Assert.Equal(first[b].AsSpan(bEdge, 4).ToArray(), first[a].AsSpan(aPad, 4).ToArray());

                            int bPad = y * stride + dx * 4;
                            int aEdge = y * stride + (pad + cell - pad + dx) * 4;
                            Assert.Equal(first[a].AsSpan(aEdge, 4).ToArray(), first[b].AsSpan(bPad, 4).ToArray());
                        }
                    }
                }
                if (((a >> 1) & 1) == ((b >> 3) & 1))
                {
                    for (int dy = 0; dy < pad; dy++)
                    {
                        for (int x = 0; x < cell + 2 * pad; x++)
                        {
                            int aPad = (pad + cell + dy) * stride + x * 4;
                            int bEdge = (pad + dy) * stride + x * 4;
                            Assert.Equal(first[b].AsSpan(bEdge, 4).ToArray(), first[a].AsSpan(aPad, 4).ToArray());

                            int bPad = dy * stride + x * 4;
                            int aEdge = (pad + cell - pad + dy) * stride + x * 4;
                            Assert.Equal(first[a].AsSpan(aEdge, 4).ToArray(), first[b].AsSpan(bPad, 4).ToArray());
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void SpritesPackOntoSingleAtlasPageAndCompileAllWorldgenGmlCleanly()
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Minor = 0;
        data.GeneralInfo.Release = 0;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;

        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            string rockPath = Path.Combine(tempDir.FullName, "rock.png");
            string boatPath = Path.Combine(tempDir.FullName, "boat.png");
            using (var rockImg = new MagickImage(new MagickColor(108, 120, 132), 512, 512))
                rockImg.Write(rockPath);
            using (var boatImg = new MagickImage(new MagickColor(90, 100, 110), 840, 315))
                boatImg.Write(boatPath);

            var imported = ModKitSpriteImporter.ImportAll(data, [
                new("spr_caverock_big", rockPath, 1, null, false, false),
                new("spr_boat3", boatPath, 1, null, false, false),
            ]);

            var world = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld"), Width = 100000, Height = 13000 };
            data.Rooms.Add(world);

            string[] neededObjects =
            [
                "Camera", "Boat7", "SaveManager", "Rod", "Lure", "Collidable", "CaveRock", "Light", "CatSwim",
                "CaveRockWide", "CaveRockLeg", "CaveRockSig", "CaveRockTear", "CaveRockTee", "CaveRockWedge",
                "JWeedSmall", "JWeedMedium", "Coral", "Coral2", "CoralDark", "CoralFG3", "GlowPlant", "BulbPlantShort", "ChainPlant",
                "Swooper", "Roundfin", "Garump", "Spinefish", "Yellowjack", "Puff", "Celad", "Inno",
                "Garder", "Pink_Kisser", "Pennyfin", "Lionfish", "Mustardfish", "Elder_Mustardfish",
                "Felish", "Torby", "Purdy", "Leofish", "Sassy", "Boom_Puff", "Dijon", "Stankfin", "Cavin",
                "Twohead", "Garm", "Jackfish", "Primal_Turgeon", "Mudback", "Cuddlecanth", "Buffalofish",
                "Geiger", "Ancient_Kingfish", "Underfin"
            ];
            foreach (string name in neededObjects)
                data.GameObjects.Add(new UndertaleGameObject { Name = data.Strings.MakeString(name) });

            var rockBig = new UndertaleGameObject { Name = data.Strings.MakeString("CaveRockBig"), Sprite = imported["spr_caverock_big"] };
            var boat3 = new UndertaleGameObject { Name = data.Strings.MakeString("Boat3"), Sprite = imported["spr_boat3"] };
            data.GameObjects.Add(rockBig);
            data.GameObjects.Add(boat3);

            foreach (string scriptName in new[] { "fish_object_to_name", "get_index_for_name", "fish_respawn" })
            {
                var sc = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + scriptName);
                data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(scriptName), Code = sc });
                var comp = new CompileGroup(data);
                comp.QueueCodeReplace(sc, "return 0;");
                ModObjects.Compile(comp);
            }

            var saveMgr = data.GameObjects.ByName("SaveManager")!;
            foreach (uint ev in new[] { 14u, 15u })
            {
                var comp = new CompileGroup(data);
                comp.QueueCodeReplace(saveMgr.EventHandlerFor(EventType.Other, ev, data), """
                    ini_write_real("Fishes", string(id), 1);
                    ini_write_real("Fishes", string(id), 2);
                    """);
                ModObjects.Compile(comp);
            }

            EndlessOceanMod.Apply(data);

            var atlas = data.Sprites.ByName("spr_jmk_ocean_underlay_tex");
            Assert.NotNull(atlas);
            Assert.Equal((uint)EndlessOceanSprites.CellSize, atlas!.Width);
            Assert.Equal((uint)EndlessOceanSprites.CellSize, atlas.Height);
            Assert.Equal(EndlessOceanSprites.VariantCount, atlas.Textures.Count);
            Assert.Single(atlas.Textures.Select(t => t.Texture.TexturePage).Distinct());
            foreach (var texEntry in atlas.Textures)
            {
                var item = texEntry.Texture;
                Assert.Equal(EndlessOceanSprites.CellSize, item.SourceWidth);
                Assert.Equal(EndlessOceanSprites.CellSize, item.SourceHeight);
                Assert.True(item.SourceX >= EndlessOceanSprites.WrapPad);
                Assert.True(item.SourceY >= EndlessOceanSprites.WrapPad);
                Assert.True(item.SourceX + item.SourceWidth + EndlessOceanSprites.WrapPad <= item.TexturePage.TextureData.Width);
                Assert.True(item.SourceY + item.SourceHeight + EndlessOceanSprites.WrapPad <= item.TexturePage.TextureData.Height);
            }
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
