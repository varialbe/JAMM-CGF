using System.Globalization;
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
        Assert.Contains("array_length_1d(UpgradeData.unlocks)", umAlarm);
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
        Assert.Contains("array_length_1d(UpgradeData.unlocks)", PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuAlarm).Text);
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

    [Fact]
    public void RodSlotPolicyBuildsAndRaisesColumnBoundsAcrossRuntimeAndSaves()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");

        WriteSyntheticUpgradeGame(basePath, edit: d =>
        {
            CompileObj(d, "RodData", EventType.Create, 0, """
                nItems = 6;
                for (i = 0; i < 6; i++)
                {
                    unlocked[i] = 1;
                    cost[i] = 100 * (i + 1);
                    cast_speed[i] = 8 + i;
                    rod_power[i] = 1 + (0.2 * i);
                    rod_power_flat[i] = i;
                    reel_speed[i] = 8 + i;
                    sinker_distance[i] = 1200 + (500 * i);
                    name[i] = "Rod" + string(i);
                }
                """);
            CompileObj(d, "UpgradeData", EventType.Create, 0, """
                for (i = 0; i <= 17; i++)
                {
                    unlocks[i] = 1;
                    name[i] = "U" + string(i);
                    desc[i] = "Desc";
                    desc2[i] = "Desc2";
                    cost[i] = 0.1;
                }
                for (ix = 0; ix < 6; ix++)
                {
                    for (iy = 0; iy < 8; iy++)
                    {
                        if (iy < ix + 3) { upg[ix, iy] = -1; } else { upg[ix, iy] = -2; }
                        object[ix, iy] = -1;
                    }
                }
                """);
        });

        WriteSyntheticUpgradeGame(modPath, edit: d =>
        {
            CompileObj(d, "RodData", EventType.Create, 0, """
                nItems = 6;
                for (i = 0; i < 6; i++)
                {
                    unlocked[i] = 1;
                    cost[i] = 100 * (i + 1);
                    cast_speed[i] = 8 + i;
                    rod_power[i] = 1 + (0.2 * i);
                    rod_power_flat[i] = i;
                    reel_speed[i] = 8 + i;
                    sinker_distance[i] = 1200 + (500 * i);
                    name[i] = "Rod" + string(i);
                }
                """);
            CompileObj(d, "UpgradeData", EventType.Create, 0, """
                for (i = 0; i <= 17; i++)
                {
                    unlocks[i] = 1;
                    name[i] = "U" + string(i);
                    desc[i] = "Desc";
                    desc2[i] = "Desc2";
                    cost[i] = 0.1;
                }
                for (ix = 0; ix < 6; ix++)
                {
                    for (iy = 0; iy < 8; iy++)
                    {
                        if (iy < ix + 3) { upg[ix, iy] = -1; } else { upg[ix, iy] = -2; }
                        object[ix, iy] = -1;
                    }
                }
                """);
            AddUpgradePlaceholders(d);
            AddScript(d, "jamm_rod_register", "return -1;");
            var spr = AddSprite(d, "spr_mod_rod");
            AddScript(d, "jamm_init_rods", $$"""
                jamm_rod_register("rods:twelve", "Twelve Rod", 50000, 14, 2.0, 4, 11, 5000, {{spr.Name.Content}}, 12, 1);
                jamm_rod_register("rods:prog1", "Prog Rod 1", 60000, 15, 2.1, 5, 12, 6000, {{spr.Name.Content}}, -1, 1);
                jamm_rod_register("rods:prog2", "Prog Rod 2", 70000, 16, 2.2, 6, 13, 7000, {{spr.Name.Content}}, -1, 1);
                """);
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "RodSlots"), modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);

        string rodReg = PatchBuilder.ReadCode(merged, "gml_Script_jamm_rod_register").Text;
        Assert.Contains("jamm_slots < -1 || jamm_slots > 16", rodReg);
        Assert.Contains("RodData.jamm_slots", rodReg);
        Assert.Contains("UpgradeData.jamm_max_slots", rodReg);
        Assert.Contains("min(16,", rodReg);

        string ensureBase = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_ensure_base").Text;
        Assert.Contains("jamm_max_slots", ensureBase);
        Assert.Contains("RodData.jamm_slots", ensureBase);
        Assert.DoesNotContain("< 8", ensureBase);

        string upgStacks = PatchBuilder.ReadCode(merged, "gml_Script_jamm_upgrade_stacks").Text;
        Assert.DoesNotContain("min(8", upgStacks);

        string saveScript = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_save").Text;
        Assert.DoesNotContain("< 8", saveScript);

        string loadScript = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_load").Text;
        Assert.DoesNotContain("< 8", loadScript);

        string rodOther10 = PatchBuilder.ReadCode(merged, "gml_Object_Rod_Other_10").Text;
        Assert.Contains("array_length_2d(UpgradeData.upg, rod_id)", rodOther10);
        Assert.DoesNotContain("i < 8", rodOther10);
        Assert.DoesNotContain("min(8", rodOther10);

        string upgDraw = PatchBuilder.ReadCode(merged, "gml_Object_UpgradeData_Draw_0").Text;
        Assert.Contains("array_length_2d(UpgradeData.upg, Rod.rod_id)", upgDraw);
        Assert.DoesNotContain("i < 8", upgDraw);

        string cardClick = PatchBuilder.ReadCode(merged, "gml_Object_UpgradeCard_Other_10").Text;
        Assert.DoesNotContain("min(8", cardClick);

        foreach (string saveEntry in new[]
        {
            "gml_Object_SaveManager_Other_13",
            "gml_Object_SaveManager_Other_14",
            "gml_Object_SaveManager_Other_15"
        })
        {
            string saveText = PatchBuilder.ReadCode(merged, saveEntry).Text;
            Assert.Contains("min(10, array_length_2d(upg, ix))", saveText);
            Assert.DoesNotContain("iy < 8", saveText);
        }
    }

    [Fact]
    public void RodSlotPolicyRuntimeScriptsEnforceLimitsProgressionAndLegacyNineColumnPersistence()
    {
        var scripts = ItemsRuntimeScripts.BuildScripts(null, null);

        // 1. Vanilla 6 rods (slots 3..8): explicit 12 accepted, 17 rejected, -1 after vanilla -> 9, two -1 rods -> 9 then 10.
        var vm = CreateVanillaVm(scripts, includeTgeNineColRod: false);
        Assert.Equal(1.0, vm.Call("jamm_items_ensure_base"));
        Assert.Equal(8.0, vm.Get2DMaxRowCols("UpgradeData", "jamm_max_slots"));

        // Invalid slot counts rejected
        Assert.Equal(-1.0, vm.Call("jamm_rod_register", "test:too_many", "Bad 17", 1000.0, 10.0, 1.5, 1.0, 9.0, 2000.0, -1.0, 17.0, 1.0));
        Assert.Equal(-1.0, vm.Call("jamm_rod_register", "test:neg2", "Bad -2", 1000.0, 10.0, 1.5, 1.0, 9.0, 2000.0, -1.0, -2.0, 1.0));
        Assert.Equal(-1.0, vm.Call("jamm_rod_register", "test:frac", "Bad 9.5", 1000.0, 10.0, 1.5, 1.0, 9.0, 2000.0, -1.0, 9.5, 1.0));
        Assert.Equal(6.0, vm.GetReal("RodData", "nItems"));

        // Two -1 rods after vanilla -> 9 then 10
        double prog1 = vm.Call("jamm_rod_register", "test:prog1", "Prog 1", 20000.0, 14.0, 2.0, 4.0, 11.0, 5000.0, 10.0, -1.0, 1.0);
        double prog2 = vm.Call("jamm_rod_register", "test:prog2", "Prog 2", 30000.0, 15.0, 2.1, 5.0, 12.0, 6000.0, 10.0, -1.0, 1.0);
        Assert.Equal(6.0, prog1);
        Assert.Equal(7.0, prog2);
        Assert.Equal(9.0, vm.GetArray1D("RodData", "jamm_slots", 6));
        Assert.Equal(10.0, vm.GetArray1D("RodData", "jamm_slots", 7));
        Assert.Equal(9, vm.GetRowLength2D("UpgradeData", "upg", 6));
        Assert.Equal(10, vm.GetRowLength2D("UpgradeData", "upg", 7));
        Assert.Equal(10.0, vm.GetReal("UpgradeData", "jamm_max_slots"));

        // Explicit 12 slots accepted; -1 after it -> 13; explicit 16 -> 16; -1 after 16 clamps to 16
        double r12 = vm.Call("jamm_rod_register", "test:r12", "Rod 12", 40000.0, 16.0, 2.2, 5.0, 12.0, 7000.0, 10.0, 12.0, 1.0);
        Assert.Equal(8.0, r12);
        Assert.Equal(12.0, vm.GetArray1D("RodData", "jamm_slots", 8));
        Assert.Equal(12, vm.GetRowLength2D("UpgradeData", "upg", 8));
        Assert.Equal(-1.0, vm.GetArray2D("UpgradeData", "upg", 8, 11));
        Assert.Equal(12.0, vm.GetReal("UpgradeData", "jamm_max_slots"));

        double r13 = vm.Call("jamm_rod_register", "test:r13", "Rod 13", 50000.0, 16.0, 2.2, 5.0, 12.0, 7000.0, 10.0, -1.0, 1.0);
        Assert.Equal(13.0, vm.GetArray1D("RodData", "jamm_slots", (int)r13));
        double r16 = vm.Call("jamm_rod_register", "test:r16", "Rod 16", 60000.0, 17.0, 2.3, 6.0, 13.0, 8000.0, 10.0, 16.0, 1.0);
        Assert.Equal(16.0, vm.GetArray1D("RodData", "jamm_slots", (int)r16));
        double rClamped = vm.Call("jamm_rod_register", "test:rclamped", "Rod Clamped", 70000.0, 18.0, 2.4, 6.0, 14.0, 9000.0, 10.0, -1.0, 1.0);
        Assert.Equal(16.0, vm.GetArray1D("RodData", "jamm_slots", (int)rClamped));

        // 2. Save/load sidecar round-trips column 8+ (including slotobj on column 11)
        vm.SetArray2D("UpgradeData", "upg", 8, 8, 3.0); // vanilla upgrade 3 in col 8
        vm.SetArray2D("UpgradeData", "upg", 8, 11, 4.0); // Attract in col 11
        vm.SetArray2D("UpgradeData", "object", 8, 11, 42.0); // Goldfish object id 42
        vm.Call("jamm_items_save");
        Assert.Equal("base:upgrade3", vm.IniString("JAMM_Items", "slot:test:r12:8"));
        Assert.Equal("base:upgrade4", vm.IniString("JAMM_Items", "slot:test:r12:11"));
        Assert.Equal("Goldfish", vm.IniString("JAMM_Items", "slotobj:test:r12:11"));

        // Clear in-memory slots, then load from sidecar
        vm.SetArray2D("UpgradeData", "upg", 8, 8, -1.0);
        vm.SetArray2D("UpgradeData", "upg", 8, 11, -1.0);
        vm.SetArray2D("UpgradeData", "object", 8, 11, -1.0);
        vm.Call("jamm_items_load");
        Assert.Equal(3.0, vm.GetArray2D("UpgradeData", "upg", 8, 8));
        Assert.Equal(4.0, vm.GetArray2D("UpgradeData", "upg", 8, 11));
        Assert.Equal(42.0, vm.GetArray2D("UpgradeData", "object", 8, 11));

        // 3. Legacy 9-column row (TGE Proto-Rod at rod 6, upg[6, 0..8])
        var tgeVm = CreateVanillaVm(scripts, includeTgeNineColRod: true);
        Assert.Equal(1.0, tgeVm.Call("jamm_items_ensure_base"));
        Assert.Equal(9.0, tgeVm.GetArray1D("RodData", "jamm_slots", 6));
        Assert.Equal(9.0, tgeVm.GetReal("UpgradeData", "jamm_max_slots"));

        // -1 after TGE's 9-slot Proto-Rod -> 10
        double afterTge = tgeVm.Call("jamm_rod_register", "test:after_tge", "After TGE", 55000.0, 15.0, 2.1, 5.0, 12.0, 6000.0, 10.0, -1.0, 1.0);
        Assert.Equal(7.0, afterTge);
        Assert.Equal(10.0, tgeVm.GetArray1D("RodData", "jamm_slots", 7));

        // Legacy reset (no [JAMM_Items] section): col 8 of Proto-Rod survives untouched!
        tgeVm.SetArray2D("UpgradeData", "upg", 6, 8, 3.0);
        tgeVm.SetArray2D("UpgradeData", "object", 6, 8, -1.0);
        tgeVm.ClearIni();
        tgeVm.Call("jamm_items_load");
        Assert.Equal(3.0, tgeVm.GetArray2D("UpgradeData", "upg", 6, 8));

        // Partial [JAMM_Items] load (missing slot:base:rod6:8): col 8 survives untouched!
        tgeVm.SetIni("JAMM_Items", "eq:rod", "base:rod6");
        tgeVm.Call("jamm_items_load");
        Assert.Equal(3.0, tgeVm.GetArray2D("UpgradeData", "upg", 6, 8));

        // Full [JAMM_Items] save/load round-trips col 8 of Proto-Rod
        tgeVm.SetArray2D("UpgradeData", "upg", 6, 8, 4.0);
        tgeVm.SetArray2D("UpgradeData", "object", 6, 8, 42.0);
        tgeVm.Call("jamm_items_save");
        Assert.Equal("base:upgrade4", tgeVm.IniString("JAMM_Items", "slot:base:rod6:8"));
        Assert.Equal("Goldfish", tgeVm.IniString("JAMM_Items", "slotobj:base:rod6:8"));
        tgeVm.SetArray2D("UpgradeData", "upg", 6, 8, -1.0);
        tgeVm.SetArray2D("UpgradeData", "object", 6, 8, -1.0);
        tgeVm.Call("jamm_items_load");
        Assert.Equal(4.0, tgeVm.GetArray2D("UpgradeData", "upg", 6, 8));
        Assert.Equal(42.0, tgeVm.GetArray2D("UpgradeData", "object", 6, 8));
    }

    [Fact]
    public void ItemSaveWaitsForLoadAndStaleItemFlagsCannotRelockVanillaProgress()
    {
        var scripts = ItemsRuntimeScripts.BuildScripts(null, null);
        var vm = CreateVanillaVm(scripts, includeTgeNineColRod: false);

        // A save hook can run before SaveManager_Other_13. That must not create default sidecar state.
        vm.GetObject("SaveManager").Set("jamm_items_loaded", 0.0);
        vm.Call("jamm_items_save");
        Assert.False(vm.HasIniKey("JAMM_Items", "u:rod:base:rod5"));

        // Legacy load (Other_13) restores a rod the stale JAMM_Items section says is locked.
        vm.SetIni("JAMM_Items", "u:rod:base:rod5", 0.0);
        vm.SetIni("JAMM_Items", "eq:rod", "base:rod0");
        vm.SetIni("JAMM_Items", "slot:base:rod0:0", "#locked");
        vm.SetArray1D("RodData", "unlocked", 5, 1.0);
        vm.SetArray2D("UpgradeData", "upg", 0, 0, 3.0);
        vm.Call("jamm_items_load");
        Assert.Equal(1.0, vm.GetArray1D("RodData", "unlocked", 5));
        Assert.Equal(3.0, vm.GetArray2D("UpgradeData", "upg", 0, 0));

        // Once the load completed, sidecar writes are enabled.
        vm.Call("jamm_items_save");
        Assert.Equal(1.0, vm.IniReal("JAMM_Items", "u:rod:base:rod5"));
    }

    [Fact]
    public void SaveManagerTwelveSlotHighRodAndLegacyNineColumnRoundTripWithoutKeyCollision()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");

        static void SetupTwelveRodBase(UndertaleData d)
        {
            CompileObj(d, "RodData", EventType.Create, 0, """
                nItems = 12;
                for (i = 0; i < 12; i++)
                {
                    unlocked[i] = 1;
                    cost[i] = 100 * (i + 1);
                    cast_speed[i] = 8 + i;
                    rod_power[i] = 1 + (0.2 * i);
                    rod_power_flat[i] = i;
                    reel_speed[i] = 8 + i;
                    sinker_distance[i] = 1200 + (500 * i);
                    name[i] = "Rod" + string(i);
                }
                """);
            CompileObj(d, "UpgradeData", EventType.Create, 0, """
                for (i = 0; i <= 17; i++)
                {
                    unlocks[i] = 1;
                    name[i] = "U" + string(i);
                    desc[i] = "Desc";
                    desc2[i] = "Desc2";
                    cost[i] = 0.1;
                }
                for (ix = 0; ix < 12; ix++)
                {
                    var cols = 8;
                    if (ix == 1 || ix == 11) { cols = 12; }
                    else if (ix == 6) { cols = 9; }
                    for (iy = 0; iy < cols; iy++)
                    {
                        upg[ix, iy] = -1;
                        object[ix, iy] = -1;
                    }
                }
                """);
            CompileObj(d, "SaveManager", EventType.Other, 13, """
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
                        for (i = 0; i <= 17; i++)
                        {
                            unlocks[i] = ini_read_real("Upgrades", "unlocks" + string(i), unlocks[i]);
                        }
                        for (ix = 0; ix < 12; ix++)
                        {
                            for (iy = 0; iy < array_length_2d(upg, ix); iy++)
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
            CompileObj(d, "SaveManager", EventType.Other, 14, """
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
                        for (i = 0; i <= 17; i++)
                        {
                            ini_write_real("Upgrades", "unlocks" + string(i), unlocks[i]);
                        }
                        for (ix = 0; ix < 12; ix++)
                        {
                            for (iy = 0; iy < array_length_2d(upg, ix); iy++)
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
            CompileObj(d, "SaveManager", EventType.Other, 15, """
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
                        for (i = 0; i <= 17; i++)
                        {
                            ini_write_real("Upgrades", "unlocks" + string(i), unlocks[i]);
                        }
                        for (ix = 0; ix < 12; ix++)
                        {
                            for (iy = 0; iy < array_length_2d(upg, ix); iy++)
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
        }

        WriteSyntheticUpgradeGame(basePath, rodLoopCount: 12, edit: SetupTwelveRodBase);
        WriteSyntheticUpgradeGame(modPath, rodLoopCount: 12, edit: d =>
        {
            SetupTwelveRodBase(d);
            AddUpgradePlaceholders(d);
            AddScript(d, "jamm_rod_register", "return -1;");
            var spr = AddSprite(d, "spr_mod_rod");
            AddScript(d, "jamm_init_rods", $$"""
                jamm_rod_register("rods:api12", "API 12 Rod", 90000, 18, 2.5, 6, 14, 9000, {{spr.Name.Content}}, 12, 1);
                """);
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "TwelveRodSave"), modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);

        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string scr in new[] { "jamm_items_ensure_base", "jamm_item_find", "jamm_rod_register", "jamm_items_save", "jamm_items_load" })
            scripts[scr] = PatchBuilder.ReadCode(merged, "gml_Script_" + scr).Text;
        scripts["save_other_13"] = PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_13").Text;
        scripts["save_other_14"] = PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_14").Text;
        scripts["save_other_15"] = PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_15").Text;

        var vm = new GmlTestVm(scripts);
        vm.RegisterSprite(10, "spr_mod_rod");
        vm.RegisterObjectAsset(42, "Goldfish");

        var boatData = vm.AddObject("BoatData");
        boatData.Set("nItems", 1.0);
        boatData.Set1D("unlocked", 0, 1.0);

        boatData.Set1D("object", 0, -1.0);

        var rodData = vm.AddObject("RodData");
        rodData.Set("nItems", 12.0);
        for (int i = 0; i < 12; i++)
        {
            rodData.Set1D("unlocked", i, 1.0);
            rodData.Set1D("cost", i, 100.0 * (i + 1));
            rodData.Set1D("cast_speed", i, 8.0 + i);
            rodData.Set1D("rod_power", i, 1.0 + (0.2 * i));
            rodData.Set1D("rod_power_flat", i, (double)i);
            rodData.Set1D("reel_speed", i, 8.0 + i);
            rodData.Set1D("sinker_distance", i, 1200.0 + (500.0 * i));
            rodData.Set1D("name", i, $"Rod{i}");
        }

        var lureData = vm.AddObject("LureData");
        lureData.Set("nItems", 1.0);
        lureData.Set1D("unlocked", 0, 1.0);

        var toolData = vm.AddObject("ToolData");
        toolData.Set("nItems", 1.0);
        toolData.Set1D("unlocked", 0, 0.0);
        toolData.Set1D("item", 0, -1.0);

        var gearData = vm.AddObject("GearData");
        gearData.Set("n_gear", 1.0);
        gearData.Set1D("unlocked", 0, 1.0);

        var upgData = vm.AddObject("UpgradeData");
        for (int i = 0; i < 18; i++)
            upgData.Set1D("unlocks", i, 1.0);
        for (int ix = 0; ix < 12; ix++)
        {
            int cols = (ix == 1 || ix == 11) ? 12 : (ix == 6 ? 9 : 8);
            for (int iy = 0; iy < cols; iy++)
            {
                upgData.Set2D("upg", ix, iy, -1.0);
                upgData.Set2D("object", ix, iy, -1.0);
            }
        }

        var girl = vm.AddObject("Girl");
        girl.Set("gear", -1.0);
        girl.Set("gear_id", -1.0);
        girl.Set("bestRod", 0.0);
        girl.Set("bestBoat", 0.0);

        var rod = vm.AddObject("Rod");
        rod.Set("rod_id", 0.0);
        rod.Set("allowUpgrades", 1.0);

        var saveMgr = vm.AddObject("SaveManager");
        saveMgr.Set("saving_locked", 0.0);
        saveMgr.Set("jamm_items_loaded", 1.0);
        saveMgr.Set("gear_id", -1.0);
        saveMgr.Set("active_rod", 0.0);

        Assert.Equal(1.0, vm.Call("jamm_items_ensure_base"));
        double api12Idx = vm.Call("jamm_rod_register", "rods:api12", "API 12 Rod", 90000.0, 18.0, 2.5, 6.0, 14.0, 9000.0, 10.0, 12.0, 1.0);
        Assert.Equal(12.0, api12Idx);

        // Populate colliding coordinates: (ix=1, iy=10..11), (ix=11, iy=0,9..11), (ix=6, iy=8), and API rod ix=12 (iy=0,10..11)
        vm.SetArray2D("UpgradeData", "upg", 1, 0, 1.0);
        vm.SetArray2D("UpgradeData", "upg", 1, 9, 2.0);
        vm.SetArray2D("UpgradeData", "upg", 1, 10, 3.0);
        vm.SetArray2D("UpgradeData", "upg", 1, 11, 4.0);
        vm.SetArray2D("UpgradeData", "object", 1, 11, 42.0);

        vm.SetArray2D("UpgradeData", "upg", 6, 8, 7.0);
        vm.SetArray2D("UpgradeData", "object", 6, 8, 42.0);

        vm.SetArray2D("UpgradeData", "upg", 11, 0, 5.0);
        vm.SetArray2D("UpgradeData", "upg", 11, 1, 13.0);
        vm.SetArray2D("UpgradeData", "upg", 11, 9, 6.0);
        vm.SetArray2D("UpgradeData", "upg", 11, 10, 8.0);
        vm.SetArray2D("UpgradeData", "upg", 11, 11, 9.0);
        vm.SetArray2D("UpgradeData", "object", 11, 10, 42.0);

        vm.SetArray2D("UpgradeData", "upg", 12, 0, 10.0);
        vm.SetArray2D("UpgradeData", "upg", 12, 10, 11.0);
        vm.SetArray2D("UpgradeData", "upg", 12, 11, 12.0);
        vm.SetArray2D("UpgradeData", "object", 12, 11, 42.0);

        // 1. Run SaveManager_Other_14: [Upgrades] writes iy 0..9 only, never iy >= 10, and never collides upg110/upg111
        vm.CallOn("save_other_14", "SaveManager");
        Assert.Equal(1.0, vm.IniReal("Upgrades", "upg10"));
        Assert.Equal(2.0, vm.IniReal("Upgrades", "upg19"));
        Assert.Equal(5.0, vm.IniReal("Upgrades", "upg110")); // (ix=11, iy=0), NOT (ix=1, iy=10)=3!
        Assert.Equal(13.0, vm.IniReal("Upgrades", "upg111")); // (ix=11, iy=1), NOT (ix=1, iy=11)=4!
        Assert.Equal(6.0, vm.IniReal("Upgrades", "upg119"));
        Assert.Equal(7.0, vm.IniReal("Upgrades", "upg68"));
        Assert.Equal(42.0, vm.IniReal("Upgrades", "obj68"));
        Assert.False(vm.HasIniKey("Upgrades", "upg1110"));
        Assert.False(vm.HasIniKey("Upgrades", "obj1110"));
        Assert.False(vm.HasIniKey("Upgrades", "upg1111"));
        Assert.False(vm.HasIniKey("Upgrades", "upg120"));
        Assert.False(vm.HasIniKey("Upgrades", "upg1210"));

        // Sidecar [JAMM_Items] covers every rod (including pre-API ix=1, ix=6, ix=11 and API ix=12) across iy 0..11
        Assert.Equal("base:upgrade3", vm.IniString("JAMM_Items", "slot:base:rod1:10"));
        Assert.Equal("base:upgrade4", vm.IniString("JAMM_Items", "slot:base:rod1:11"));
        Assert.Equal("Goldfish", vm.IniString("JAMM_Items", "slotobj:base:rod1:11"));
        Assert.Equal("base:upgrade7", vm.IniString("JAMM_Items", "slot:base:rod6:8"));
        Assert.Equal("Goldfish", vm.IniString("JAMM_Items", "slotobj:base:rod6:8"));
        Assert.Equal("base:upgrade5", vm.IniString("JAMM_Items", "slot:base:rod11:0"));
        Assert.Equal("base:upgrade13", vm.IniString("JAMM_Items", "slot:base:rod11:1"));
        Assert.Equal("base:upgrade8", vm.IniString("JAMM_Items", "slot:base:rod11:10"));
        Assert.Equal("Goldfish", vm.IniString("JAMM_Items", "slotobj:base:rod11:10"));
        Assert.Equal("base:upgrade9", vm.IniString("JAMM_Items", "slot:base:rod11:11"));
        Assert.Equal("base:upgrade10", vm.IniString("JAMM_Items", "slot:rods:api12:0"));
        Assert.Equal("base:upgrade11", vm.IniString("JAMM_Items", "slot:rods:api12:10"));
        Assert.Equal("base:upgrade12", vm.IniString("JAMM_Items", "slot:rods:api12:11"));
        Assert.Equal("Goldfish", vm.IniString("JAMM_Items", "slotobj:rods:api12:11"));

        // Backup save SaveManager_Other_15 obeys the exact same bounds
        vm.ClearIni();
        vm.CallOn("save_other_15", "SaveManager");
        Assert.Equal(5.0, vm.IniReal("Upgrades", "upg110"));
        Assert.Equal(13.0, vm.IniReal("Upgrades", "upg111"));
        Assert.Equal(7.0, vm.IniReal("Upgrades", "upg68"));
        Assert.False(vm.HasIniKey("Upgrades", "upg1110"));
        Assert.Equal("base:upgrade3", vm.IniString("JAMM_Items", "slot:base:rod1:10"));
        Assert.Equal("base:upgrade8", vm.IniString("JAMM_Items", "slot:base:rod11:10"));
        Assert.Equal("base:upgrade12", vm.IniString("JAMM_Items", "slot:rods:api12:11"));

        // 2. Full round-trip via SaveManager_Other_13 restores all slots without collision
        foreach (int rx in new[] { 1, 6, 11, 12 })
        {
            for (int cy = 0; cy < vm.GetRowLength2D("UpgradeData", "upg", rx); cy++)
            {
                vm.SetArray2D("UpgradeData", "upg", rx, cy, -1.0);
                vm.SetArray2D("UpgradeData", "object", rx, cy, -1.0);
            }
        }
        vm.CallOn("save_other_13", "SaveManager");
        Assert.Equal(1.0, vm.GetArray2D("UpgradeData", "upg", 1, 0));
        Assert.Equal(2.0, vm.GetArray2D("UpgradeData", "upg", 1, 9));
        Assert.Equal(3.0, vm.GetArray2D("UpgradeData", "upg", 1, 10));
        Assert.Equal(4.0, vm.GetArray2D("UpgradeData", "upg", 1, 11));
        Assert.Equal(42.0, vm.GetArray2D("UpgradeData", "object", 1, 11));
        Assert.Equal(7.0, vm.GetArray2D("UpgradeData", "upg", 6, 8));
        Assert.Equal(42.0, vm.GetArray2D("UpgradeData", "object", 6, 8));
        Assert.Equal(5.0, vm.GetArray2D("UpgradeData", "upg", 11, 0));
        Assert.Equal(13.0, vm.GetArray2D("UpgradeData", "upg", 11, 1));
        Assert.Equal(6.0, vm.GetArray2D("UpgradeData", "upg", 11, 9));
        Assert.Equal(8.0, vm.GetArray2D("UpgradeData", "upg", 11, 10));
        Assert.Equal(42.0, vm.GetArray2D("UpgradeData", "object", 11, 10));
        Assert.Equal(9.0, vm.GetArray2D("UpgradeData", "upg", 11, 11));
        Assert.Equal(10.0, vm.GetArray2D("UpgradeData", "upg", 12, 0));
        Assert.Equal(11.0, vm.GetArray2D("UpgradeData", "upg", 12, 10));
        Assert.Equal(12.0, vm.GetArray2D("UpgradeData", "upg", 12, 11));
        Assert.Equal(42.0, vm.GetArray2D("UpgradeData", "object", 12, 11));

        // 3. Vanilla-only load (no [JAMM_Items]): legacy 9-col row persists via vanilla keys,
        //    upg110/upg111 load (ix=11, iy=0..1) and NEVER write iy >= 10
        vm.RemoveIniSection("JAMM_Items");
        foreach (int rx in new[] { 1, 6, 11, 12 })
        {
            for (int cy = 0; cy < vm.GetRowLength2D("UpgradeData", "upg", rx); cy++)
            {
                vm.SetArray2D("UpgradeData", "upg", rx, cy, -1.0);
                vm.SetArray2D("UpgradeData", "object", rx, cy, -1.0);
            }
        }
        vm.SetArray2D("UpgradeData", "upg", 11, 10, 3.0); // stale in-memory value must reset to default -1
        vm.CallOn("save_other_13", "SaveManager");
        Assert.Equal(7.0, vm.GetArray2D("UpgradeData", "upg", 6, 8));
        Assert.Equal(42.0, vm.GetArray2D("UpgradeData", "object", 6, 8));
        Assert.Equal(5.0, vm.GetArray2D("UpgradeData", "upg", 11, 0));
        Assert.Equal(13.0, vm.GetArray2D("UpgradeData", "upg", 11, 1));
        Assert.Equal(-1.0, vm.GetArray2D("UpgradeData", "upg", 1, 10));
        Assert.Equal(-1.0, vm.GetArray2D("UpgradeData", "upg", 1, 11));
        Assert.Equal(-1.0, vm.GetArray2D("UpgradeData", "object", 1, 11));
        Assert.Equal(-1.0, vm.GetArray2D("UpgradeData", "upg", 11, 10));
    }

    private static GmlTestVm CreateVanillaVm(IReadOnlyDictionary<string, string> scripts, bool includeTgeNineColRod)
    {
        var vm = new GmlTestVm(scripts);
        vm.AddObject("SaveManager").Set("jamm_items_loaded", 1.0);
        vm.RegisterSprite(10, "spr_mod_rod");
        vm.RegisterObjectAsset(42, "Goldfish");

        var boatData = vm.AddObject("BoatData");
        boatData.Set("nItems", 1.0);
        boatData.Set1D("unlocked", 0, 1.0);

        int rodCount = includeTgeNineColRod ? 7 : 6;
        var rodData = vm.AddObject("RodData");
        rodData.Set("nItems", (double)rodCount);
        for (int i = 0; i < rodCount; i++)
        {
            rodData.Set1D("unlocked", i, 1.0);
            rodData.Set1D("cost", i, 100.0 * (i + 1));
            rodData.Set1D("cast_speed", i, 8.0 + i);
            rodData.Set1D("rod_power", i, 1.0 + (0.2 * i));
            rodData.Set1D("rod_power_flat", i, (double)i);
            rodData.Set1D("reel_speed", i, 8.0 + i);
            rodData.Set1D("sinker_distance", i, 1200.0 + (500.0 * i));
            rodData.Set1D("name", i, $"Rod{i}");
        }

        var lureData = vm.AddObject("LureData");
        lureData.Set("nItems", 1.0);
        lureData.Set1D("unlocked", 0, 1.0);

        var toolData = vm.AddObject("ToolData");
        toolData.Set("nItems", 1.0);
        toolData.Set1D("unlocked", 0, 0.0);
        toolData.Set1D("item", 0, -1.0);

        var gearData = vm.AddObject("GearData");
        gearData.Set("n_gear", 1.0);
        gearData.Set1D("unlocked", 0, 1.0);

        var upgData = vm.AddObject("UpgradeData");
        for (int i = 0; i < 18; i++)
            upgData.Set1D("unlocks", i, 1.0);
        for (int ix = 0; ix < 6; ix++)
        {
            for (int iy = 0; iy < 8; iy++)
            {
                upgData.Set2D("upg", ix, iy, iy < ix + 3 ? -1.0 : -2.0);
                upgData.Set2D("object", ix, iy, -1.0);
            }
        }
        if (includeTgeNineColRod)
        {
            for (int iy = 0; iy < 9; iy++)
            {
                upgData.Set2D("upg", 6, iy, -1.0);
                upgData.Set2D("object", 6, iy, -1.0);
            }
        }

        var girl = vm.AddObject("Girl");
        girl.Set("gear", -1.0);
        girl.Set("gear_id", -1.0);
        girl.Set("bestRod", 0.0);
        girl.Set("bestBoat", 0.0);

        var rod = vm.AddObject("Rod");
        rod.Set("rod_id", 0.0);
        rod.Set("allowUpgrades", 1.0);

        return vm;
    }

    private sealed class GmlTestVm
    {
        private readonly Dictionary<string, List<Tok>> scripts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, GmlObject> objects = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> sprites = [];
        private readonly Dictionary<int, string> objectAssets = [];
        private readonly Dictionary<string, int> objectAssetsByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, object>> ini = new(StringComparer.Ordinal);

        internal GmlTestVm(IReadOnlyDictionary<string, string> gmlScripts)
        {
            foreach (var (name, body) in gmlScripts)
                scripts[name] = Tokenize(body);
        }

        internal void RegisterSprite(int id, string name) => sprites[id] = name;
        internal void RegisterObjectAsset(int id, string name) { objectAssets[id] = name; objectAssetsByName[name] = id; }

        internal GmlObject AddObject(string name)
        {
            var obj = new GmlObject(name);
            objects[name] = obj;
            return obj;
        }

        internal GmlObject GetObject(string name) => objects[name];

        internal double GetReal(string obj, string field) => AsDouble(objects[obj].Fields[field]);
        internal double Get2DMaxRowCols(string obj, string field) => AsDouble(objects[obj].Fields[field]);
        internal double GetArray1D(string obj, string field, int idx) => AsDouble(((GmlArray1D)objects[obj].Fields[field]!)[idx]);
        internal double GetArray2D(string obj, string field, int r, int c) => AsDouble(((GmlArray2D)objects[obj].Fields[field]!)[r, c]);
        internal void SetArray2D(string obj, string field, int r, int c, double val) => ((GmlArray2D)objects[obj].Fields[field]!)[r, c] = val;
        internal void SetArray1D(string obj, string field, int idx, double val) => ((GmlArray1D)objects[obj].Fields[field]!)[idx] = val;
        internal int GetRowLength2D(string obj, string field, int r) => ((GmlArray2D)objects[obj].Fields[field]!).RowLength(r);
        internal void ClearIni() => ini.Clear();
        internal void RemoveIniSection(string sec) => ini.Remove(sec);
        internal bool HasIniKey(string sec, string key) => ini.TryGetValue(sec, out var dict) && dict.ContainsKey(key);
        internal void SetIni(string sec, string key, object val)
        {
            if (!ini.TryGetValue(sec, out var dict)) ini[sec] = dict = new(StringComparer.Ordinal);
            dict[key] = val;
        }
        internal string IniString(string sec, string key) => Convert.ToString(ini[sec][key], CultureInfo.InvariantCulture) ?? "";
        internal double IniReal(string sec, string key) => AsDouble(ini[sec][key]);

        internal double Call(string scriptName, params object[] args)
            => AsDouble(InvokeScript(scriptName, args, null));

        internal double CallOn(string scriptName, string targetObj, params object[] args)
            => AsDouble(InvokeScript(scriptName, args, objects[targetObj]));

        private object? InvokeScript(string scriptName, object?[] args, GmlObject? currentTarget)
        {
            var toks = scripts[scriptName];
            var frame = new Frame(args, currentTarget);
            int pos = 0;
            ExecBlock(toks, ref pos, frame, stopAtBrace: false);
            return frame.ReturnValue;
        }

        private void ExecBlock(List<Tok> toks, ref int pos, Frame frame, bool stopAtBrace)
        {
            while (pos < toks.Count && !frame.Returned)
            {
                if (stopAtBrace && toks[pos].Text == "}")
                    return;
                ExecStmt(toks, ref pos, frame);
            }
        }

        private void ExecStmt(List<Tok> toks, ref int pos, Frame frame)
        {
            while (pos < toks.Count && toks[pos].Text == ";") pos++;
            if (pos >= toks.Count || toks[pos].Text == "}" || frame.Returned) return;

            string head = toks[pos].Text;
            if (head == "{")
            {
                pos++;
                ExecBlock(toks, ref pos, frame, stopAtBrace: true);
                if (pos < toks.Count && toks[pos].Text == "}") pos++;
                return;
            }
            if (head == "var")
            {
                pos++;
                while (pos < toks.Count && toks[pos].Text != ";")
                {
                    string vname = toks[pos++].Text;
                    object? init = 0.0;
                    if (pos < toks.Count && toks[pos].Text == "=")
                    {
                        pos++;
                        init = EvalExpr(toks, ref pos, frame);
                    }
                    frame.Locals[vname] = init;
                    if (pos < toks.Count && toks[pos].Text == ",") pos++;
                }
                if (pos < toks.Count && toks[pos].Text == ";") pos++;
                return;
            }
            if (head == "return")
            {
                pos++;
                frame.ReturnValue = (pos < toks.Count && toks[pos].Text != ";") ? EvalExpr(toks, ref pos, frame) : 0.0;
                frame.Returned = true;
                if (pos < toks.Count && toks[pos].Text == ";") pos++;
                return;
            }
            if (head == "if")
            {
                pos++;
                Expect(toks, ref pos, "(");
                bool cond = IsTruthy(EvalExpr(toks, ref pos, frame));
                Expect(toks, ref pos, ")");
                if (cond)
                {
                    ExecStmt(toks, ref pos, frame);
                    if (pos < toks.Count && toks[pos].Text == "else")
                    {
                        pos++;
                        SkipStmt(toks, ref pos);
                    }
                }
                else
                {
                    SkipStmt(toks, ref pos);
                    if (pos < toks.Count && toks[pos].Text == "else")
                    {
                        pos++;
                        ExecStmt(toks, ref pos, frame);
                    }
                }
                return;
            }
            if (head == "for")
            {
                pos++;
                Expect(toks, ref pos, "(");
                if (toks[pos].Text == "var")
                {
                    pos++;
                    frame.Locals[toks[pos].Text] = 0.0;
                }
                if (toks[pos].Text != ";") ExecSimpleAssignOrCall(toks, ref pos, frame);
                Expect(toks, ref pos, ";");
                int condStart = pos;
                SkipUntil(toks, ref pos, ";");
                Expect(toks, ref pos, ";");
                int stepStart = pos;
                SkipUntilMatchingParen(toks, ref pos);
                Expect(toks, ref pos, ")");
                int bodyStart = pos;
                SkipStmt(toks, ref pos);
                int afterLoop = pos;

                while (!frame.Returned)
                {
                    int cp = condStart;
                    if (toks[cp].Text != ";" && !IsTruthy(EvalExpr(toks, ref cp, frame)))
                        break;
                    int bp = bodyStart;
                    ExecStmt(toks, ref bp, frame);
                    if (frame.Returned) break;
                    int sp = stepStart;
                    if (toks[sp].Text != ")") ExecSimpleAssignOrCall(toks, ref sp, frame);
                }
                pos = afterLoop;
                return;
            }
            if (head == "with")
            {
                pos++;
                Expect(toks, ref pos, "(");
                string targetName = toks[pos++].Text;
                Expect(toks, ref pos, ")");
                GmlObject? target = objects.TryGetValue(targetName, out var found)
                    ? found
                    : frame.Locals.TryGetValue(targetName, out var localVal) && localVal is GmlObject lo ? lo : null;
                if (target is null)
                {
                    SkipStmt(toks, ref pos);
                }
                else
                {
                    var prev = frame.WithTarget;
                    frame.WithTarget = target;
                    ExecStmt(toks, ref pos, frame);
                    frame.WithTarget = prev;
                }
                return;
            }

            ExecSimpleAssignOrCall(toks, ref pos, frame);
            if (pos < toks.Count && toks[pos].Text == ";") pos++;
        }

        private void ExecSimpleAssignOrCall(List<Tok> toks, ref int pos, Frame frame)
        {
            if (toks[pos].Text is "++" or "--")
            {
                string preOp = toks[pos++].Text;
                var preLval = ParseLValue(toks, ref pos, frame);
                WriteLValue(preLval, AsDouble(ReadLValue(preLval)) + (preOp == "++" ? 1.0 : -1.0));
                return;
            }
            if (pos + 1 < toks.Count && toks[pos + 1].Text == "(")
            {
                EvalExpr(toks, ref pos, frame);
                return;
            }
            var lval = ParseLValue(toks, ref pos, frame);
            string op = toks[pos].Text;
            if (op == "++") { pos++; WriteLValue(lval, AsDouble(ReadLValue(lval)) + 1.0); return; }
            if (op == "--") { pos++; WriteLValue(lval, AsDouble(ReadLValue(lval)) - 1.0); return; }
            if (op is "=" or "+=" or "-=")
            {
                pos++;
                object? rhs = EvalExpr(toks, ref pos, frame);
                if (op == "=") WriteLValue(lval, rhs);
                else if (op == "+=") WriteLValue(lval, AsDouble(ReadLValue(lval)) + AsDouble(rhs));
                else WriteLValue(lval, AsDouble(ReadLValue(lval)) - AsDouble(rhs));
                return;
            }
            throw new InvalidOperationException($"Unexpected token '{op}' in statement.");
        }

        private LVal ParseLValue(List<Tok> toks, ref int pos, Frame frame)
        {
            string first = toks[pos++].Text;
            GmlObject? target = frame.WithTarget;
            string field = first;
            bool isLocal = false;

            if (pos < toks.Count && toks[pos].Text == ".")
            {
                pos++;
                field = toks[pos++].Text;
                target = objects.TryGetValue(first, out var obj) ? obj
                    : (frame.Locals.TryGetValue(first, out var lv) && lv is GmlObject go ? go : throw new InvalidOperationException($"Unknown object '{first}'"));
            }
            else if (frame.Locals.ContainsKey(first))
            {
                isLocal = true;
            }

            int? idx1 = null, idx2 = null;
            if (pos < toks.Count && toks[pos].Text == "[")
            {
                pos++;
                idx1 = (int)AsDouble(EvalExpr(toks, ref pos, frame));
                if (pos < toks.Count && toks[pos].Text == ",")
                {
                    pos++;
                    idx2 = (int)AsDouble(EvalExpr(toks, ref pos, frame));
                }
                Expect(toks, ref pos, "]");
            }
            return new LVal(isLocal ? null : target, frame, field, idx1, idx2);
        }

        private object? ReadLValue(LVal lval)
        {
            var dict = lval.Target is not null ? lval.Target.Fields : lval.Frame.Locals;
            if (lval.Idx1 is null)
            {
                if (lval.Field == "id" && lval.Target is not null) return lval.Target;
                return dict.GetValueOrDefault(lval.Field);
            }
            if (lval.Idx2 is null)
                return ((GmlArray1D)dict[lval.Field]!)[lval.Idx1.Value];
            return ((GmlArray2D)dict[lval.Field]!)[lval.Idx1.Value, lval.Idx2.Value];
        }

        private static void WriteLValue(LVal lval, object? value)
        {
            var dict = lval.Target is not null ? lval.Target.Fields : lval.Frame.Locals;
            if (lval.Idx1 is null) { dict[lval.Field] = value; return; }
            if (lval.Idx2 is null)
            {
                if (!dict.TryGetValue(lval.Field, out var existing) || existing is not GmlArray1D arr1)
                    dict[lval.Field] = arr1 = new GmlArray1D();
                arr1[lval.Idx1.Value] = value;
                return;
            }
            if (!dict.TryGetValue(lval.Field, out var existing2) || existing2 is not GmlArray2D arr2)
                dict[lval.Field] = arr2 = new GmlArray2D();
            arr2[lval.Idx1.Value, lval.Idx2.Value] = value;
        }

        private object? EvalExpr(List<Tok> toks, ref int pos, Frame frame) => EvalOr(toks, ref pos, frame);

        private object? EvalOr(List<Tok> toks, ref int pos, Frame frame)
        {
            object? left = EvalAnd(toks, ref pos, frame);
            while (pos < toks.Count && toks[pos].Text == "||")
            {
                pos++;
                if (IsTruthy(left)) { SkipExpr(toks, ref pos, minPrec: 1); left = 1.0; }
                else left = IsTruthy(EvalAnd(toks, ref pos, frame)) ? 1.0 : 0.0;
            }
            return left;
        }

        private object? EvalAnd(List<Tok> toks, ref int pos, Frame frame)
        {
            object? left = EvalEq(toks, ref pos, frame);
            while (pos < toks.Count && toks[pos].Text == "&&")
            {
                pos++;
                if (!IsTruthy(left)) { SkipExpr(toks, ref pos, minPrec: 2); left = 0.0; }
                else left = IsTruthy(EvalEq(toks, ref pos, frame)) ? 1.0 : 0.0;
            }
            return left;
        }

        private object? EvalEq(List<Tok> toks, ref int pos, Frame frame)
        {
            object? left = EvalRel(toks, ref pos, frame);
            while (pos < toks.Count && toks[pos].Text is "==" or "!=")
            {
                string op = toks[pos++].Text;
                object? right = EvalRel(toks, ref pos, frame);
                bool eq = GmlEquals(left, right);
                left = (op == "==" ? eq : !eq) ? 1.0 : 0.0;
            }
            return left;
        }

        private object? EvalRel(List<Tok> toks, ref int pos, Frame frame)
        {
            object? left = EvalAdd(toks, ref pos, frame);
            while (pos < toks.Count && toks[pos].Text is "<" or "<=" or ">" or ">=")
            {
                string op = toks[pos++].Text;
                double l = AsDouble(left), r = AsDouble(EvalAdd(toks, ref pos, frame));
                left = (op switch { "<" => l < r, "<=" => l <= r, ">" => l > r, _ => l >= r }) ? 1.0 : 0.0;
            }
            return left;
        }

        private object? EvalAdd(List<Tok> toks, ref int pos, Frame frame)
        {
            object? left = EvalMul(toks, ref pos, frame);
            while (pos < toks.Count && toks[pos].Text is "+" or "-")
            {
                string op = toks[pos++].Text;
                object? right = EvalMul(toks, ref pos, frame);
                if (op == "+" && (left is string || right is string))
                    left = (Convert.ToString(left, CultureInfo.InvariantCulture) ?? "") + (Convert.ToString(right, CultureInfo.InvariantCulture) ?? "");
                else
                    left = op == "+" ? AsDouble(left) + AsDouble(right) : AsDouble(left) - AsDouble(right);
            }
            return left;
        }

        private object? EvalMul(List<Tok> toks, ref int pos, Frame frame)
        {
            object? left = EvalUnary(toks, ref pos, frame);
            while (pos < toks.Count && toks[pos].Text is "*" or "/")
            {
                string op = toks[pos++].Text;
                double r = AsDouble(EvalUnary(toks, ref pos, frame));
                left = op == "*" ? AsDouble(left) * r : AsDouble(left) / r;
            }
            return left;
        }

        private object? EvalUnary(List<Tok> toks, ref int pos, Frame frame)
        {
            if (toks[pos].Text == "!") { pos++; return IsTruthy(EvalUnary(toks, ref pos, frame)) ? 0.0 : 1.0; }
            if (toks[pos].Text == "-") { pos++; return -AsDouble(EvalUnary(toks, ref pos, frame)); }
            return EvalPrimary(toks, ref pos, frame);
        }

        private object? EvalPrimary(List<Tok> toks, ref int pos, Frame frame)
        {
            var tok = toks[pos++];
            if (tok.Kind == 's') return tok.Text;
            if (tok.Kind == 'n') return double.Parse(tok.Text, CultureInfo.InvariantCulture);
            if (tok.Text == "(")
            {
                object? v = EvalExpr(toks, ref pos, frame);
                Expect(toks, ref pos, ")");
                return v;
            }
            if (tok.Kind == 'i')
            {
                if (pos < toks.Count && toks[pos].Text == "(")
                {
                    pos++;
                    List<object?> args = [];
                    if (toks[pos].Text != ")")
                    {
                        while (true)
                        {
                            args.Add(EvalExpr(toks, ref pos, frame));
                            if (toks[pos].Text == ",") { pos++; continue; }
                            break;
                        }
                    }
                    Expect(toks, ref pos, ")");
                    return CallBuiltinOrScript(tok.Text, args, frame);
                }
                if (tok.Text == "argument_count") return (double)frame.Args.Length;
                if (tok.Text == "asset_object") return 0.0;
                if (tok.Text.StartsWith("argument", StringComparison.Ordinal) && int.TryParse(tok.Text[8..], NumberStyles.None, CultureInfo.InvariantCulture, out int argIdx))
                    return argIdx < frame.Args.Length ? frame.Args[argIdx] : 0.0;
                if (objects.TryGetValue(tok.Text, out var directObj) && (pos >= toks.Count || toks[pos].Text != "."))
                    return directObj;
                pos--;
                return ReadLValue(ParseLValue(toks, ref pos, frame));
            }
            throw new InvalidOperationException($"Unexpected token '{tok.Text}'.");
        }

        private object? CallBuiltinOrScript(string name, List<object?> args, Frame frame)
        {
            if (scripts.ContainsKey(name))
                return InvokeScript(name, args.ToArray(), frame.WithTarget);

            switch (name)
            {
                case "instance_exists":
                    return args[0] is GmlObject go ? (objects.ContainsKey(go.Name) ? 1.0 : 0.0)
                        : args[0] is double d && d > -1 ? 1.0 : 0.0;
                case "variable_instance_exists":
                    return args[0] is GmlObject inst && inst.Fields.ContainsKey(Convert.ToString(args[1], CultureInfo.InvariantCulture) ?? "") ? 1.0 : 0.0;
                case "array_length_1d":
                    return args[0] is GmlArray1D a1 ? (double)a1.Length : 0.0;
                case "array_height_2d":
                    return args[0] is GmlArray2D a2h ? (double)a2h.Height : 0.0;
                case "array_length_2d":
                    return args[0] is GmlArray2D a2r ? (double)a2r.RowLength((int)AsDouble(args[1])) : 0.0;
                case "is_string":
                    return args[0] is string ? 1.0 : 0.0;
                case "is_real":
                    return args[0] is double ? 1.0 : 0.0;
                case "sprite_exists":
                    return sprites.ContainsKey((int)AsDouble(args[0])) ? 1.0 : 0.0;
                case "object_exists":
                    return objectAssets.ContainsKey((int)AsDouble(args[0])) ? 1.0 : 0.0;
                case "object_get_name":
                    return objectAssets.GetValueOrDefault((int)AsDouble(args[0]), "");
                case "asset_get_index":
                    return objectAssetsByName.TryGetValue(Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "", out int aid) ? (double)aid : -1.0;
                case "asset_get_type":
                    return objectAssetsByName.ContainsKey(Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "") ? 0.0 : -1.0;
                case "script_exists":
                    return 0.0;
                case "floor":
                    return Math.Floor(AsDouble(args[0]));
                case "min":
                    return args.Select(AsDouble).Min();
                case "max":
                    return args.Select(AsDouble).Max();
                case "string":
                    return args[0] is double num
                        ? (Math.Abs(num - Math.Truncate(num)) < 1e-9 ? ((long)Math.Truncate(num)).ToString(CultureInfo.InvariantCulture) : num.ToString(CultureInfo.InvariantCulture))
                        : Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "";
                case "ini_section_exists":
                    return ini.TryGetValue(Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "", out var sec) && sec.Count > 0 ? 1.0 : 0.0;
                case "ini_key_exists":
                    return ini.TryGetValue(Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "", out var s2)
                        && s2.ContainsKey(Convert.ToString(args[1], CultureInfo.InvariantCulture) ?? "") ? 1.0 : 0.0;
                case "ini_write_real":
                    SetIni(Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "", Convert.ToString(args[1], CultureInfo.InvariantCulture) ?? "", AsDouble(args[2]));
                    return 0.0;
                case "ini_write_string":
                    SetIni(Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "", Convert.ToString(args[1], CultureInfo.InvariantCulture) ?? "", Convert.ToString(args[2], CultureInfo.InvariantCulture) ?? "");
                    return 0.0;
                case "ini_read_real":
                {
                    string s = Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "";
                    string k = Convert.ToString(args[1], CultureInfo.InvariantCulture) ?? "";
                    return ini.TryGetValue(s, out var sd) && sd.TryGetValue(k, out var v) ? AsDouble(v) : AsDouble(args[2]);
                }
                case "ini_read_string":
                {
                    string s = Convert.ToString(args[0], CultureInfo.InvariantCulture) ?? "";
                    string k = Convert.ToString(args[1], CultureInfo.InvariantCulture) ?? "";
                    return ini.TryGetValue(s, out var sd) && sd.TryGetValue(k, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : Convert.ToString(args[2], CultureInfo.InvariantCulture) ?? "";
                }
                case "ini_open":
                case "ini_close":
                case "event_perform":
                case "instance_destroy":
                    return 0.0;
                default:
                    throw new InvalidOperationException($"Unsupported GML function '{name}'.");
            }
        }

        private static void SkipExpr(List<Tok> toks, ref int pos, int minPrec)
        {
            int depth = 0;
            while (pos < toks.Count)
            {
                string t = toks[pos].Text;
                if (depth == 0)
                {
                    if (t is ")" or "]" or "," or ";") return;
                    if (minPrec <= 1 && t == "||") return;
                    if (minPrec <= 2 && t == "&&") return;
                }
                if (t is "(" or "[") depth++;
                else if (t is ")" or "]") depth--;
                pos++;
            }
        }

        private static void SkipStmt(List<Tok> toks, ref int pos)
        {
            if (pos >= toks.Count) return;
            string head = toks[pos].Text;
            if (head == "{")
            {
                int depth = 0;
                while (pos < toks.Count)
                {
                    if (toks[pos].Text == "{") depth++;
                    else if (toks[pos].Text == "}") { depth--; if (depth == 0) { pos++; return; } }
                    pos++;
                }
                return;
            }
            if (head is "if" or "for" or "with")
            {
                pos++;
                if (pos < toks.Count && toks[pos].Text == "(") { pos++; SkipUntilMatchingParen(toks, ref pos); pos++; }
                SkipStmt(toks, ref pos);
                if (head == "if" && pos < toks.Count && toks[pos].Text == "else") { pos++; SkipStmt(toks, ref pos); }
                return;
            }
            while (pos < toks.Count && toks[pos].Text != ";") pos++;
            if (pos < toks.Count && toks[pos].Text == ";") pos++;
        }

        private static void SkipUntil(List<Tok> toks, ref int pos, string stop)
        {
            int depth = 0;
            while (pos < toks.Count)
            {
                string t = toks[pos].Text;
                if (depth == 0 && t == stop) return;
                if (t is "(" or "[") depth++;
                else if (t is ")" or "]") depth--;
                pos++;
            }
        }

        private static void SkipUntilMatchingParen(List<Tok> toks, ref int pos)
        {
            int depth = 0;
            while (pos < toks.Count)
            {
                string t = toks[pos].Text;
                if (t == "(") depth++;
                else if (t == ")") { if (depth == 0) return; depth--; }
                pos++;
            }
        }

        private static void Expect(List<Tok> toks, ref int pos, string expected)
        {
            if (pos >= toks.Count || toks[pos].Text != expected)
                throw new InvalidOperationException($"Expected '{expected}', got '{(pos < toks.Count ? toks[pos].Text : "<eof>")}'.");
            pos++;
        }

        private static bool IsTruthy(object? v) => v is double d && d != 0.0;
        private static double AsDouble(object? v) => v switch { double d => d, int i => i, _ => 0.0 };
        private static bool GmlEquals(object? a, object? b)
        {
            if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);
            if (a is double da && b is double db) return da == db;
            return Equals(a, b);
        }

        private static List<Tok> Tokenize(string text)
        {
            List<Tok> list = [];
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (c == '"')
                {
                    i++;
                    var sb = new System.Text.StringBuilder();
                    while (i < text.Length && text[i] != '"')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length) { i++; sb.Append(text[i++]); }
                        else sb.Append(text[i++]);
                    }
                    if (i < text.Length) i++;
                    list.Add(new Tok('s', sb.ToString()));
                    continue;
                }
                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                    list.Add(new Tok('n', text[start..i]));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    list.Add(new Tok('i', text[start..i]));
                    continue;
                }
                if (i + 1 < text.Length)
                {
                    string two = text.Substring(i, 2);
                    if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "+=" or "-=" or "++" or "--")
                    {
                        list.Add(new Tok('o', two));
                        i += 2;
                        continue;
                    }
                }
                list.Add(new Tok('o', c.ToString()));
                i++;
            }
            return list;
        }

        private sealed record Tok(char Kind, string Text);
        private sealed record LVal(GmlObject? Target, Frame Frame, string Field, int? Idx1, int? Idx2);

        private sealed class Frame(object?[] args, GmlObject? withTarget)
        {
            internal object?[] Args { get; } = args;
            internal GmlObject? WithTarget { get; set; } = withTarget;
            internal Dictionary<string, object?> Locals { get; } = new(StringComparer.Ordinal);
            internal bool Returned { get; set; }
            internal object? ReturnValue { get; set; }
        }

        internal sealed class GmlObject(string name)
        {
            internal string Name { get; } = name;
            internal Dictionary<string, object?> Fields { get; } = new(StringComparer.Ordinal);
            internal void Set(string field, object? value) => Fields[field] = value;
            internal void Set1D(string field, int idx, object? value)
            {
                if (!Fields.TryGetValue(field, out var ex) || ex is not GmlArray1D arr)
                    Fields[field] = arr = new GmlArray1D();
                arr[idx] = value;
            }
            internal void Set2D(string field, int r, int c, object? value)
            {
                if (!Fields.TryGetValue(field, out var ex) || ex is not GmlArray2D arr)
                    Fields[field] = arr = new GmlArray2D();
                arr[r, c] = value;
            }
        }

        private sealed class GmlArray1D
        {
            private readonly Dictionary<int, object?> data = [];
            internal object? this[int idx]
            {
                get => data.GetValueOrDefault(idx, 0.0);
                set => data[idx] = value;
            }
            internal int Length => data.Count == 0 ? 0 : data.Keys.Max() + 1;
        }

        private sealed class GmlArray2D
        {
            private readonly Dictionary<(int Row, int Col), object?> data = [];
            internal object? this[int r, int c]
            {
                get => data.GetValueOrDefault((r, c), 0.0);
                set => data[(r, c)] = value;
            }
            internal int Height => data.Count == 0 ? 0 : data.Keys.Max(k => k.Row) + 1;
            internal int RowLength(int row) => data.Keys.Where(k => k.Row == row).Select(k => k.Col + 1).DefaultIfEmpty(0).Max();
        }
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
