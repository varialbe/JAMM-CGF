using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Phase 22c custom upgrades API, draw/limit/effect/girl/gear/save patches, and legacy compatibility tests.</summary>
public sealed class UpgradesRuntimeTests
{
    [Fact]
    public void UpgradeRegisterCallAndInitScriptReplacePlaceholdersAndPlaceController()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string directModPath = temp.PathFor("direct.win");
        string initModPath = temp.PathFor("init.win");

        WriteSyntheticUpgradeGame(basePath);
        WriteSyntheticUpgradeGame(directModPath, edit: d =>
        {
            var spr = AddSprite(d, "spr_direct_upg");
            var scrApply = AddScript(d, "scr_direct_apply", "upgrades[1] += 2 * argument0;");
            AddUpgradePlaceholders(d, "return 999;");
            var code = d.Code.ByName("gml_Object_UpgradeData_Create_0")!;
            string existing = Decompile(d, code);
            Compile(d, code, existing + $"\njamm_upgrade_register(\"direct:turbo\", \"Direct Turbo\", \"Fast\", \"Faster\", 0.5, {spr.Name.Content}, 3, {scrApply.Name.Content}, -1);");
        });
        WriteSyntheticUpgradeGame(initModPath, edit: d =>
        {
            var spr = AddSprite(d, "spr_init_upg");
            var scrApply = AddScript(d, "scr_init_apply", "upgrades[0] += argument0;");
            var scrStep = AddScript(d, "scr_init_step", "var s = argument0; return s;");
            AddUpgradePlaceholders(d, "return 777;");
            AddScript(d, "jamm_init_upgmod", $$"""
                var u, stk;
                u = jamm_upgrade_register("upgmod:diver", "Deep Diver", "Sink fast", "Sinks fast", 0.3, {{spr.Name.Content}}, 2, {{scrApply.Name.Content}}, {{scrStep.Name.Content}});
                stk = jamm_upgrade_stacks("upgmod:diver");
                return u + stk;
                """);
        });

        using var original = GameFileLoader.Load(basePath);
        using var directMod = GameFileLoader.Load(directModPath);
        using var initMod = GameFileLoader.Load(initModPath);

        string directOut = temp.PathFor("direct-merged.win");
        var directBuild = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, directMod, "DirectUpg"), directModPath)], directOut);
        Assert.NotNull(directBuild.Verification);
        using (var merged = GameFileLoader.Load(directOut))
        {
            Assert.NotNull(merged.Data.Scripts.ByName("jamm_api_init"));
            Assert.NotNull(merged.Data.Scripts.ByName("jamm_upgrade_register"));
            Assert.NotNull(merged.Data.Scripts.ByName("jamm_upgrade_stacks"));
            Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_api"));
        }

        string initOut = temp.PathFor("init-merged.win");
        var initBuild = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, initMod, "InitUpg"), initModPath)], initOut);
        Assert.NotNull(initBuild.Verification);

        using var initMerged = GameFileLoader.Load(initOut);
        var controller = initMerged.Data.GameObjects.ByName("obj_jamm_api");
        Assert.NotNull(controller);
        Assert.True(controller.Persistent);
        Assert.Same(controller, initMerged.Data.Rooms.ByName("NewWorld")!.GameObjects[0].ObjectDefinition);
        Assert.Same(controller, initMerged.Data.Rooms.ByName("room11")!.GameObjects[0].ObjectDefinition);

        string ctrlCreate = PatchBuilder.ReadCode(initMerged, "gml_Object_obj_jamm_api_Create_0").Text;
        Assert.Contains("instance_number(object_index) > 1", ctrlCreate);
        Assert.Contains("persistent = true", ctrlCreate);

        string ctrlStep = PatchBuilder.ReadCode(initMerged, "gml_Object_obj_jamm_api_Step_1").Text;
        Assert.Contains("UpgradeData.jamm_api_done", ctrlStep);
        Assert.Contains("Rod.jamm_upg", ctrlStep);
        Assert.Contains("UpgradeData.jamm_step", ctrlStep);
        Assert.Contains("script_execute(", ctrlStep);

        string apiInit = PatchBuilder.ReadCode(initMerged, "gml_Script_jamm_api_init").Text;
        Assert.Contains("obj_jamm_api", apiInit);
        Assert.Contains("jamm_init_upgmod()", apiInit);
        Assert.Contains("with (Rod)", apiInit);

        string upgReg = PatchBuilder.ReadCode(initMerged, "gml_Script_jamm_upgrade_register").Text;
        Assert.DoesNotContain("777", upgReg);
        Assert.Contains("array_length_1d(UpgradeData.unlocks)", upgReg);
        Assert.Contains("jamm_item_find(\"upgrade\"", upgReg);
        Assert.Contains("UpgradeData.disp", upgReg);
        Assert.Contains("UpgradeData.jamm_max_stacks", upgReg);
        Assert.Contains("UpgradeData.jamm_apply", upgReg);
        Assert.Contains("UpgradeData.jamm_step", upgReg);
        Assert.Contains("UpgradeData.jamm_is_api", upgReg);
        Assert.Contains("UpgradeData.nItems", upgReg);
        Assert.Contains("Girl.gear_upgrade_scalar", upgReg);
        Assert.Contains("upgrade_scalar", upgReg);

        string upgStacks = PatchBuilder.ReadCode(initMerged, "gml_Script_jamm_upgrade_stacks").Text;
        Assert.DoesNotContain("777", upgStacks);
        Assert.Contains("Rod.jamm_upg", upgStacks);
        Assert.Contains("UpgradeData.upg", upgStacks);
    }

    [Fact]
    public void UpgradeSpritesAndApplyStepScriptsRelinkAcrossMultipleMods()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modAPath = temp.PathFor("modA.win");
        string modBPath = temp.PathFor("modB.win");
        WriteSyntheticUpgradeGame(basePath);

        WriteSyntheticUpgradeGame(modAPath, edit: d =>
        {
            AddUpgradePlaceholders(d);
            AddSprite(d, "spr_extra_a");
            var sprA = AddSprite(d, "spr_upg_a");
            var appA = AddScript(d, "jmk_a_apply", "upgrades[1] += 1.5 * argument0;");
            var stepA = AddScript(d, "jmk_a_step", "var n = argument0; return n;");
            AddScript(d, "jamm_init_a", $$"""
                jamm_upgrade_register("moda:a1", "Mod A1", "Long A1", "Short A1", 0.4, {{sprA.Name.Content}}, 3, {{appA.Name.Content}}, {{stepA.Name.Content}});
                jamm_upgrade_register("moda:a2", "Mod A2", "Long A2", "Short A2", 0.2, -1, -1, -1, -1);
                """);
        });

        WriteSyntheticUpgradeGame(modBPath, edit: d =>
        {
            AddUpgradePlaceholders(d);
            AddSprite(d, "spr_extra_b1");
            AddSprite(d, "spr_extra_b2");
            var sprB = AddSprite(d, "spr_upg_b");
            AddScript(d, "scr_padding_b", "return 0;");
            var appB = AddScript(d, "jmk_b_apply", "upgrades[13] += 3 * argument0;");
            var stepB = AddScript(d, "jmk_b_step", "var x0 = argument0; return x0;");
            AddScript(d, "jamm_init_b", $$"""
                jamm_upgrade_register("modb:b1", "Mod B1", "Long B1", "Short B1", 0.8, {{sprB.Name.Content}}, 1, {{appB.Name.Content}}, {{stepB.Name.Content}});
                """);
        });

        using var original = GameFileLoader.Load(basePath);
        using var modA = GameFileLoader.Load(modAPath);
        using var modB = GameFileLoader.Load(modBPath);

        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath,
            [new(PatchBuilder.Create(original, modA, "ModA"), modAPath), new(PatchBuilder.Create(original, modB, "ModB"), modBPath)],
            output);
        Assert.NotNull(build.Verification);
        Assert.True(build.SemanticEntries > 0);

        using var merged = GameFileLoader.Load(output);
        string initA = PatchBuilder.ReadCode(merged, "gml_Script_jamm_init_a").Text;
        string initB = PatchBuilder.ReadCode(merged, "gml_Script_jamm_init_b").Text;
        Assert.Contains("spr_upg_a", initA);
        Assert.Contains("jmk_a_apply", initA);
        Assert.Contains("jmk_a_step", initA);
        Assert.Contains("spr_upg_b", initB);
        Assert.Contains("jmk_b_apply", initB);
        Assert.Contains("jmk_b_step", initB);
    }

    [Fact]
    public void UpgradeDrawLimitsEffectsGirlGearSaveAndMenuPagingComposeCleanly()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        WriteSyntheticUpgradeGame(basePath);
        WriteSyntheticUpgradeGame(modPath, edit: d =>
        {
            AddUpgradePlaceholders(d);
            var spr = AddSprite(d, "spr_turbo");
            var app = AddScript(d, "jmk_turbo_apply", "upgrades[1] += 2 * argument0;");
            var step = AddScript(d, "jmk_turbo_step", "var s = argument0; return s;");
            AddScript(d, "jamm_init_turbo",
                $"jamm_upgrade_register(\"demo:turbo\", \"Turbo\", \"Fast reel\", \"Faster reel\", 0.4, {spr.Name.Content}, 3, {app.Name.Content}, {step.Name.Content});");
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "Turbo"), modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);

        foreach (string drawEntry in new[]
        {
            "gml_Object_UpgradeCard_Other_11",
            "gml_Object_UpgradeSlot_Other_11",
            "gml_Object_UpgradeData_Draw_0"
        })
        {
            string drawText = PatchBuilder.ReadCode(merged, drawEntry).Text;
            Assert.Contains("UpgradeData.disp", drawText);
            Assert.Contains("sprite_get_number(spr_upgradeicons)", drawText);
        }

        string cardClick = PatchBuilder.ReadCode(merged, "gml_Object_UpgradeCard_Other_10").Text;
        Assert.Contains("UpgradeData.jamm_is_api", cardClick);
        Assert.Contains("UpgradeData.jamm_max_stacks", cardClick);
        Assert.Contains("Message_TooManyUpgrades", cardClick);

        string rodOther10 = PatchBuilder.ReadCode(merged, "gml_Object_Rod_Other_10").Text;
        Assert.Contains("jamm_upg", rodOther10);
        Assert.Contains("UpgradeData.jamm_apply", rodOther10);
        Assert.Contains("script_execute(", rodOther10);

        string girlRefresh = PatchBuilder.ReadCode(merged, "gml_Object_Girl_Other_10").Text;
        Assert.Contains("array_length_1d(UpgradeData.unlocks)", girlRefresh);
        Assert.Contains("array_length_1d(gear.upgrade_scalar)", girlRefresh);
        Assert.DoesNotContain("<= 17", girlRefresh);

        string gearStats = PatchBuilder.ReadCode(merged, "gml_Object_Gear_Other_15").Text;
        Assert.Contains("array_length_1d(UpgradeData.unlocks)", gearStats);
        Assert.DoesNotContain("<= 17", gearStats);

        string upgAch = PatchBuilder.ReadCode(merged, "gml_Script_upgrade_was_purchased").Text;
        Assert.Contains("UpgradeData.jamm_base_count", upgAch);

        foreach (string saveEntry in new[]
        {
            "gml_Object_SaveManager_Other_13",
            "gml_Object_SaveManager_Other_14",
            "gml_Object_SaveManager_Other_15"
        })
        {
            string saveText = PatchBuilder.ReadCode(merged, saveEntry).Text;
            Assert.Contains("i < jamm_base_count", saveText);
            Assert.Contains("RodData.jamm_base_count", saveText);
            Assert.DoesNotContain("<= 17", saveText);
        }

        string loadScript = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_load").Text;
        Assert.Contains("jamm_rod_refreshed", loadScript);
        Assert.Contains("UpgradeData.jamm_base_count", loadScript);

        string ensureBase = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_ensure_base").Text;
        Assert.Contains("array_length_1d(unlocks)", ensureBase);
        Assert.Contains("jamm_max_stacks", ensureBase);

        string umAlarm = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuAlarm).Text;
        Assert.Contains("UpgradeData.nItems", umAlarm);
    }

    [Fact]
    public void PondweedStyleLegacyUpgrade18AndNumericVariationsWorkEndToEnd()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("pondweed-base.win");
        string modPath = temp.PathFor("api-mod.win");

        WriteSyntheticUpgradeGame(basePath, legacyUpgradeCount: 19, rodLoopCount: 7);
        WriteSyntheticUpgradeGame(modPath, legacyUpgradeCount: 19, rodLoopCount: 7, edit: d =>
        {
            AddUpgradePlaceholders(d);
            var spr = AddSprite(d, "spr_api_u");
            var app = AddScript(d, "jmk_api_apply", "upgrades[0] += argument0;");
            AddScript(d, "jamm_init_api",
                $"jamm_upgrade_register(\"api:deep\", \"Deep\", \"D\", \"D2\", 0.5, {spr.Name.Content}, 2, {app.Name.Content}, -1);");
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        string output = temp.PathFor("pondweed-merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "ApiOnPondweed"), modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);
        Assert.DoesNotContain("<= 18", PatchBuilder.ReadCode(merged, "gml_Object_Girl_Other_10").Text);
        Assert.DoesNotContain("<= 18", PatchBuilder.ReadCode(merged, "gml_Object_Gear_Other_15").Text);
        Assert.DoesNotContain("<= 18", PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_13").Text);
        Assert.DoesNotContain("< 7", PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_13").Text);
        Assert.Contains("UpgradeData.jamm_max_stacks", PatchBuilder.ReadCode(merged, "gml_Object_UpgradeCard_Other_10").Text);
        Assert.Contains("UpgradeData.nItems", PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuAlarm).Text);
    }

    [Fact]
    public void NonApiModsDoNotInjectApiControllerOrUpgradeScripts()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        WriteSyntheticUpgradeGame(basePath);
        WriteSyntheticUpgradeGame(modPath, edit: d => d.GameObjects.ByName("Girl")!.Depth = 10);

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "Plain"), modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);
        Assert.Null(merged.Data.GameObjects.ByName("obj_jamm_api"));
        Assert.Null(merged.Data.Scripts.ByName("jamm_upgrade_register"));
        Assert.Null(merged.Data.Scripts.ByName("jamm_upgrade_stacks"));
    }

    [Fact]
    public void BrokenUpgradeDataLoopInSaveManagerThrowsInvalidDataExceptionNamingEntry()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");

        void BreakSave13(UndertaleData d)
        {
            var code = d.Code.ByName("gml_Object_SaveManager_Other_13")!;
            string text = Decompile(d, code);
            int idx = text.IndexOf("with (UpgradeData)", StringComparison.Ordinal);
            Assert.True(idx >= 0);
            int end = text.IndexOf("with (GearData)", idx, StringComparison.Ordinal);
            Assert.True(end > idx);
            string updated = text[..idx] + "with (UpgradeData) { unlocks[0] = 1; }\n" + text[end..];
            Compile(d, code, updated);
        }

        WriteSyntheticUpgradeGame(basePath, edit: BreakSave13);
        WriteSyntheticUpgradeGame(modPath, edit: d =>
        {
            BreakSave13(d);
            AddUpgradePlaceholders(d);
            AddScript(d, "jamm_init_b", "jamm_upgrade_register(\"b:u\", \"U\", \"D\", \"D2\", 0.1, -1, 1, -1, -1);");
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var ex = Assert.Throws<InvalidDataException>(() =>
            ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "Broken13"), modPath)], temp.PathFor("out.win")));
        Assert.Contains("gml_Object_SaveManager_Other_13", ex.Message);
        Assert.Contains("UpgradeData", ex.Message);
    }

    [Theory]
    [InlineData("gml_Object_UpgradeCard_Other_11", "draw_self();")]
    [InlineData("gml_Object_UpgradeSlot_Other_11", "draw_self();")]
    [InlineData("gml_Object_UpgradeData_Draw_0", "draw_self();")]
    [InlineData("gml_Object_UpgradeCard_Other_10", "allowPurchase = 1;")]
    [InlineData("gml_Object_Girl_Other_10", "gear_upgrade_scalar[0] = 1;")]
    [InlineData("gml_Object_Gear_Other_15", "upgrade_scalar[0] = 1;")]
    [InlineData("gml_Object_SaveManager_Other_13", "var sf = 1; var lf = 1; if (sf > 0 || lf > 0) {\nwith (UpgradeData) { var jamm_x = 1; }\n} else { with (Analytics) {} }")]
    public void MissingUpgradeAnchorsThrowInvalidDataExceptionNamingEntry(string brokenEntry, string replacementCode)
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");

        void Break(UndertaleData d)
        {
            Compile(d, d.Code.ByName(brokenEntry)!, replacementCode);
        }

        WriteSyntheticUpgradeGame(basePath, edit: Break);
        WriteSyntheticUpgradeGame(modPath, edit: d =>
        {
            Break(d);
            AddUpgradePlaceholders(d);
            AddScript(d, "jamm_init_broken", "jamm_upgrade_register(\"x:u\", \"U\", \"D\", \"D2\", 0.1, -1, 1, -1, -1);");
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var ex = Assert.Throws<InvalidDataException>(() =>
            ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "Broken"), modPath)], temp.PathFor("out.win")));
        Assert.Contains(brokenEntry, ex.Message);
    }

    [RealFilesFact]
    public void RealOriginalArchiveWithUpgradeApiModBuildsAndVerifiesCleanly()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string baseHash = BuildFiles.Hash(basePath);
        using var temp = new TemporaryDirectory();
        string modPath = temp.PathFor("real-upgrade-mod.win");
        File.Copy(basePath, modPath);

        using (var modData = UndertaleIO.Read(new FileStream(modPath, FileMode.Open, FileAccess.Read)))
        {
            var spr = new UndertaleSprite
            {
                Name = modData.Strings.MakeString("spr_jamm_test_upg"),
                Width = 50,
                Height = 50
            };
            modData.Sprites.Add(spr);

            var apply = AddScript(modData, "scr_jamm_test_apply", "upgrades[1] += 1.0 * argument0;");
            var step = AddScript(modData, "scr_jamm_test_step", "var s = argument0;");
            AddUpgradePlaceholders(modData);
            AddScript(modData, "jamm_init_realtest",
                $"jamm_upgrade_register(\"realtest:turbo\", \"Real Turbo\", \"Desc\", \"Desc2\", 0.5, {spr.Name.Content}, 3, {apply.Name.Content}, {step.Name.Content});");

            using var outStream = File.Create(modPath + ".tmp");
            UndertaleIO.Write(outStream, modData);
        }
        File.Move(modPath + ".tmp", modPath, overwrite: true);

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "RealUpgradeTest");
        string mergedPath = temp.PathFor("merged-real.win");
        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], mergedPath);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(mergedPath);
        Assert.NotNull(merged.Data.GameObjects.ByName("obj_jamm_api"));
        Assert.Contains("UpgradeData.disp", PatchBuilder.ReadCode(merged, "gml_Object_UpgradeCard_Other_11").Text);
        Assert.Contains("UpgradeData.jamm_max_stacks", PatchBuilder.ReadCode(merged, "gml_Object_UpgradeCard_Other_10").Text);
        Assert.Contains("UpgradeData.jamm_apply", PatchBuilder.ReadCode(merged, "gml_Object_Rod_Other_10").Text);
        Assert.Equal(baseHash, BuildFiles.Hash(basePath));
    }

    private static void WriteSyntheticUpgradeGame(
        string path,
        int legacyUpgradeCount = 18,
        int rodLoopCount = 6,
        Action<UndertaleData>? edit = null)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;

        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babySmall"), DisplayName = data.Strings.MakeString("Small") });
        AddScript(data, "draw_text_shadowed", "draw_text(argument0, argument1, argument2);");
        AddScript(data, "draw_text_shadowed2", "draw_text(argument0, argument1, argument2);");
        AddScript(data, "number_to_dollar_string", "return \"$\" + string(argument0);");
        AddScript(data, "fish_object_to_name", "return \"Fish\";");

        foreach (string name in new[] { "NewWorld", "room11" })
            data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });

        foreach (string name in new[]
        {
            "fish", "BoatData", "RodData", "LureData", "ToolData", "GearData", "UpgradeData",
            "SaveManager", "Girl", "Rod", "Boat", "Lure", "Gear", "Analytics", "GameModeMenu",
            "Handler", "CardHandler", "RowHandler", "Card", "UpgradeMenu", "UpgradeCard", "UpgradeSlot",
            "icon_Rods", "Colors", "Fish", "Message_TooManyUpgrades", "Message_MoreMoney",
            "Message_NoDullGlitter", "Message_AttractRepel", "Message_NoFishOnRod",
            "RodCard", "ToolCard", "BoatCard", "LureCard", "Rocket", "DistQuest", "UpgradeSlotMenu",
            "Boat1", "LureRocket", "Minimap", "Message_Minimap", "Gear0"
        })
            AddObject(data, name);

        data.GameObjects.ByName("CardHandler")!.ParentId = data.GameObjects.ByName("Handler");
        data.GameObjects.ByName("RowHandler")!.ParentId = data.GameObjects.ByName("Handler");
        data.GameObjects.ByName("UpgradeMenu")!.ParentId = data.GameObjects.ByName("RowHandler");
        data.GameObjects.ByName("UpgradeCard")!.ParentId = data.GameObjects.ByName("Card");

        var sprRod = AddSprite(data, "spr_rod");
        var sprTools = AddSprite(data, "spr_tools");
        var sprBoat0 = AddSprite(data, "spr_noboat");
        var sprLure0 = AddSprite(data, "spr_lure0");
        AddSprite(data, "spr_upgradeicons", width: 50, height: 50);
        AddSprite(data, "spr_upgradeicon_bg", width: 50, height: 50);

        AddScript(data, "gear_equip", "Girl.gear = id; Girl.gear_id = gear_id;");
        AddScript(data, "steam_set_achievement", "return 0;");
        AddScript(data, "steam_inc_stat", "return 0;");
        AddScript(data, "start_quest_dist", "dist[0] = 1; dpth[0] = 1; q = instance_create(0, 0, DistQuest); q.goal = dist[Girl.bestRod]; q.goal = dpth[Girl.bestRod]; return q;");
        AddScript(data, "start_quest_rods", "var index = Girl.bestRod + 1; q = instance_create(0, 0, DistQuest); q.cash = min(RodData.cost[index] / 3, 1000); return q;");
        AddScript(data, "boat_was_unlocked", "steam_set_achievement(\"boat_\" + string(argument0));");
        AddScript(data, "rod_was_unlocked", "steam_set_achievement(\"rod_\" + string(argument0));");
        AddScript(data, "lure_was_purchased", "steam_inc_stat(\"lp_\" + string(argument0 + 1), 1);\nsteam_set_achievement(\"lure_\" + string(argument0));");
        AddScript(data, "tool_was_purchased", "steam_set_achievement(\"tool_\" + string(argument0 + 1));");
        AddScript(data, "upgrade_was_purchased", "with (Rod) { event_perform(ev_other, ev_user0); }\nsteam_set_achievement(\"upg_\" + string(argument0));");
        AddScript(data, "calculate_upgrade_cost", "var cost = (UpgradeData.cost[argument0] * RodData.cost[argument1] * Girl.gear_upgrade_scalar[argument0]) / max(1, argument1);\nreturn max(300, ceil(cost / 100) * 100);");

        int maxUpgId = legacyUpgradeCount - 1;
        CompileObj(data, "BoatData", EventType.Create, 0, $"unlocked[0] = 1; cost[0] = 0; name[0] = \"No Boat\"; object[0] = -1; disp[0] = {sprBoat0.Name.Content}; desc[0] = \"\"; nItems = array_length_1d(unlocked);");
        CompileObj(data, "RodData", EventType.Create, 0, "nItems = 1; unlocked[0] = 1; cost[0] = 100; cast_speed[0] = 8; rod_power[0] = 1; rod_power_flat[0] = 0; reel_speed[0] = 8; sinker_distance[0] = 1200; name[0] = \"Basic\";");
        CompileObj(data, "LureData", EventType.Create, 0, $"unlocked[0] = 1; cost[0] = 400; name[0] = \"Rocket\"; item[0] = LureRocket; disp[0] = {sprLure0.Name.Content}; desc[0] = \"\"; nItems = array_length_1d(unlocked);");
        CompileObj(data, "ToolData", EventType.Create, 0, "nItems = 1; unlocked[0] = 0; cost[0] = 5000; quests[0] = 10; name[0] = \"Minimap\"; item[0] = Minimap; message[0] = Message_Minimap; desc[0] = \"\";");
        CompileObj(data, "GearData", EventType.Create, 0, "n_gear = 1; unlocked[0] = 1; gear[0] = Gear0; value[0] = 1200; weight[0] = 12; range_min[0] = 0; range_max[0] = 0.2; offset_x[0] = 0; offset_y[0] = 0; mythic[0] = 0; map[0] = 0;");
        CompileObj(data, "UpgradeData", EventType.Create, 0, $$"""
            for (i = 0; i <= {{maxUpgId}}; i++)
            {
                unlocks[i] = 0;
                name[i] = "U" + string(i);
                desc[i] = "Desc";
                desc2[i] = "Desc2";
                cost[i] = 0.1;
            }
            unlocks[1] = 1;
            for (ix = 0; ix < {{rodLoopCount}}; ix++)
            {
                for (iy = 0; iy < 8; iy++)
                {
                    upg[ix, iy] = -1;
                    object[ix, iy] = -1;
                }
            }
            """);

        CompileObj(data, "CardHandler", EventType.Create, 0, "x = icon_Rods.ORIGIN_X; y = icon_Rods.ORIGIN_Y; DELAY = 2; ORIGIN_X = x; ORIGIN_Y = y; X_GAP = 160; Y_GAP = 230; iCard = 0; alarm[0] = DELAY; done_spawning = 0; card_width = 3;");
        CompileObj(data, "CardHandler", EventType.Alarm, 0, "r = instance_create(x, y, card_type); r.item_id = iCard; iCard++; if (iCard < data_object.nItems) { alarm[0] = DELAY; } else { done_spawning = 1; }");
        CompileObj(data, "RowHandler", EventType.Create, 0, "x = icon_Rods.ORIGIN_X; y = icon_Rods.ORIGIN_Y; DELAY = 2; ORIGIN_X = x; X_GAP = 325; Y_GAP = 85; iCard = 0; alarm[0] = DELAY; done_spawning = 0;");
        CompileObj(data, "UpgradeMenu", EventType.Create, 0, $$"""
            action_inherited();
            Y_GAP = 40;
            for (i = 0; i <= {{maxUpgId}}; i++)
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
            if (iCard <= {{maxUpgId}}) { alarm[0] = DELAY; } else { done_spawning = 1; }
            """);

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
                    for (i = 0; i <= {{maxUpgId}}; i++)
                    {
                        unlocks[i] = ini_read_real("Upgrades", "unlocks" + string(i), unlocks[i]);
                    }
                    for (ix = 0; ix < {{rodLoopCount}}; ix++)
                    {
                        for (iy = 0; iy < 8; iy++)
                        {
                            upg[ix, iy] = ini_read_real("Upgrades", "upg" + string(ix) + string(iy), upg[ix, iy]);
                            object[ix, iy] = ini_read_real("Upgrades", "obj" + string(ix) + string(iy), -1);
                        }
                    }
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
                with (Girl)
                {
                    ini_write_real("Main", "activerod", Rod.rod_id);
                    if (gear_id >= 0) { ini_write_real("Main", "gear", gear_id); } else { ini_write_real("Main", "gear", -1); }
                }
                with (RodData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "rod" + string(i), unlocked[i]); } }
                with (BoatData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "boat" + string(i), unlocked[i]); } }
                if (instance_exists(Boat)) { ini_write_real("Main", "boat", Boat.boat_index); } else { ini_write_real("Main", "boat", -1); }
                with (LureData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "lure" + string(i), unlocked[i]); } }
                with (ToolData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "tool" + string(i), unlocked[i]); } }
                with (UpgradeData)
                {
                    for (i = 0; i <= {{maxUpgId}}; i++)
                    {
                        ini_write_real("Upgrades", "unlocks" + string(i), unlocks[i]);
                    }
                    for (ix = 0; ix < {{rodLoopCount}}; ix++)
                    {
                        for (iy = 0; iy < 8; iy++)
                        {
                            ini_write_real("Upgrades", "upg" + string(ix) + string(iy), upg[ix, iy]);
                            ini_write_real("Upgrades", "obj" + string(ix) + string(iy), object[ix, iy]);
                        }
                    }
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
                with (Girl)
                {
                    ini_write_real("Main", "activerod", Rod.rod_id);
                    if (gear_id >= 0) { ini_write_real("Main", "gear", gear_id); } else { ini_write_real("Main", "gear", -1); }
                }
                with (RodData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "rod" + string(i), unlocked[i]); } }
                with (BoatData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "boat" + string(i), unlocked[i]); } }
                with (LureData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "lure" + string(i), unlocked[i]); } }
                with (ToolData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "tool" + string(i), unlocked[i]); } }
                with (UpgradeData)
                {
                    for (i = 0; i <= {{maxUpgId}}; i++)
                    {
                        ini_write_real("Upgrades", "unlocks" + string(i), unlocks[i]);
                    }
                    for (ix = 0; ix < {{rodLoopCount}}; ix++)
                    {
                        for (iy = 0; iy < 8; iy++)
                        {
                            ini_write_real("Upgrades", "upg" + string(ix) + string(iy), upg[ix, iy]);
                            ini_write_real("Upgrades", "obj" + string(ix) + string(iy), object[ix, iy]);
                        }
                    }
                }
                with (GearData) { for (i = 0; i < n_gear; i++) { ini_write_real("Hatalog", "hat" + string(i), unlocked[i]); } }
                ini_close();
                ini_open("meta.ini");
                ini_close();
            }
            """);

        CompileObj(data, "RodCard", EventType.Other, 11, $"draw_sprite_ext({sprRod.Name.Content}, item_id, x + 42, y + 116, 0.5, 0.5, 45, c_white, 1);");
        CompileObj(data, "ToolCard", EventType.Other, 11, $"draw_sprite_ext({sprTools.Name.Content}, item_id, x + 76, y + 82, 1, 1, 0, c_white, 1);");
        CompileObj(data, "BoatCard", EventType.Other, 11, "draw_sprite_ext(BoatData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);");
        CompileObj(data, "LureCard", EventType.Other, 11, "draw_sprite_ext(LureData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);");
        CompileObj(data, "BoatCard", EventType.Other, 10, "if (BoatData.object[item_id] > 0) { instance_create(0, 0, BoatData.object[item_id]); }");
        CompileObj(data, "ToolCard", EventType.Other, 10, "if (ToolData.unlocked[item_id]) { with (ToolData.item[item_id]) { active = !active; } instance_create(0, 0, ToolData.message[item_id]); }");
        CompileObj(data, "Rocket", EventType.Step, (uint)EventSubtypeStep.EndStep, "if (Lure.state == 1) { target_distance[Rod.rod_id] += Girl.x; }");
        CompileObj(data, "Rod", EventType.KeyPress, 88, """
            if (!instance_exists(Lure))
            {
                if (state == 0 && Lure.state == 0)
                {
                    rid = rod_id + 1;
                    while (rid < RodData.nItems)
                    {
                        if (RodData.unlocked[rid])
                        {
                            rod_id = rid;
                            event_perform(ev_other, ev_user0);
                            break;
                        }
                        rid--;
                    }
                }
            }
            """);

        CompileObj(data, "Rod", EventType.Other, 10, """
            CAST_SPEED = RodData.cast_speed[rod_id];
            image_index = rod_id;
            upgrades[0] = 0;
            upgrades[1] = 0;
            upgrades[13] = 0;
            allowUpgrades = 1;
            if (allowUpgrades)
            {
                for (i = 0; i < 8; i++)
                {
                    if (UpgradeData.upg[rod_id, i] == 1) { upgrades[0] += 1; }
                }
            }
            with (Fish) { base_lunge_mod = 1; base_lunge_offset = 0; }
            """);

        CompileObj(data, "UpgradeCard", EventType.Other, 11, """
            icon_scale = 0.5;
            icon_x = x + 5;
            icon_y = y + 5;
            draw_sprite_ext(spr_upgradeicon_bg, 0, icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);
            draw_sprite_ext(spr_upgradeicons, 1 + card_id, icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);
            """);

        CompileObj(data, "UpgradeSlot", EventType.Other, 11, """
            if (UpgradeData.upg[Rod.rod_id, slot_id] != -1)
            {
                icon_scale = 0.5;
                icon_x = (x + sprite_width) - 35;
                icon_y = (y + sprite_height) - 45;
                draw_sprite_ext(spr_upgradeicon_bg, 0, icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);
                draw_sprite_ext(spr_upgradeicons, 1 + UpgradeData.upg[Rod.rod_id, slot_id], icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);
            }
            """);

        CompileObj(data, "UpgradeData", EventType.Draw, 0, """
            if (Rod.allowUpgrades && view_current == 0 && !Girl.hide_ui)
            {
                icon_scale = 0.5;
                for (i = 0; i < 8; i++)
                {
                    if (UpgradeData.upg[Rod.rod_id, i] < -1) { break; }
                    icon_x = 10 + ((i % 3) * 30);
                    icon_y = 445 - (floor(i / 3) * 30);
                    draw_sprite_ext(spr_upgradeicons, 1 + UpgradeData.upg[Rod.rod_id, i], icon_x, icon_y, icon_scale, icon_scale, 0, c_white, 1);
                }
            }
            """);

        string extraCase = legacyUpgradeCount > 18
            ? "\n    case 18:\n        if (Rod.upgrades[0] >= 1) { allowPurchase = 0; }\n        break;"
            : "";
        CompileObj(data, "UpgradeCard", EventType.Other, 10, $$"""
            action_inherited();
            cost = calculate_upgrade_cost(card_id, Rod.rod_id);
            allowPurchase = 1;
            switch (card_id)
            {
                case 0:
                    if (Rod.upgrades[0] >= 1) { allowPurchase = 0; }
                    break;{{extraCase}}
            }
            if (!allowPurchase)
            {
                instance_create(0, 0, Message_TooManyUpgrades);
            }
            else if (Girl.dollars >= cost)
            {
                Girl.dollars -= cost;
                UpgradeData.upg[Rod.rod_id, Rod.slot_id] = card_id;
                upgrade_was_purchased(card_id);
            }
            else
            {
                allowPurchase = 0;
                instance_create(0, 0, Message_MoreMoney);
            }
            with (UpgradeMenu) { instance_destroy(); }
            if (allowPurchase) { instance_create(0, 0, UpgradeSlotMenu); }
            """);

        CompileObj(data, "Girl", EventType.Other, 10, $$"""
            if (gear < 0)
            {
                for (i = 0; i <= {{maxUpgId}}; i++)
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
                for (i = 0; i <= {{maxUpgId}}; i++)
                {
                    gear_upgrade_scalar[i] = gear.upgrade_scalar[i];
                }
                gear_lure_scalar[0] = gear.lure_scalar[0];
                gear_lure_scalar[1] = gear.lure_scalar[1];
                gear_lure_scalar[2] = gear.lure_scalar[2];
                gear_lure_scalar[3] = gear.lure_scalar[3];
                gear_lure_scalar[4] = gear.lure_scalar[4];
            }
            """);

        CompileObj(data, "Gear", EventType.Other, 15, $$"""
            for (i = 0; i <= {{maxUpgId}}; i++)
            {
                upgrade_scalar[i] = 1;
            }
            lure_scalar[0] = 1;
            lure_scalar[1] = 1;
            lure_scalar[2] = 1;
            lure_scalar[3] = 1;
            lure_scalar[4] = 1;
            """);

        edit?.Invoke(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static UndertaleGameObject AddObject(UndertaleData d, string name)
    {
        var existing = d.GameObjects.ByName(name);
        if (existing is not null) return existing;
        var obj = new UndertaleGameObject { Name = d.Strings.MakeString(name) };
        d.GameObjects.Add(obj);
        return obj;
    }

    private static UndertaleSprite AddSprite(UndertaleData d, string name, uint width = 1, uint height = 1)
    {
        var existing = d.Sprites.ByName(name);
        if (existing is not null) return existing;
        var spr = new UndertaleSprite { Name = d.Strings.MakeString(name), Width = width, Height = height };
        d.Sprites.Add(spr);
        return spr;
    }

    private static void AddUpgradePlaceholders(UndertaleData d, string body = "return -1;")
    {
        foreach (string name in new[] { "jamm_upgrade_register", "jamm_upgrade_stacks", "jamm_item_find" })
            AddScript(d, name, body);
    }

    private static UndertaleScript AddScript(UndertaleData d, string name, string gml)
    {
        string codeName = "gml_Script_" + name;
        var code = d.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(d, codeName);
        var existing = d.Scripts.ByName(name);
        if (existing is null)
        {
            existing = new UndertaleScript { Name = d.Strings.MakeString(name), Code = code };
            d.Scripts.Add(existing);
        }
        else existing.Code = code;
        Compile(d, code, gml);
        return existing;
    }

    private static void CompileObj(UndertaleData d, string objName, EventType type, uint subtype, string gml)
    {
        var obj = d.GameObjects.ByName(objName) ?? throw new InvalidOperationException(objName);
        var code = obj.EventHandlerFor(type, subtype, d);
        Compile(d, code, gml);
    }

    private static void Compile(UndertaleData d, UndertaleCode code, string gml)
    {
        var compiler = new CompileGroup(d);
        compiler.QueueCodeReplace(code, gml);
        var result = compiler.Compile();
        Assert.True(result.Successful, result.PrintAllErrors(true));
    }

    private static string Decompile(UndertaleData d, UndertaleCode code)
        => new Underanalyzer.Decompiler.DecompileContext(new UndertaleModLib.Decompiler.GlobalDecompileContext(d), code).DecompileToString();

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cgfmm-upgrades-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
