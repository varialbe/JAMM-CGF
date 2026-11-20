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

        // Minimal intrusion: only the 7 events of obj_jamm_bench are added; existing code is untouched.
        Assert.Equal(originalCodeCount + 7, data.Code.Count);

        string create = Code(data, "Create_0");
        string destroy = Code(data, "Destroy_0");
        string roomStart = Code(data, "Other_4");
        string beginStep = Code(data, "Step_1");
        string endStep = Code(data, "Step_2");
        string drawGui = Code(data, "Draw_64");
        string drawGuiEnd = Code(data, "Draw_75");

        Assert.Contains($"jamm_b_scene = \"{expectedScene}\"", create);
        Assert.Contains("jamm_bench.ini", create);
        Assert.Contains("jamm_b_seed = 4242", create);
        Assert.Contains("jamm_b_frame_list = ds_list_create()", create);
        Assert.Contains("jamm_b_loop_list = ds_list_create()", create);
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

        // Active capture suppresses DrawGUI HUD drawing.
        Assert.Contains("jamm_b_state == 2", drawGui);

        // DrawGUIEnd measures frame-to-frame get_timer, batches into ds_list, and flushes once on bound.
        Assert.Contains("get_timer()", drawGuiEnd);
        Assert.Contains("ds_list_add(jamm_b_frame_list", drawGuiEnd);
        Assert.Contains("ds_list_add(jamm_b_loop_list", drawGuiEnd);
        Assert.Contains("ds_list_sort", drawGuiEnd);
        Assert.Contains("file_text_open_write(jamm_b_output_file)", drawGuiEnd);
        Assert.Contains("frame_index,frame_us,loop_us,present_sleep_us", drawGuiEnd);
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
        HdMod.Apply(data, upscaledPages: null, debug: false);

        int countBeforeBench = data.Code.Count;
        BenchmarkMod.Apply(data, new BenchmarkOptions(Scene: BenchmarkScene.Sonar));

        Assert.Equal(countBeforeBench + 7, data.Code.Count);
        Assert.Equal(-999999, data.GameObjects.ByName(BenchmarkMod.ObjectName)!.Depth);
        Assert.Equal(-1000000, data.GameObjects.ByName(SmoothMod.ObjectName)!.Depth);

        // Double instrumentation is cleanly rejected.
        Assert.Throws<InvalidDataException>(() => BenchmarkMod.Apply(data));
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
        Assert.Contains("__jamm_prof_original_", eventStub);
        Assert.Contains("ds_map_set(global.__jamm_prof_us", eventStub);
        Assert.Contains("get_timer() - __jamm_prof_start", eventStub);

        var wrappedScript = data.Scripts.Single(s => s.Name.Content == "profile_test").Code;
        string scriptStub = ModObjects.Decompile(data, wrappedScript);
        Assert.Contains("switch (argument_count)", scriptStub);
        Assert.Contains("return __jamm_prof_result", scriptStub);
        Assert.Contains("ds_map_set(global.__jamm_prof_n", scriptStub);

        string benchCreate = Code(data, "Create_0");
        string drawEnd = Code(data, "Draw_75");
        Assert.Contains("jamm_b_profile = 1", benchCreate);
        Assert.Contains("global.__jamm_prof_on = 1", drawEnd);
        Assert.Contains("global.__jamm_prof_on = 0", drawEnd);
        Assert.Contains("jamm_profile_", drawEnd);
        Assert.Contains("code_entry,calls,total_us,us_per_tick,share_of_loop", drawEnd);
        Assert.Contains("get_timer_start_and_end_per_invocation", drawEnd);
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
    }

    [Fact]
    public void UnprofiledBenchLeavesExistingCodeUnchanged()
    {
        using var data = CreateSyntheticGame(includeOptionalObjects: true);
        var fishStep = data.GameObjects.ByName("Fish")!.EventHandlerFor(EventType.Step, 2u, data);
        string before = ModObjects.Decompile(data, fishStep);

        BenchmarkMod.Apply(data, new BenchmarkOptions(Profile: false));

        Assert.Equal(before, ModObjects.Decompile(data, fishStep));
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
}
