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
        Assert.Equal(ModSanitizer.BenignPerformance, distHunk.Category);
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
    public void KeepsOffscreenDrawCullsButClassifiesFishTickThrottleAsHarmful()
    {
        const string fishBase = "speed = spd;\n";
        const string fishMod = "check_timer++;\nif (check_timer >= 6)\n{\n    check_timer = 0;\n    if (point_distance(x, y, Camera.x, Camera.y) > 5000 && state != 12 && state != 6)\n    {\n        speed = spd;\n    }\n}\n";
        var fishThrottle = Assert.Single(ModSanitizer.ClassifyEntry("gml_Object_Fish_Step_2", fishBase, fishMod));
        Assert.Equal(ModSanitizer.TickThrottle, fishThrottle.Category);

        var safeDraw = Assert.Single(ModSanitizer.ClassifyEntry(
            "gml_Object_Fish_Draw_0", "fish_predraw_switchcase();\nfish_basicdraw();\n",
            "if (point_distance(x, y, Camera.x, Camera.y) < 5000)\n{\n    fish_predraw_switchcase();\n    fish_basicdraw();\n}\n"));
        Assert.Equal(ModSanitizer.BenignPerformance, safeDraw.Category);

        var harmfulSonar = Assert.Single(ModSanitizer.ClassifyEntry(
            "gml_Object_SonarPanel_Draw_0", "draw_self();\n",
            "if (point_distance(x, y, Camera.x, Camera.y) < 5000) visible = false;\ndraw_self();\n"));
        Assert.Equal(ModSanitizer.DistanceCull, harmfulSonar.Category);

        var harmfulTiming = Assert.Single(ModSanitizer.ClassifyEntry(
            "gml_Object_Girl_Step_0", "delta = delta_time;\n", "delta = room_speed / 30;\n"));
        Assert.Equal(ModSanitizer.Timing, harmfulTiming.Category);
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
        Assert.Contains(sanitized.Report.Removed, r => r.Category == ModSanitizer.TickThrottle && r.Entry == "gml_Object_Fish_Step_2");
        Assert.Contains(sanitized.Report.Removed, r => r.Category == ModSanitizer.Timing && r.Entry == "objects/fpsController");
        Assert.Contains(sanitized.Report.Removed, r => r.Category == ModSanitizer.Timing && r.Entry.StartsWith("rooms/", StringComparison.Ordinal));

        // Girl_Create_0 and Fish_Create_0 are reverted with their harmful tick-throttle changes.
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
    public void LibrarySanitizesHarmfulAuthorPerformanceByDefaultAndAllowsKeepingAllChanges()
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

        // New and legacy records sanitize harmful performance edits unless explicitly opted out.
        string metadataPath = Path.Combine(store.Root, "mods", added.Mod.Id, "mod.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(
            added.Mod with { KeepAuthorPerformanceChanges = true, KeepAuthorPerformanceChangesConfigured = false }, ManagerStore.JsonOptions));
        var legacyMod = library.Find(added.Mod.Id);
        Assert.True(legacyMod.KeepAuthorPerformanceChanges);
        Assert.False(legacyMod.KeepAuthorPerformanceChangesConfigured);
        Assert.False(legacyMod.EffectiveKeepAuthorPerformanceChanges);
        Assert.Empty(library.Patch(legacyMod).Changes["code"].Changed);

        // The Advanced switch remains available to use JAMM's sanitized performance patch.
        var updated = library.SetKeepAuthorPerformanceChanges(added.Mod.Id, false);
        Assert.False(updated.KeepAuthorPerformanceChanges);
        Assert.Empty(library.Patch(updated).Changes["code"].Changed);

        library.SetKeepAuthorPerformanceChanges(added.Mod.Id, true);
        Assert.Single(library.Patch(library.Find(added.Mod.Id)).Changes["code"].Changed);
    }

    [Fact]
    public void HudStyleSanitizerRevertsBaseHudReplacementsPreservesAppendedReferencedFramesAndHonorsKeepHudSwitch()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modReferencedPath = temp.PathFor("mod-referenced.win");
        string modUnreferencedPath = temp.PathFor("mod-unreferenced.win");

        WriteSyntheticGame(basePath, data =>
        {
            AddSprite(data, "spr_wallet", 40, 20, frameCount: 1);
            AddSprite(data, "spr_upgradeicons", 50, 50, frameCount: 2);
            AddSprite(data, "spr_lightbulb", 60, 60, frameCount: 2);
            AddFont(data, "fnt_babySmall", 14);
            AddCode(data, "gml_Object_UpgradeData_Create_0", "unlocks[0] = 0;\nname[0] = \"Scouting\";\n");
            AddCode(data, "gml_Object_Girl_Create_0", "time_scalar = 0.00003;\n");
        });

        WriteSyntheticGame(modReferencedPath, data =>
        {
            // Replaced HUD sprite (no appended frames) -> must revert to base
            AddSprite(data, "spr_wallet", 44, 22, frameCount: 1);
            // HUD sprite with modified base frames AND an appended frame 2 referenced via upgrade index 1 -> keep base frames 0..1, keep frame 2
            AddSprite(data, "spr_upgradeicons", 54, 54, frameCount: 3);
            // Non-HUD sprite -> kept intact
            AddSprite(data, "spr_lightbulb", 64, 64, frameCount: 3);
            // Added new HUD sprite -> kept intact
            AddSprite(data, "spr_boat_icon8", 32, 32, frameCount: 1);
            // Changed HUD font -> must revert to base
            AddFont(data, "fnt_babySmall", 18);
            // Code references upgrade index 1 (which maps to spr_upgradeicons frame 1 + 1 = 2)
            ReplaceCode(data, "gml_Object_UpgradeData_Create_0", "unlocks[0] = 0;\nunlocks[1] = 0;\nname[0] = \"Scouting\";\nname[1] = \"Bulbguard\";\n");
            // Performance hack -> reverted by performance sanitizer
            ReplaceCode(data, "gml_Object_Girl_Create_0", "time_scalar = 0.00006;\n");
        });

        WriteSyntheticGame(modUnreferencedPath, data =>
        {
            AddSprite(data, "spr_wallet", 40, 20, frameCount: 1);
            // Appended frame 2 on spr_upgradeicons, but NO code references upgrade 1 or frame 2 -> entire sprite reverts to base
            AddSprite(data, "spr_upgradeicons", 50, 50, frameCount: 3);
            AddSprite(data, "spr_lightbulb", 60, 60, frameCount: 2);
            AddFont(data, "fnt_babySmall", 14);
            ReplaceCode(data, "gml_Object_UpgradeData_Create_0", "unlocks[0] = 0;\nname[0] = \"Scouting\";\n");
            ReplaceCode(data, "gml_Object_Girl_Create_0", "time_scalar = 0.00003;\n");
        });

        using var origLoaded = GameFileLoader.Load(basePath);
        using var modRefLoaded = GameFileLoader.Load(modReferencedPath);
        using var modUnrefLoaded = GameFileLoader.Load(modUnreferencedPath);

        var rawRefPatch = PatchBuilder.Create(origLoaded, modRefLoaded, "RefMod");
        var sanitizedRef = ModSanitizer.Sanitize(rawRefPatch);

        Assert.Equal(1, sanitizedRef.Report.RemovedPerformanceCount);
        Assert.Equal(3, sanitizedRef.Report.RemovedHudCount);
        Assert.Contains(sanitizedRef.Report.Removed, r => r.Category == ModSanitizer.HudStyle && r.Entry == "sprites/spr_wallet");
        Assert.Contains(sanitizedRef.Report.Removed, r => r.Category == ModSanitizer.HudStyle && r.Entry == "sprites/spr_upgradeicons");
        Assert.Contains(sanitizedRef.Report.Removed, r => r.Category == ModSanitizer.HudStyle && r.Entry == "fonts/fnt_babySmall");

        // spr_wallet and fnt_babySmall reverted completely; spr_boat_icon8 (added) and spr_lightbulb (non-HUD) kept
        Assert.DoesNotContain(sanitizedRef.Patch.Changes["sprites"].Changed, s => s.Name == "spr_wallet");
        Assert.DoesNotContain(sanitizedRef.Patch.Changes["fonts"].Changed, f => f.Name == "fnt_babySmall");
        Assert.Contains(sanitizedRef.Patch.Changes["sprites"].Added, s => s.Name == "spr_boat_icon8");
        Assert.Contains(sanitizedRef.Patch.Changes["sprites"].Changed, s => s.Name == "spr_lightbulb");

        // spr_upgradeicons keeps base width/height (50x50) and base frames 0..1, plus appended frame 2 from mod
        var upgIcons = Assert.Single(sanitizedRef.Patch.Changes["sprites"].Changed, s => s.Name == "spr_upgradeicons");
        Assert.Equal(50, upgIcons.Mod.GetProperty("width").GetInt32());
        Assert.Equal(3, upgIcons.Mod.GetProperty("frameCount").GetInt32());
        var baseFrames = upgIcons.Base!.Value.GetProperty("frames").EnumerateArray().ToArray();
        var modFrames = upgIcons.Mod.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal(3, modFrames.Length);
        Assert.True(JsonElement.DeepEquals(baseFrames[0], modFrames[0]));
        Assert.True(JsonElement.DeepEquals(baseFrames[1], modFrames[1]));

        // Unreferenced appended frame is reverted completely to base
        var rawUnrefPatch = PatchBuilder.Create(origLoaded, modUnrefLoaded, "UnrefMod");
        var sanitizedUnref = ModSanitizer.Sanitize(rawUnrefPatch);
        Assert.DoesNotContain(sanitizedUnref.Patch.Changes["sprites"].Changed, s => s.Name == "spr_upgradeicons");
        Assert.Contains(sanitizedUnref.Report.Removed, r => r.Category == ModSanitizer.HudStyle && r.Entry == "sprites/spr_upgradeicons");

        // Verify independent KeepAuthorHudDesign and KeepAuthorPerformanceChanges switches in ModLibrary
        var store = new ManagerStore(temp.PathFor("store"));
        store.Set("base", basePath);
        var library = new ModLibrary(store);
        var added = library.Add(modReferencedPath, "RefMod").Mod;

        // Harmful timing changes are sanitized by default while HUD replacements remain sanitized.
        var defaultPatch = library.Patch(added);
        Assert.DoesNotContain(defaultPatch.Changes["code"].Changed, c => c.Name == "gml_Object_Girl_Create_0");
        Assert.DoesNotContain(defaultPatch.Changes["sprites"].Changed, s => s.Name == "spr_wallet");

        // HUD and performance choices remain independent.
        var keepHud = library.SetKeepAuthorHudDesign(added.Id, true);
        Assert.True(keepHud.KeepAuthorHudDesign);
        var hudKeptPatch = library.Patch(keepHud);
        Assert.DoesNotContain(hudKeptPatch.Changes["code"].Changed, c => c.Name == "gml_Object_Girl_Create_0");
        Assert.Contains(hudKeptPatch.Changes["sprites"].Changed, s => s.Name == "spr_wallet");
        Assert.Contains(hudKeptPatch.Changes["fonts"].Changed, f => f.Name == "fnt_babySmall");

        // The Advanced performance switch keeps all author performance changes.
        library.SetKeepAuthorHudDesign(added.Id, false);
        var keepPerf = library.SetKeepAuthorPerformanceChanges(added.Id, true);
        Assert.True(keepPerf.KeepAuthorPerformanceChangesConfigured);
        Assert.True(keepPerf.EffectiveKeepAuthorPerformanceChanges);
        var rawPerfPatch = library.Patch(keepPerf);
        Assert.Contains(rawPerfPatch.Changes["code"].Changed, c => c.Name == "gml_Object_Girl_Create_0");
        Assert.DoesNotContain(rawPerfPatch.Changes["sprites"].Changed, s => s.Name == "spr_wallet");
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
            library.SetKeepAuthorPerformanceChanges(mod.Id, false);
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

        // Verify Abyss has all 11 purple HUD sprites reverted and 0 performance removals; Cave has 0; Colour has 1 (FishThinner do_zoning).
        string[] abyssHudSprites =
        [
            "spr_catalog_arrow", "spr_catalog_btn", "spr_catalog_tab", "spr_icon", "spr_iconsmall",
            "spr_lurecard", "spr_radarbutton", "spr_rodcard", "spr_savecard_btn", "spr_sonarpanel", "spr_wallet"
        ];
        Assert.Equal(0, reports["Abyss"].RemovedPerformanceCount);
        Assert.Equal(11, reports["Abyss"].RemovedHudCount);
        foreach (string hudSpr in abyssHudSprites)
            Assert.Contains(reports["Abyss"].Removed, r => r.Category == ModSanitizer.HudStyle && r.Entry == $"sprites/{hudSpr}");
        Assert.Empty(reports["Cave"].Removed);
        Assert.Equal(1, reports["Colour"].RemovedPerformanceCount);
        Assert.Equal(1, reports["Colour"].RemovedGameplayCount);
        Assert.Equal(0, reports["Colour"].RemovedHudCount);
        Assert.Contains(reports["Colour"].Removed, r => r.Category == ModSanitizer.DistanceCull && r.Entry == "gml_Object_FishThinner_Step_0");
        Assert.Contains(reports["Colour"].Removed, r => r.Category == ModSanitizer.GameplaySonar && r.Entry == "code/gml_Object_Zebriolet_Step_0");

        // Verify OAC has all 22 timing/throttle/cull/fps/debug-overlay hacks removed + 1 hud-style base-frame revert on spr_upgradeicons, and 0 unclassified.
        var oacReport = reports["Oops! All Chaws!"];
        Assert.False(oacReport.IsJammMod);
        Assert.Empty(oacReport.Unclassified);
        Assert.Equal(22, oacReport.RemovedPerformanceCount);
        Assert.Equal(1, oacReport.RemovedHudCount);
        Assert.Contains(oacReport.Removed, r => r.Category == ModSanitizer.HudStyle && r.Entry == "sprites/spr_upgradeicons");
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

        using var origGame = GameFileLoader.Load(basePath);
        using var oacGame = GameFileLoader.Load(Path.Combine(store.Root, "mods", library.Find("Oops! All Chaws!").Id, "data.win"));
        using var merged = GameFileLoader.Load(output);
        CgfAssetTypes.Register(merged.Data);

        // Verify all 11 Abyss HUD sprites in the merged archive match the base game's original HUD sprites:
        foreach (string hudSpr in abyssHudSprites)
        {
            var baseSnap = PatchBuilder.ReadAsset(origGame, AssetKind.Sprites, hudSpr);
            var mergedSnap = PatchBuilder.ReadAsset(merged, AssetKind.Sprites, hudSpr);
            Assert.True(JsonElement.DeepEquals(baseSnap, mergedSnap), $"Expected HUD sprite '{hudSpr}' to match base game.");
        }

        // Verify OAC's spr_upgradeicons keeps base frames 0..18 unchanged and keeps appended Bulbguard frame 19 from OAC:
        var baseUpgSnap = PatchBuilder.ReadAsset(origGame, AssetKind.Sprites, "spr_upgradeicons");
        var oacUpgSnap = PatchBuilder.ReadAsset(oacGame, AssetKind.Sprites, "spr_upgradeicons");
        var mergedUpgSnap = PatchBuilder.ReadAsset(merged, AssetKind.Sprites, "spr_upgradeicons");
        Assert.Equal(19, baseUpgSnap.GetProperty("frameCount").GetInt32());
        Assert.Equal(20, mergedUpgSnap.GetProperty("frameCount").GetInt32());
        var baseUpgFrames = baseUpgSnap.GetProperty("frames").EnumerateArray().ToArray();
        var oacUpgFrames = oacUpgSnap.GetProperty("frames").EnumerateArray().ToArray();
        var mergedUpgFrames = mergedUpgSnap.GetProperty("frames").EnumerateArray().ToArray();
        for (int i = 0; i < 19; i++)
            Assert.True(JsonElement.DeepEquals(baseUpgFrames[i], mergedUpgFrames[i]), $"Expected spr_upgradeicons frame {i} to match base.");
        Assert.True(JsonElement.DeepEquals(oacUpgFrames[19], mergedUpgFrames[19]), "Expected spr_upgradeicons frame 19 (Bulbguard) to match OAC.");

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

        // Build and verify the optional Console mod from the original legacy data.win:
        string consoleOut = temp.PathFor("jamm-console.win");
        Assert.Equal(0, Jamm.ModMaker.Program.Main(["console", basePath, consoleOut]));
        Assert.Equal(0, Jamm.Cli.BuildCommand.Run("verify", [consoleOut]));
        using var consoleLoaded = GameFileLoader.Load(consoleOut);
        var consolePatch = PatchBuilder.Create(origGame, consoleLoaded, "Console", options: options);
        var consoleSanitized = ModSanitizer.Sanitize(consolePatch);
        Assert.True(consoleSanitized.Report.IsJammMod);
        Assert.Empty(consoleSanitized.Report.Removed);

        // Verify Colour Expansion's Zebriolet sonar bypass is sanitized cleanly in the merged archive while keeping FishThinner/content:
        string zebrioletMerged = PatchBuilder.ReadCode(merged, "gml_Object_Zebriolet_Step_0").Text;
        Assert.Contains("jamm_sonar_jammed", zebrioletMerged);
        Assert.Contains("ToolData.unlocked[1] == 1", zebrioletMerged);
        Assert.DoesNotContain("shouldDraw = 1", zebrioletMerged);
    }

    [Fact]
    public void SonarSanitizerDerivesBaseConditionAndEnforcesProgressionWhilePreservingJamMechanic()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string customBasePath = temp.PathFor("custom-base.win");
        string jamModPath = temp.PathFor("jam-mod.win");
        string unlockBypassModPath = temp.PathFor("unlock-bypass.win");

        WriteSyntheticGame(basePath, data =>
        {
            var toolData = new UndertaleGameObject { Name = data.Strings.MakeString("ToolData") };
            var radarButton = new UndertaleGameObject { Name = data.Strings.MakeString("RadarButton"), Visible = true };
            var sonarPanel = new UndertaleGameObject { Name = data.Strings.MakeString("SonarPanel"), Visible = true };
            var sonarAdv = new UndertaleGameObject { Name = data.Strings.MakeString("SonarPanelAdvanced"), Visible = false };
            data.GameObjects.Add(toolData);
            data.GameObjects.Add(radarButton);
            data.GameObjects.Add(sonarPanel);
            data.GameObjects.Add(sonarAdv);
            AddCode(data, "gml_Object_ToolData_Create_0", "item[0] = 0;\nitem[1] = SonarPanel;\nunlocked[1] = 0;\nitem[2] = SonarPanelAdvanced;\nunlocked[2] = -1;\n");
            AddCode(data, "gml_Object_RadarButton_Step_0", "if (ToolData.unlocked[1] == 1 && SonarPanel.active) { visible = 1; } else { visible = 0; }\n");
            AddCode(data, "gml_Object_SonarPanel_Create_0", "active = 0;\nshouldDraw = 0;\ndrawOverride = 0;\n");
            data.Rooms[0].Views.Add(new UndertaleRoom.View { Enabled = false });
            data.Rooms[0].Views.Add(new UndertaleRoom.View { Enabled = false });
            data.Rooms[0].Views.Add(new UndertaleRoom.View { Enabled = false });
            data.Rooms[0].Views.Add(new UndertaleRoom.View { Enabled = false });
        });

        // Derived condition works with custom non-1 slot indices too:
        WriteSyntheticGame(customBasePath, data =>
        {
            var toolData = new UndertaleGameObject { Name = data.Strings.MakeString("ToolData") };
            var radarButton = new UndertaleGameObject { Name = data.Strings.MakeString("RadarButton") };
            var sonarPanel = new UndertaleGameObject { Name = data.Strings.MakeString("SonarPanel") };
            var sonarAdv = new UndertaleGameObject { Name = data.Strings.MakeString("SonarPanelAdvanced") };
            data.GameObjects.Add(toolData);
            data.GameObjects.Add(radarButton);
            data.GameObjects.Add(sonarPanel);
            data.GameObjects.Add(sonarAdv);
            AddCode(data, "gml_Object_ToolData_Create_0", "item[4] = SonarPanel;\nunlocked[4] = 0;\nitem[5] = SonarPanelAdvanced;\nunlocked[5] = -1;\n");
            AddCode(data, "gml_Object_RadarButton_Step_0", "if (ToolData.unlocked[4] == 1 && SonarPanel.active) { visible = 1; }\n");
        });
        using (var customLoaded = GameFileLoader.Load(customBasePath))
        {
            var derivedCustom = VanillaSonarCondition.FromData(customLoaded.Data);
            Assert.Equal(4, derivedCustom.SonarToolSlot);
            Assert.Equal(5, derivedCustom.DeepSonarToolSlot);
            Assert.Equal("ToolData.unlocked[4] == 1", derivedCustom.UnlockExpression);
        }

        // Mod 1: Added fish Step event that jams sonar in state == 3 and forces SonarPanel on when state != 3.
        File.Copy(basePath, jamModPath);
        using (var stream = File.Open(jamModPath, FileMode.Open, FileAccess.ReadWrite))
        {
            var data = UndertaleIO.Read(stream);
            var fish = new UndertaleGameObject { Name = data.Strings.MakeString("Zebriolet"), Visible = true };
            data.GameObjects.Add(fish);
            var step = fish.EventHandlerFor(EventType.Step, 0u, data);
            ReplaceCode(data, step.Name.Content,
                "var state = 0;\nif (state == 3)\n{\n    SonarPanel.active = 0;\n    SonarPanel.shouldDraw = 0;\n}\nif (state != 3)\n{\n    SonarPanel.active = 1;\n    SonarPanel.shouldDraw = 1;\n}\n");
            stream.SetLength(0);
            stream.Position = 0;
            UndertaleIO.Write(stream, data);
        }

        // Mod 2: Changed vanilla files unlocking SonarPanel early + enabling view[3].
        File.Copy(basePath, unlockBypassModPath);
        using (var stream = File.Open(unlockBypassModPath, FileMode.Open, FileAccess.ReadWrite))
        {
            var data = UndertaleIO.Read(stream);
            ReplaceCode(data, "gml_Object_ToolData_Create_0", "item[0] = 0;\nitem[1] = SonarPanel;\nunlocked[1] = 1;\nitem[2] = SonarPanelAdvanced;\nunlocked[2] = -1;\n");
            ReplaceCode(data, "gml_Object_RadarButton_Step_0", "if (SonarPanel.active) { visible = 1; } else { visible = 0; }\n");
            data.Rooms[0].Views[3].Enabled = true;
            stream.SetLength(0);
            stream.Position = 0;
            UndertaleIO.Write(stream, data);
        }

        var store = new ManagerStore(temp.PathFor("store"));
        store.Set("base", basePath);
        var library = new ModLibrary(store);

        var jamAdded = library.Add(jamModPath, "JamMod").Mod;
        var jamReport = library.SanitizerReport(jamAdded);
        Assert.Equal(0, jamReport.RemovedPerformanceCount);
        Assert.Equal(0, jamReport.RemovedHudCount);
        Assert.Equal(1, jamReport.RemovedGameplayCount);
        Assert.Contains(jamReport.Removed, r => r.Category == ModSanitizer.GameplaySonar && r.Entry == "code/gml_Object_Zebriolet_Step_0");

        var jamSanitizedPatch = library.Patch(jamAdded);
        string jamRewritten = jamSanitizedPatch.Changes["code"].Added.Single(c => c.Name == "gml_Object_Zebriolet_Step_0").ModCode!.Text;
        Assert.Contains("jamm_sonar_jammed", jamRewritten);
        Assert.Contains("ToolData.unlocked[1] == 1", jamRewritten);
        Assert.DoesNotContain("SonarPanel.shouldDraw = 1", jamRewritten);

        // Build archive with sanitized JamMod to confirm rewritten GML compiles with UndertaleModLib cleanly.
        string builtJam = temp.PathFor("built-jam.win");
        var buildResult = ArchiveBuilder.Build(basePath, [new(jamSanitizedPatch, jamModPath)], builtJam);
        Assert.NotNull(buildResult.Verification);

        // Opt-out restores original author code without re-diffing archives.
        library.SetKeepAuthorGameplayChanges(jamAdded.Id, true);
        var jamKeptPatch = library.Patch(library.Find(jamAdded.Id));
        string jamRaw = jamKeptPatch.Changes["code"].Added.Single(c => c.Name == "gml_Object_Zebriolet_Step_0").ModCode!.Text;
        Assert.Contains("SonarPanel.shouldDraw = 1;", jamRaw);
        Assert.DoesNotContain("jamm_sonar_jammed", jamRaw);

        // Mod 2 reverts ToolData.unlocked[1] = 1, RadarButton_Step_0 unlock removal, and room view 3.
        var bypassAdded = library.Add(unlockBypassModPath, "BypassMod").Mod;
        var bypassReport = library.SanitizerReport(bypassAdded);
        Assert.Equal(3, bypassReport.RemovedGameplayCount);
        var bypassPatch = library.Patch(bypassAdded);
        Assert.Empty(bypassPatch.Changes["code"].Changed);
        Assert.Empty(bypassPatch.Changes["rooms"].Changed);
    }

    private static void AddSprite(UndertaleData data, string name, uint width, uint height, int frameCount)
    {
        var existing = data.Sprites.ByName(name);
        if (existing is not null) data.Sprites.Remove(existing);
        var spr = new UndertaleSprite
        {
            Name = data.Strings.MakeString(name),
            Width = width,
            Height = height,
            MarginRight = (int)width - 1,
            MarginBottom = (int)height - 1
        };
        for (int i = 0; i < frameCount; i++)
        {
            var item = new UndertaleTexturePageItem
            {
                Name = data.Strings.MakeString($"PageItem_{name}_{i}_{width}"),
                SourceX = (ushort)(i * 16),
                SourceY = (ushort)width,
                SourceWidth = (ushort)width,
                SourceHeight = (ushort)height,
                TargetWidth = (ushort)width,
                TargetHeight = (ushort)height,
                BoundingWidth = (ushort)width,
                BoundingHeight = (ushort)height
            };
            data.TexturePageItems.Add(item);
            spr.Textures.Add(new UndertaleSprite.TextureEntry { Texture = item });
        }
        data.Sprites.Add(spr);
    }

    private static void AddFont(UndertaleData data, string name, uint emSize)
    {
        var existing = data.Fonts.ByName(name);
        if (existing is not null) data.Fonts.Remove(existing);
        data.Fonts.Add(new UndertaleFont
        {
            Name = data.Strings.MakeString(name),
            DisplayName = data.Strings.MakeString(name),
            EmSize = emSize
        });
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
