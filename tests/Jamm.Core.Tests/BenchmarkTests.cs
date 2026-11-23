using System.Security.Cryptography;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Validates the test-only benchmark instrumentation pass against synthetic game archives.</summary>
public class BenchmarkTests
{
    [Fact]
    public void DeepwaterDefaultsUseCensusedViewportCenter()
    {
        var options = new BenchmarkOptions(Scene: BenchmarkScene.Deepwater);
        Assert.Equal(21000, options.DeepX);
        Assert.Equal(7200, options.DeepY);

        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        BenchmarkMod.Apply(data, options);

        string create = Code(data, "Create_0");
        Assert.Contains("jamm_b_deep_x = 21000", create);
        Assert.Contains("jamm_b_deep_y = 7200", create);
    }

    [Theory]
    [InlineData(BenchmarkScene.Dock, "dock")]
    [InlineData(BenchmarkScene.Sonar, "sonar")]
    [InlineData(BenchmarkScene.Deepwater, "deepwater")]
    [InlineData(BenchmarkScene.Lightcycle, "lightcycle")]
    public void BenchmarkPassCompilesDeterministicallyAcrossAllScenes(BenchmarkScene scene, string expectedScene)
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        int originalCodeCount = data.Code.Count;

        var options = new BenchmarkOptions(
            Scene: scene,
            WarmupSeconds: 3.5,
            CaptureSeconds: 12.0,
            MaxSamples: 24000,
            OutputFile: null,
            AutoExit: true,
            Seed: 4242,
            DockX: 815,
            DeepX: 34000,
            DeepY: 7500,
            BoatSlot: 7,
            DeepSonar: scene == BenchmarkScene.Sonar,
            ShowHud: false);

        BenchmarkMod.Apply(data, options);

        var bench = data.GameObjects.ByName(BenchmarkMod.ObjectName);
        Assert.NotNull(bench);
        Assert.True(bench.Persistent);
        Assert.Equal(-999999, bench.Depth);
        Assert.Same(bench, data.Rooms.ByName("NewWorld")!.GameObjects[0].ObjectDefinition);
        Assert.Same(bench, data.Rooms.ByName("room11")!.GameObjects[0].ObjectDefinition);

        // Adds 8 obj_jamm_bench events (incl. PreDraw) + jamm_b_stats_line + jamm_b_apply_fish_cap + 2 fish timing scripts.
        Assert.Equal(originalCodeCount + 12, data.Code.Count);

        string create = Code(data, "Create_0");
        string destroy = Code(data, "Destroy_0");
        string roomStart = Code(data, "Other_4");
        string beginStep = Code(data, "Step_1");
        string endStep = Code(data, "Step_2");
        string preDraw = Code(data, "Draw_76");
        string drawGui = Code(data, "Draw_64");
        string drawGuiEnd = Code(data, "Draw_75");

        Assert.Contains($"jamm_b_scene = \"{expectedScene}\"", create);
        Assert.Contains("jamm_bench.ini", create);
        Assert.Contains("jamm_b_seed = 4242", create);
        Assert.Contains("jamm_b_frame_list = ds_list_create()", create);
        Assert.Contains("jamm_b_loop_list = ds_list_create()", create);
        Assert.Contains("jamm_b_step_list = ds_list_create()", create);
        Assert.Contains("jamm_b_draw_list = ds_list_create()", create);
        Assert.Contains("jamm_b_fish_step_list = ds_list_create()", create);
        Assert.Contains("jamm_b_fish_count_list = ds_list_create()", create);
        Assert.Contains("ds_list_destroy(jamm_b_frame_list)", destroy);

        // SaveManager is locked and slot flags cleared so no real user saves are loaded or written.
        Assert.Contains("SaveManager.saving_locked = 1", roomStart);
        Assert.Contains("SaveManager.saveflag = 0", roomStart);
        Assert.Contains("SaveManager.loadflag = 0", roomStart);
        Assert.Contains("room_goto(NewWorld)", roomStart);

        Assert.Contains("jamm_b_step_t = get_timer()", beginStep);
        Assert.Contains("random_set_seed(jamm_b_seed)", beginStep);
        Assert.Contains("AmbianceController.lockDay = 1", beginStep);
        Assert.Contains("BoatData.object", beginStep);
        Assert.Contains("SonarPanel.active = 1", beginStep);
        Assert.Contains("Camera.x = jamm_b_deep_x", beginStep);
        Assert.Contains("window_set_fullscreen(false)", beginStep);
        Assert.Contains("window_set_size(1200, 720)", beginStep);
        Assert.Contains("surface_resize(application_surface, 1200, 720)", beginStep);

        Assert.Contains("SaveManager.saving_locked = 1", endStep);
        Assert.Contains("view_xview[0]", endStep);
        Assert.Contains("view_yview[0]", endStep);

        Assert.Contains("jamm_b_draw_start_t = get_timer()", preDraw);

        // Active capture suppresses DrawGUI HUD drawing.
        Assert.Contains("jamm_b_state == 2", drawGui);

        // DrawGUIEnd measures frame-to-frame get_timer, batches into ds_list, and flushes once on bound.
        Assert.Contains("get_timer()", drawGuiEnd);
        Assert.Contains("ds_list_add(jamm_b_frame_list", drawGuiEnd);
        Assert.Contains("ds_list_add(jamm_b_loop_list", drawGuiEnd);
        Assert.Contains("ds_list_sort", drawGuiEnd);
        Assert.Contains("file_text_open_write(jamm_b_output_file)", drawGuiEnd);
        Assert.Contains("frame_index,frame_us,loop_us,present_sleep_us,step_us,draw_us,fish_step_us,fish_count", drawGuiEnd);
        Assert.Contains("present_sleep_us_mean=", drawGuiEnd);
        Assert.Contains("step_samples=", drawGuiEnd);
        Assert.Contains("draw_samples=", drawGuiEnd);
        Assert.Contains("application_surface=", drawGuiEnd);
        Assert.Contains("render_size_normalization_not_applied_or_overridden", drawGuiEnd);
        Assert.Contains("game_end()", drawGuiEnd);
    }

    [Fact]
    public void BenchmarkPassCompilesWhenOptionalRoomsAndObjectsAreAbsent()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: false);

        BenchmarkMod.Apply(data, new BenchmarkOptions(Scene: BenchmarkScene.Deepwater));

        var bench = data.GameObjects.ByName(BenchmarkMod.ObjectName);
        Assert.NotNull(bench);
        Assert.Contains(data.Rooms.ByName("NewWorld")!.GameObjects, i => i.ObjectDefinition == bench);

        string beginStep = Code(data, "Step_1");
        string drawGui = Code(data, "Draw_64");
        Assert.DoesNotContain("SaveManager", beginStep);
        Assert.DoesNotContain("BoatData", beginStep);
        Assert.DoesNotContain("SonarPanel", beginStep);
        Assert.Contains("draw_set_font(-1)", drawGui);
    }

    [Fact]
    public void BenchmarkPassComposesAfterSmoothPerfAndHdWithoutTouchingExistingCode()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        SmoothMod.Apply(data, debug: false);
        PerfMod.Apply(data, debug: false);
        Assert.Null(data.GameObjects.ByName(FishGrid.Controller));
        HdMod.Apply(data, upscaledPages: null, debug: false);

        int countBeforeBench = data.Code.Count;
        BenchmarkMod.Apply(data, new BenchmarkOptions(Scene: BenchmarkScene.Sonar));

        Assert.Equal(countBeforeBench + 12, data.Code.Count);
        Assert.Equal(-999999, data.GameObjects.ByName(BenchmarkMod.ObjectName)!.Depth);
        Assert.Equal(-1000000, data.GameObjects.ByName(SmoothMod.ObjectName)!.Depth);
        string summary = Code(data, $"Draw_{(uint)EventSubtypeDraw.DrawGUIEnd}");
        Assert.Contains("# fish_grid=", summary);
        Assert.Contains("# perf_mode=", summary);
        Assert.Contains("var jamm_b_fish_grid = 0", summary);
        Assert.Contains("var jamm_b_perf_mode = 0", summary);

        // Double instrumentation is cleanly rejected.
        Assert.Throws<InvalidDataException>(() => BenchmarkMod.Apply(data));
    }

    [Fact]
    public void PerfSchedulerIsOmittedByDefaultAndAddedForEnabledProfiles()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        PerfMod.Apply(data, applyFishGrid: false, applyScheduler: false, applyCoreOptimizations: false);

        Assert.Null(data.GameObjects.ByName(FishGrid.Controller));
        Assert.Null(data.GameObjects.ByName(PerfMode.ControllerName));
        var fishStep = data.Code.ByName("gml_Object_Fish_Step_2")!;
        Assert.DoesNotContain(fishStep.Instructions, instruction => instruction.ValueVariable?.Name.Content == "jamm_perf_mode");
        var simpleStep = new CompileGroup(data);
        simpleStep.QueueCodeReplace(fishStep, "x += 1;");
        ModObjects.Compile(simpleStep);
        var edits = new List<UndertaleCode>();
        PerfMode.ApplyScheduling(data, edits);

        Assert.Contains(fishStep.Instructions, instruction => instruction.ValueVariable?.Name.Content == "jamm_perf_mode");
        Assert.Contains(fishStep, edits);
    }

    [Fact]
    public void ProfiledBenchWrapsEventsAndScriptsAndCountsAfterExitPaths()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        var fish = data.GameObjects.ByName("Fish")!;
        var exitEvent = fish.EventHandlerFor(EventType.Alarm, 0u, data);
        var script = UndertaleCode.CreateEmptyEntry(data, "gml_Script_profile_test");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("profile_test"), Code = script });
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(exitEvent, "if (x > 0) exit; return 7;");
        compiler.QueueCodeReplace(script, "if (argument0) exit; return argument0;");
        ModObjects.Compile(compiler);

        var options = new BenchmarkOptions(Profile: true, ProfileScripts: true);
        BenchmarkMod.Apply(data, options);

        var wrappedEvent = fish.Events[(int)EventType.Alarm][0].Actions[0].CodeId;
        string eventStub = ModObjects.Decompile(data, wrappedEvent);
        Assert.Contains("global.__jamm_prof_on", eventStub);
        Assert.Contains("ds_map_set(global.__jamm_prof_us", eventStub);
        Assert.Contains("get_timer()", eventStub);

        var wrappedScript = data.Scripts.Single(s => s.Name.Content == "profile_test").Code;
        string scriptStub = ModObjects.Decompile(data, wrappedScript);
        Assert.Contains("switch (argument_count)", scriptStub);
        Assert.Contains("return __jamm_prof_result", scriptStub);
        Assert.Contains("ds_map_set(global.__jamm_prof_n", scriptStub);

        string benchCreate = Code(data, "Create_0");
        string drawEnd = Code(data, "Draw_75");
        string beginStep = ModObjects.Decompile(data, data.GameObjects.ByName(BenchmarkMod.ObjectName)!
            .EventHandlerFor(EventType.Step, EventSubtypeStep.BeginStep, data));
        Assert.Contains("jamm_b_profile = 1", benchCreate);
        Assert.Contains("global.__jamm_prof_frame", beginStep);
        Assert.Contains("global.__jamm_prof_on = 0", drawEnd);
        Assert.Contains("jamm_profile_", drawEnd);
        Assert.Contains("code_entry,calls,total_us,us_per_tick,share_of_loop", drawEnd);
        Assert.Contains("sampled_frame_inclusive", drawEnd);
    }

    [Fact]
    public void FishTraceControlsSimulationInputsAndWritesOneHashPerStep()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        var step = data.GameObjects.ByName("Fish")!.EventHandlerFor(EventType.Step, 2u, data);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(step, "if (x > 0) exit; x = current_time + delta_time + fps + random(1);");
        ModObjects.Compile(compiler);

        BenchmarkMod.Apply(data, new BenchmarkOptions(TraceFish: 1800));

        string instrumented = ModObjects.Decompile(data, step);
        Assert.Contains("jamm_trace_clock()", instrumented);
        Assert.Contains("jamm_trace_delta()", instrumented);
        Assert.Contains("jamm_trace_fps()", instrumented);
        Assert.Contains("jamm_trace_rng()", instrumented);
        Assert.Contains("random(1)", instrumented);
        Assert.Equal(default, step.Instructions.First(instruction => instruction.ValueFunction?.Name.Content == "jamm_trace_rng").ReferenceType);
        string drawEnd = Code(data, "Draw_75");
        Assert.Contains("jamm_trace_n >= 1800", drawEnd);
        Assert.Contains("buffer_md5", drawEnd);
        Assert.Contains("buffer_f64", drawEnd);
        Assert.Contains("buffer_u32", drawEnd);
    }

    [Fact]
    public void UnprofiledBenchLeavesNonFishCodeUnchangedAndOmitsProfilerWrappers()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        var sceneStep = data.GameObjects.ByName("SceneItem")!.EventHandlerFor(EventType.Step, 2u, data);
        string sceneBefore = ModObjects.Decompile(data, sceneStep);

        BenchmarkMod.Apply(data, new BenchmarkOptions(Profile: false));

        Assert.Equal(sceneBefore, ModObjects.Decompile(data, sceneStep));
        Assert.Null(data.Code.ByName("gml_Script___jamm_prof_original_0"));
        Assert.DoesNotContain("global.__jamm_prof_on", Code(data, "Draw_75"));
    }

    [Fact]
    public void ModMakerBenchCliPreservesInputArchiveAndVerifiesOutput()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "jamm-bench-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string inputPath = Path.Combine(tempDir, "input.win");
            string outputPath = Path.Combine(tempDir, "bench-out.win");
            using (var data = CreateSyntheticGame(includeOptionalObjects: true))
            using (var fs = File.Create(inputPath))
            {
                UndertaleIO.Write(fs, data);
            }

            byte[] beforeHash = SHA256.HashData(File.ReadAllBytes(inputPath));

            int rc = Jamm.ModMaker.Program.Main([
                "bench", inputPath, outputPath,
                "--scene", "deepwater",
                "--warmup", "2",
                "--duration", "10",
                "--max-samples", "12000",
                "--out-file", "bench_dw.csv",
                "--no-exit",
                "--seed", "99",
                "--deep-x", "30000",
                "--deep-y", "8000",
                "--boat-slot", "5"
            ]);

            Assert.Equal(0, rc);
            Assert.True(File.Exists(outputPath));
            byte[] afterHash = SHA256.HashData(File.ReadAllBytes(inputPath));
            Assert.Equal(beforeHash, afterHash);

            using var readStream = File.OpenRead(outputPath);
            using var reloaded = UndertaleIO.Read(readStream);
            Assert.NotNull(reloaded.GameObjects.ByName(BenchmarkMod.ObjectName));
            string create = Code(reloaded, "Create_0");
            Assert.Contains("jamm_b_scene = \"deepwater\"", create);
            Assert.Contains("jamm_b_warmup_sec = 2", create);
            Assert.Contains("jamm_b_capture_sec = 10", create);
            Assert.Contains("jamm_b_max_samples = 12000", create);
            Assert.Contains("jamm_b_output_file = \"bench_dw.csv\"", create);
            Assert.Contains("jamm_b_auto_exit = 0", create);
            Assert.Contains("jamm_b_seed = 99", create);
            Assert.Contains("jamm_b_deep_x = 30000", create);
            Assert.Contains("jamm_b_deep_y = 8000", create);
            Assert.Contains("jamm_b_boat_slot = 5", create);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("sdata1n_beta.ini")]
    [InlineData("meta.ini")]
    [InlineData("../escape.csv")]
    [InlineData("sub/bench.csv")]
    [InlineData("bad.win")]
    [InlineData("")]
    public void UnsafeOutputFilenamesAreRejected(string unsafeName)
    {
        Assert.Throws<ArgumentException>(() => BenchmarkMod.ValidateOutputFileName(unsafeName));
    }

    [Fact]
    public void ModMakerBenchCliRejectsInvalidArgumentsAndExistingOutput()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "jamm-bench-neg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string inputPath = Path.Combine(tempDir, "input.win");
            string existingOut = Path.Combine(tempDir, "existing.win");
            using (var data = CreateSyntheticGame(includeOptionalObjects: false))
            using (var fs = File.Create(inputPath))
            {
                UndertaleIO.Write(fs, data);
            }
            File.WriteAllText(existingOut, "do not overwrite");

            Assert.Equal(1, Jamm.ModMaker.Program.Main(["bench", inputPath, inputPath]));
            Assert.Equal(1, Jamm.ModMaker.Program.Main(["bench", inputPath, existingOut]));
            Assert.Equal("do not overwrite", File.ReadAllText(existingOut));

            string freshOut = Path.Combine(tempDir, "fresh.win");
            Assert.Equal(1, Jamm.ModMaker.Program.Main(["bench", inputPath, freshOut, "--scene", "bogus"]));
            Assert.Equal(1, Jamm.ModMaker.Program.Main(["bench", inputPath, freshOut, "--duration", "0"]));
            Assert.Equal(1, Jamm.ModMaker.Program.Main(["bench", inputPath, freshOut, "--warmup", "-1"]));
            Assert.Equal(1, Jamm.ModMaker.Program.Main(["bench", inputPath, freshOut, "--out-file", "sdata1n_beta.ini"]));
            Assert.Equal(1, Jamm.ModMaker.Program.Main(["bench", inputPath, freshOut, "--unknown-flag"]));
            Assert.False(File.Exists(freshOut));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("full", BenchmarkFishMode.Full)]
    [InlineData("nostep", BenchmarkFishMode.NoStep)]
    [InlineData("NoDraw", BenchmarkFishMode.NoDraw)]
    [InlineData("DEACTIVATE", BenchmarkFishMode.Deactivate)]
    public void FishExperimentsCliOptionParsingHandlesModesCapAndScale(string modeText, BenchmarkFishMode expectedMode)
    {
        var opts = Jamm.ModMaker.Program.ParseBenchOptions([
            "--scene", "deepwater",
            "--fish-mode", modeText,
            "--fish-cap", "500",
            "--fish-scale", "2.5"
        ]);
        Assert.Equal(BenchmarkScene.Deepwater, opts.Scene);
        Assert.Equal(expectedMode, opts.FishMode);
        Assert.Equal(500, opts.FishCap);
        Assert.Equal(2.5, opts.FishScale);
    }

    [Theory]
    [InlineData("--fish-mode", "bogus")]
    [InlineData("--fish-cap", "-1")]
    [InlineData("--fish-cap", "abc")]
    [InlineData("--fish-scale", "0.5")]
    [InlineData("--fish-scale", "25")]
    [InlineData("--fish-scale", "nan")]
    public void FishExperimentsCliOptionParsingRejectsInvalidValues(string flag, string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Jamm.ModMaker.Program.ParseBenchOptions([flag, value]));
    }

    [Fact]
    public void FishExperimentsEmitIniOverridesCsvColumnsAndSummaryLines()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        BenchmarkMod.Apply(data, new BenchmarkOptions(
            Scene: BenchmarkScene.Deepwater,
            FishMode: BenchmarkFishMode.NoStep,
            FishCap: 1000,
            FishScale: 2));

        string create = Code(data, "Create_0");
        string beginStep = Code(data, "Step_1");
        string endStep = Code(data, "Step_2");
        string preDraw = Code(data, "Draw_76");
        string drawEnd = Code(data, "Draw_75");
        string capScript = ModObjects.Decompile(data, data.Scripts.Single(s => s.Name.Content == "jamm_b_apply_fish_cap").Code);

        Assert.Contains("jamm_b_fish_mode = \"nostep\"", create);
        Assert.Contains("jamm_b_fish_cap = 1000", create);
        Assert.Contains("jamm_b_fish_scale = 2", create);
        Assert.Contains("ini_read_string(\"Bench\", \"FishMode\"", create);
        Assert.Contains("ini_read_real(\"Bench\", \"FishCap\"", create);
        Assert.Contains("ini_read_real(\"Bench\", \"FishScale\"", create);
        Assert.Contains("global.jamm_b_no_thin = 1", create);

        Assert.Contains("global.jamm_b_fish_step_us = 0", beginStep);
        Assert.Contains("instance_copy(false)", beginStep);
        Assert.Contains("instance_deactivate_object(Fish)", endStep);
        Assert.Contains("jamm_b_apply_fish_cap", endStep);
        Assert.Contains("jamm_b_draw_start_t = get_timer()", preDraw);

        Assert.Contains("global.jamm_b_freeze_spawners = 1", drawEnd);
        Assert.Contains("jamm_b_stats_line(jamm_b_pres_list, \"present_sleep_us\")", drawEnd);
        Assert.Contains("jamm_b_stats_line(jamm_b_step_list, \"step_us\")", drawEnd);
        Assert.Contains("jamm_b_stats_line(jamm_b_draw_list, \"draw_us\")", drawEnd);
        Assert.Contains("jamm_b_stats_line(jamm_b_fish_step_list, \"fish_step_us\")", drawEnd);
        Assert.Contains("jamm_b_stats_line(jamm_b_fish_count_list, \"fish_count\")", drawEnd);
        Assert.Contains("frame_index,frame_us,loop_us,present_sleep_us,step_us,draw_us,fish_step_us,fish_count", drawEnd);

        Assert.Contains("Lure.fish", capScript);
        Assert.Contains("Lure.stolenBy", capScript);
        Assert.Contains("jamm_out_ord", capScript);
        Assert.Contains("jamm_dead", capScript);
        Assert.Contains("view_xview", capScript);
        Assert.Contains("instance_destroy()", capScript);
    }

    [Fact]
    public void CheapWinFlagsAreIndependentAndCompileTheirMatchedTransforms()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        var fish = data.GameObjects.ByName("Fish")!;
        var bubble = ModObjects.Create(data, "Bubble", persistent: false);
        var particleSystems = ModObjects.Create(data, "ParticleSystems", persistent: false);
        var effects = ModObjects.Create(data, "Effects", persistent: false);
        var weed = ModObjects.Create(data, "Weed", persistent: false);
        var mask = new UndertaleSprite
        {
            Name = data.Strings.MakeString("spr_fish_test"),
            SepMasks = UndertaleSprite.SepMaskType.Precise
        };
        data.Sprites.Add(mask);
        fish.Sprite = mask;
        bubble.Sprite = mask;

        var compiler = new CompileGroup(data);
        var fishOther = fish.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User13, data);
        compiler.QueueCodeReplace(fishOther, "fish_lunge();\nspeed = 0;");
        var lungeCode = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_lunge");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("fish_lunge"), Code = lungeCode });
        compiler.QueueCodeReplace(lungeCode, "if (lungeCooldown > 0)\n{\n    lungeCooldown -= (1 * Girl.delta_fixed);\n    return -4;\n}\ndistance_to_lure = point_distance(x, y, Lure.x, Lure.y);");
        compiler.QueueCodeReplace(data.Code.ByName("gml_Object_Fish_Step_2")!, """
            if (check_timer >= 6)
            {
                camera_distance = point_distance(x, y, Camera.x, Camera.y);
            }
            switch (state)
            {
                case 0: speed = 1; break;
            }
            if (point_distance(x, y, Camera.x, Camera.y) > 1000) speed = 0;
            lure_distance_squared = ((x - Lure.x) * (x - Lure.x)) + ((y - Lure.y) * (y - Lure.y));
            """);
        compiler.QueueCodeReplace(weed.EventHandlerFor(EventType.Step, 0u, data), """
            action_inherited();
            if (point_distance(x, y, Camera.x, Camera.y) > 5000) exit;
            image_angle = sin((current_time + jitter + x) / 1000) * 10;
            """);
        compiler.QueueCodeReplace(particleSystems.EventHandlerFor(EventType.Create, 0u, data),
            "background = part_system_create();\nwater_splash = part_type_create();");
        compiler.QueueCodeReplace(effects.EventHandlerFor(EventType.Step, 0u, data), "instance_create(x, y, Bubble);");
        compiler.QueueCodeReplace(data.GameObjects.ByName("SonarPanel")!.EventHandlerFor(EventType.Step, 1u, data), """
            with (Fish)
            {
                if (SonarPanelAdvanced.advancedDrawEnabled || other.cave_sonar) s1 = sprite_index;
            }
            """);
        ModObjects.Compile(compiler);

        var decisions = GmlCheapWins.Apply(data, [], masksRect: true, fishLod: true,
            decorAnimNative: true, cacheHot: true, particlesFx: true);

        Assert.Equal(UndertaleSprite.SepMaskType.AxisAlignedRect, mask.SepMasks);
        string fishStep = ModObjects.Decompile(data, data.Code.ByName("gml_Object_Fish_Step_2")!);
        Assert.Contains("jamm_lod_far", fishStep);
        Assert.Contains("jamm_lure_x", fishStep);
        Assert.True(fishStep.IndexOf("var jamm_camera_y", StringComparison.Ordinal)
            < fishStep.IndexOf("if (check_timer >= 6)", StringComparison.Ordinal));
        Assert.Contains("point_distance(x, y, jamm_camera_x, jamm_camera_y)", fishStep);
        Assert.Contains("lungeCooldown -= (1 * Girl.delta_fixed)",
            ModObjects.Decompile(data, data.Code.ByName("gml_Object_Fish_Other_23")!));
        Assert.Contains("25000000", ModObjects.Decompile(data, data.Code.ByName("gml_Object_Weed_Step_0")!));
        Assert.Contains("jamm_sonar_advanced", ModObjects.Decompile(data, data.Code.ByName("gml_Object_SonarPanel_Step_1")!));
        Assert.Contains(decisions, d => d.StartsWith("[CGF-CHEAP-PARTICLES] converted 1", StringComparison.Ordinal));
        Assert.Contains("jamm_cw_bubble(x, y)", ModObjects.Decompile(data, data.Code.ByName("gml_Object_Effects_Step_0")!));
        Assert.Equal(5, decisions.Count);
    }

    [Fact]
    public void ExperimentalCheapWinPerfSettingsDefaultOffExceptForBehaviorPreservingCache()
    {
        var settings = new PerfSettings();
        Assert.False(settings.MasksRect);
        Assert.False(settings.FishLod);
        Assert.False(settings.DecorAnimNative);
        Assert.True(settings.CacheHot);
        Assert.False(settings.ParticlesFx);
    }

    [Fact]
    public void FishCapGuardsNearestArraysAndStalePartnerReferencesAcrossFishObjects()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        var fish = data.GameObjects.ByName("Fish")!;
        var fishMedium = ModObjects.Create(data, "FishMedium", persistent: false);
        fishMedium.ParentId = fish;
        var felish = ModObjects.Create(data, "Felish", persistent: false);
        felish.ParentId = fish;
        var dataObj = ModObjects.Create(data, "dataobject", persistent: false);
        var bulbfish = ModObjects.Create(data, "Bulbfish", persistent: false);
        bulbfish.ParentId = fish;

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(bulbfish.EventHandlerFor(EventType.Other, 10u, data), """
            with (dataobject)
            {
                if (other.isVisible)
                {
                    nearest[0] = instance_nearest(other.x, other.y, FishMedium);
                    nearest[1] = instance_nearest(other.x, other.y, Felish);
                }
            }
            """);
        compiler.QueueCodeReplace(felish.EventHandlerFor(EventType.Step, 0u, data), """
            if (state != 10 && target > 0)
            {
                target.hasFelish = 0;
                target = -1;
            }
            """);
        var ivState = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jamm_iv_state");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("jamm_iv_state"), Code = ivState });
        compiler.QueueCodeReplace(ivState, "if (argument0 >= 0 && instance_exists(argument0)) return argument0.state; return 0;");
        var flock = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_flock");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("fish_flock"), Code = flock });
        compiler.QueueCodeReplace(flock, """
            if (jamm_iv_state(pal) != 0 && jamm_iv_state(pal) != 7)
                pal = -1;
            if (pal > 0)
                return 1;
            return 0;
            """);
        ModObjects.Compile(compiler);

        BenchmarkMod.Apply(data, new BenchmarkOptions(FishCap: 500));

        string bulbOut = ModObjects.Decompile(data, bulbfish.EventHandlerFor(EventType.Other, 10u, data));
        string felishOut = ModObjects.Decompile(data, felish.EventHandlerFor(EventType.Step, 0u, data));
        string flockOut = ModObjects.Decompile(data, flock);

        Assert.Contains("instance_exists(FishMedium)", bulbOut);
        Assert.Contains("instance_exists(Felish)", bulbOut);
        Assert.Contains("instance_exists(target)", felishOut);
        Assert.Contains("!instance_exists(pal)", flockOut);
    }

    [Fact]
    public void FishCapGuardsBishopEscortTargetFumerAndCaval()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        var fish = data.GameObjects.ByName("Fish")!;
        var fishMinor = ModObjects.Create(data, "FishMinorPiece", persistent: false);
        fishMinor.ParentId = fish;
        var bishop = ModObjects.Create(data, "Bishop", persistent: false);
        bishop.ParentId = fishMinor;
        var fumer = ModObjects.Create(data, "Fumer", persistent: false);
        fumer.ParentId = fishMinor;
        var caval = ModObjects.Create(data, "Caval", persistent: false);
        caval.ParentId = fishMinor;

        var compiler = new CompileGroup(data);
        foreach (string sname in new[] { "fish_king_hide", "fish_king_special", "fish_elast_towards_target", "fish_lunge", "fish_shove_schoolmates" })
        {
            var sc = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + sname);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(sname), Code = sc });
            compiler.QueueCodeReplace(sc, "return 0;");
        }
        compiler.QueueCodeReplace(bishop.EventHandlerFor(EventType.Other, 10u, data), """
            fish_king_hide();
            if (escort_target.state == 3)
            {
                target_x = Lure.x;
            }
            """);
        compiler.QueueCodeReplace(fumer.EventHandlerFor(EventType.Other, 18u, data), """
            fish_king_special();
            target = instance_nearest(x, y, FishMinorPiece);
            target_x = target.x;
            target_y = target.y;
            """);
        compiler.QueueCodeReplace(caval.EventHandlerFor(EventType.Other, 10u, data), """
            fish_king_hide();
            target_x = pather2.x;
            target_y = pather2.y;
            """);
        ModObjects.Compile(compiler);

        BenchmarkMod.Apply(data, new BenchmarkOptions(FishCap: 500));

        string bishopOut = ModObjects.Decompile(data, bishop.EventHandlerFor(EventType.Other, 10u, data));
        string fumerOut = ModObjects.Decompile(data, fumer.EventHandlerFor(EventType.Other, 18u, data));
        string cavalOut = ModObjects.Decompile(data, caval.EventHandlerFor(EventType.Other, 10u, data));

        Assert.Contains("variable_instance_exists(id, \"escort_target\")", bishopOut);
        Assert.Contains("instance_exists(escort_target)", bishopOut);
        Assert.Contains("instance_exists(pather2)", cavalOut);
        Assert.True(fumerOut.Contains("jamm_iv_x(target)", StringComparison.Ordinal) || fumerOut.Contains("instance_exists(target)", StringComparison.Ordinal));
    }

    [Fact]
    public void FishExperimentsInstrumentGenericHierarchyGuardsSpawnersAndExternalRefs()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        var fish = data.GameObjects.ByName("Fish")!;
        var fishSmall = ModObjects.Create(data, "FishSmall", persistent: false);
        fishSmall.ParentId = fish;
        var modFish = ModObjects.Create(data, "ModFish", persistent: false);
        modFish.ParentId = fishSmall;
        var modSpawner = ModObjects.Create(data, "ModSpawner", persistent: false);
        var fishThinner = ModObjects.Create(data, "FishThinner", persistent: false);
        var weatherCtrl = ModObjects.Create(data, "obj_jamm_weather", persistent: true);
        var npc = ModObjects.Create(data, "SomeNpc", persistent: false);

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(fish.EventHandlerFor(EventType.Draw, 0u, data), "draw_self();");
        compiler.QueueCodeReplace(modFish.EventHandlerFor(EventType.Step, 0u, data), "x += 1;");
        compiler.QueueCodeReplace(modFish.EventHandlerFor(EventType.Step, 2u, data), "action_inherited();");
        compiler.QueueCodeReplace(modFish.EventHandlerFor(EventType.Draw, 72u, data), "y += 0;");
        compiler.QueueCodeReplace(modFish.EventHandlerFor(EventType.Draw, 0u, data), "draw_self();");
        compiler.QueueCodeReplace(modSpawner.EventHandlerFor(EventType.Step, 0u, data), "instance_create(0, 0, ModFish);");
        compiler.QueueCodeReplace(modSpawner.EventHandlerFor(EventType.Other, 10u, data), "instance_create(0, 0, ModFish);");
        compiler.QueueCodeReplace(fishThinner.EventHandlerFor(EventType.Step, 0u, data), "cull_trigger += 1;");
        compiler.QueueCodeReplace(fishThinner.EventHandlerFor(EventType.Other, 10u, data), "n_culled = 0;");
        compiler.QueueCodeReplace(npc.EventHandlerFor(EventType.Step, 0u, data), "target_x = ModFish.x;");

        var respawn = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_respawn");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("fish_respawn"), Code = respawn });
        compiler.QueueCodeReplace(respawn, "state = 2; return 1;");

        var weatherPop = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jamm_weather_population");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("jamm_weather_population"), Code = weatherPop });
        compiler.QueueCodeReplace(weatherPop, "return 1;");
        ModObjects.Compile(compiler);

        BenchmarkMod.Apply(data, new BenchmarkOptions(FishMode: BenchmarkFishMode.NoStep, FishSleep: true));

        // Round-trip serialize/deserialize to prove bytecode + variable chains are valid.
        using var ms = new MemoryStream();
        UndertaleIO.Write(ms, data);
        ms.Position = 0;
        using var reloaded = UndertaleIO.Read(ms);

        string fishStep2 = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_Fish_Step_2")!);
        string modFishStep0 = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_ModFish_Step_0")!);
        string modFishStep2 = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_ModFish_Step_2")!);
        string fishDraw0 = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_Fish_Draw_0")!);
        string modFishDraw72 = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_ModFish_Draw_72")!);
        string modFishDraw0 = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_ModFish_Draw_0")!);
        string respawnOut = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Script_fish_respawn")!);
        string weatherPopOut = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Script_jamm_weather_population")!);
        string thinnerStepOut = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_FishThinner_Step_0")!);
        string thinnerUser0Out = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_FishThinner_Other_10")!);
        string spawnerStepOut = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_ModSpawner_Step_0")!);
        string spawnerUser0Out = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_ModSpawner_Other_10")!);
        string npcStepOut = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_SomeNpc_Step_0")!);

        Assert.Contains("if (global.jamm_b_fish_nostep)", fishStep2);
        int sleepGuard = fishStep2.IndexOf("global.jamm_sleep_tick < jamm_sleep_next", StringComparison.Ordinal);
        int enterHook = fishStep2.IndexOf("jamm_b_fish_step_enter()", StringComparison.Ordinal);
        Assert.True(sleepGuard >= 0 && sleepGuard < enterHook, fishStep2);
        Assert.Contains("jamm_b_fish_step_leave()", fishStep2);
        Assert.Contains("if (global.jamm_b_fish_nostep)", modFishStep0);
        Assert.Contains("if (global.jamm_b_fish_nostep)", modFishStep2);

        Assert.Contains("if (global.jamm_b_fish_nodraw)", fishDraw0);
        Assert.Contains("if (global.jamm_b_fish_nodraw)", modFishDraw72);
        Assert.Contains("if (global.jamm_b_fish_nodraw)", modFishDraw0);

        Assert.Contains("if (global.jamm_b_freeze_spawners)", respawnOut);
        Assert.Contains("return -4;", respawnOut);
        Assert.Contains("if (global.jamm_b_freeze_spawners)", weatherPopOut);
        Assert.Contains("if (global.jamm_b_freeze_spawners)", thinnerStepOut);
        Assert.Contains("if (global.jamm_b_freeze_spawners || global.jamm_b_no_thin)", thinnerUser0Out);
        Assert.Contains("if (global.jamm_b_freeze_spawners)", spawnerStepOut);
        Assert.Contains("if (global.jamm_b_freeze_spawners)", spawnerUser0Out);

        Assert.Contains("jamm_iv_x(", npcStepOut);
    }

    [RealFilesFact]
    public void FishExperimentsCompileOnRealLegacyAndModdedArchives()
    {
        string vanillaPath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string modsDir = Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!;
        string tempDir = Path.Combine(Path.GetTempPath(), "jamm-bench-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string vanillaOut = Path.Combine(tempDir, "vanilla-bench.win");
            Assert.Equal(0, Jamm.ModMaker.Program.Main([
                "bench", vanillaPath, vanillaOut,
                "--scene", "dock", "--fish-mode", "nostep", "--fish-cap", "500", "--fish-scale", "2"
            ]));
            Assert.True(File.Exists(vanillaOut));

            string modPath = Directory.GetFiles(modsDir, "*.win").OrderBy(p => p, StringComparer.Ordinal).First();
            string modOut = Path.Combine(tempDir, "mod-bench.win");
            Assert.Equal(0, Jamm.ModMaker.Program.Main([
                "bench", modPath, modOut,
                "--scene", "deepwater", "--fish-mode", "deactivate", "--fish-cap", "200"
            ]));
            Assert.True(File.Exists(modOut));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string Code(UndertaleData data, string suffix)
        => ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{BenchmarkMod.ObjectName}_{suffix}")!);

    private static UndertaleData CreateSyntheticGame(bool includeOptionalObjects)
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;

        data.Rooms.Add(new UndertaleRoom
        {
            Name = data.Strings.MakeString("NewWorld"),
            Width = 100000,
            Height = 13000,
            Speed = 30
        });

        if (!includeOptionalObjects)
            return data;

        data.Fonts.Add(new UndertaleFont
        {
            Name = data.Strings.MakeString("fnt_babySmall"),
            DisplayName = data.Strings.MakeString("Small")
        });
        data.Rooms.Add(new UndertaleRoom
        {
            Name = data.Strings.MakeString("room11"),
            Width = 1200,
            Height = 720,
            Speed = 30
        });

        foreach (string name in new[]
        {
            "SaveManager", "GameModeMenu", "GM_Classic", "GM_Realism",
            "Girl", "Rod", "Lure", "Boat", "BoatData",
            "ToolData", "SonarPanel", "SonarPanelAdvanced", "RadarButton",
            "Camera", "AmbianceController", "QuestEngine",
            "Fish", "SceneItem", "Magmer", "Volteen"
        })
        {
            ModObjects.Create(data, name, persistent: false);
        }

        // Provide minimal entries required by SmoothMod/PerfMod when testing composition.
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(data.GameObjects.ByName("QuestEngine")!.EventHandlerFor(EventType.Step, 1u, data), "quest_counter = 0;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Girl")!.EventHandlerFor(EventType.Step, 0u, data), "a = delta_time; b = delta_time; c = delta_time;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Fish")!.EventHandlerFor(EventType.Step, 2u, data), "if (y > 5000 && !deepfish)\n{\n    var r = 1;\n}");
        compiler.QueueCodeReplace(data.GameObjects.ByName("SceneItem")!.EventHandlerFor(EventType.Step, 2u, data), "scene_depth = depth + 18;\nx = home_x;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Magmer")!.EventHandlerFor(EventType.Draw, 0u, data), "light.image_alpha = 1 - power(1 - heat_level, 2);");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Volteen")!.EventHandlerFor(EventType.Other, 13u, data), "light.add_blend = 4210752;\nlight.sub_blend = 4210752;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Volteen")!.EventHandlerFor(EventType.Other, 10u, data), "if (charge > 0)\n{\n    light.add_blend = 0;\n    light.sub_blend = 0;\n}\nelse\n{\n    light.add_blend = 4210752;\n    light.sub_blend = 4210752;\n}");

        var avoid = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_avoid_fish");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("fish_avoid_fish"), Code = avoid });
        compiler.QueueCodeReplace(avoid, "if (d < (argument1 * argument1))\n{\n    h += (100000 * argument2) / d;\n    v += (100000 * argument2) / d;\n}");

        var trawler = UndertaleCode.CreateEmptyEntry(data, "gml_Script_fish_check_trawler");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("fish_check_trawler"), Code = trawler });
        compiler.QueueCodeReplace(trawler, "if (collision_line(Net.top.x, Net.top.y, Net.bot.x, Net.bot.y, id, true, false))\n{\n    state = 3;\n}");

        ModObjects.Compile(compiler);
        return data;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoplaySceneCompilesWithAndWithoutOptionalModObjects(bool includeOptionalObjects)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "jamm-autoplay-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string inputPath = Path.Combine(tempDir, "input.win");
            string outputPath = Path.Combine(tempDir, "autoplay-out.win");
            using (var data = CreateSyntheticGame(includeOptionalObjects))
            {
                if (!includeOptionalObjects)
                    ModObjects.Create(data, "Girl", persistent: false);
                if (includeOptionalObjects)
                {
                    foreach (string extra in new[]
                    {
                        "Boat7", "BoatMenu", "BoatCard", "Handler", "icon_Boats", "icon_Cancel",
                        "obj_jmk_endless_ocean", "obj_jamm_weather", "jmk_submarine_submarine",
                        "jmk_jamm_silo_object_hatch", "obj_jamm_console"
                    })
                    {
                        ModObjects.Create(data, extra, persistent: false);
                    }
                }
                using var fs = File.Create(inputPath);
                UndertaleIO.Write(fs, data);
            }

            byte[] beforeHash = SHA256.HashData(File.ReadAllBytes(inputPath));
            int rc = Jamm.ModMaker.Program.Main(["bench", inputPath, outputPath, "--scene", "autoplay"]);
            Assert.Equal(0, rc);
            Assert.True(File.Exists(outputPath));
            Assert.Equal(beforeHash, SHA256.HashData(File.ReadAllBytes(inputPath)));

            using var readStream = File.OpenRead(outputPath);
            using var reloaded = UndertaleIO.Read(readStream);
            var probe = reloaded.GameObjects.ByName("obj_jamm_autoplay");
            Assert.NotNull(probe);
            Assert.True(probe.Persistent);

            string create = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_obj_jamm_autoplay_Create_0")!);
            string endStep = ModObjects.Decompile(reloaded, reloaded.Code.ByName("gml_Object_obj_jamm_autoplay_Step_2")!);
            Assert.Contains("ms,room,fps,x,y,depth,dollars,level,lure_state,lure_y,instances,boat,chunks,weather,has_sub", create);
            Assert.Contains("ocean_start", create);
            Assert.Contains("ocean_start", endStep);
            Assert.Contains("asset_get_index", endStep);
            Assert.Contains("object_exists", endStep);
            Assert.Contains("instance_exists", endStep);
            Assert.Contains("variable_instance_exists", endStep);
            Assert.Contains("jmk_submarine_submarine", endStep);
            Assert.Contains("obj_jmk_endless_ocean", endStep);
            Assert.Contains("obj_jamm_weather", endStep);
            Assert.Contains("jmk_jamm_silo_object_hatch", endStep);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

}
