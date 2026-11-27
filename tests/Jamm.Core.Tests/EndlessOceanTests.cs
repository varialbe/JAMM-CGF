using System.Text.RegularExpressions;
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
    [Fact]
    public void SonarHooksAreNarrowIdempotentAndPreserveVanillaText()
    {
        const string vanillaStep = """
            if (Lure.state == 0)
            {
                readyToBeginTracking = 1;
            }
            """;
        const string expectedStep = vanillaStep + "\njmk_endless_ocean_sonar_view();\n";
        string patchedStep = EndlessOceanMod.PatchSonarStep(vanillaStep);
        Assert.Equal(expectedStep, patchedStep);
        Assert.Equal(patchedStep, EndlessOceanMod.PatchSonarStep(patchedStep));
        Assert.Single(Regex.Matches(patchedStep, "jmk_endless_ocean_sonar_view\\(\\);").Cast<Match>());
        const string moddedStep = vanillaStep + "\n// merged mod tail";
        Assert.Equal(moddedStep + "\njmk_endless_ocean_sonar_view();\n", EndlessOceanMod.PatchSonarStep(moddedStep));

        const string vanillaDraw = """
            noise_alpha = (((min(Girl.x, Lure.x) - 10000) / 2.6) + (max(Lure.y, 1294) - 4000) - 4000) / 1000;
            if (instance_exists(Boat) && Boat.boost_radar)
            {
                noise_alpha = 0;
            }
            draw_sprite_ext(spr_sonarnoise, irandom(4), view_xview[3] + draw_offset, view_yview[3] + draw_offset, noise_scalar, noise_scalar, 0, c_white, min(1, noise_alpha));
            """;
        const string hook = "if (jmk_endless_ocean_sonar_clear()) noise_alpha = 0;";
        string patchedDraw = EndlessOceanMod.PatchSonarDraw(vanillaDraw);
        Assert.Equal(vanillaDraw, patchedDraw.Replace("\n" + hook, "", StringComparison.Ordinal));
        Assert.Equal(patchedDraw, EndlessOceanMod.PatchSonarDraw(patchedDraw));
        Assert.Single(Regex.Matches(patchedDraw, "jmk_endless_ocean_sonar_clear\\(\\)").Cast<Match>());
        Assert.Single(Regex.Matches(patchedDraw, "Boat\\.boost_radar").Cast<Match>());
        const string decompiledHook = "if (jmk_endless_ocean_sonar_clear())\n{\n    noise_alpha = 0;\n}";
        string decompiledDraw = vanillaDraw + "\n" + decompiledHook;
        Assert.Equal(decompiledDraw, EndlessOceanMod.PatchSonarDraw(decompiledDraw));
        Assert.DoesNotContain("Boat.boost_radar =", EndlessOceanMod.Source("step"));
        Assert.DoesNotContain("MAX_X", EndlessOceanMod.Source("step").Replace("with (Boat7)\n{\n    MAX_X = max(MAX_X, other.ocean_end - 400);\n}", "", StringComparison.Ordinal));
    }

    [Fact]
    public void SonarHooksFailClearlyWhenTheVanillaAnchorsAreMissing()
    {
        var stepError = Assert.Throws<InvalidDataException>(() => EndlessOceanMod.PatchSonarStep("readyToBeginTracking = 1;"));
        Assert.Contains("gml_Object_SonarPanel_Step_0", stepError.Message);
        var drawError = Assert.Throws<InvalidDataException>(() => EndlessOceanMod.PatchSonarDraw("noise_alpha = 1;"));
        Assert.Contains("gml_Object_SonarPanel_Draw_0", drawError.Message);
    }

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
        AddSonarPatchFixtures(data, recompile);

        EndlessOceanMod.RefreshMergedLayout(data, recompile, generated, decisions);
        string source = generated[settings];
        uint width = world.Width;
        Assert.Contains("ocean_start = 100000;", source);
        Assert.Contains($"ocean_end = {width};", source);
        Assert.Equal(source, recompile[settings]);
        Assert.Contains("jamm_chunk_stream(ocean_chunks, first, last, jmk_endless_ocean_load, jmk_endless_ocean_release)", generated[step]);
        Assert.Equal(generated[step], recompile[step]);
        Assert.Contains("jmk_endless_ocean_sonar_view();", recompile["gml_Object_SonarPanel_Step_0"]);
        Assert.Contains("jmk_endless_ocean_sonar_clear()", recompile["gml_Object_SonarPanel_Draw_0"]);
        Assert.Single(decisions);
        EndlessOceanMod.RefreshMergedLayout(data, recompile, generated, decisions);
        Assert.Equal(width, world.Width);
        Assert.Single(Regex.Matches(recompile["gml_Object_SonarPanel_Step_0"], "jmk_endless_ocean_sonar_view\\(\\);").Cast<Match>());

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
    public void GeneratedFishInitializeSonarStateBeforeTheyCanBeDrawn()
    {
        using var data = UndertaleData.CreateNew();
        // Shift object and sprite indices past vanilla ranges to ensure no bare vanilla indices are emitted.
        for (int i = 0; i < 500; i++)
        {
            ModObjects.Create(data, $"dummy_obj_{i}", persistent: false);
            data.Sprites.Add(new UndertaleSprite { Name = data.Strings.MakeString($"dummy_spr_{i}") });
        }
        foreach (string name in new[] { "Fish", "Abyssal", "Dragon", "Problematicus", "SonarPanel", "SonarPanelAdvanced" })
            ModObjects.Create(data, name, persistent: false);
        foreach (string sprite in new[] { "spr_abyssal_head", "spr_dragon_head", "spr_problem_head", "spr_sonarblip" })
            data.Sprites.Add(new UndertaleSprite { Name = data.Strings.MakeString(sprite) });

        var sonar = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jamm_fish_sonar_init");
        data.Scripts.Add(new UndertaleScript
        {
            Name = data.Strings.MakeString("jamm_fish_sonar_init"),
            Code = sonar
        });
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(sonar, EndlessOceanMod.Source("jamm_fish_sonar_init"));
        ModObjects.Compile(compiler);

        string compiled = ModObjects.Decompile(data, sonar);
        Assert.Contains("instance_exists", compiled);
        Assert.Contains("asset_get_index(\"SonarPanelAdvanced\")", compiled);
        Assert.Contains("asset_get_index(\"SonarPanel\")", compiled);
        Assert.Contains("asset_get_index(\"Abyssal\")", compiled);
        Assert.Contains("asset_get_index(\"spr_abyssal_head\")", compiled);
        Assert.Contains("asset_get_index(\"Dragon\")", compiled);
        Assert.Contains("asset_get_index(\"spr_dragon_head\")", compiled);
        Assert.Contains("asset_get_index(\"Problematicus\")", compiled);
        Assert.Contains("asset_get_index(\"spr_problem_head\")", compiled);
        Assert.Contains("asset_get_index(\"spr_sonarblip\")", compiled);
        Assert.Contains("s1 = sprite_index", compiled);
        Assert.Contains("s2 = image_index", compiled);
        Assert.Contains("s3 = image_xscale", compiled);
        Assert.Contains("s4 = image_yscale", compiled);
        Assert.Contains("s5 = 1", compiled);
        Assert.Contains("s5 = (size + 0.5) / 3.5", compiled);
        Assert.DoesNotMatch(@"\b(132|135|225|238|242|334|424|429|430)\b", compiled);
        int[] bareVanillaIndices = [132, 135, 225, 238, 242, 334, 424, 429, 430];
        foreach (var instr in sonar.Instructions)
        {
            if (instr.Type1 == UndertaleInstruction.DataType.Int16)
                Assert.DoesNotContain((int)instr.ValueShort, bareVanillaIndices);
            if (instr.Type1 == UndertaleInstruction.DataType.Int32)
                Assert.DoesNotContain(instr.ValueInt, bareVanillaIndices);
        }
        Assert.Contains("jamm_fish_sonar_init(swimmer)", EndlessOceanMod.Source("jmk_endless_ocean_load"));
        Assert.Contains("if (!instance_exists(swimmer)) continue;", EndlessOceanMod.Source("jmk_endless_ocean_load"));
    }

    [Fact]
    public void GeneratorInitializesLungeModifiersExcludesSingletonFishAndClearsDeactivatedFlag()
    {
        using var data = UndertaleData.CreateNew();
        foreach (string name in new[] { "Collidable", "Fish", "Rod", "Girl", "Lure", "CatSwim", "Light" })
            ModObjects.Create(data, name, persistent: false);
        foreach (string scriptName in new[]
        {
            "jmk_endless_ocean_layout", "jmk_endless_ocean_ground", "jmk_endless_ocean_scenery",
            "jamm_fish_sonar_init", "fish_object_to_name", "get_index_for_name",
            "jamm_chunk_unit", "jmk_endless_ocean_species"
        })
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + scriptName);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(scriptName), Code = code });
        }
        foreach (string species in new[]
        {
            "Swooper", "Roundfin", "Garump", "Spinefish", "Yellowjack", "Puff", "Celad", "Inno",
            "Garder", "Pink_Kisser", "Pennyfin", "Lionfish", "Mustardfish", "Elder_Mustardfish",
            "Felish", "Purdy", "Leofish", "Sassy", "Boom_Puff", "Dijon", "Stankfin", "Cavin",
            "Twohead", "Garm", "Jackfish", "Primal_Turgeon", "Mudback", "Cuddlecanth", "Buffalofish",
            "Geiger", "Underfin"
        })
            ModObjects.Create(data, species, persistent: false);

        var loadCode = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jmk_endless_ocean_load");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("jmk_endless_ocean_load"), Code = loadCode });
        var disposeCode = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jmk_endless_ocean_dispose");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("jmk_endless_ocean_dispose"), Code = disposeCode });

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(data.Code.ByName("gml_Script_jmk_endless_ocean_species")!, EndlessOceanMod.Source("jmk_endless_ocean_species"));
        compiler.QueueCodeReplace(loadCode, EndlessOceanMod.Source("jmk_endless_ocean_load"));
        compiler.QueueCodeReplace(disposeCode, EndlessOceanMod.Source("jmk_endless_ocean_dispose"));
        ModObjects.Compile(compiler);

        string compiledSpecies = ModObjects.Decompile(data, data.Code.ByName("gml_Script_jmk_endless_ocean_species")!);
        Assert.DoesNotContain("Torby", compiledSpecies);
        Assert.DoesNotContain("Ancient_Kingfish", compiledSpecies);
        Assert.DoesNotContain("Torby", EndlessOceanMod.Source("jmk_endless_ocean_species"));
        Assert.DoesNotContain("Ancient_Kingfish", EndlessOceanMod.Source("jmk_endless_ocean_species"));

        string compiledLoad = ModObjects.Decompile(data, loadCode);
        int guardPos = compiledLoad.IndexOf("instance_exists(swimmer)", StringComparison.Ordinal);
        int sonarPos = compiledLoad.IndexOf("jamm_fish_sonar_init(swimmer)", StringComparison.Ordinal);
        int lungePos = compiledLoad.IndexOf("base_lunge_mod = 1", StringComparison.Ordinal);
        Assert.True(guardPos >= 0, compiledLoad);
        Assert.True(sonarPos > guardPos, compiledLoad);
        Assert.True(lungePos > sonarPos, compiledLoad);
        Assert.Contains("base_lunge_offset = 0", compiledLoad);
        Assert.Contains("gear_attract_scalar", compiledLoad);
        Assert.Contains("gear_repel_scalar", compiledLoad);
        Assert.Contains("gear_lunge_scalar", compiledLoad);
        Assert.Contains("gear_target_lunge_scalar", compiledLoad);

        string compiledDispose = ModObjects.Decompile(data, disposeCode);
        Assert.Contains("deactivated = 0", compiledDispose);
        Assert.Matches(@"deactivated = 0;\s*return (0|false);", compiledDispose);
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
                "Camera", "Boat", "Boat1", "Boat7", "SaveManager", "Rod", "Lure", "Girl", "Message", "SonarPanel", "BuoyAbstract", "Collidable", "CaveRock", "Light", "CatSwim",
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

            var buoySprite = new UndertaleSprite { Name = data.Strings.MakeString("spr_test_buoy"), Width = 32, Height = 64 };
            data.Sprites.Add(buoySprite);
            data.GameObjects.ByName("BuoyAbstract")!.Sprite = buoySprite;
            AddSonarPatchFixtures(data, null);
            var boatCreateCompiler = new CompileGroup(data);
            boatCreateCompiler.QueueCodeReplace(data.GameObjects.ByName("Boat7")!.EventHandlerFor(EventType.Create, 0u, data), "boost_radar = 1;\nMAX_X = 65000;");
            boatCreateCompiler.QueueCodeReplace(data.GameObjects.ByName("Boat1")!.EventHandlerFor(EventType.Create, 0u, data), "MAX_X = 12000;");
            ModObjects.Compile(boatCreateCompiler);
            string boat7Create = ModObjects.Decompile(data, data.Code.ByName("gml_Object_Boat7_Create_0")!);
            string boat1Create = ModObjects.Decompile(data, data.Code.ByName("gml_Object_Boat1_Create_0")!);
            Assert.Contains("boost_radar = 1", boat7Create);
            Assert.Contains("MAX_X = 65000", boat7Create);

            var rockBig = new UndertaleGameObject { Name = data.Strings.MakeString("CaveRockBig"), Sprite = imported["spr_caverock_big"] };
            var boat3 = new UndertaleGameObject { Name = data.Strings.MakeString("Boat3"), Sprite = imported["spr_boat3"] };
            data.GameObjects.Add(rockBig);
            data.GameObjects.Add(boat3);

            foreach (string scriptName in new[] { "fish_object_to_name", "get_index_for_name", "fish_respawn", "x_pos_to_y_off" })
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

            Assert.Equal(boat7Create, ModObjects.Decompile(data, data.Code.ByName("gml_Object_Boat7_Create_0")!));
            Assert.Equal(boat1Create, ModObjects.Decompile(data, data.Code.ByName("gml_Object_Boat1_Create_0")!));

            string compiledSonar = ModObjects.Decompile(data, data.Code.ByName("gml_Script_jamm_fish_sonar_init")!);
            Assert.DoesNotMatch(@"\b(132|135|225|238|242|334|424|429|430)\b", compiledSonar);

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

    private static void AddSonarPatchFixtures(UndertaleData data, Dictionary<string, string>? recompile)
    {
        foreach ((string name, string source) in new[]
        {
            ("gml_Object_SonarPanel_Step_0", "if (Lure.state == 0) { readyToBeginTracking = 1; }"),
            ("gml_Object_SonarPanel_Draw_0", "if (instance_exists(Boat) && Boat.boost_radar) { noise_alpha = 0; }")
        })
        {
            var code = data.Code.ByName(name) ?? UndertaleCode.CreateEmptyEntry(data, name);
            if (recompile is not null) recompile[name] = source;
            else
            {
                var compiler = new CompileGroup(data);
                compiler.QueueCodeReplace(code, source);
                ModObjects.Compile(compiler);
            }
        }
    }
}
