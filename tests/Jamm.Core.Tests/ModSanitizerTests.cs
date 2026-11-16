using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class ModSanitizerTests
{
    [Fact]
    public void ClassifyEntryClassifiesAllSixCategoriesAndUnclassifiedDeterministically()
    {
        // 1. timing
        var timing = ModSanitizer.ClassifyEntry(
            "gml_Object_Girl_Step_0",
            "var ts = 0.00003;\ndelta = delta_time;\ndelta_fixed = clamp(delta_time / 16000, 1, 50);\n",
            "var ts = 0.00003;\ndelta = room_speed / 30;\ndelta_fixed = room_speed / 30;\n");
        Assert.All(timing, h => Assert.Equal(ModSanitizer.Timing, h.Category));

        // 2. tick-throttle
        var throttle = ModSanitizer.ClassifyEntry(
            "gml_Object_Fish_Step_2",
            "if (speed > maxSpeed)\n{\n    speed = maxSpeed;\n}\n",
            "check_timer++;\nif (check_timer >= 6)\n{\n    check_timer = 0;\n    if (speed > maxSpeed)\n    {\n        speed = maxSpeed;\n    }\n}\n");
        var throttleHunk = Assert.Single(throttle);
        Assert.Equal(ModSanitizer.TickThrottle, throttleHunk.Category);
        Assert.Equal("if (speed > maxSpeed)\n{\n    speed = maxSpeed;\n}\n", throttleHunk.RevertedText);

        // 3. distance-cull
        var distCull = ModSanitizer.ClassifyEntry(
            "gml_Object_Weed_Step_0",
            "action_inherited();\nimage_angle = sin((current_time + jitter + x) / 1000) * 10;\n",
            "action_inherited();\nif (point_distance(x, y, Camera.x, Camera.y) > 5000)\n{\n    visible = false;\n    exit;\n}\nelse\n{\n    image_angle = sin((current_time + jitter + x) / 1000) * 10;\n}\n");
        var distHunk = Assert.Single(distCull);
        Assert.Equal(ModSanitizer.DistanceCull, distHunk.Category);
        Assert.Equal("image_angle = sin((current_time + jitter + x) / 1000) * 10;\n", distHunk.RevertedText);

        // 4. fps-adaptive
        var fpsAdaptive = ModSanitizer.ClassifyEntry(
            "gml_Object_FishThinner_Step_0",
            "if (fps < 30)\n{\n    cull_trigger += 5;\n}\nelse if (fps < 60)\n{\n    cull_trigger += 0.5;\n}\n",
            "if (fps < 60)\n{\n    cull_trigger += 65;\n}\n");
        var fpsHunk = Assert.Single(fpsAdaptive);
        Assert.Equal(ModSanitizer.FpsAdaptive, fpsHunk.Category);

        // 5. debug-overlay
        var debugOverlay = ModSanitizer.ClassifyEntry(
            "gml_Object_ExperienceBar_Draw_0",
            "draw_self();\n",
            "draw_self();\ndraw_set_color(Colors.ui_light);\ndraw_text_shadowed(draw_x + 430, draw_y - 60, \"FPS:\" + string(fps));\ndraw_text_shadowed(draw_x + 430, draw_y - 40, \"X:\" + string(Camera.x));\n");
        var debugHunk = Assert.Single(debugOverlay);
        Assert.Equal(ModSanitizer.DebugOverlay, debugHunk.Category);
        Assert.Equal("", debugHunk.RevertedText);

        // 6. content
        var content = ModSanitizer.ClassifyEntry(
            "gml_Object_Boat7_Create_0",
            "MAX_X = 65000;\n",
            "MAX_X = 82430;\n");
        Assert.Equal(ModSanitizer.Content, Assert.Single(content).Category);

        // 7. unclassified (ambiguous mix of fps read and gameplay reward — must not guess)
        var unclassified = ModSanitizer.ClassifyEntry(
            "gml_Object_Girl_Step_0",
            "dollars = dollars;\n",
            "if (fps < 30)\n{\n    dollars += 500;\n}\n");
        var uncHunk = Assert.Single(unclassified);
        Assert.Equal(ModSanitizer.Unclassified, uncHunk.Category);
    }

    [Fact]
    public void FishStep2PreservesContentLinesWhileRevertingPerformanceHunks()
    {
        const string baseGml = """
            speed = spd;
            if (speed > maxSpeed)
            {
                speed = maxSpeed;
            }
            frames_since_last_flip++;
            """;
        const string modGml = """
            speed = spd;
            if (object_index == Elder_Chaw)
            {
                spd = 12;
            }
            check_timer++;
            if (check_timer >= 6)
            {
                check_timer = 0;
                if (speed > maxSpeed)
                {
                    speed = maxSpeed;
                }
            }
            frames_since_last_flip++;
            """;

        var hunks = ModSanitizer.ClassifyEntry("gml_Object_Fish_Step_2", baseGml, modGml);
        Assert.Equal(2, hunks.Count);
        Assert.Equal(ModSanitizer.Content, hunks[0].Category);
        Assert.Equal(ModSanitizer.TickThrottle, hunks[1].Category);

        // Also test content nested inside a tick-throttle gate: gate is unwrapped and content survives.
        const string wrappedContentGml = """
            speed = spd;
            check_timer++;
            if (check_timer >= 6)
            {
                check_timer = 0;
                if (speed > maxSpeed)
                {
                    speed = maxSpeed;
                }
                if (object_index == Elder_Chaw)
                {
                    spd = 12;
                }
            }
            frames_since_last_flip++;
            """;
        var wrappedHunks = ModSanitizer.ClassifyEntry("gml_Object_Fish_Step_2", baseGml, wrappedContentGml);
        var single = Assert.Single(wrappedHunks);
        Assert.Equal(ModSanitizer.TickThrottle, single.Category);
        Assert.Contains("if (object_index == Elder_Chaw)", single.RevertedText);
        Assert.DoesNotContain("check_timer", single.RevertedText);
    }

    [Fact]
    public void SanitizerRemovesUnreferencedHelpersAndNeverTouchesReferencedContentOrJammMods()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string jammPath = temp.PathFor("jamm.win");

        WriteSyntheticGame(basePath, data =>
        {
            AddCode(data, "gml_Object_Girl_Create_0", "time_scalar = 0.00003;\ntime_scalar_last = 0.00003;\n");
            AddCode(data, "gml_Object_Fish_Create_0", "hollow = 0;\n");
            AddCode(data, "gml_Object_Fish_Step_2", "if (speed > maxSpeed)\n{\n    speed = maxSpeed;\n}\n");
        });

        WriteSyntheticGame(modPath, data =>
        {
            ReplaceCode(data, "gml_Object_Girl_Create_0", "time_scalar = 0.00006;\ntime_scalar_last = 0.00006;\n");
            ReplaceCode(data, "gml_Object_Fish_Create_0", "check_timer = 0;\nhollow = 0;\n");
            ReplaceCode(data, "gml_Object_Fish_Step_2", "check_timer++;\nif (check_timer >= 6)\n{\n    check_timer = 0;\n    if (speed > maxSpeed)\n    {\n        speed = maxSpeed;\n    }\n}\nif (object_index == Elder_Chaw)\n{\n    speed = 5;\n}\n");

            var fpsObj = new UndertaleGameObject { Name = data.Strings.MakeString("fpsController"), Visible = false };
            data.GameObjects.Add(fpsObj);
            var fpsCode = fpsObj.EventHandlerFor(EventType.Create, 0u, data);
            ReplaceCode(data, fpsCode.Name.Content, "target_fps = 60;\ntarget_frame_time = 1000000 / target_fps;\n");

            var chawObj = new UndertaleGameObject { Name = data.Strings.MakeString("Elder_Chaw"), Visible = true };
            data.GameObjects.Add(chawObj);
            var chawCode = chawObj.EventHandlerFor(EventType.Create, 0u, data);
            ReplaceCode(data, chawCode.Name.Content, "BASE_VALUE = 500000;\n");

            data.Rooms[0].Speed = 1;
            data.Rooms[0].GameObjects.Add(new UndertaleRoom.GameObject
            {
                InstanceID = ++data.GeneralInfo.LastObj,
                ObjectDefinition = fpsObj,
                X = 0,
                Y = 240
            });
        });

        WriteSyntheticGame(jammPath, data =>
        {
            ReplaceCode(data, "gml_Object_Girl_Create_0", "time_scalar = 0.00006;\ntime_scalar_last = 0.00003;\n");
            var jammObj = new UndertaleGameObject { Name = data.Strings.MakeString("obj_jamm_smooth"), Persistent = true };
            data.GameObjects.Add(jammObj);
        });

        using var origLoaded = GameFileLoader.Load(basePath);
        using var modLoaded = GameFileLoader.Load(modPath);
        using var jammLoaded = GameFileLoader.Load(jammPath);

        var rawPatch = PatchBuilder.Create(origLoaded, modLoaded, "TestMod");
        var sanitized = ModSanitizer.Sanitize(rawPatch);

        Assert.False(sanitized.Report.IsJammMod);
        Assert.NotEmpty(sanitized.Report.Removed);
        Assert.Contains(sanitized.Report.Removed, r => r.Category == ModSanitizer.Timing && r.Entry == "gml_Object_Girl_Create_0");
        Assert.Contains(sanitized.Report.Removed, r => r.Category == ModSanitizer.TickThrottle && r.Entry == "gml_Object_Fish_Create_0");
        Assert.Contains(sanitized.Report.Removed, r => r.Category == ModSanitizer.TickThrottle && r.Entry == "gml_Object_Fish_Step_2");
        Assert.Contains(sanitized.Report.Removed, r => r.Category == ModSanitizer.Timing && r.Entry == "objects/fpsController");
        Assert.Contains(sanitized.Report.Removed, r => r.Category == ModSanitizer.Timing && r.Entry.StartsWith("rooms/", StringComparison.Ordinal));

        // Girl_Create_0 and Fish_Create_0 were completely reverted, so they are dropped from code.Changed.
        Assert.DoesNotContain(sanitized.Patch.Changes["code"].Changed, c => c.Name == "gml_Object_Girl_Create_0");
        Assert.DoesNotContain(sanitized.Patch.Changes["code"].Changed, c => c.Name == "gml_Object_Fish_Create_0");

        // Fish_Step_2 retains the Elder_Chaw content hunk and drops check_timer.
        var fishStep = Assert.Single(sanitized.Patch.Changes["code"].Changed, c => c.Name == "gml_Object_Fish_Step_2");
        Assert.Contains("Elder_Chaw", fishStep.ModCode!.Text);
        Assert.DoesNotContain("check_timer", fishStep.ModCode!.Text);

        // fpsController is removed, Elder_Chaw is kept.
        Assert.DoesNotContain(sanitized.Patch.Changes["objects"].Added, o => o.Name == "fpsController");
        Assert.Contains(sanitized.Patch.Changes["objects"].Added, o => o.Name == "Elder_Chaw");

        // JAMM own mod (obj_jamm_smooth) is never sanitized.
        var jammPatch = PatchBuilder.Create(origLoaded, jammLoaded, "Smooth");
        var jammSanitized = ModSanitizer.Sanitize(jammPatch);
        Assert.True(jammSanitized.Report.IsJammMod);
        Assert.Empty(jammSanitized.Report.Removed);
        Assert.Same(jammPatch, jammSanitized.Patch);
    }

    [Fact]
    public void LibraryStoresSanitizedPatchByDefaultAndHonorsKeepAuthorSwitch()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");

        WriteSyntheticGame(basePath, data =>
            AddCode(data, "gml_Object_Girl_Create_0", "time_scalar = 0.00003;\n"));
        WriteSyntheticGame(modPath, data =>
            ReplaceCode(data, "gml_Object_Girl_Create_0", "time_scalar = 0.00006;\n"));

        var store = new ManagerStore(temp.PathFor("store"));
        store.Set("base", basePath);
        var library = new ModLibrary(store);
        var added = library.Add(modPath, "OACSynthetic");

        Assert.True(File.Exists(library.PatchPath(added.Mod)));
        Assert.True(File.Exists(library.SanitizedPatchPath(added.Mod)));
        Assert.True(File.Exists(library.SanitizerReportPath(added.Mod)));

        var report = library.SanitizerReport(added.Mod);
        Assert.Single(report.Removed);
        Assert.Equal(ModSanitizer.Timing, report.Removed[0].Category);

        // By default, library.Patch returns the sanitized patch (0 changed code entries).
        Assert.Empty(library.Patch(added.Mod).Changes["code"].Changed);

        // Toggling KeepAuthorPerformanceChanges returns the unsanitized patch.
        var updated = library.SetKeepAuthorPerformanceChanges(added.Mod.Id, true);
        Assert.True(updated.KeepAuthorPerformanceChanges);
        Assert.Single(library.Patch(updated).Changes["code"].Changed);

        library.SetKeepAuthorPerformanceChanges(added.Mod.Id, false);
        Assert.Empty(library.Patch(added.Mod).Changes["code"].Changed);
    }

    [RealFilesFact]
    public void RealSevenModSetSanitizesBuildsAndVerifiesWithContentIntactAndHacksRemoved()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        using var temp = new TemporaryDirectory();
        var store = new ManagerStore(temp.PathFor("manager"));
        store.Set("base", basePath);
        string modsDirectory = Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!;
        var library = new ModLibrary(store);

        (string Name, string File)[] fixtures =
        [
            ("Abyss", "the-abyss-beta.win"), ("Cave", "cave-expansion.win"),
            ("Colour", "colour-expansion.win"), ("HDLite", "jamm-hd-lite.win"),
            ("Perf", "jamm-perf.win"), ("Smooth", "jamm-smooth-fps.win"),
            ("Oops! All Chaws!", "oops-all-chaws.win")
        ];
        foreach (var (_, file) in fixtures)
            Assert.True(File.Exists(Path.Combine(modsDirectory, file)),
                $"Missing real-file fixture '{file}' in JAMM_MODS_DIR. See docs/real-file-tests.md.");
        List<BuildInput> inputs = [];
        Dictionary<string, ModSanitizerReport> reports = new(StringComparer.Ordinal);
        foreach (var (name, file) in fixtures)
        {
            var mod = library.Add(Path.Combine(modsDirectory, file), name).Mod;
            var report = library.SanitizerReport(mod);
            reports[name] = report;
            var patch = library.Patch(mod);
            inputs.Add(new(patch, Path.Combine(store.Root, "mods", mod.Id, "data.win")));
        }

        // Verify JAMM own mods are marked IsJammMod and have 0 removed changes.
        Assert.True(reports["HDLite"].IsJammMod);
        Assert.Empty(reports["HDLite"].Removed);
        Assert.True(reports["Perf"].IsJammMod);
        Assert.Empty(reports["Perf"].Removed);
        Assert.True(reports["Smooth"].IsJammMod);
        Assert.Empty(reports["Smooth"].Removed);

        // Verify Abyss and Cave have 0 removed changes; Colour has 1 (FishThinner do_zoning).
        Assert.Empty(reports["Abyss"].Removed);
        Assert.Empty(reports["Cave"].Removed);
        Assert.Single(reports["Colour"].Removed);
        Assert.Equal(ModSanitizer.DistanceCull, reports["Colour"].Removed[0].Category);
        Assert.Equal("gml_Object_FishThinner_Step_0", reports["Colour"].Removed[0].Entry);

        // Verify OAC has all timing, tick-throttle, distance-cull, fps-adaptive, debug-overlay hacks removed and 0 unclassified.
        var oacReport = reports["Oops! All Chaws!"];
        Assert.False(oacReport.IsJammMod);
        Assert.Empty(oacReport.Unclassified);
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.Timing && r.Entry == "gml_Object_Girl_Create_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.Timing && r.Entry == "gml_Object_Girl_Step_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.Timing && r.Entry == "objects/fpsController");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.Timing && r.Entry == "rooms/NewWorld");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.TickThrottle && r.Entry == "gml_Object_Fish_Create_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.TickThrottle && r.Entry == "gml_Object_Fish_Step_2");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.TickThrottle && r.Entry == "gml_Object_CaveRock_Create_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.DistanceCull && r.Entry == "gml_Object_Fish_Draw_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.DistanceCull && r.Entry == "gml_Script_fish_basicdraw");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.DistanceCull && r.Entry == "gml_Script_fish_predraw_switchcase");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.DistanceCull && r.Entry == "gml_Object_SonarPanel_Draw_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.DistanceCull && r.Entry == "gml_Object_Weed_Step_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.DistanceCull && r.Entry == "gml_Object_Dangler_Step_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.FpsAdaptive && r.Entry == "gml_Object_FishThinner_Step_0");
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.DebugOverlay && r.Entry == "gml_Object_ExperienceBar_Draw_0");

        string output = temp.PathFor("merged-seven.win");
        var options = new Jamm.Core.AnalysisOptions { DataDirectory = store.Root };
        var build = ArchiveBuilder.Build(basePath, inputs, output, options: options);
        Assert.NotNull(build.Verification);

        var verified = ArchiveVerifier.Verify(output, options: options);
        Assert.True(verified.DecompiledEntries > 0);
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("verify", [output]));

        using var merged = GameFileLoader.Load(output);
        CgfAssetTypes.Register(merged.Data);

        // Verify hacks are gone from the compiled merged archive:
        Assert.Null(merged.Data.GameObjects.ByName("fpsController"));
        Assert.Equal(30u, merged.Data.Rooms.ByName("NewWorld")!.Speed);
        Assert.DoesNotContain(merged.Data.Rooms.ByName("NewWorld")!.GameObjects, i => i.ObjectDefinition?.Name.Content == "fpsController");

        string girlCreate = PatchBuilder.ReadCode(merged, "gml_Object_Girl_Create_0").Text;
        Assert.Contains("time_scalar_last = 0.00003;", girlCreate);
        Assert.Contains("time_scalar = 0.00003;\nhspeed_base = 0;", girlCreate);

        string girlStep = PatchBuilder.ReadCode(merged, "gml_Object_Girl_Step_0").Text;
        Assert.DoesNotContain("room_speed / 30", girlStep);
        Assert.Contains("global.jamm_tick_delta", girlStep);

        string fishStep2 = PatchBuilder.ReadCode(merged, "gml_Object_Fish_Step_2").Text;
        Assert.DoesNotContain("check_timer", fishStep2);
        Assert.DoesNotContain("Camera.x", fishStep2);
        Assert.Contains("global.jamm_perf_mode", fishStep2);

        string fishDraw = PatchBuilder.ReadCode(merged, "gml_Object_Fish_Draw_0").Text;
        Assert.DoesNotContain("point_distance", fishDraw);

        string basicDraw = PatchBuilder.ReadCode(merged, "gml_Script_fish_basicdraw").Text;
        Assert.DoesNotContain("point_distance", basicDraw);

        string predraw = PatchBuilder.ReadCode(merged, "gml_Script_fish_predraw_switchcase").Text;
        Assert.DoesNotContain("point_distance", predraw);

        string sonarDraw = PatchBuilder.ReadCode(merged, "gml_Object_SonarPanel_Draw_0").Text;
        Assert.DoesNotContain("point_distance", sonarDraw);

        string weedStep = PatchBuilder.ReadCode(merged, "gml_Object_Weed_Step_0").Text;
        Assert.DoesNotContain("point_distance", weedStep);

        string danglerStep = PatchBuilder.ReadCode(merged, "gml_Object_Dangler_Step_0").Text;
        Assert.DoesNotContain("point_distance", danglerStep);

        string thinnerStep = PatchBuilder.ReadCode(merged, "gml_Object_FishThinner_Step_0").Text;
        Assert.Contains("do_zoning = 0;", thinnerStep);
        Assert.Contains("if (fps < 30)", thinnerStep);
        Assert.DoesNotContain("cull_trigger += 65;", thinnerStep);

        string expDraw = PatchBuilder.ReadCode(merged, "gml_Object_ExperienceBar_Draw_0").Text;
        Assert.DoesNotContain("FPS:", expDraw);
        Assert.DoesNotContain("Camera.x", expDraw);

        // Verify OAC content is kept:
        foreach (string contentObj in new[] { "Elder_Chaw", "Bassoli", "Goldi", "Kuito", "Luce", "Minau", "Mulite", "Pieraruchu", "Obliterate", "Root1", "Root2", "StartRoot", "EndRoot", "tree" })
            Assert.NotNull(merged.Data.GameObjects.ByName(contentObj));

        string catalogCode = PatchBuilder.ReadCode(merged, "gml_Object_CatalogData_Create_0").Text;
        foreach (string fishName in new[] { "Bassoli", "Goldi", "Kuito", "Luce", "Minau", "Mulite", "Pieraruchu" })
            Assert.Contains($"\"{fishName}\"", catalogCode);

        string upgradeData = PatchBuilder.ReadCode(merged, "gml_Object_UpgradeData_Create_0").Text;
        Assert.Contains("\"Bulbguard\"", upgradeData);

        string bulbeater = PatchBuilder.ReadCode(merged, "gml_Object_Bulbeater_Other_10").Text;
        Assert.Contains("sfx_bulbguard_dink", bulbeater);

        string boat7 = PatchBuilder.ReadCode(merged, "gml_Object_Boat7_Create_0").Text;
        Assert.Contains("MAX_X = 82430;", boat7);
    }

    private static void WriteSyntheticGame(string path, Action<UndertaleData> configure)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Timestamp = 1692207312;
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Minor = 0;
        data.GeneralInfo.Release = 0;
        data.GeneralInfo.Build = 1773;
        var room = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld"), Speed = 30, Width = 100000, Height = 13000 };
        data.Rooms.Add(room);
        data.GeneralInfo.RoomOrder.Add(new(room, 0));
        AddCode(data, "test_code", "x = 0;\n");
        configure(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static void AddCode(UndertaleData data, string name, string gml)
    {
        var code = UndertaleCode.CreateEmptyEntry(data, name);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(code, gml);
        Assert.True(compiler.Compile().Successful);
    }

    private static void ReplaceCode(UndertaleData data, string name, string gml)
    {
        var code = data.Code.ByName(name) ?? UndertaleCode.CreateEmptyEntry(data, name);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(code, gml);
        Assert.True(compiler.Compile().Successful);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "jamm-sanitizer-test-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(root);
        public string PathFor(string relative) => Path.Combine(root, relative);
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
