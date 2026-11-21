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
        var dark = new EndlessOceanSprites.Rgb(63, 65, 60);
        var light = new EndlessOceanSprites.Rgb(88, 90, 85);

        static EndlessOceanSprites.CobbleMask MakeCobble(int w, int h, int seed)
        {
            byte[] alpha = new byte[w * h];
            int area = 0;
            double rx = w * 0.42;
            double ry = h * 0.42;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = (x - w * 0.5) / rx;
                    double dy = (y - h * 0.5) / ry;
                    double wobble = 0.08 * Math.Sin((x + seed * 11) * 0.07) * Math.Cos((y + seed * 7) * 0.09);
                    if (dx * dx + dy * dy <= 1.0 + wobble)
                    {
                        alpha[y * w + x] = 255;
                        area++;
                    }
                }
            }
            return new(w, h, alpha, area, (double)area / (w * h), Math.Max((double)w / h, (double)h / w));
        }

        var cobbles = new[]
        {
            MakeCobble(180, 140, 1),
            MakeCobble(150, 115, 2),
            MakeCobble(120, 100, 3),
            MakeCobble(95, 80, 4),
        };
        var first = EndlessOceanSprites.BuildWangSlots(cobbles, dark, light, EndlessOceanMod.DefaultSeed);
        var repeat = EndlessOceanSprites.BuildWangSlots(cobbles, dark, light, EndlessOceanMod.DefaultSeed);
        var varied = EndlessOceanSprites.BuildWangSlots(cobbles, dark, light, EndlessOceanMod.DefaultSeed + 1);

        Assert.Equal(EndlessOceanSprites.VariantCount, first.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], repeat[i]);
            Assert.Equal(EndlessOceanSprites.SlotSize * EndlessOceanSprites.SlotSize * 4, first[i].Length);
            for (int p = 0; p < first[i].Length; p += 4)
            {
                Assert.InRange(first[i][p + 0], dark.R, light.R);
                Assert.InRange(first[i][p + 1], dark.G, light.G);
                Assert.InRange(first[i][p + 2], dark.B, light.B);
                Assert.Equal(255, first[i][p + 3]);
            }
        }
        Assert.Contains(Enumerable.Range(0, first.Count), i => !first[i].SequenceEqual(varied[i]));

        int pad = EndlessOceanSprites.SlotPad;
        int cell = EndlessOceanSprites.CellSize;
        int slotStride = EndlessOceanSprites.SlotSize * 4;
        int edgeGuard = EndlessOceanSprites.EdgeMargin;

        // Verify matching Wang edges share exact neighbor pixels across the seam and padding guard band.
        for (int a = 0; a < first.Count; a++)
        {
            for (int b = 0; b < first.Count; b++)
            {
                if (((a >> 2) & 1) == (b & 1))
                {
                    for (int y = pad; y < pad + cell; y++)
                    {
                        for (int dx = 0; dx < edgeGuard; dx++)
                        {
                            int aPad = y * slotStride + (pad + cell + dx) * 4;
                            int bEdge = y * slotStride + (pad + dx) * 4;
                            Assert.Equal(first[b].AsSpan(bEdge, 4).ToArray(), first[a].AsSpan(aPad, 4).ToArray());

                            int bPad = y * slotStride + (pad - edgeGuard + dx) * 4;
                            int aEdge = y * slotStride + (pad + cell - edgeGuard + dx) * 4;
                            Assert.Equal(first[a].AsSpan(aEdge, 4).ToArray(), first[b].AsSpan(bPad, 4).ToArray());
                        }
                    }
                }
                if (((a >> 1) & 1) == ((b >> 3) & 1))
                {
                    for (int dy = 0; dy < edgeGuard; dy++)
                    {
                        for (int x = pad; x < pad + cell; x++)
                        {
                            int aPad = (pad + cell + dy) * slotStride + x * 4;
                            int bEdge = (pad + dy) * slotStride + x * 4;
                            Assert.Equal(first[b].AsSpan(bEdge, 4).ToArray(), first[a].AsSpan(aPad, 4).ToArray());

                            int bPad = (pad - edgeGuard + dy) * slotStride + x * 4;
                            int aEdge = (pad + cell - edgeGuard + dy) * slotStride + x * 4;
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
            byte[] rockRgba = new byte[512 * 512 * 4];
            for (int y = 0; y < 512; y++)
            {
                for (int x = 0; x < 512; x++)
                {
                    bool cobble = ((x - 128) * (x - 128) + (y - 128) * (y - 128) < 4200)
                               || ((x - 360) * (x - 360) + (y - 280) * (y - 280) < 5200);
                    int i = (y * 512 + x) * 4;
                    rockRgba[i + 0] = (byte)(cobble ? 88 : 63);
                    rockRgba[i + 1] = (byte)(cobble ? 90 : 65);
                    rockRgba[i + 2] = (byte)(cobble ? 85 : 60);
                    rockRgba[i + 3] = 255;
                }
            }
            using (var rockImg = new MagickImage(rockRgba, new PixelReadSettings(512, 512, StorageType.Char, PixelMapping.RGBA)))
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
            Assert.Equal((uint)EndlessOceanSprites.AtlasSize, atlas!.Width);
            Assert.Equal((uint)EndlessOceanSprites.AtlasSize, atlas.Height);
            Assert.Single(atlas.Textures);
            var item = atlas.Textures[0].Texture;
            Assert.Equal(0, item.SourceX);
            Assert.Equal(0, item.SourceY);
            Assert.Equal(EndlessOceanSprites.AtlasSize, item.SourceWidth);
            Assert.Equal(EndlessOceanSprites.AtlasSize, item.SourceHeight);
            Assert.Equal(EndlessOceanSprites.AtlasSize, item.TexturePage.TextureData.Width);
            Assert.Equal(EndlessOceanSprites.AtlasSize, item.TexturePage.TextureData.Height);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
