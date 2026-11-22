using Jamm.Core.Management;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Covers Fish Sleep eligibility analysis, zone/wake pure helpers, INI switches, and archive integration.</summary>
public sealed class FishSleepTests
{
    [Fact]
    public void EligibilityCoversStandardAndTrivialTiersAndExemptsSpecialAndCustomFish()
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;

        var room = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld"), Width = 10000, Height = 10000 };
        data.Rooms.Add(room);

        var fish = ModObjects.Create(data, "Fish", false);
        var fishSmall = ModObjects.Create(data, "FishSmall", false); fishSmall.ParentId = fish;
        var fishMedium = ModObjects.Create(data, "FishMedium", false); fishMedium.ParentId = fish;
        var fishLarge = ModObjects.Create(data, "FishLarge", false); fishLarge.ParentId = fish;
        var fishLargePather = ModObjects.Create(data, "FishLargePather", false); fishLargePather.ParentId = fishLarge;
        var fishHuge = ModObjects.Create(data, "FishHuge", false); fishHuge.ParentId = fish;

        var normalSmall = ModObjects.Create(data, "ModBasicSmall", false); normalSmall.ParentId = fishSmall;
        var sourLike = ModObjects.Create(data, "ModTrivialStep", false); sourLike.ParentId = fishMedium;
        var visualDraw = ModObjects.Create(data, "ModVisualDraw", false); visualDraw.ParentId = fishLarge;
        var customStep = ModObjects.Create(data, "ModCustomStep", false); customStep.ParentId = fishSmall;
        var customAlarm = ModObjects.Create(data, "ModCustomAlarm", false); customAlarm.ParentId = fishMedium;
        var customDestroy = ModObjects.Create(data, "ModCustomDestroy", false); customDestroy.ParentId = fishLarge;
        var customOther = ModObjects.Create(data, "ModCustomOther", false); customOther.ParentId = fishSmall;
        var gameplayDraw = ModObjects.Create(data, "ModGameplayDraw", false); gameplayDraw.ParentId = fishSmall;
        var patherChild = ModObjects.Create(data, "ModPatherFish", false); patherChild.ParentId = fishLargePather;
        var bossChild = ModObjects.Create(data, "ModBossFish", false); bossChild.ParentId = fishHuge;
        var kingfish = ModObjects.Create(data, "Kingfish", false); kingfish.ParentId = fishMedium;
        var siloFish = ModObjects.Create(data, "ModSiloFish", false); siloFish.ParentId = fishSmall;
        var areaFish = ModObjects.Create(data, "ModAreaFish", false); areaFish.ParentId = fishLarge;

        var areaCc = UndertaleCode.CreateEmptyEntry(data, "gml_RoomCC_jamm_area_0");
        room.GameObjects.Add(new UndertaleRoom.GameObject
        {
            ObjectDefinition = areaFish,
            X = 1000,
            Y = 2000,
            CreationCode = areaCc
        });

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(sourLike.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), "// only inherited\naction_inherited();");
        compiler.QueueCodeReplace(visualDraw.EventHandlerFor(EventType.Draw, 0u, data), "image_blend = c_white; draw_self();");
        compiler.QueueCodeReplace(customStep.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), "action_inherited(); x += 1;");
        compiler.QueueCodeReplace(customAlarm.EventHandlerFor(EventType.Alarm, 0u, data), "state = 0;");
        compiler.QueueCodeReplace(customDestroy.EventHandlerFor(EventType.Destroy, 0u, data), "score += 1;");
        compiler.QueueCodeReplace(customOther.EventHandlerFor(EventType.Other, 16u, data), "custom_var = 1;");
        compiler.QueueCodeReplace(gameplayDraw.EventHandlerFor(EventType.Draw, 0u, data), "if (x > 0) state = 2; draw_self();");
        ModObjects.Compile(compiler);

        var byName = FishSleep.AnalyzeEligibility(data, new HashSet<string>(["ExplicitAreaFish"]))
            .ToDictionary(d => d.ObjectName, StringComparer.Ordinal);

        Assert.True(byName["ModBasicSmall"].Covered);
        Assert.True(byName["ModTrivialStep"].Covered);
        Assert.True(byName["ModVisualDraw"].Covered);

        Assert.False(byName["ModCustomStep"].Covered);
        Assert.False(byName["ModCustomAlarm"].Covered);
        Assert.False(byName["ModCustomDestroy"].Covered);
        Assert.False(byName["ModCustomOther"].Covered);
        Assert.False(byName["ModGameplayDraw"].Covered);
        Assert.False(byName["ModPatherFish"].Covered);
        Assert.False(byName["ModBossFish"].Covered);
        Assert.False(byName["Kingfish"].Covered);
        Assert.False(byName["ModSiloFish"].Covered);
        Assert.False(byName["ModAreaFish"].Covered);
    }

    [Fact]
    public void ZoneHysteresisAndWakeCatchUpPreventFlappingAndViewEdgePops()
    {
        double margin = FishSleep.EffectiveMargin(2000, viewWidth: 2400);
        Assert.True(margin > 2400);

        double hyst = FishSleep.HysteresisBand(margin);
        Assert.InRange(hyst, 400, 800);

        // Union of view box and lure box.
        FishSleepBox[] boxes =
        [
            new(1000, 1500, 3000, 2900),
            new(8000, 3000, 11000, 5000)
        ];
        Assert.True(FishSleep.IsInZone(1500, 2000, boxes));
        Assert.True(FishSleep.IsInZone(9500, 4000, boxes));
        Assert.False(FishSleep.IsInZone(5000, 2000, boxes));

        // Inside hysteresis band (100px outside zone edge at 3000): awake stays awake, asleep stays asleep.
        Assert.False(FishSleep.NextSleepState(currentlyAsleep: false, x: 3100, y: 2000, boxes, hyst));
        Assert.True(FishSleep.NextSleepState(currentlyAsleep: true, x: 3100, y: 2000, boxes, hyst));

        // Outside zone + hysteresis: goes to sleep. Inside inner zone: wakes up.
        Assert.True(FishSleep.NextSleepState(currentlyAsleep: false, x: 3900, y: 2000, boxes, hyst));
        Assert.False(FishSleep.NextSleepState(currentlyAsleep: true, x: 2950, y: 2000, boxes, hyst));

        // Wake catch-up clamps to water bounds and never pops directly inside active view.
        var view = new FishSleepBox(2000, 1500, 3200, 2220);
        var (wx, wy, moved) = FishSleep.ClampWakePosition(
            prevX: 4000, prevY: 900, candX: 5000, candY: 500,
            minWaterX: 740, maxWaterX: 20000, minWaterY: 1294, maxWaterY: 12000,
            activeView: view, blockedByWall: false);
        Assert.True(moved);
        Assert.Equal(5000, wx);
        Assert.Equal(1294, wy);

        var (vx, vy, popMoved) = FishSleep.ClampWakePosition(
            prevX: 4000, prevY: 1800, candX: 2500, candY: 1800,
            minWaterX: 740, maxWaterX: 20000, minWaterY: 1294, maxWaterY: 12000,
            activeView: view, blockedByWall: false);
        Assert.False(popMoved);
        Assert.Equal(4000, vx);
        Assert.Equal(1800, vy);

        var (bx, by, wallMoved) = FishSleep.ClampWakePosition(
            prevX: 4000, prevY: 1800, candX: 4200, candY: 1800,
            minWaterX: 740, maxWaterX: 20000, minWaterY: 1294, maxWaterY: 12000,
            activeView: view, blockedByWall: true);
        Assert.False(wallMoved);
        Assert.Equal(4000, bx);
        Assert.Equal(1800, by);
    }

    [Fact]
    public void PerfModeAndFishSleepSerializeDefaultsAndInjectGuardsOnCoveredEvents()
    {
        using var data = CreateSyntheticPerfArchive(out var visualDrawFish);
        var decisions = new List<string>();

        PerfMod.Apply(data, debug: false, applyFishGrid: false, decisions);
        FishSleep.Finalize(data, [], decisions, new PerfSettings(StartEnabled: false, FishGrid: true, Interval: 4)
        {
            FishSleep = false,
            FishSleepMargin = 3200
        });

        string perfCreate = ModObjects.Decompile(data, data.Code.ByName("gml_Object_obj_jamm_perf_Create_0")!);
        string perfBegin = ModObjects.Decompile(data, data.Code.ByName("gml_Object_obj_jamm_perf_Step_1")!);
        string perfDestroy = ModObjects.Decompile(data, data.Code.ByName("gml_Object_obj_jamm_perf_Destroy_0")!);
        string fishStep2 = ModObjects.Decompile(data, data.Code.ByName("gml_Object_Fish_Step_2")!);
        string customDraw = ModObjects.Decompile(data, visualDrawFish.Events[(int)EventType.Draw].First().Actions.First().CodeId!);
        string initTypes = ModObjects.Decompile(data, data.Code.ByName("gml_Script_jamm_sleep_init_types")!);

        Assert.Contains("""ini_read_real("Perf", "FishSleep", 0)""", perfCreate);
        Assert.Contains("""ini_read_real("Perf", "FishSleepMargin", 3200)""", perfCreate);
        Assert.Contains("jamm_sleep_init_types()", perfCreate);
        Assert.Contains("jamm_sleep_begin_step()", perfBegin);
        Assert.Contains("ds_map_destroy(global.jamm_sleep_types)", perfDestroy);
        Assert.Contains("ds_map_destroy(global.jamm_sleep_quest_objs)", perfDestroy);
        Assert.Contains("jamm_sleep_wake()", fishStep2);
        Assert.Contains("if (!global.jamm_perf_sleep && global.jamm_perf_mode)", fishStep2);
        Assert.Contains("jamm_asleep", customDraw);
        Assert.Contains("ModVisual", initTypes);
        Assert.Contains(decisions, d => d.Contains("[CGF-PERF-SLEEP] ModVisual: covered", StringComparison.Ordinal));
    }

    [Fact]
    public void ProfileLibraryValidatesAndPersistsFishSleepSettingsWithLegacyJsonCompatibility()
    {
        string root = Path.Combine(Path.GetTempPath(), "jamm-sleep-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ManagerStore(root);
            string basePath = Path.Combine(root, "base.win");
            using (var data = CreateSyntheticPerfArchive(out _))
            using (var fs = File.Create(basePath))
                UndertaleIO.Write(fs, data);

            store.Set("base", basePath);
            string modId = new BuiltinMods(store).Add("perf").Mod.Id;
            var profiles = new ProfileLibrary(store);
            profiles.Create("default", [modId]);

            var initial = profiles.Get("default");
            Assert.True(initial.Perf.FishSleep);
            Assert.Equal(FishSleep.DefaultMargin, initial.Perf.FishSleepMargin);

            var updated = profiles.SetPerf("default", fishSleep: false, fishSleepMargin: 4500);
            Assert.False(updated.Perf.FishSleep);
            Assert.Equal(4500, updated.Perf.FishSleepMargin);

            var reloaded = profiles.Get("default");
            Assert.False(reloaded.Perf.FishSleep);
            Assert.Equal(4500, reloaded.Perf.FishSleepMargin);

            Assert.Throws<ArgumentException>(() => profiles.SetPerf("default", fishSleepMargin: 500));
            Assert.Throws<ArgumentException>(() => profiles.SetPerf("default", fishSleepMargin: 30000));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [RealFilesFact]
    public void FishSleepCoversVanillaAndModdedArchivesCleanly()
    {
        string vanillaPath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string modsDir = Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!;

        using var input = File.OpenRead(vanillaPath);
        using var data = UndertaleIO.Read(input);
        List<string> decisions = [];
        PerfMod.Apply(data, debug: true, applyFishGrid: true, decisions);

        Assert.Contains(decisions, d => d.StartsWith("[CGF-PERF-SLEEP]", StringComparison.Ordinal));
        Assert.True(decisions.Count(d => d.Contains(": covered", StringComparison.Ordinal)) >= 50);

        using var ms = new MemoryStream();
        UndertaleIO.Write(ms, data);
        ms.Position = 0;
        using var verify = UndertaleIO.Read(ms);
        Assert.NotNull(verify.Code.ByName("gml_Script_jamm_sleep_wake"));
        Assert.NotNull(verify.Code.ByName("gml_Script_jamm_sleep_begin_step"));
        Assert.False(string.IsNullOrWhiteSpace(modsDir));
    }

    private static UndertaleData CreateSyntheticPerfArchive(out UndertaleGameObject visualDrawFish)
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Minor = 0;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.Timestamp = 1692207312;
        data.GeneralInfo.BytecodeVersion = 16;

        data.Fonts.Add(new UndertaleFont { Name = data.Strings.MakeString("fnt_babySmall"), DisplayName = data.Strings.MakeString("Small") });
        data.Paths.Add(new UndertalePath { Name = data.Strings.MakeString("synthetic_path") });
        foreach (string roomName in new[] { "NewWorld", "room11" })
            data.Rooms.Add(new UndertaleRoom { Name = data.Strings.MakeString(roomName), Width = 100000, Height = 13000, Speed = 30 });

        foreach (string objName in new[] { "Girl", "Rod", "Lure", "Boat", "CatSwim", "Net", "SonarPanel", "SonarPanelAdvanced", "QuestEngine", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons", "SceneItem", "Magmer", "Volteen", "Collidable" })
            ModObjects.Create(data, objName, false);

        var fish = ModObjects.Create(data, "Fish", false);
        var fishSmall = ModObjects.Create(data, "FishSmall", false); fishSmall.ParentId = fish;
        var fishLarge = ModObjects.Create(data, "FishLarge", false); fishLarge.ParentId = fish;
        visualDrawFish = ModObjects.Create(data, "ModVisual", false); visualDrawFish.ParentId = fishSmall;

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(data.GameObjects.ByName("QuestEngine")!.EventHandlerFor(EventType.Step, 1u, data), "quest_counter = 0;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Girl")!.EventHandlerFor(EventType.Step, 0u, data), "a = delta_time; b = delta_time; c = delta_time;");
        compiler.QueueCodeReplace(fish.EventHandlerFor(EventType.Create, 0u, data), "state = 0; life = 10; deactivated = false;");
        compiler.QueueCodeReplace(fish.EventHandlerFor(EventType.Step, 2u, data), "if (y > 5000 && !deepfish)\n{\n    x += 1;\n}");
        compiler.QueueCodeReplace(fish.EventHandlerFor(EventType.Draw, 0u, data), "draw_self();");
        compiler.QueueCodeReplace(fishLarge.EventHandlerFor(EventType.Step, 0u, data), "x += 1;");
        compiler.QueueCodeReplace(visualDrawFish.EventHandlerFor(EventType.Draw, 0u, data), "image_blend = c_white;\ndraw_self();");
        compiler.QueueCodeReplace(data.GameObjects.ByName("SceneItem")!.EventHandlerFor(EventType.Step, 2u, data), "scene_depth = depth + 18;\nx = home_x;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Magmer")!.EventHandlerFor(EventType.Draw, 0u, data), "light.image_alpha = 1 - power(1 - heat_level, 2);");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Volteen")!.EventHandlerFor(EventType.Other, 13u, data), "light.add_blend = 4210752;\nlight.sub_blend = 4210752;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Volteen")!.EventHandlerFor(EventType.Other, 10u, data), "if (active) { light.add_blend = 0; light.sub_blend = 0; } else { light.add_blend = 4210752; light.sub_blend = 4210752; }");

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
