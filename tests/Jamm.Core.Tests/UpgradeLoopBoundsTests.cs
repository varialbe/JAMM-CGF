using System.Text.RegularExpressions;
using Jamm.Core.Acceptance;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Tests dynamic upgrade_scalar loop rewriting when UpgradeData grows past 18 entries and acceptance loop-bound checks.</summary>
public sealed class UpgradeLoopBoundsTests
{
    [Fact]
    public void NineteenEntryLegacyUpgradeTableRewritesGirlAndGearScalarLoopsAndClearsAcceptanceWarning()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteSyntheticUpgradeArchive(basePath, upgradeCount: 18, girlResetCount: 18, girlCopyCount: 18, gearCount: 18, menuCount: 18);
        WriteSyntheticUpgradeArchive(modPath, upgradeCount: 19, girlResetCount: 19, girlCopyCount: 18, gearCount: 18, menuCount: 19);

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "ExtraUpgrade");

        var result = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(result.Verification);
        Assert.NotNull(result.Acceptance);

        Assert.Contains(result.Plan.Decisions, d =>
            d.StartsWith("[CGF-UPGRADE-LOOP-001]", StringComparison.Ordinal)
            && d.Contains("gml_Object_Girl_Other_10", StringComparison.Ordinal)
            && d.Contains("gml_Object_Gear_Other_15", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Acceptance.Risks, r => r.Code == "TABLE-LENGTH");

        using var merged = GameFileLoader.Load(output);
        string girl = PatchBuilder.ReadCode(merged, "gml_Object_Girl_Other_10").Text;
        Assert.Contains("array_length_1d(UpgradeData.unlocks)", girl, StringComparison.Ordinal);
        Assert.Contains("array_length_1d(gear.upgrade_scalar)", girl, StringComparison.Ordinal);
        Assert.Contains("gear_lure_scalar[0] = 1", girl, StringComparison.Ordinal);
        Assert.DoesNotContain("<= 17", girl, StringComparison.Ordinal);
        Assert.DoesNotContain("<= 18", girl, StringComparison.Ordinal);

        string gear = PatchBuilder.ReadCode(merged, "gml_Object_Gear_Other_15").Text;
        Assert.Contains("array_length_1d(UpgradeData.unlocks)", gear, StringComparison.Ordinal);
        Assert.Contains("lure_scalar[0] = 1", gear, StringComparison.Ordinal);
        Assert.DoesNotContain("<= 17", gear, StringComparison.Ordinal);
        Assert.DoesNotContain("<= 18", gear, StringComparison.Ordinal);
    }

    [Fact]
    public void EighteenEntryUpgradeTableLeavesGirlAndGearLoopsUnchanged()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteSyntheticUpgradeArchive(basePath, upgradeCount: 18, girlResetCount: 18, girlCopyCount: 18, gearCount: 18, menuCount: 18);
        WriteSyntheticUpgradeArchive(modPath, upgradeCount: 18, girlResetCount: 18, girlCopyCount: 18, gearCount: 18, menuCount: 18,
            edit: d => d.GameObjects.ByName("Girl")!.Depth = 10);

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "Plain");

        var result = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(result.Verification);
        Assert.DoesNotContain(result.Plan.Decisions, d => d.Contains("[CGF-UPGRADE-LOOP-001]", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(output);
        string girl = PatchBuilder.ReadCode(merged, "gml_Object_Girl_Other_10").Text;
        string gear = PatchBuilder.ReadCode(merged, "gml_Object_Gear_Other_15").Text;
        Assert.DoesNotContain("UpgradeData.unlocks", girl, StringComparison.Ordinal);
        Assert.DoesNotContain("UpgradeData.unlocks", gear, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemsApiBuildWithNineteenEntryUpgradeTableIsNotDoublePatched()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteSyntheticUpgradeArchive(basePath, upgradeCount: 18, girlResetCount: 18, girlCopyCount: 18, gearCount: 18, menuCount: 18,
            includeItemsApiAnchors: true);
        WriteSyntheticUpgradeArchive(modPath, upgradeCount: 19, girlResetCount: 19, girlCopyCount: 18, gearCount: 18, menuCount: 19,
            includeItemsApiAnchors: true,
            edit: d =>
            {
                AddObject(d, "obj_api_boat");
                AddScript(d, "jamm_boat_register", "return 0;");
                AddScript(d, "jamm_init_api_demo", "jamm_boat_register(\"demo:boat\", \"API Boat\", 1000, -1, obj_api_boat, \"Boat\");");
            });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "ApiWith19");

        var result = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(result.Verification);
        Assert.NotNull(result.Acceptance);

        Assert.Contains(result.Plan.Decisions, d => d.StartsWith("Items API:", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Plan.Decisions, d => d.Contains("[CGF-UPGRADE-LOOP-001]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Acceptance.Risks, r => r.Code == "TABLE-LENGTH");

        using var merged = GameFileLoader.Load(output);
        string girl = PatchBuilder.ReadCode(merged, "gml_Object_Girl_Other_10").Text;
        string gear = PatchBuilder.ReadCode(merged, "gml_Object_Gear_Other_15").Text;
        Assert.Equal(2, Regex.Matches(girl, @"array_length_1d\s*\(\s*UpgradeData\.unlocks\s*\)").Count);
        Assert.Single(Regex.Matches(girl, @"array_length_1d\s*\(\s*gear\.upgrade_scalar\s*\)"));
        Assert.Single(Regex.Matches(gear, @"array_length_1d\s*\(\s*UpgradeData\.unlocks\s*\)"));
    }

    [Fact]
    public void VanillaOnlyBuildRemainsByteIdenticalEvenOnNineteenEntryBase()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base19.win");
        string output = temp.PathFor("vanilla.win");

        WriteSyntheticUpgradeArchive(basePath, upgradeCount: 19, girlResetCount: 19, girlCopyCount: 18, gearCount: 18, menuCount: 19);

        var result = ArchiveBuilder.Build(basePath, [], output);
        Assert.Empty(result.Plan.Decisions);
        Assert.Equal(BuildFiles.Hash(basePath), BuildFiles.Hash(output));
    }

    [Fact]
    public void PackAcceptanceDistinguishesCrashRiskFromMissingScalarInitAndSkipsRepairedLoops()
    {
        const string upgradeCreate = "gml_Object_UpgradeData_Create_0";
        const string menuAlarm = "gml_Object_UpgradeMenu_Alarm_0";
        const string girlOther = "gml_Object_Girl_Other_10";
        const string gearOther = "gml_Object_Gear_Other_15";

        using var baseline = UndertaleData.CreateNew();
        using var mod = UndertaleData.CreateNew();
        using var merged = UndertaleData.CreateNew();
        var plan = new BuildPlan([], []);

        string nineteenCreate = BuildUpgradeCreateGml(19);
        string repairedMenu = "for (jamm_i = 0; jamm_i < array_length_1d(UpgradeData.unlocks); jamm_i += 1) { if (UpgradeData.unlocks[jamm_i]) { visible_items[n_visible] = jamm_i; n_visible += 1; } }";
        string repairedGirl = """
            if (gear < 0)
            {
                var jamm_un, jamm_ui;
                jamm_un = 18;
                if (instance_exists(UpgradeData)) { jamm_un = array_length_1d(UpgradeData.unlocks); }
                for (jamm_ui = 0; jamm_ui < jamm_un; jamm_ui += 1) { gear_upgrade_scalar[jamm_ui] = 1; }
            }
            else
            {
                var jamm_un2, jamm_ui2, jamm_uglen;
                jamm_un2 = 18;
                if (instance_exists(UpgradeData)) { jamm_un2 = array_length_1d(UpgradeData.unlocks); }
                jamm_uglen = array_length_1d(gear.upgrade_scalar);
                for (jamm_ui2 = 0; jamm_ui2 < jamm_un2; jamm_ui2 += 1) {
                    if (jamm_ui2 < jamm_uglen) { gear_upgrade_scalar[jamm_ui2] = gear.upgrade_scalar[jamm_ui2]; }
                    else { gear_upgrade_scalar[jamm_ui2] = 1; }
                }
            }
            """;
        string repairedGear = """
            var jamm_un, jamm_ui;
            jamm_un = 18;
            if (instance_exists(UpgradeData)) { jamm_un = array_length_1d(UpgradeData.unlocks); }
            for (jamm_ui = 0; jamm_ui < jamm_un; jamm_ui += 1) { upgrade_scalar[jamm_ui] = 1; }
            """;

        // 1. Repaired loops -> no TABLE-LENGTH warnings.
        var repaired = PackAcceptance.Analyze(baseline, [new("M", "m.win", mod)], plan, merged,
            mergedCode: new Dictionary<string, string>
            {
                [upgradeCreate] = nineteenCreate,
                [menuAlarm] = repairedMenu,
                [girlOther] = repairedGirl,
                [gearOther] = repairedGear
            });
        Assert.DoesNotContain(repaired.Risks, r => r.Code == "TABLE-LENGTH");

        // 2. Both Girl copy loop and Gear loop stop at 18 -> missing scalar init only, no crash risk.
        var missingInitOnly = PackAcceptance.Analyze(baseline, [new("M", "m.win", mod)], plan, merged,
            mergedCode: new Dictionary<string, string>
            {
                [upgradeCreate] = nineteenCreate,
                [menuAlarm] = repairedMenu,
                [girlOther] = BuildGirlOther10(resetCount: 19, copyCount: 18),
                [gearOther] = BuildGearOther15(resetCount: 18)
            });
        Assert.Contains(missingInitOnly.Risks, r =>
            r.Code == "TABLE-LENGTH" && r.Target == "UpgradeData/gml_Object_Gear_Other_15"
            && r.Message.Contains("Missing scalar init", StringComparison.Ordinal)
            && r.Message.Contains("no hat-equip crash risk", StringComparison.Ordinal));
        Assert.Contains(missingInitOnly.Risks, r =>
            r.Code == "TABLE-LENGTH" && r.Target == "UpgradeData/gml_Object_Girl_Other_10"
            && r.Message.Contains("Missing scalar init", StringComparison.Ordinal));
        Assert.DoesNotContain(missingInitOnly.Risks, r => r.Message.Contains("Crash risk", StringComparison.Ordinal));

        // 3. Girl copy loop bumped to 19 without bounds check while Gear loop stops at 18 -> crash risk.
        var crashRisk = PackAcceptance.Analyze(baseline, [new("M", "m.win", mod)], plan, merged,
            mergedCode: new Dictionary<string, string>
            {
                [upgradeCreate] = nineteenCreate,
                [menuAlarm] = repairedMenu,
                [girlOther] = BuildGirlOther10(resetCount: 19, copyCount: 19),
                [gearOther] = BuildGearOther15(resetCount: 18)
            });
        Assert.Contains(crashRisk.Risks, r =>
            r.Code == "TABLE-LENGTH" && r.Target == "UpgradeData/gml_Object_Girl_Other_10"
            && r.Message.Contains("Crash risk", StringComparison.Ordinal));
        Assert.Contains(crashRisk.Risks, r =>
            r.Code == "TABLE-LENGTH" && r.Target == "UpgradeData/gml_Object_Gear_Other_15"
            && r.Message.Contains("Missing scalar init", StringComparison.Ordinal));

        // 4. Girl copy loop at 19 WITH array_length_1d(gear.upgrade_scalar) check while Gear loop at 18 -> no crash risk.
        var guardedCopy = PackAcceptance.Analyze(baseline, [new("M", "m.win", mod)], plan, merged,
            mergedCode: new Dictionary<string, string>
            {
                [upgradeCreate] = nineteenCreate,
                [menuAlarm] = repairedMenu,
                [girlOther] = """
                    if (gear < 0)
                    {
                        for (i = 0; i <= 18; i++) { gear_upgrade_scalar[i] = 1; }
                    }
                    else
                    {
                        var glen;
                        glen = array_length_1d(gear.upgrade_scalar);
                        for (i = 0; i <= 18; i++) { if (i < glen) { gear_upgrade_scalar[i] = gear.upgrade_scalar[i]; } else { gear_upgrade_scalar[i] = 1; } }
                    }
                    """,
                [gearOther] = BuildGearOther15(resetCount: 18)
            });
        Assert.DoesNotContain(guardedCopy.Risks, r => r.Message.Contains("Crash risk", StringComparison.Ordinal));
        Assert.Contains(guardedCopy.Risks, r =>
            r.Code == "TABLE-LENGTH" && r.Target == "UpgradeData/gml_Object_Gear_Other_15"
            && r.Message.Contains("Missing scalar init", StringComparison.Ordinal));
    }

    private static void WriteSyntheticUpgradeArchive(
        string path,
        int upgradeCount,
        int girlResetCount,
        int girlCopyCount,
        int gearCount,
        int menuCount,
        bool includeItemsApiAnchors = false,
        Action<UndertaleData>? edit = null)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;

        foreach (string name in new[]
        {
            "UpgradeData", "Girl", "Gear", "UpgradeMenu", "UpgradeCard", "RowHandler", "Handler", "icon_Rods",
            "BoatData", "RodData", "LureData", "ToolData", "GearData", "SaveManager", "Rod", "Boat", "Lure",
            "Analytics", "GameModeMenu", "CardHandler", "Card", "UpgradeSlot", "UpgradeSlotMenu",
            "Boat1", "LureRocket", "Minimap", "Message_Minimap", "Gear0", "Colors", "Fish",
            "Message_TooManyUpgrades", "Message_MoreMoney", "Message_NoDullGlitter", "Message_AttractRepel",
            "Message_NoFishOnRod", "RodCard", "ToolCard", "BoatCard", "LureCard", "Rocket", "DistQuest"
        })
            AddObject(data, name);

        data.GameObjects.ByName("CardHandler")!.ParentId = data.GameObjects.ByName("Handler");
        data.GameObjects.ByName("RowHandler")!.ParentId = data.GameObjects.ByName("Handler");
        data.GameObjects.ByName("UpgradeMenu")!.ParentId = data.GameObjects.ByName("RowHandler");
        data.GameObjects.ByName("UpgradeCard")!.ParentId = data.GameObjects.ByName("Card");

        var sprBoat = AddSprite(data, "spr_noboat");
        var sprLure = AddSprite(data, "spr_lure0");
        var sprRod = AddSprite(data, "spr_rod");
        var sprTools = AddSprite(data, "spr_tools");
        AddSprite(data, "spr_upgradeicons", width: 50, height: 50);
        AddSprite(data, "spr_upgradeicon_bg", width: 50, height: 50);

        CompileObj(data, "UpgradeData", EventType.Create, 0, BuildUpgradeCreateGml(upgradeCount));
        CompileObj(data, "Girl", EventType.Other, 10, BuildGirlOther10(girlResetCount, girlCopyCount));
        CompileObj(data, "Gear", EventType.Other, 15, BuildGearOther15(gearCount));
        CompileObj(data, "RowHandler", EventType.Create, 0, "x = icon_Rods.ORIGIN_X; y = icon_Rods.ORIGIN_Y; DELAY = 2; ORIGIN_X = x; X_GAP = 325; Y_GAP = 85; iCard = 0; alarm[0] = DELAY; done_spawning = 0;");
        CompileObj(data, "UpgradeMenu", EventType.Create, 0, $$"""
            action_inherited();
            Y_GAP = 40;
            for (i = 0; i <= {{menuCount - 1}}; i++)
            {
                spawn_order[i] = i;
            }
            spawns = 0;
            """);
        CompileObj(data, "UpgradeMenu", EventType.Alarm, 0, $$"""
            if (UpgradeData.unlocks[spawn_order[iCard]])
            {
                r = instance_create(x, y, UpgradeCard);
                r.card_id = spawn_order[iCard];
                spawns++;
                if ((spawns % 2) == 0) { x = ORIGIN_X; y += Y_GAP; } else { x += X_GAP; }
            }
            iCard++;
            if (iCard <= {{menuCount - 1}}) { alarm[0] = DELAY; } else { done_spawning = 1; }
            """);

        if (includeItemsApiAnchors)
        {
            foreach (string roomName in new[] { "NewWorld", "room11" })
                data.Rooms.Add(new() { Name = data.Strings.MakeString(roomName) });
            AddScript(data, "gear_equip", "Girl.gear = id; Girl.gear_id = gear_id;");
            AddScript(data, "steam_set_achievement", "return 0;");
            AddScript(data, "steam_inc_stat", "return 0;");
            AddScript(data, "boat_was_unlocked", "steam_set_achievement(\"boat_\" + string(argument0));");
            AddScript(data, "rod_was_unlocked", "steam_set_achievement(\"rod_\" + string(argument0));");
            AddScript(data, "lure_was_purchased", "steam_inc_stat(\"lp_\" + string(argument0 + 1), 1);\nsteam_set_achievement(\"lure_\" + string(argument0));");
            AddScript(data, "tool_was_purchased", "steam_set_achievement(\"tool_\" + string(argument0 + 1));");
            AddScript(data, "upgrade_was_purchased", "with (Rod) { event_perform(ev_other, ev_user0); }\nsteam_set_achievement(\"upg_\" + string(argument0));");

            CompileObj(data, "BoatData", EventType.Create, 0, $"unlocked[0] = 1; cost[0] = 0; name[0] = \"No Boat\"; object[0] = -1; disp[0] = {sprBoat.Name.Content}; desc[0] = \"\"; nItems = array_length_1d(unlocked);");
            CompileObj(data, "RodData", EventType.Create, 0, "nItems = 1; unlocked[0] = 1; cost[0] = 100; cast_speed[0] = 8; rod_power[0] = 1; rod_power_flat[0] = 0; reel_speed[0] = 8; sinker_distance[0] = 1200; name[0] = \"Basic\";");
            CompileObj(data, "LureData", EventType.Create, 0, $"unlocked[0] = 1; cost[0] = 400; name[0] = \"Rocket\"; item[0] = LureRocket; disp[0] = {sprLure.Name.Content}; desc[0] = \"\"; nItems = array_length_1d(unlocked);");
            CompileObj(data, "ToolData", EventType.Create, 0, "nItems = 1; unlocked[0] = 0; cost[0] = 5000; quests[0] = 10; name[0] = \"Minimap\"; item[0] = Minimap; message[0] = Message_Minimap; desc[0] = \"\";");
            CompileObj(data, "GearData", EventType.Create, 0, "n_gear = 1; unlocked[0] = 1; gear[0] = Gear0; value[0] = 1200; weight[0] = 12; range_min[0] = 0; range_max[0] = 0.2; offset_x[0] = 0; offset_y[0] = 0; mythic[0] = 0; map[0] = 0;");
            CompileObj(data, "RodCard", EventType.Other, 11, $"draw_sprite_ext({sprRod.Name.Content}, item_id, x + 42, y + 116, 0.5, 0.5, 45, c_white, 1);");
            CompileObj(data, "ToolCard", EventType.Other, 11, $"draw_sprite_ext({sprTools.Name.Content}, item_id, x + 76, y + 82, 1, 1, 0, c_white, 1);");
            CompileObj(data, "BoatCard", EventType.Other, 11, "draw_sprite_ext(BoatData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);");
            CompileObj(data, "LureCard", EventType.Other, 11, "draw_sprite_ext(LureData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);");
            CompileObj(data, "BoatCard", EventType.Other, 10, "if (BoatData.object[item_id] > 0) { instance_create(0, 0, BoatData.object[item_id]); }");
            CompileObj(data, "ToolCard", EventType.Other, 10, "if (ToolData.unlocked[item_id]) { with (ToolData.item[item_id]) { active = !active; } instance_create(0, 0, ToolData.message[item_id]); }");
            CompileObj(data, "Rod", EventType.Other, 10, "CAST_SPEED = RodData.cast_speed[rod_id]; image_index = rod_id; upgrades[0] = 0; allowUpgrades = 1; if (allowUpgrades) { for (i = 0; i < 8; i++) { if (UpgradeData.upg[rod_id, i] == 1) { upgrades[0] += 1; } } }");
            CompileObj(data, "SaveManager", EventType.Step, (uint)EventSubtypeStep.Step, "Girl.time = current_time;\nif (!loaded) { loaded = 1; }");
            CompileObj(data, "SaveManager", EventType.Other, 13, $$"""
                var sf = 1;
                var lf = 1;
                if (sf > 0 || lf > 0)
                {
                    gear_id = ini_read_real("Main", "gear", -1);
                    if (gear_id >= 0) { gear = instance_create(x, y, GearData.gear[gear_id]); gear.gear_id = gear_id; with (gear) { gear_equip(); } }
                    with (RodData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "rod" + string(i), -1); } }
                    active_rod = ini_read_real("Main", "activerod", -1);
                    if (active_rod >= 0) { Rod.rod_id = active_rod; }
                    with (LureData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "lure" + string(i), -1); } }
                    with (BoatData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "boat" + string(i), -1); } }
                    var bi = ini_read_real("Main", "boat", -1);
                    if (bi > 0 && BoatData.object[bi] > 0) { instance_create(0, 0, BoatData.object[bi]); }
                    with (ToolData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "tool" + string(i), -1); } }
                    with (UpgradeData)
                    {
                        for (i = 0; i <= {{upgradeCount - 1}}; i++) { unlocks[i] = ini_read_real("Upgrades", "unlocks" + string(i), unlocks[i]); }
                        for (ix = 0; ix < 1; ix++) { for (iy = 0; iy < 8; iy++) { upg[ix, iy] = ini_read_real("Upgrades", "upg" + string(ix) + string(iy), upg[ix, iy]); } }
                    }
                    with (GearData) { for (i = 0; i < n_gear; i++) { unlocked[i] = ini_read_real("Hatalog", "hat" + string(i), 0); } }
                }
                else
                {
                    with (Analytics) { event_perform(ev_other, ev_user0); }
                    instance_create(0, 0, GameModeMenu);
                }
                ini_close();
                """);
            CompileObj(data, "SaveManager", EventType.Other, 14, $$"""
                if (!saving_locked)
                {
                    ini_open("s.ini");
                    with (Girl) { ini_write_real("Main", "activerod", Rod.rod_id); if (gear_id >= 0) { ini_write_real("Main", "gear", gear_id); } else { ini_write_real("Main", "gear", -1); } }
                    with (RodData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "rod" + string(i), unlocked[i]); } }
                    with (BoatData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "boat" + string(i), unlocked[i]); } }
                    if (instance_exists(Boat)) { ini_write_real("Main", "boat", Boat.boat_index); } else { ini_write_real("Main", "boat", -1); }
                    with (LureData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "lure" + string(i), unlocked[i]); } }
                    with (ToolData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "tool" + string(i), unlocked[i]); } }
                    with (UpgradeData)
                    {
                        for (i = 0; i <= {{upgradeCount - 1}}; i++) { ini_write_real("Upgrades", "unlocks" + string(i), unlocks[i]); }
                        for (ix = 0; ix < 1; ix++) { for (iy = 0; iy < 8; iy++) { ini_write_real("Upgrades", "upg" + string(ix) + string(iy), upg[ix, iy]); } }
                    }
                    with (GearData) { for (i = 0; i < n_gear; i++) { ini_write_real("Hatalog", "hat" + string(i), unlocked[i]); } }
                    ini_close();
                    ini_open("meta.ini");
                    ini_close();
                }
                """);
            CompileObj(data, "SaveManager", EventType.Other, 15, $$"""
                if (!saving_locked)
                {
                    ini_open("b.ini");
                    with (Girl) { ini_write_real("Main", "activerod", Rod.rod_id); if (gear_id >= 0) { ini_write_real("Main", "gear", gear_id); } else { ini_write_real("Main", "gear", -1); } }
                    with (RodData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "rod" + string(i), unlocked[i]); } }
                    with (BoatData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "boat" + string(i), unlocked[i]); } }
                    with (LureData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "lure" + string(i), unlocked[i]); } }
                    with (ToolData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "tool" + string(i), unlocked[i]); } }
                    with (UpgradeData)
                    {
                        for (i = 0; i <= {{upgradeCount - 1}}; i++) { ini_write_real("Upgrades", "unlocks" + string(i), unlocks[i]); }
                        for (ix = 0; ix < 1; ix++) { for (iy = 0; iy < 8; iy++) { ini_write_real("Upgrades", "upg" + string(ix) + string(iy), upg[ix, iy]); } }
                    }
                    with (GearData) { for (i = 0; i < n_gear; i++) { ini_write_real("Hatalog", "hat" + string(i), unlocked[i]); } }
                    ini_close();
                    ini_open("meta.ini");
                    ini_close();
                }
                """);
        }

        edit?.Invoke(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static string BuildUpgradeCreateGml(int count)
    {
        var lines = new List<string>
        {
            $"for (i = 0; i <= {count - 1}; i++) {{ unlocks[i] = 0; }}"
        };
        for (int i = 0; i < count; i++)
        {
            lines.Add($"name[{i}] = \"U{i}\";");
            lines.Add($"desc[{i}] = \"D{i}\";");
            lines.Add($"desc2[{i}] = \"D2_{i}\";");
            lines.Add($"cost[{i}] = 0.1;");
        }
        lines.Add("for (ix = 0; ix < 1; ix++) { for (iy = 0; iy < 8; iy++) { upg[ix, iy] = -1; } }");
        return string.Join("\n", lines);
    }

    private static string BuildGirlOther10(int resetCount, int copyCount) => $$"""
        if (gear < 0)
        {
            for (i = 0; i <= {{resetCount - 1}}; i++)
            {
                gear_upgrade_scalar[i] = 1;
            }
            gear_lure_scalar[0] = 1;
            gear_lure_scalar[1] = 1;
            gear_lure_scalar[2] = 1;
            gear_lure_scalar[3] = 1;
            gear_lure_scalar[4] = 1;
            gear_lure_scalar[5] = 1;
        }
        else
        {
            for (i = 0; i <= {{copyCount - 1}}; i++)
            {
                gear_upgrade_scalar[i] = gear.upgrade_scalar[i];
            }
            gear_lure_scalar[0] = gear.lure_scalar[0];
            gear_lure_scalar[1] = gear.lure_scalar[1];
            gear_lure_scalar[2] = gear.lure_scalar[2];
            gear_lure_scalar[3] = gear.lure_scalar[3];
            gear_lure_scalar[4] = gear.lure_scalar[4];
        }
        """;

    private static string BuildGearOther15(int resetCount) => $$"""
        for (i = 0; i <= {{resetCount - 1}}; i++)
        {
            upgrade_scalar[i] = 1;
        }
        lure_scalar[0] = 1;
        lure_scalar[1] = 1;
        lure_scalar[2] = 1;
        lure_scalar[3] = 1;
        lure_scalar[4] = 1;
        """;

    private static UndertaleGameObject AddObject(UndertaleData d, string name)
    {
        var existing = d.GameObjects.ByName(name);
        if (existing is not null) return existing;
        var obj = new UndertaleGameObject { Name = d.Strings.MakeString(name) };
        d.GameObjects.Add(obj);
        return obj;
    }

    private static UndertaleSprite AddSprite(UndertaleData d, string name, uint width = 4, uint height = 4)
    {
        var existing = d.Sprites.ByName(name);
        if (existing is not null) return existing;
        var spr = new UndertaleSprite { Name = d.Strings.MakeString(name), Width = width, Height = height };
        d.Sprites.Add(spr);
        return spr;
    }

    private static void AddScript(UndertaleData d, string name, string gml)
    {
        string codeName = "gml_Script_" + name;
        var code = d.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(d, codeName);
        var existing = d.Scripts.ByName(name);
        if (existing is null)
            d.Scripts.Add(new UndertaleScript { Name = d.Strings.MakeString(name), Code = code });
        else
            existing.Code = code;
        CompileCode(d, codeName, gml);
    }

    private static void CompileObj(UndertaleData d, string objName, EventType type, uint subtype, string gml)
    {
        var obj = d.GameObjects.ByName(objName)!;
        var code = obj.EventHandlerFor(type, subtype, d);
        var compiler = new CompileGroup(d);
        compiler.QueueCodeReplace(code, gml);
        var result = compiler.Compile();
        Assert.True(result.Successful, result.PrintAllErrors(true));
    }

    private static void CompileCode(UndertaleData d, string codeName, string gml)
    {
        var code = d.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(d, codeName);
        var compiler = new CompileGroup(d);
        compiler.QueueCodeReplace(code, gml);
        var result = compiler.Compile();
        Assert.True(result.Successful, result.PrintAllErrors(true));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cgfmm-upgrade-loop-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
