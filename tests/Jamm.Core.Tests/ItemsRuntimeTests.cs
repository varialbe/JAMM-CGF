using Jamm.Core.Building;
using Jamm.Core.Diffing;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Phase 22b Item API, init discovery, draw patches, and [JAMM_Items] persistence tests.</summary>
public sealed class ItemsRuntimeTests
{
    [Fact]
    public void NonApiModsLeaveArchivesUntouchedWithoutInjectingItemsRuntime()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        WriteSyntheticGame(basePath);
        WriteSyntheticGame(modPath, d => d.GameObjects.ByName("Girl")!.Depth = 42);

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "Tweak");
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);

        Assert.NotNull(build.Verification);
        Assert.DoesNotContain(build.Plan.Assets, a => a.Name == "gml_Script_jamm_api_init");
        Assert.DoesNotContain(build.Plan.Assets, a => a.Name == "gml_Object_SaveManager_Step_0");
        using var merged = GameFileLoader.Load(output);
        Assert.Null(merged.Data.Scripts.ByName("jamm_api_init"));
        Assert.Null(merged.Data.Scripts.ByName("jamm_boat_register"));
        Assert.DoesNotContain("jamm_items_load", PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_13").Text);
    }

    [Fact]
    public void InitScriptsRunInLoadOrderThenNameAndReplacePlaceholdersDeterministically()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modAPath = temp.PathFor("modA.win");
        string modBPath = temp.PathFor("modB.win");
        WriteSyntheticGame(basePath);
        WriteSyntheticGame(modAPath, d =>
        {
            var spr = AddSprite(d, "spr_alpha_boat");
            var obj = AddObject(d, "obj_alpha_boat");
            AddScript(d, "jamm_boat_register", "return 999;");
            AddScript(d, "jamm_init_zeta", $"jamm_boat_register(\"moda:zeta\", \"Zeta Boat\", 12000, {spr.Name.Content}, {obj.Name.Content}, \"Zeta\");");
            AddScript(d, "jamm_init_alpha", $"jamm_boat_register(\"moda:alpha\", \"Alpha Boat\", 10000, {spr.Name.Content}, {obj.Name.Content}, \"Alpha\");");
        });
        WriteSyntheticGame(modBPath, d =>
        {
            var obj = AddObject(d, "obj_beta_boat");
            AddApiPlaceholders(d, "return 888;");
            AddScript(d, "jamm_init_beta", $"jamm_boat_register(\"modb:beta\", \"Beta Boat\", 25000, -1, {obj.Name.Content}, \"Beta\", 1);");
        });

        using var original = GameFileLoader.Load(basePath);
        using var modA = GameFileLoader.Load(modAPath);
        using var modB = GameFileLoader.Load(modBPath);
        var patchA = PatchBuilder.Create(original, modA, "ModA");
        var patchB = PatchBuilder.Create(original, modB, "ModB");

        string out1 = temp.PathFor("merged1.win");
        string out2 = temp.PathFor("merged2.win");
        var build = ArchiveBuilder.Build(basePath, [new(patchA, modAPath), new(patchB, modBPath)], out1);
        ArchiveBuilder.Build(basePath, [new(patchA, modAPath), new(patchB, modBPath)], out2);

        Assert.NotNull(build.Verification);
        Assert.Equal(BuildFiles.Hash(out1), BuildFiles.Hash(out2));
        Assert.Contains(build.Plan.Decisions, d => d.Contains("Items API:", StringComparison.Ordinal)
            && d.Contains("jamm_init_alpha, jamm_init_zeta, jamm_init_beta", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(out1);
        foreach (string script in new[]
        {
            "jamm_api_init", "jamm_items_ensure_base", "jamm_item_find",
            "jamm_boat_register", "jamm_rod_register", "jamm_lure_register",
            "jamm_tool_register", "jamm_hat_register", "jamm_items_save", "jamm_items_load"
        })
        {
            Assert.NotNull(merged.Data.Scripts.ByName(script));
            Assert.NotNull(merged.Data.Code.ByName("gml_Script_" + script));
        }

        string apiInit = PatchBuilder.ReadCode(merged, "gml_Script_jamm_api_init").Text;
        int alphaPos = apiInit.IndexOf("jamm_init_alpha()", StringComparison.Ordinal);
        int zetaPos = apiInit.IndexOf("jamm_init_zeta()", StringComparison.Ordinal);
        int betaPos = apiInit.IndexOf("jamm_init_beta()", StringComparison.Ordinal);
        Assert.True(alphaPos >= 0 && zetaPos > alphaPos && betaPos > zetaPos, apiInit);
        Assert.Contains("jamm_api_done", apiInit);
        Assert.Contains("jamm_items_ensure_base()", apiInit);

        string saveStep = PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Step_0").Text;
        int callPos = saveStep.IndexOf("jamm_api_init()", StringComparison.Ordinal);
        int loadPos = saveStep.IndexOf("if (!loaded)", StringComparison.Ordinal);
        Assert.True(callPos >= 0 && loadPos > callPos, saveStep);

        string boatReg = PatchBuilder.ReadCode(merged, "gml_Script_jamm_boat_register").Text;
        Assert.DoesNotContain("999", boatReg);
        Assert.Contains("jamm_item_find(\"boat\"", boatReg);
    }

    [Fact]
    public void DirectRegistrationCallTriggersRuntimeWhileCommentAloneDoesNot()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string commentModPath = temp.PathFor("comment.win");
        string directModPath = temp.PathFor("direct.win");
        WriteSyntheticGame(basePath);
        WriteSyntheticGame(commentModPath, d =>
        {
            AddScript(d, "helper_note", "var s = \"jamm_boat_register(\";\n// jamm_boat_register();\nreturn 0;");
        });
        WriteSyntheticGame(directModPath, d =>
        {
            var obj = AddObject(d, "obj_direct_boat");
            AddScript(d, "jamm_boat_register", "return 0;");
            var code = d.Code.ByName("gml_Object_BoatData_Create_0")!;
            string existing = new Underanalyzer.Decompiler.DecompileContext(new GlobalDecompileContext(d), code).DecompileToString();
            Compile(d, code, existing + $"\njamm_boat_register(\"direct:raft\", \"Direct Raft\", 500, -1, {obj.Name.Content}, \"Raft\");");
        });

        using var original = GameFileLoader.Load(basePath);
        using var commentMod = GameFileLoader.Load(commentModPath);
        using var directMod = GameFileLoader.Load(directModPath);

        string commentOut = temp.PathFor("comment-merged.win");
        ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, commentMod, "Comment"), commentModPath)], commentOut);
        using (var merged = GameFileLoader.Load(commentOut))
            Assert.Null(merged.Data.Scripts.ByName("jamm_api_init"));

        string directOut = temp.PathFor("direct-merged.win");
        var directBuild = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, directMod, "Direct"), directModPath)], directOut);
        Assert.NotNull(directBuild.Verification);
        using (var merged = GameFileLoader.Load(directOut))
        {
            Assert.NotNull(merged.Data.Scripts.ByName("jamm_api_init"));
            Assert.Contains("obj_direct_boat", PatchBuilder.ReadCode(merged, "gml_Object_BoatData_Create_0").Text);
        }
    }

    [Fact]
    public void AllFiveRegistrationsRelinkTypedAssetsAcrossModsAndProvideItemFindAndBaseKeys()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modAPath = temp.PathFor("modA.win");
        string modBPath = temp.PathFor("modB.win");
        WriteSyntheticGame(basePath);

        WriteSyntheticGame(modAPath, d =>
        {
            AddApiPlaceholders(d, "return 777;");
            var sBoat = AddSprite(d, "spr_a_boat");
            var sRod = AddSprite(d, "spr_a_rod");
            var sLure = AddSprite(d, "spr_a_lure");
            var sTool = AddSprite(d, "spr_a_tool");
            var oBoat = AddObject(d, "obj_a_boat");
            var oLure = AddObject(d, "obj_a_lure");
            var oTool = AddObject(d, "obj_a_tool");
            var oToolMsg = AddObject(d, "obj_a_tool_msg");
            var oHat = AddObject(d, "obj_a_hat");

            AddScript(d, "jamm_init_a", $$"""
                var b, r, l, t, h, found;
                b = jamm_boat_register("moda:boat", "A Boat", 1000, {{sBoat.Name.Content}}, {{oBoat.Name.Content}}, "Boat desc", 1);
                r = jamm_rod_register("moda:rod", "A Rod", 2000, 11, 1.8, 3, 10, 4000, {{sRod.Name.Content}}, 6, 1);
                l = jamm_lure_register("moda:lure", "A Lure", 300, {{oLure.Name.Content}}, {{sLure.Name.Content}}, "Lure desc");
                t = jamm_tool_register("moda:tool", "A Tool", 5000, 5, {{oTool.Name.Content}}, {{oToolMsg.Name.Content}}, {{sTool.Name.Content}}, "Tool desc", 0);
                h = jamm_hat_register("moda:hat", {{oHat.Name.Content}}, 4500, 3, 0.1, 0.8, 2, -4, 1);
                found = jamm_item_find("boat", "base:boat0");
                """);
        });

        WriteSyntheticGame(modBPath, d =>
        {
            AddApiPlaceholders(d, "return 888;");
            var sBoat = AddSprite(d, "spr_b_boat");
            var sRod = AddSprite(d, "spr_b_rod");
            var oBoat = AddObject(d, "obj_b_boat");
            var oTool = AddObject(d, "obj_b_tool");
            var oHat = AddObject(d, "obj_b_hat");

            AddScript(d, "jamm_init_b", $$"""
                jamm_boat_register("modb:boat", "B Boat", 1500, {{sBoat.Name.Content}}, {{oBoat.Name.Content}}, "B desc");
                jamm_rod_register("modb:rod", "B Rod", 2500, 12, 2.0, 2, 11, 5000, {{sRod.Name.Content}});
                jamm_tool_register("modb:tool", "B Tool", 6000, 0, {{oTool.Name.Content}}, -1, -1, "No msg tool");
                jamm_hat_register("modb:hat", {{oHat.Name.Content}}, 1200, 5, 0, 0.5);
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

        using var merged = GameFileLoader.Load(output);
        string initA = PatchBuilder.ReadCode(merged, "gml_Script_jamm_init_a").Text;
        string initB = PatchBuilder.ReadCode(merged, "gml_Script_jamm_init_b").Text;
        Assert.Contains("spr_a_boat", initA);
        Assert.Contains("obj_a_boat", initA);
        Assert.Contains("spr_a_rod", initA);
        Assert.Contains("obj_a_lure", initA);
        Assert.Contains("spr_a_lure", initA);
        Assert.Contains("obj_a_tool", initA);
        Assert.Contains("obj_a_tool_msg", initA);
        Assert.Contains("spr_a_tool", initA);
        Assert.Contains("obj_a_hat", initA);
        Assert.Contains("spr_b_boat", initB);
        Assert.Contains("obj_b_boat", initB);
        Assert.Contains("spr_b_rod", initB);
        Assert.Contains("obj_b_tool", initB);
        Assert.Contains("obj_b_hat", initB);

        string ensureBase = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_ensure_base").Text;
        foreach (string prefix in new[] { "base:boat", "base:rod", "base:lure", "base:tool", "base:hat", "base:upgrade" })
            Assert.Contains(prefix, ensureBase);

        string itemFind = PatchBuilder.ReadCode(merged, "gml_Script_jamm_item_find").Text;
        foreach (string type in new[] { "boat", "rod", "lure", "tool", "hat", "upgrade" })
            Assert.Contains($"\"{type}\"", itemFind);

        string rodReg = PatchBuilder.ReadCode(merged, "gml_Script_jamm_rod_register").Text;
        Assert.Contains("UpgradeData.upg", rodReg);
        Assert.Contains("UpgradeData.object", rodReg);

        string lureReg = PatchBuilder.ReadCode(merged, "gml_Script_jamm_lure_register").Text;
        Assert.Contains("Girl.gear_lure_scalar", lureReg);
        Assert.Contains("lure_scalar", lureReg);

        string toolReg = PatchBuilder.ReadCode(merged, "gml_Script_jamm_tool_register").Text;
        Assert.Contains("instance_create(0, 0, jamm_obj)", toolReg);

        string hatReg = PatchBuilder.ReadCode(merged, "gml_Script_jamm_hat_register").Text;
        Assert.Contains("GearData.map", hatReg);
        Assert.Contains("GearData.n_gear", hatReg);
    }

    [Fact]
    public void DrawSafetyAndSaveLoadPatchesVerifyCleanlyAndEnforceBaseBoundsAndKeyPersistence()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        WriteSyntheticGame(basePath);
        WriteSyntheticGame(modPath, d =>
        {
            AddApiPlaceholders(d);
            var spr = AddSprite(d, "spr_mod_rod");
            AddScript(d, "jamm_init_demo", $"jamm_rod_register(\"demo:rod\", \"Demo Rod\", 999, 10, 1.5, 2, 9, 3000, {spr.Name.Content});");
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "Demo"), modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);

        string rodCard = PatchBuilder.ReadCode(merged, "gml_Object_RodCard_Other_11").Text;
        Assert.Contains("RodData.disp", rodCard);
        Assert.Contains("sprite_get_number(spr_rod)", rodCard);

        string rodEquip = PatchBuilder.ReadCode(merged, "gml_Object_Rod_Other_10").Text;
        Assert.Contains("RodData.disp", rodEquip);
        Assert.Contains("sprite_index = spr_rod", rodEquip);
        Assert.Contains("sprite_get_number(spr_rod)", rodEquip);

        string toolCard = PatchBuilder.ReadCode(merged, "gml_Object_ToolCard_Other_11").Text;
        Assert.Contains("ToolData.disp", toolCard);
        Assert.Contains("sprite_get_number(spr_tools)", toolCard);

        Assert.Contains("BoatData.disp[item_id] > -1", PatchBuilder.ReadCode(merged, "gml_Object_BoatCard_Other_11").Text);
        Assert.Contains("LureData.disp[item_id] > -1", PatchBuilder.ReadCode(merged, "gml_Object_LureCard_Other_11").Text);
        Assert.Contains(".boat_index = item_id", PatchBuilder.ReadCode(merged, "gml_Object_BoatCard_Other_10").Text);
        Assert.Contains("ToolData.message[item_id] > -1", PatchBuilder.ReadCode(merged, "gml_Object_ToolCard_Other_10").Text);
        Assert.Contains("LureData.nItems", PatchBuilder.ReadCode(merged, "gml_Object_Girl_Other_10").Text);
        Assert.Contains("LureData.nItems", PatchBuilder.ReadCode(merged, "gml_Object_Gear_Other_15").Text);
        Assert.Contains("clamp(Rod.rod_id, 0, 5)", PatchBuilder.ReadCode(merged, "gml_Object_Rocket_Step_2").Text);
        Assert.Contains("clamp(Girl.bestRod, 0, 5)", PatchBuilder.ReadCode(merged, "gml_Script_start_quest_dist").Text);
        Assert.Contains("clamp(index, 0, RodData.nItems - 1)", PatchBuilder.ReadCode(merged, "gml_Script_start_quest_rods").Text);
        Assert.Contains("rid++", PatchBuilder.ReadCode(merged, "gml_Object_Rod_KeyPress_88").Text);
        Assert.DoesNotContain("rid--", PatchBuilder.ReadCode(merged, "gml_Object_Rod_KeyPress_88").Text);
        Assert.Contains("BoatData.jamm_base_count", PatchBuilder.ReadCode(merged, "gml_Script_boat_was_unlocked").Text);
        Assert.Contains("RodData.jamm_base_count", PatchBuilder.ReadCode(merged, "gml_Script_rod_was_unlocked").Text);
        Assert.Contains("LureData.jamm_base_count", PatchBuilder.ReadCode(merged, "gml_Script_lure_was_purchased").Text);
        Assert.Contains("ToolData.jamm_base_count", PatchBuilder.ReadCode(merged, "gml_Script_tool_was_purchased").Text);

        string save13 = PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_13").Text;
        Assert.Contains("jamm_items_load()", save13);
        Assert.Contains("ini_section_exists(\"JAMM_Items\")", save13);
        Assert.Contains("jamm_base_count", save13);
        Assert.DoesNotContain("for (i = 0; i < nItems; i++)", save13);
        Assert.DoesNotContain("for (i = 0; i < n_gear; i++)", save13);

        foreach (string saveEntry in new[] { "gml_Object_SaveManager_Other_14", "gml_Object_SaveManager_Other_15" })
        {
            string saveText = PatchBuilder.ReadCode(merged, saveEntry).Text;
            Assert.Contains("jamm_items_save()", saveText);
            Assert.Contains("jamm_base_count", saveText);
            Assert.DoesNotContain("for (i = 0; i < nItems; i++)", saveText);
            Assert.DoesNotContain("for (i = 0; i < n_gear; i++)", saveText);
        }

        string saveScript = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_save").Text;
        foreach (string expected in new[]
        {
            "\"u:boat:\"", "\"u:rod:\"", "\"u:lure:\"", "\"u:tool:\"", "\"u:hat:\"", "\"u:upgrade:\"",
            "\"eq:boat\"", "\"eq:rod\"", "\"eq:hat\"", "\"slot:\"", "\"#locked\"", "\"slotobj:\"", "object_get_name"
        })
            Assert.Contains(expected, saveScript);

        string loadScript = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_load").Text;
        Assert.Contains("ini_section_exists(\"JAMM_Items\")", loadScript);
        Assert.Contains("jamm_default_unlocked", loadScript);
        Assert.Contains("jamm_default_upg", loadScript);
        Assert.Contains("asset_get_index", loadScript);
        Assert.Contains("asset_get_index(\"gear_equip\")", loadScript);
    }

    [Fact]
    public void OldSaveWithoutNewToolKeyPreservesBoatKeysAndDefaultsToolToLocked()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        WriteSyntheticGame(basePath);
        WriteSyntheticGame(modPath, d =>
        {
            AddApiPlaceholders(d);
            var hullSpr = AddSprite(d, "spr_sub_card");
            var winchIcon = AddSprite(d, "spr_bow_winch");
            var subObj = AddObject(d, "jmk_sub_submarine");
            var winchObj = AddObject(d, "jmk_sub_bow_winch_tool");
            AddScript(d, "jamm_init_sub", $"""
                jamm_boat_register("submarine:submarine", "Submarine", 125000000, {hullSpr.Name.Content}, {subObj.Name.Content}, "Sub", 0);
                jamm_tool_register("submarine:bow-winch", "Bow Winch", 250000000, 0, {winchObj.Name.Content}, -1, {winchIcon.Name.Content}, "Winch", 0);
                """);
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "Submarine"), modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);
        string toolReg = PatchBuilder.ReadCode(merged, "gml_Script_jamm_tool_register").Text;
        Assert.Contains("ToolData.unlocked[jamm_slot] = jamm_unl", toolReg);
        Assert.Contains("ToolData.jamm_default_unlocked[jamm_slot] = jamm_unl", toolReg);

        string loadScript = PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_load").Text;
        Assert.Contains("\"u:boat:\"", loadScript);
        Assert.Contains("\"eq:boat\"", loadScript);
        Assert.Contains("\"u:tool:\"", loadScript);
        Assert.Contains("ToolData.unlocked[jamm_i] = ToolData.jamm_default_unlocked[jamm_i]", loadScript);
        Assert.Contains("ToolData.item[jamm_i].active = 0", loadScript);
    }

    [Fact]
    public void MissingOrChangedAnchorThrowsInvalidDataExceptionNamingCodeEntry()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");

        WriteSyntheticGame(basePath, d =>
        {
            Compile(d, d.Code.ByName("gml_Object_RodCard_Other_11")!, "draw_self();");
        });
        WriteSyntheticGame(modPath, d =>
        {
            AddApiPlaceholders(d);
            Compile(d, d.Code.ByName("gml_Object_RodCard_Other_11")!, "draw_self();");
            AddScript(d, "jamm_init_broken", "jamm_rod_register(\"x:r\", \"R\", 1, 1, 1, 0, 1, 100, -1);");
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var ex = Assert.Throws<InvalidDataException>(() =>
            ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "Broken"), modPath)], temp.PathFor("out.win")));
        Assert.Contains("gml_Object_RodCard_Other_11", ex.Message);

        string missingSaveBase = temp.PathFor("missing-save-base.win");
        string missingSaveMod = temp.PathFor("missing-save-mod.win");
        WriteMinimalDataAndSaveStubs(missingSaveBase, includeSaveOther14: false);
        WriteMinimalDataAndSaveStubs(missingSaveMod, includeSaveOther14: false, edit: d =>
        {
            AddApiPlaceholders(d);
            AddScript(d, "jamm_init_min", "jamm_boat_register(\"m:b\", \"B\", 10, -1, BoatData, \"D\");");
        });

        using var orig2 = GameFileLoader.Load(missingSaveBase);
        using var mod2 = GameFileLoader.Load(missingSaveMod);
        var ex2 = Assert.Throws<InvalidDataException>(() =>
            ArchiveBuilder.Build(missingSaveBase, [new(PatchBuilder.Create(orig2, mod2, "Min"), missingSaveMod)], temp.PathFor("out2.win")));
        Assert.Contains("gml_Object_SaveManager_Other_14", ex2.Message);
    }

    [Fact]
    public void MinimalStubsWithoutOptionalObjectsStillCompileAndVerify()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("min-base.win");
        string modPath = temp.PathFor("min-mod.win");
        WriteMinimalDataAndSaveStubs(basePath, includeSaveOther14: true);
        WriteMinimalDataAndSaveStubs(modPath, includeSaveOther14: true, edit: d =>
        {
            AddApiPlaceholders(d);
            var obj = AddObject(d, "obj_min_boat");
            AddScript(d, "jamm_init_min", $"jamm_boat_register(\"min:raft\", \"Min Raft\", 100, -1, {obj.Name.Content}, \"Minimal\");");
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        string output = temp.PathFor("min-merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "Min"), modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_api_init"));
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_items_load"));
        Assert.DoesNotContain("gear_equip()", PatchBuilder.ReadCode(merged, "gml_Script_jamm_items_load").Text);
    }

    [Fact]
    public void CatalogApiAndItemsApiCombineCleanlyInSameBuild()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("combo-base.win");
        string catalogModPath = temp.PathFor("combo-cat.win");
        string itemsModPath = temp.PathFor("combo-items.win");

        WriteSyntheticGame(basePath, d =>
        {
            AddObject(d, "CatalogData");
            AddObject(d, "BaseFish");
            CompileObj(d, "CatalogData", EventType.Create, 0,
                "object[0, 0] = BaseFish;\nname[0, 0] = \"BaseFish\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"Base\";");
            var load = d.Code.ByName("gml_Object_SaveManager_Other_13")!;
            var save14 = d.Code.ByName("gml_Object_SaveManager_Other_14")!;
            string loadText = new Underanalyzer.Decompiler.DecompileContext(new GlobalDecompileContext(d), load).DecompileToString();
            string save14Text = new Underanalyzer.Decompiler.DecompileContext(new GlobalDecompileContext(d), save14).DecompileToString();
            Compile(d, load, loadText.Replace("ini_close();", "with (CatalogData) { i = 0; j = 0; count[i, j] = ini_read_real(\"Catalog\", \"f\" + string(i) + string(j), -1); }\nini_close();", StringComparison.Ordinal));
            Compile(d, save14, save14Text.Replace("ini_close();\n    ini_open(\"meta.ini\");", "with (CatalogData) { i = 0; j = 0; ini_write_real(\"Catalog\", \"f\" + string(i) + string(j), count[i, j]); }\n    ini_close();\n    ini_open(\"meta.ini\");", StringComparison.Ordinal));
        });

        WriteSyntheticGame(catalogModPath, d =>
        {
            AddObject(d, "CatalogData");
            AddObject(d, "BaseFish");
            AddObject(d, "ModFish");
            CompileObj(d, "CatalogData", EventType.Create, 0,
                "object[0, 0] = BaseFish;\nname[0, 0] = \"BaseFish\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"Base\";\nobject[0, 1] = ModFish;\nname[0, 1] = \"ModFish\";\nmap[0, 1] = 1;\ndesc[0, 1] = \"Mod\";");
            var load = d.Code.ByName("gml_Object_SaveManager_Other_13")!;
            var save14 = d.Code.ByName("gml_Object_SaveManager_Other_14")!;
            string loadText = new Underanalyzer.Decompiler.DecompileContext(new GlobalDecompileContext(d), load).DecompileToString();
            string save14Text = new Underanalyzer.Decompiler.DecompileContext(new GlobalDecompileContext(d), save14).DecompileToString();
            Compile(d, load, loadText.Replace("ini_close();", "with (CatalogData) { i = 0; j = 0; count[i, j] = ini_read_real(\"Catalog\", \"f\" + string(i) + string(j), -1); }\nini_close();", StringComparison.Ordinal));
            Compile(d, save14, save14Text.Replace("ini_close();\n    ini_open(\"meta.ini\");", "with (CatalogData) { i = 0; j = 0; ini_write_real(\"Catalog\", \"f\" + string(i) + string(j), count[i, j]); }\n    ini_close();\n    ini_open(\"meta.ini\");", StringComparison.Ordinal));
        });

        WriteSyntheticGame(itemsModPath, d =>
        {
            AddObject(d, "CatalogData");
            AddObject(d, "BaseFish");
            CompileObj(d, "CatalogData", EventType.Create, 0,
                "object[0, 0] = BaseFish;\nname[0, 0] = \"BaseFish\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"Base\";");
            var load = d.Code.ByName("gml_Object_SaveManager_Other_13")!;
            var save14 = d.Code.ByName("gml_Object_SaveManager_Other_14")!;
            string loadText = new Underanalyzer.Decompiler.DecompileContext(new GlobalDecompileContext(d), load).DecompileToString();
            string save14Text = new Underanalyzer.Decompiler.DecompileContext(new GlobalDecompileContext(d), save14).DecompileToString();
            Compile(d, load, loadText.Replace("ini_close();", "with (CatalogData) { i = 0; j = 0; count[i, j] = ini_read_real(\"Catalog\", \"f\" + string(i) + string(j), -1); }\nini_close();", StringComparison.Ordinal));
            Compile(d, save14, save14Text.Replace("ini_close();\n    ini_open(\"meta.ini\");", "with (CatalogData) { i = 0; j = 0; ini_write_real(\"Catalog\", \"f\" + string(i) + string(j), count[i, j]); }\n    ini_close();\n    ini_open(\"meta.ini\");", StringComparison.Ordinal));

            AddApiPlaceholders(d);
            var boatObj = AddObject(d, "obj_combo_boat");
            AddScript(d, "jamm_init_combo", $"jamm_boat_register(\"combo:boat\", \"Combo Boat\", 500, -1, {boatObj.Name.Content}, \"Combo\");");
        });

        using var original = GameFileLoader.Load(basePath);
        using var catMod = GameFileLoader.Load(catalogModPath);
        using var itemsMod = GameFileLoader.Load(itemsModPath);
        string output = temp.PathFor("combo-merged.win");
        var build = ArchiveBuilder.Build(basePath,
            [new(PatchBuilder.Create(original, catMod, "Cat"), catalogModPath), new(PatchBuilder.Create(original, itemsMod, "Items"), itemsModPath)],
            output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_catalog_register"));
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_api_init"));
        string save13 = PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_13").Text;
        string save14Merged = PatchBuilder.ReadCode(merged, "gml_Object_SaveManager_Other_14").Text;
        Assert.Contains("jamm_catalog_read(", save13);
        Assert.Contains("jamm_items_load()", save13);
        Assert.Contains("\"JAMM_Catalog\"", save14Merged);
        Assert.Contains("jamm_items_save()", save14Merged);
    }

    private static void WriteMinimalDataAndSaveStubs(string path, bool includeSaveOther14, Action<UndertaleData>? edit = null)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;

        foreach (string name in new[]
        {
            "fish", "BoatData", "RodData", "LureData", "ToolData", "GearData", "UpgradeData",
            "SaveManager", "Girl", "Rod", "Boat", "Analytics", "GameModeMenu", "Gear"
        })
            AddObject(data, name);

        CompileObj(data, "BoatData", EventType.Create, 0, "unlocked[0] = 1; cost[0] = 0; name[0] = \"No Boat\"; object[0] = -1; disp[0] = -1; desc[0] = \"\"; nItems = array_length_1d(unlocked);");
        CompileObj(data, "RodData", EventType.Create, 0, "nItems = 1; unlocked[0] = 1; cost[0] = 100; cast_speed[0] = 8; rod_power[0] = 1; rod_power_flat[0] = 0; reel_speed[0] = 8; sinker_distance[0] = 1200; name[0] = \"Basic\";");
        CompileObj(data, "LureData", EventType.Create, 0, "unlocked[0] = 1; cost[0] = 100; name[0] = \"Rocket\"; item[0] = -1; disp[0] = -1; desc[0] = \"\"; nItems = array_length_1d(unlocked);");
        CompileObj(data, "ToolData", EventType.Create, 0, "nItems = 1; unlocked[0] = 0; cost[0] = 500; quests[0] = 0; name[0] = \"Mini\"; item[0] = -1; message[0] = -1; desc[0] = \"\";");
        CompileObj(data, "GearData", EventType.Create, 0, "n_gear = 1; unlocked[0] = 1; gear[0] = -1; value[0] = 100; weight[0] = 1; range_min[0] = 0; range_max[0] = 1; offset_x[0] = 0; offset_y[0] = 0; mythic[0] = 0; map[0] = 0;");
        CompileObj(data, "UpgradeData", EventType.Create, 0, "unlocks[0] = 1; name[0] = \"Scout\"; desc[0] = \"\"; desc2[0] = \"\"; cost[0] = 0.1; upg[0, 0] = -1; object[0, 0] = -1;");

        CompileObj(data, "SaveManager", EventType.Step, (uint)EventSubtypeStep.Step, "if (!loaded) { loaded = 1; }");
        CompileObj(data, "SaveManager", EventType.Other, 13, """
            var sf = 1;
            var lf = 1;
            if (sf > 0 || lf > 0)
            {
                gear_id = ini_read_real("Main", "gear", -1);
                if (gear_id >= 0) { }
                with (RodData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "rod" + string(i), -1); } }
                active_rod = ini_read_real("Main", "activerod", -1);
                if (active_rod >= 0) { Rod.rod_id = active_rod; }
                with (LureData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "lure" + string(i), -1); } }
                with (BoatData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "boat" + string(i), -1); } }
                var bi = ini_read_real("Main", "boat", -1);
                if (bi > 0 && BoatData.object[bi] > 0) { instance_create(0, 0, BoatData.object[bi]); }
                with (ToolData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "tool" + string(i), -1); } }
                with (GearData) { for (i = 0; i < n_gear; i++) { unlocked[i] = ini_read_real("Hatalog", "hat" + string(i), 0); } }
            }
            else
            {
                with (Analytics) { event_perform(ev_other, ev_user0); }
                instance_create(0, 0, GameModeMenu);
            }
            ini_close();
            """);
        if (includeSaveOther14)
        {
            CompileObj(data, "SaveManager", EventType.Other, 14, """
                if (!saving_locked)
                {
                    ini_open("s.ini");
                    with (Girl)
                    {
                        ini_write_real("Main", "activerod", Rod.rod_id);
                        if (gear_id >= 0) { ini_write_real("Main", "gear", gear_id); }
                    }
                    with (RodData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "rod" + string(i), unlocked[i]); } }
                    with (BoatData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "boat" + string(i), unlocked[i]); } }
                    if (instance_exists(Boat)) { ini_write_real("Main", "boat", Boat.boat_index); }
                    with (LureData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "lure" + string(i), unlocked[i]); } }
                    with (ToolData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "tool" + string(i), unlocked[i]); } }
                    with (GearData) { for (i = 0; i < n_gear; i++) { ini_write_real("Hatalog", "hat" + string(i), unlocked[i]); } }
                    ini_close();
                    ini_open("meta.ini");
                    ini_close();
                }
                """);
        }
        CompileObj(data, "SaveManager", EventType.Other, 15, """
            if (!saving_locked)
            {
                ini_open("b.ini");
                with (Girl)
                {
                    ini_write_real("Main", "activerod", Rod.rod_id);
                    if (gear_id >= 0) { ini_write_real("Main", "gear", gear_id); }
                }
                with (RodData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "rod" + string(i), unlocked[i]); } }
                with (BoatData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "boat" + string(i), unlocked[i]); } }
                with (LureData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "lure" + string(i), unlocked[i]); } }
                with (ToolData) { for (i = 0; i < nItems; i++) { ini_write_real("Unlocks", "tool" + string(i), unlocked[i]); } }
                with (GearData) { for (i = 0; i < n_gear; i++) { ini_write_real("Hatalog", "hat" + string(i), unlocked[i]); } }
                ini_close();
                ini_open("meta.ini");
                ini_close();
            }
            """);

        edit?.Invoke(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static void WriteSyntheticGame(string path, Action<UndertaleData>? edit = null)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;

        foreach (string name in new[]
        {
            "fish", "BoatData", "RodData", "LureData", "ToolData", "GearData", "UpgradeData",
            "SaveManager", "Girl", "Rod", "Boat", "Lure", "Gear", "Analytics", "GameModeMenu",
            "RodCard", "ToolCard", "BoatCard", "LureCard", "Rocket", "DistQuest"
        })
            AddObject(data, name);

        var sprRod = AddSprite(data, "spr_rod");
        var sprTools = AddSprite(data, "spr_tools");
        var sprBoat0 = AddSprite(data, "spr_noboat");
        var sprLure0 = AddSprite(data, "spr_lure0");
        AddObject(data, "Boat1");
        AddObject(data, "LureRocket");
        AddObject(data, "Minimap");
        AddObject(data, "Message_Minimap");
        AddObject(data, "Gear0");

        AddScript(data, "gear_equip", "Girl.gear = id; Girl.gear_id = gear_id;");
        AddScript(data, "steam_set_achievement", "return 0;");
        AddScript(data, "steam_inc_stat", "return 0;");
        AddScript(data, "start_quest_dist", "dist[0] = 1; dpth[0] = 1; q = instance_create(0, 0, DistQuest); q.goal = dist[Girl.bestRod]; q.goal = dpth[Girl.bestRod]; return q;");
        AddScript(data, "start_quest_rods", "var index = Girl.bestRod + 1; q = instance_create(0, 0, DistQuest); q.cash = min(RodData.cost[index] / 3, 1000); return q;");
        AddScript(data, "boat_was_unlocked", "steam_set_achievement(\"boat_\" + string(argument0));");
        AddScript(data, "rod_was_unlocked", "steam_set_achievement(\"rod_\" + string(argument0));");
        AddScript(data, "lure_was_purchased", "steam_inc_stat(\"lp_\" + string(argument0 + 1), 1);\nsteam_set_achievement(\"lure_\" + string(argument0));");
        AddScript(data, "tool_was_purchased", "steam_set_achievement(\"tool_\" + string(argument0 + 1));");

        CompileObj(data, "BoatData", EventType.Create, 0, $"unlocked[0] = 1; unlocked[1] = 0; cost[0] = 0; cost[1] = 1000; name[0] = \"No Boat\"; name[1] = \"Basic\"; object[0] = -1; object[1] = Boat1; disp[0] = {sprBoat0.Name.Content}; disp[1] = {sprBoat0.Name.Content}; desc[0] = \"\"; desc[1] = \"\"; nItems = array_length_1d(unlocked);");
        CompileObj(data, "RodData", EventType.Create, 0, "nItems = 2; unlocked[0] = 1; unlocked[1] = 0; cost[0] = 100; cost[1] = 500; cast_speed[0] = 8; cast_speed[1] = 10; rod_power[0] = 1; rod_power[1] = 1.5; rod_power_flat[0] = 0; rod_power_flat[1] = 0; reel_speed[0] = 8; reel_speed[1] = 9; sinker_distance[0] = 1200; sinker_distance[1] = 2400; name[0] = \"Basic\"; name[1] = \"Good\";");
        CompileObj(data, "LureData", EventType.Create, 0, $"unlocked[0] = 1; cost[0] = 400; name[0] = \"Rocket\"; item[0] = LureRocket; disp[0] = {sprLure0.Name.Content}; desc[0] = \"\"; nItems = array_length_1d(unlocked);");
        CompileObj(data, "ToolData", EventType.Create, 0, "nItems = 1; unlocked[0] = 0; cost[0] = 5000; quests[0] = 10; name[0] = \"Minimap\"; item[0] = Minimap; message[0] = Message_Minimap; desc[0] = \"\";");
        CompileObj(data, "GearData", EventType.Create, 0, "n_gear = 1; unlocked[0] = 1; gear[0] = Gear0; value[0] = 1200; weight[0] = 12; range_min[0] = 0; range_max[0] = 0.2; offset_x[0] = 0; offset_y[0] = 0; mythic[0] = 0; map[0] = 0;");
        CompileObj(data, "UpgradeData", EventType.Create, 0, "unlocks[0] = 1; name[0] = \"Scouting\"; desc[0] = \"\"; desc2[0] = \"\"; cost[0] = 0.05; upg[0, 0] = -1; object[0, 0] = -1;");

        CompileObj(data, "SaveManager", EventType.Step, (uint)EventSubtypeStep.Step, "Girl.time = current_time;\nif (!loaded) { loaded = 1; }");
        CompileObj(data, "SaveManager", EventType.Other, 13, """
            var sf = 1;
            var lf = 1;
            if (sf > 0 || lf > 0)
            {
                gear_id = ini_read_real("Main", "gear", -1);
                if (gear_id >= 0) { gear = instance_create(x, y, GearData.gear[gear_id]); gear.gear_id = gear_id; with (gear) { gear_equip(); } }
                with (RodData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "rod" + string(i), -1); if (unlocked[i]) { Rod.rod_id = i; Girl.bestRod = i; steam_set_achievement("rod_" + string(i)); } } }
                active_rod = ini_read_real("Main", "activerod", -1);
                if (active_rod >= 0) { Rod.rod_id = active_rod; }
                with (LureData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "lure" + string(i), -1); } }
                with (BoatData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "boat" + string(i), -1); if (unlocked[i]) { Girl.bestBoat = i; steam_set_achievement("boat_" + string(i)); } } }
                var bi = ini_read_real("Main", "boat", -1);
                if (bi > 0 && BoatData.object[bi] > 0) { instance_create(0, 0, BoatData.object[bi]); }
                with (ToolData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "tool" + string(i), -1); if (unlocked[i]) { ToolData.item[i].active = 1; steam_set_achievement("tool_" + string(i + 1)); } } }
                with (GearData) { for (i = 0; i < n_gear; i++) { unlocked[i] = ini_read_real("Hatalog", "hat" + string(i), 0); } }
            }
            else
            {
                with (Analytics) { event_perform(ev_other, ev_user0); }
                instance_create(0, 0, GameModeMenu);
            }
            ini_close();
            """);
        CompileObj(data, "SaveManager", EventType.Other, 14, """
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
                with (GearData) { for (i = 0; i < n_gear; i++) { ini_write_real("Hatalog", "hat" + string(i), unlocked[i]); } }
                ini_close();
                ini_open("meta.ini");
                ini_close();
            }
            """);
        CompileObj(data, "SaveManager", EventType.Other, 15, """
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
                with (GearData) { for (i = 0; i < n_gear; i++) { ini_write_real("Hatalog", "hat" + string(i), unlocked[i]); } }
                ini_close();
                ini_open("meta.ini");
                ini_close();
            }
            """);

        CompileObj(data, "RodCard", EventType.Other, 11, $"draw_sprite_ext({sprRod.Name.Content}, item_id, x + 42, y + 116, 0.5, 0.5, 45, c_white, 1);");
        CompileObj(data, "Rod", EventType.Other, 10, "image_index = rod_id;");
        CompileObj(data, "ToolCard", EventType.Other, 11, $"draw_sprite_ext({sprTools.Name.Content}, item_id, x + 76, y + 82, 1, 1, 0, c_white, 1);");
        CompileObj(data, "BoatCard", EventType.Other, 11, "draw_sprite_ext(BoatData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);");
        CompileObj(data, "LureCard", EventType.Other, 11, "draw_sprite_ext(LureData.disp[item_id], 0, x + 76, y + 85, 1, 1, 0, c_white, 1);");
        CompileObj(data, "BoatCard", EventType.Other, 10, "if (BoatData.object[item_id] > 0) { instance_create(0, 0, BoatData.object[item_id]); }");
        CompileObj(data, "ToolCard", EventType.Other, 10, "if (ToolData.unlocked[item_id]) { with (ToolData.item[item_id]) { active = !active; } instance_create(0, 0, ToolData.message[item_id]); }");
        CompileObj(data, "Girl", EventType.Other, 10, """
            if (gear < 0)
            {
                gear_lure_scalar[0] = 1;
                gear_lure_scalar[1] = 1;
                gear_lure_scalar[2] = 1;
                gear_lure_scalar[3] = 1;
                gear_lure_scalar[4] = 1;
                gear_lure_scalar[5] = 1;
            }
            else
            {
                gear_lure_scalar[0] = gear.lure_scalar[0];
                gear_lure_scalar[1] = gear.lure_scalar[1];
                gear_lure_scalar[2] = gear.lure_scalar[2];
                gear_lure_scalar[3] = gear.lure_scalar[3];
                gear_lure_scalar[4] = gear.lure_scalar[4];
            }
            """);
        CompileObj(data, "Gear", EventType.Other, 15, """
            lure_scalar[0] = 1;
            lure_scalar[1] = 1;
            lure_scalar[2] = 1;
            lure_scalar[3] = 1;
            lure_scalar[4] = 1;
            """);
        CompileObj(data, "Rocket", EventType.Step, (uint)EventSubtypeStep.EndStep, """
            if (Lure.state == 1)
            {
                target_distance[Rod.rod_id] += Girl.x;
                time = (target_distance[Rod.rod_id] - Lure.x) / Lure.hspeed;
                if (fuel > 0 && Lure.x < target_distance[Rod.rod_id] && Lure.vspeed > target_y_velocity && delta_y > 0 && Lure.hspeed > 0) { fuel -= 1; }
            }
            """);
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

    private static UndertaleSprite AddSprite(UndertaleData d, string name)
    {
        var existing = d.Sprites.ByName(name);
        if (existing is not null) return existing;
        var spr = new UndertaleSprite { Name = d.Strings.MakeString(name), Width = 1, Height = 1 };
        d.Sprites.Add(spr);
        return spr;
    }

    private static void AddApiPlaceholders(UndertaleData d, string body = "return -1;")
    {
        foreach (string name in new[]
        {
            "jamm_boat_register", "jamm_rod_register", "jamm_lure_register",
            "jamm_tool_register", "jamm_hat_register", "jamm_item_find"
        })
            AddScript(d, name, body);
    }

    private static void AddScript(UndertaleData d, string name, string gml)
    {
        string codeName = "gml_Script_" + name;
        var code = d.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(d, codeName);
        var existing = d.Scripts.ByName(name);
        if (existing is null) d.Scripts.Add(new UndertaleScript { Name = d.Strings.MakeString(name), Code = code });
        else existing.Code = code;
        Compile(d, code, gml);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cgfmm-items-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
