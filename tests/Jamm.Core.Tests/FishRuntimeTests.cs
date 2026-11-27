using System.Text.Json;
using ImageMagick;
using Jamm.Core.Building;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies FishRuntime standalone jamm_api_init fallback, jamm_fish_register, and 22b coordination.</summary>
public sealed class FishRuntimeTests
{
    [Fact]
    public void FishOnlyInitializerDoesNotActivateItemSavePatches()
    {
        const string name = "gml_Script_jamm_init_demo";
        const string gml = "jamm_fish_register(\"demo:f\", DemoFish, 0, \"F\", \"Desc\", 0, 10, 1400, 1600, 1);\nreturn 0;";
        var snapshot = AssetFields.Json(new { });
        var source = new CodeText(gml, "gml", 0, 0, null, []);
        var plan = new BuildPlan([new("code", name, 0, new(name, null, snapshot, [], null, source), snapshot, gml)], []);
        var baseline = new Dictionary<string, CodeText>
        {
            ["gml_Object_SaveManager_Other_13"] = new("return 0;", "gml", 0, 0, null, [])
        };
        Assert.Same(plan, ItemsRuntime.PreparePlan(plan, baseline));
    }

    [Fact]
    public void FishRuntimeInjectsRegisterStandaloneApiInitAndNameLookup()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteSyntheticGame(basePath, includeApiInit: false);
        WriteSyntheticGame(modPath, includeApiInit: false, d =>
        {
            var fishParent = d.GameObjects.ByName("FishSmall")!;
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("jmk_demo_minnow"), ParentId = fishParent });
            var fishCreate = d.GameObjects.ByName("jmk_demo_minnow")!.EventHandlerFor(EventType.Create, 0u, d);

            var regCode = UndertaleCode.CreateEmptyEntry(d, "gml_Script_jamm_fish_register");
            d.Scripts.Add(new() { Name = d.Strings.MakeString("jamm_fish_register"), Code = regCode });

            var initCode = UndertaleCode.CreateEmptyEntry(d, "gml_Script_jamm_init_demo");
            d.Scripts.Add(new() { Name = d.Strings.MakeString("jamm_init_demo"), Code = initCode });

            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(fishCreate, "instance_destroy();\n");
            compiler.QueueCodeReplace(regCode, "return -1;\n");
            compiler.QueueCodeReplace(initCode, "jamm_fish_register(\"demo:minnow\", jmk_demo_minnow, 0, \"Minnow\", \"Tiny fish.\", 1100, 1500, 1400, 1700, 3);\nreturn 0;\n");
            Assert.True(compiler.Compile().Successful);
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "DemoFishMod");

        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.Contains("Fish API:", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(output);
        string reg = PatchBuilder.ReadCode(merged, FishRuntime.RegisterCode).Text;
        Assert.Contains("instance_number(", reg);
        Assert.Contains("instance_create(", reg);
        int createPos = reg.IndexOf("jamm_inst = instance_create(", StringComparison.Ordinal);
        Assert.True(createPos >= 0);
        int guardPos = reg.IndexOf("if (instance_exists(jamm_inst))", createPos, StringComparison.Ordinal);
        int sonarPos = reg.IndexOf("jamm_fish_sonar_init(jamm_inst)", createPos, StringComparison.Ordinal);
        int metadataPos = reg.IndexOf("jamm_inst.start_x =", createPos, StringComparison.Ordinal);
        int catalogPos = reg.IndexOf("jamm_catalog_register(", StringComparison.Ordinal);
        Assert.True(guardPos > createPos && sonarPos > guardPos && metadataPos > sonarPos);
        Assert.True(catalogPos > metadataPos);
        Assert.Contains("TRUE_START_X", reg);
        Assert.Contains("jamm_fish_names", reg);
        Assert.Contains("CatalogData", reg);
        Assert.Contains("jamm_catalog_register(", reg);
        Assert.Contains("jamm_fish_sonar_init(jamm_inst)", reg);
        string sonar = PatchBuilder.ReadCode(merged, FishSonarRuntime.Code).Text;
        Assert.Contains("s1 = sprite_index", sonar);
        Assert.Contains("s5 = (size + 0.5) / 3.5", sonar);
        Assert.Single(merged.Data.Scripts, s => s.Name.Content == FishSonarRuntime.Script);

        string apiInit = PatchBuilder.ReadCode(merged, FishRuntime.ApiInitCode).Text;
        Assert.Contains("jamm_init_demo()", apiInit);

        string saveStep = PatchBuilder.ReadCode(merged, FishRuntime.SaveManagerStep).Text;
        Assert.Contains("jamm_api_done", saveStep);
        Assert.Contains("jamm_api_init()", saveStep);
        Assert.True(saveStep.IndexOf("jamm_api_init()", StringComparison.Ordinal) < saveStep.IndexOf("if (!loaded)", StringComparison.Ordinal));

        string toName = PatchBuilder.ReadCode(merged, FishRuntime.FishObjectToName).Text;
        Assert.Contains("jamm_fish_names", toName);
    }

    [Fact]
    public void SharedSonarInitializerIsAddedOnlyOnceForMultipleFishFeatures()
    {
        using var data = UndertaleData.CreateNew();
        var recompile = new Dictionary<string, string>(StringComparer.Ordinal);
        var generated = new Dictionary<string, string>(StringComparer.Ordinal);

        FishSonarRuntime.Ensure(data, recompile, generated);
        FishSonarRuntime.Ensure(data, recompile, generated);

        Assert.Single(data.Scripts, s => s.Name.Content == FishSonarRuntime.Script);
        Assert.Single(recompile);
        Assert.Single(generated);
        Assert.Contains("asset_get_index(\"SonarPanel\")", recompile[FishSonarRuntime.Code]);
    }

    [Fact]
    public void SharedSonarInitializerReusesExistingCodeEntryWithoutDuplicating()
    {
        using var data = UndertaleData.CreateNew();
        var existingCode = UndertaleCode.CreateEmptyEntry(data, FishSonarRuntime.Code);
        var recompile = new Dictionary<string, string>(StringComparer.Ordinal);
        var generated = new Dictionary<string, string>(StringComparer.Ordinal);

        FishSonarRuntime.Ensure(data, recompile, generated);

        var script = Assert.Single(data.Scripts, s => s.Name.Content == FishSonarRuntime.Script);
        Assert.Same(existingCode, Assert.Single(data.Code, c => c.Name.Content == FishSonarRuntime.Code));
        Assert.Same(existingCode, script.Code);

        script.Code = null;
        FishSonarRuntime.Ensure(data, recompile, generated);

        Assert.Single(data.Scripts, s => s.Name.Content == FishSonarRuntime.Script);
        Assert.Single(data.Code, c => c.Name.Content == FishSonarRuntime.Code);
        Assert.Same(existingCode, script.Code);
    }

    [Fact]
    public void SharedSonarInitializerIsEmittedOnceAcrossFishApiWeatherApiAndEndlessOcean()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string fishModPath = temp.PathFor("fish-mod.win");
        string weatherModPath = temp.PathFor("weather-mod.win");
        string mergedPath = temp.PathFor("merged.win");

        WriteCombinedSyntheticGame(basePath, temp);
        WriteCombinedSyntheticGame(fishModPath, temp, d =>
        {
            var fishParent = d.GameObjects.ByName("FishSmall")!;
            var minnow = new UndertaleGameObject { Name = d.Strings.MakeString("jmk_demo_minnow"), ParentId = fishParent };
            d.GameObjects.Add(minnow);
            var fishCreate = minnow.EventHandlerFor(EventType.Create, 0u, d);

            var regCode = UndertaleCode.CreateEmptyEntry(d, "gml_Script_jamm_fish_register");
            d.Scripts.Add(new() { Name = d.Strings.MakeString("jamm_fish_register"), Code = regCode });

            var initCode = UndertaleCode.CreateEmptyEntry(d, "gml_Script_jamm_init_demo");
            d.Scripts.Add(new() { Name = d.Strings.MakeString("jamm_init_demo"), Code = initCode });

            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(fishCreate, "instance_destroy();\n");
            compiler.QueueCodeReplace(regCode, "return -1;\n");
            compiler.QueueCodeReplace(initCode, "jamm_fish_register(\"demo:minnow\", jmk_demo_minnow, 0, \"Minnow\", \"Tiny fish.\", 1100, 1500, 1400, 1700, 3);\nreturn 0;\n");
            Assert.True(compiler.Compile().Successful);
        });

        string weatherPkg = Directory.CreateDirectory(temp.PathFor("weather-pkg")).FullName;
        File.WriteAllText(Path.Combine(weatherPkg, "jamm-mod.json"), """
            {"id":"weather-test","name":"Weather Test","weathers":[{"id":"rain","name":"Rain"}]}
            """);
        ModKitBuilder.Build(weatherPkg, basePath, weatherModPath);

        using var baseline = GameFileLoader.Load(basePath);
        using var fishFile = GameFileLoader.Load(fishModPath);
        using var weatherFile = GameFileLoader.Load(weatherModPath);
        var fishInput = new BuildInput(PatchBuilder.Create(baseline, fishFile, "DemoFishMod"), fishModPath);
        var weatherInput = new BuildInput(PatchBuilder.Create(baseline, weatherFile, "WeatherTest"), weatherModPath);

        var build = ArchiveBuilder.Build(basePath, [fishInput, weatherInput], mergedPath);
        Assert.Contains(build.Plan.Decisions, d => d.Contains("Fish API:", StringComparison.Ordinal));
        Assert.Contains(build.Plan.Decisions, d => d.Contains("Weather API:", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(mergedPath);
        Assert.Single(merged.Data.Scripts, s => s.Name.Content == FishSonarRuntime.Script);
        Assert.Single(merged.Data.Code, c => c.Name.Content == FishSonarRuntime.Code);
        Assert.Contains("jamm_fish_sonar_init(jamm_inst)", PatchBuilder.ReadCode(merged, FishRuntime.RegisterCode).Text);
        Assert.Contains("jamm_fish_sonar_init(fish)", PatchBuilder.ReadCode(merged, "gml_Script_jamm_weather_population").Text);

        EndlessOceanMod.Apply(merged.Data);

        Assert.Single(merged.Data.Scripts, s => s.Name.Content == FishSonarRuntime.Script);
        Assert.Single(merged.Data.Code, c => c.Name.Content == FishSonarRuntime.Code);
        string compiledSonar = ModObjects.Decompile(merged.Data, merged.Data.Code.ByName(FishSonarRuntime.Code)!);
        Assert.Contains("s1 = sprite_index", compiledSonar);
        Assert.Contains("s5 = (size + 0.5) / 3.5", compiledSonar);
        string compiledLoad = ModObjects.Decompile(merged.Data, merged.Data.Code.ByName("gml_Script_jmk_endless_ocean_load")!);
        Assert.Contains("jamm_fish_sonar_init(swimmer)", compiledLoad);
    }

    [Fact]
    public void FishRuntimeCoordinatesWithExistingApiInitWithoutDuplicating()
    {
        var snapshot = JsonSerializer.SerializeToElement(new { });
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [CatalogMerge.Create] = new("object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\njamm_catalog_notice = 0;\njamm_catalog_legacy[0, 0] = 0;\n", "gml", 0, 0, null, []),
            [FishRuntime.SaveManagerStep] = new("// already wired by 22b\njamm_api_init();\nif (!loaded) { loaded = 1; }\n", "gml", 0, 0, null, []),
            [FishRuntime.ApiInitCode] = new("jamm_init_items();\njamm_init_fish();\n", "gml", 0, 0, null, []),
            [FishRuntime.FishObjectToName] = new("return object_get_name(argument0);\n", "gml", 0, 0, null, [])
        };

        var assets = new List<PlannedAsset>
        {
            new("code", "gml_Script_jamm_init_fish", 0, new("gml_Script_jamm_init_fish", null, snapshot, [], null, new("jamm_fish_register(\"a:b\", MyFish, 0, \"B\", \"d\", 0, 10, 0, 10, 1);", "gml", 0, 0, null, [])), snapshot, "jamm_fish_register(\"a:b\", MyFish, 0, \"B\", \"d\", 0, 10, 0, 10, 1);")
        };

        var plan = FishRuntime.PreparePlan(new BuildPlan(assets, []), baseline);
        Assert.Contains(plan.Assets, a => a.Name == FishRuntime.RegisterCode);
        Assert.Contains(plan.Assets, a => a.Name == FishRuntime.FishObjectToName);
        Assert.DoesNotContain(plan.Assets, a => a.Name == FishRuntime.ApiInitCode);
        Assert.DoesNotContain(plan.Assets, a => a.Name == FishRuntime.SaveManagerStep);
        Assert.Contains(plan.Decisions, d => d.Contains("existing jamm_api_init", StringComparison.Ordinal));
    }

    [Fact]
    public void FishRuntimeLeavesBuildsWithoutFishApiUntouched()
    {
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [FishRuntime.SaveManagerStep] = new("if (!loaded) { loaded = 1; }\n", "gml", 0, 0, null, [])
        };
        var input = new BuildPlan([], []);
        var result = FishRuntime.PreparePlan(input, baseline);
        Assert.Empty(result.Assets);
        Assert.Empty(result.Decisions);
    }

    [Fact]
    public void FishRuntimeGuardsCaughtFishNameAndValueBeforeHudDraw()
    {
        const string draw = "with (Lure.fish)\n{\n    draw_text_shadowed(Camera.x, Camera.y - 6, name);\n    draw_text_shadowed(Camera.x, Camera.y + 26, \"Worth \" + number_to_dollar_string(value));\n}\n";
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [FishRuntime.CatalogDataDraw] = new(draw, "gml", 0, 0, null, [])
        };

        var plan = FishRuntime.PreparePlan(new BuildPlan([], []), baseline);

        string guarded = Assert.Single(plan.Assets, a => a.Name == FishRuntime.CatalogDataDraw).Gml!;
        int nameGuard = guarded.IndexOf("if (!variable_instance_exists(id, \"name\"))", StringComparison.Ordinal);
        int nameDraw = guarded.IndexOf("draw_text_shadowed(Camera.x, Camera.y - 6, name);", StringComparison.Ordinal);
        Assert.True(nameGuard >= 0 && nameGuard < nameDraw);
        Assert.Contains("name = object_get_name(object_index);", guarded);
        int valueGuard = guarded.IndexOf("if (!variable_instance_exists(id, \"value\"))", StringComparison.Ordinal);
        int valueDraw = guarded.IndexOf("number_to_dollar_string(value)", StringComparison.Ordinal);
        Assert.True(valueGuard >= nameDraw && valueGuard < valueDraw);
        Assert.Contains("value = 0;", guarded);
    }

    [Fact]
    public void FishApiModAlsoActivatesCatalogRuntimeNameKeyedSavePersistence()
    {
        var snapshot = JsonSerializer.SerializeToElement(new { });
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [CatalogMerge.Create] = new("object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\n", "gml", 0, 0, null, []),
            [FishRuntime.SaveManagerStep] = new("if (!loaded) { loaded = 1; }\n", "gml", 0, 0, null, []),
            [FishRuntime.FishObjectToName] = new("return object_get_name(argument0);\n", "gml", 0, 0, null, []),
            ["gml_Object_SaveManager_Other_13"] = new(
                "var i = 0, j = 0;\ncount[i, j] = ini_read_real(\"Catalog\", \"f\" + string(i) + string(j), 0);\nlife[i, j] = ini_read_real(\"Catalog\", \"li\" + string(i) + string(j), 3);\n",
                "gml", 0, 0, null, []),
            ["gml_Object_SaveManager_Other_14"] = new(
                "var i = 0, j = 0;\nini_write_real(\"Catalog\", \"f\" + string(i) + string(j), count[i, j]);\nini_write_real(\"Catalog\", \"li\" + string(i) + string(j), life[i, j]);\n",
                "gml", 0, 0, null, [])
        };

        var assets = new List<PlannedAsset>
        {
            new("code", "gml_Script_jamm_init_demo", 0, new("gml_Script_jamm_init_demo", null, snapshot, [], null, new("jamm_fish_register(\"demo:minnow\", jmk_demo_minnow, 0, \"Minnow\", \"Tiny fish.\", 1100, 1500, 1400, 1700, 3);", "gml", 0, 0, null, [])), snapshot, "jamm_fish_register(\"demo:minnow\", jmk_demo_minnow, 0, \"Minnow\", \"Tiny fish.\", 1100, 1500, 1400, 1700, 3);")
        };

        var plan = CatalogRuntime.PreparePlan(FishRuntime.PreparePlan(new BuildPlan(assets, []), baseline), baseline);
        string mergedRead = plan.Assets.Single(a => a.Name == "gml_Object_SaveManager_Other_13").Gml!;
        string mergedWrite = plan.Assets.Single(a => a.Name == "gml_Object_SaveManager_Other_14").Gml!;
        Assert.Contains("jamm_catalog_read(\"f\"", mergedRead);
        Assert.Contains("jamm_catalog_read(\"li\"", mergedRead);
        Assert.Contains("\"JAMM_Catalog\"", mergedWrite);
        Assert.Contains("\"f:\" + object_get_name(object[i, j])", mergedWrite);
        Assert.Contains("\"li:\" + object_get_name(object[i, j])", mergedWrite);
    }

    private static void WriteSyntheticGame(string path, bool includeApiInit, Action<UndertaleData>? edit = null)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.GeneralInfo.Build = 1773;

        foreach (string name in new[] { "Fish", "FishSmall", "BaseFish", "CatalogData", "SaveManager" })
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        data.GameObjects.ByName("FishSmall")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("BaseFish")!.ParentId = data.GameObjects.ByName("FishSmall");

        var catalogCreate = data.GameObjects.ByName("CatalogData")!.EventHandlerFor(EventType.Create, 0u, data);
        var saveStep = data.GameObjects.ByName("SaveManager")!.EventHandlerFor(EventType.Step, 0u, data);
        var toNameCode = UndertaleCode.CreateEmptyEntry(data, FishRuntime.FishObjectToName);
        data.Scripts.Add(new() { Name = data.Strings.MakeString("fish_object_to_name"), Code = toNameCode });

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(catalogCreate,
            """
            object[0, 0] = BaseFish;
            name[0, 0] = "Base";
            map[0, 0] = 0;
            desc[0, 0] = "Base desc";
            """);
        compiler.QueueCodeReplace(saveStep,
            """
            if (!loaded)
            {
                loaded = 1;
            }
            """);
        compiler.QueueCodeReplace(toNameCode,
            """
            var n;
            n = string_replace_all(object_get_name(argument0), "_", " ");
            return n;
            """);
        if (includeApiInit)
        {
            var apiCode = UndertaleCode.CreateEmptyEntry(data, FishRuntime.ApiInitCode);
            data.Scripts.Add(new() { Name = data.Strings.MakeString(FishRuntime.ApiInitScript), Code = apiCode });
            compiler.QueueCodeReplace(apiCode, "return 0;\n");
        }
        Assert.True(compiler.Compile().Successful);

        edit?.Invoke(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static void WriteCombinedSyntheticGame(string path, TemporaryDirectory temp, Action<UndertaleData>? edit = null)
    {
        string rockPath = temp.PathFor("rock.png");
        string boatPath = temp.PathFor("boat.png");
        if (!File.Exists(rockPath))
        {
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
            using var rockImg = new MagickImage(rockRgba, new PixelReadSettings(512, 512, StorageType.Char, PixelMapping.RGBA));
            rockImg.Write(rockPath);
        }
        if (!File.Exists(boatPath))
        {
            using var boatImg = new MagickImage(new MagickColor(90, 100, 110), 840, 315);
            boatImg.Write(boatPath);
        }

        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.GeneralInfo.Build = 1773;
        data.Rooms[0].Name = data.Strings.MakeString("NewWorld");
        data.Rooms[0].Width = 100000;
        data.Rooms[0].Height = 13000;
        data.Rooms.Add(new() { Name = data.Strings.MakeString("room11") });
        data.Sprites.Add(new() { Name = data.Strings.MakeString("spr"), Width = 1, Height = 1 });

        var imported = ModKitSpriteImporter.ImportAll(data, [
            new("spr_caverock_big", rockPath, 1, null, false, false),
            new("spr_boat3", boatPath, 1, null, false, false),
        ]);

        string[] objects =
        [
            "Fish", "FishSmall", "BaseFish", "CatalogData", "SaveManager",
            "Girl", "Sea", "ParallaxBackground", "AmbianceController", "Collidable",
            "Rod", "Lure", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons",
            "Camera", "Boat", "Boat7", "CaveRock", "Light", "CatSwim", "SonarPanel", "Message", "BuoyAbstract",
            "CaveRockWide", "CaveRockLeg", "CaveRockSig", "CaveRockTear", "CaveRockTee", "CaveRockWedge",
            "JWeedSmall", "JWeedMedium", "Coral", "Coral2", "CoralDark", "CoralFG3", "GlowPlant", "BulbPlantShort", "ChainPlant",
            "Swooper", "Roundfin", "Garump", "Spinefish", "Yellowjack", "Puff", "Celad", "Inno",
            "Garder", "Pink_Kisser", "Pennyfin", "Lionfish", "Mustardfish", "Elder_Mustardfish",
            "Felish", "Torby", "Purdy", "Leofish", "Sassy", "Boom_Puff", "Dijon", "Stankfin", "Cavin",
            "Twohead", "Garm", "Jackfish", "Primal_Turgeon", "Mudback", "Cuddlecanth", "Buffalofish",
            "Geiger", "Ancient_Kingfish", "Underfin"
        ];
        foreach (string name in objects)
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        data.GameObjects.ByName("BuoyAbstract")!.Sprite = data.Sprites.ByName("spr");
        data.GameObjects.ByName("FishSmall")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("BaseFish")!.ParentId = data.GameObjects.ByName("FishSmall");
        data.GameObjects.Add(new() { Name = data.Strings.MakeString("CaveRockBig"), Sprite = imported["spr_caverock_big"] });
        data.GameObjects.Add(new() { Name = data.Strings.MakeString("Boat3"), Sprite = imported["spr_boat3"] });

        var compiler = new CompileGroup(data);
        Event("CatalogData", EventType.Create, 0, """
            object[0, 0] = BaseFish;
            name[0, 0] = "Base";
            map[0, 0] = 0;
            desc[0, 0] = "Base desc";
            """);
        Event("SaveManager", EventType.Step, 0, """
            if (!loaded)
            {
                loaded = 1;
            }
            """);
        foreach (uint ev in new[] { 14u, 15u })
        {
            Event("SaveManager", EventType.Other, ev, """
                ini_write_real("Fishes", string(id), 1);
                ini_write_real("Fishes", string(id), 2);
                """);
        }
        Event("Girl", EventType.Step, 0, "audio_sound_gain(background_music, gain * power(background_music_volume, 2), 0);");
        Event("SonarPanel", EventType.Step, 0, "if (Lure.state == 0) { readyToBeginTracking = 1; }");
        Event("SonarPanel", EventType.Draw, 0, "if (instance_exists(Boat) && Boat.boost_radar) { noise_alpha = 0; }");
        Event("Fish", EventType.Step, 2, "speed = spd;");
        Event("Sea", EventType.Step, 0, "x = origin + (sin(phase) * SCALE);");
        Event("ParallaxBackground", EventType.Draw, 0, """
            draw_rectangle_colour(0, 0, 100, 100, sky_color, sky_color, sky_color, sky_color, 0);
            alpha = 1 - power(1 - value, 2);
            """);
        Script("fish_object_to_name", """
            var n;
            n = string_replace_all(object_get_name(argument0), "_", " ");
            return n;
            """);
        Script("is_it_night", "return false;");
        Script("x_pos_to_y_off", "var ty = argument0; return ty;");
        Script("color_from_depth", "return make_color_rgb(val * 255, val * 255, val * val * 240);");
        Script("get_index_for_name", "return 0;");
        Script("fish_respawn", "return 0;");
        ArchiveVerifier.Compile(compiler, "synthetic combined world");

        edit?.Invoke(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);

        void Event(string name, EventType type, uint subtype, string gml) =>
            compiler.QueueCodeReplace(data.GameObjects.ByName(name)!.EventHandlerFor(type, subtype, data), gml);
        void Script(string name, string gml)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new() { Name = data.Strings.MakeString(name), Code = code });
            compiler.QueueCodeReplace(code, gml);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgfmm-fish-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => System.IO.Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
