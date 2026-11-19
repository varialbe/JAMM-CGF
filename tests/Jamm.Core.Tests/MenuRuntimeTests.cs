using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies MenuRuntime paging injection, compilation, and no-op behavior when menu objects are absent.</summary>
public sealed class MenuRuntimeTests
{
    [Fact]
    public void SyntheticArchiveWithMenuObjectsCompilesAndInjectsPaging()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteMenuModel(basePath, includeFont: true, includeHud: false, modEdit: null);
        WriteMenuModel(modPath, includeFont: true, includeHud: false, modEdit: d =>
        {
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("BoatModMarker") });
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "TestMod");

        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.Contains("Menu paging:", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(output);

        var cardHandler = merged.Data.GameObjects.ByName("CardHandler");
        Assert.NotNull(cardHandler);
        Assert.Equal(MenuRuntime.CardHandlerStep, cardHandler.Events[(int)EventType.Step].Single(e => e.EventSubtype == 0).Actions[0].CodeId.Name.Content);
        Assert.Equal(MenuRuntime.CardHandlerDraw, cardHandler.Events[(int)EventType.Draw].Single(e => e.EventSubtype == (uint)EventSubtypeDraw.DrawGUI).Actions[0].CodeId.Name.Content);

        var upgradeMenu = merged.Data.GameObjects.ByName("UpgradeMenu");
        Assert.NotNull(upgradeMenu);
        Assert.Equal(MenuRuntime.UpgradeMenuStep, upgradeMenu.Events[(int)EventType.Step].Single(e => e.EventSubtype == 0).Actions[0].CodeId.Name.Content);
        Assert.Equal(MenuRuntime.UpgradeMenuDraw, upgradeMenu.Events[(int)EventType.Draw].Single(e => e.EventSubtype == (uint)EventSubtypeDraw.DrawGUI).Actions[0].CodeId.Name.Content);

        string chCreate = PatchBuilder.ReadCode(merged, MenuRuntime.CardHandlerCreate).Text;
        Assert.Contains("visible_rows = 2", chCreate);
        Assert.Contains("scroll_row = 0", chCreate);
        Assert.Contains("max_scroll_row = 0", chCreate);
        Assert.Contains("jamm_menu_init = 0", chCreate);

        string chAlarm = PatchBuilder.ReadCode(merged, MenuRuntime.CardHandlerAlarm).Text;
        Assert.Contains("visible_items[n_visible]", chAlarm);
        Assert.Contains("max_scroll_row = max(0,", chAlarm);
        Assert.Contains("ceil(jamm_total_rows / visible_rows) - 1", chAlarm);
        Assert.Contains("window_pos = scroll_row * visible_rows * card_width", chAlarm);
        Assert.Contains("window_end = min(n_visible, (scroll_row + 1) * visible_rows * card_width)", chAlarm);
        Assert.Contains("window_pos < window_end", chAlarm);
        Assert.Contains("done_spawning = 1", chAlarm);
        Assert.DoesNotContain("iCard + 1", chAlarm);

        string chStep = PatchBuilder.ReadCode(merged, MenuRuntime.CardHandlerStep).Text;
        Assert.Contains("mouse_wheel_up()", chStep);
        Assert.Contains("mouse_wheel_down()", chStep);
        Assert.Contains("vk_up", chStep);
        Assert.Contains("vk_down", chStep);
        Assert.Contains("mouse_check_button_pressed(mb_left)", chStep);
        Assert.Contains("device_mouse_x_to_gui(0)", chStep);
        Assert.Contains("display_get_gui_width()", chStep);
        Assert.Contains("with (Card)", chStep);
        Assert.Contains("done_spawning = 0", chStep);
        Assert.Contains("window_pos = scroll_row * visible_rows * card_width", chStep);
        Assert.Contains("alarm[0] = 1", chStep);

        string chDraw = PatchBuilder.ReadCode(merged, MenuRuntime.CardHandlerDraw).Text;
        Assert.Contains("max_scroll_row > 0", chDraw);
        Assert.Contains("draw_triangle(", chDraw);
        Assert.Contains("\"Page \"", chDraw);
        Assert.Contains("display_get_gui_width()", chDraw);
        Assert.Contains("Page ", chDraw);

        string rodDraw = PatchBuilder.ReadCode(merged, MenuRuntime.RodMenuDraw).Text;
        Assert.Contains("event_inherited()", rodDraw);

        // UpgradeData Create stays untouched (mods edit it); the menu counts upgrades at runtime.
        Assert.DoesNotContain("nItems", PatchBuilder.ReadCode(merged, "gml_Object_UpgradeData_Create_0").Text);

        string umCreate = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuCreate).Text;
        Assert.Contains("ORIGIN_Y = y", umCreate);
        Assert.Contains("card_width = 2", umCreate);
        Assert.Contains("visible_rows = 9", umCreate);

        string umAlarm = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuAlarm).Text;
        Assert.Contains("array_length_1d(UpgradeData.unlocks)", umAlarm);
        Assert.Contains("spawn_order[jamm_i] = jamm_i", umAlarm);
        Assert.Contains("UpgradeData.unlocks[jamm_id]", umAlarm);
        Assert.Contains("visible_items[window_pos]", umAlarm);
        Assert.Contains("window_pos = scroll_row * visible_rows * card_width", umAlarm);
        Assert.DoesNotContain("17", umAlarm);

        string umStep = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuStep).Text;
        Assert.Contains("with (UpgradeCard)", umStep);
        Assert.Contains("done_spawning = 0", umStep);

        string umDraw = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuDraw).Text;
        Assert.Contains("\"Page \"", umDraw);
        Assert.Contains("draw_triangle(", umDraw);
    }

    [Fact]
    public void SyntheticArchiveWithoutSmallFontStillCompilesAndVerifies()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteMenuModel(basePath, includeFont: false, includeHud: false, modEdit: null);
        WriteMenuModel(modPath, includeFont: false, includeHud: false, modEdit: d => d.GameObjects[0].Depth = 5);

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "NoFontMod");

        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);
        string chDraw = PatchBuilder.ReadCode(merged, MenuRuntime.CardHandlerDraw).Text;
        Assert.Contains("draw_triangle(", chDraw);
        Assert.DoesNotContain("fnt_babySmall", chDraw);
    }

    [Fact]
    public void BuildWithoutMenuObjectsLeavesArchiveUnchanged()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteMinimalModel(basePath, _ => { });
        WriteMinimalModel(modPath, d => d.GameObjects[0].Depth = 3);

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "PlainMod");

        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.DoesNotContain(build.Plan.Decisions, d => d.Contains("Menu paging:", StringComparison.Ordinal));
        Assert.DoesNotContain(build.Plan.Assets, a => a.Name.Contains("CardHandler", StringComparison.Ordinal) || a.Name.Contains("UpgradeMenu", StringComparison.Ordinal) || a.Name.Contains("UpgradeSlotMenu", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(output);
        Assert.Null(merged.Data.Code.ByName(MenuRuntime.CardHandlerStep));
        Assert.Null(merged.Data.Code.ByName(MenuRuntime.CardHandlerDraw));
        Assert.Null(merged.Data.Code.ByName(MenuRuntime.UpgradeMenuStep));
        Assert.Null(merged.Data.Code.ByName(MenuRuntime.UpgradeMenuDraw));
        Assert.Null(merged.Data.Code.ByName(MenuRuntime.UpgradeSlotMenuStep));
        Assert.Null(merged.Data.Code.ByName(MenuRuntime.UpgradeSlotMenuDraw));
    }

    [Fact]
    public void MissingExpectedAnchorThrowsInvalidDataExceptionNamingCodeEntry()
    {
        var cardBaseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [MenuRuntime.CardHandlerCreate] = new("card_width = 3;\ndone_spawning = 0;", "gml", 0, 0, null, []),
            [MenuRuntime.CardHandlerAlarm] = new("// unexpected body", "gml", 0, 0, null, [])
        };
        var cardPlan = MenuRuntime.PreparePlan(new BuildPlan([], []), cardBaseline);
        Assert.Empty(cardPlan.Assets);
        Assert.Contains(cardPlan.Decisions, d => d.Contains("Menu paging skipped", StringComparison.Ordinal) && d.Contains(MenuRuntime.CardHandlerAlarm, StringComparison.Ordinal));

        var upgradeBaseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [MenuRuntime.UpgradeMenuCreate] = new("spawn_order[17] = 17;\nspawns = 0;", "gml", 0, 0, null, []),
            [MenuRuntime.UpgradeMenuAlarm] = new("// unexpected body", "gml", 0, 0, null, [])
        };
        var upgradePlan = MenuRuntime.PreparePlan(new BuildPlan([], []), upgradeBaseline);
        Assert.Empty(upgradePlan.Assets);
        Assert.Contains(upgradePlan.Decisions, d => d.Contains("Menu paging skipped", StringComparison.Ordinal) && d.Contains(MenuRuntime.UpgradeMenuAlarm, StringComparison.Ordinal));
    }

    [Fact]
    public void UpgradeMenuPagingAcceptsModsThatAlreadyAddedUpgrades()
    {
        // e.g. Pondweed Basin appends upgrade 18 legacy-style: spawn_order[18] and iCard <= 18.
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [MenuRuntime.UpgradeMenuCreate] = new("spawn_order[17] = 17;\nspawn_order[18] = 18;\nspawns = 0;", "gml", 0, 0, null, []),
            [MenuRuntime.UpgradeMenuAlarm] = new("if (UpgradeData.unlocks[spawn_order[iCard]]) { }\niCard++;\nif (iCard <= 18) { alarm[0] = DELAY; }", "gml", 0, 0, null, [])
        };
        var plan = MenuRuntime.PreparePlan(new BuildPlan([], []), baseline);
        var alarm = Assert.Single(plan.Assets, a => a.Name == MenuRuntime.UpgradeMenuAlarm);
        Assert.Contains("array_length_1d(spawn_order)", alarm.Gml);
        Assert.DoesNotContain(plan.Decisions, d => d.Contains("skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void LayoutRepairsUseGuiControlsFishProvenanceAndValidBoatEquipIndex()
    {
        const string waitCreate = "gml_Object_icon_Wait_Create_0";
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [MenuRuntime.BoatCardClick] = new("instance_create(0, 0, BoatData.object[item_id]);", "gml", 0, 0, null, []),
            [MenuRuntime.BoatDataCreate] = new("name[8] = \"Dark Boat\";\nobject[8] = BoatBackground;\ndisp[8] = bg_parallax;\ndesc[8] = \"The most op Boat\";", "gml", 0, 0, null, []),
            [waitCreate] = new("hidden_y = 0;", "gml", 0, 0, null, []),
            [MenuRuntime.FishalogDraw] = new("if (!surface_exists(csurf)) { csurf = surface_create(570, 274); } scroll_height = py - scroll_y - CatalogItem.sprite_height; draw_surface(csurf, x + surfx, y + surfy); draw_text(x + 475, y + 10, \"Completion: \" + string(floor(Catalog.fish_completion[selected] * 100)) + \"%\");", "gml", 0, 0, null, []),
            [MenuRuntime.FishalogHandleStep] = new("if (ys < 0) ys = 0; if (ys > ymax) ys = ymax; y = yoffset + ys; ymax = Catalog.scroll_height * 0.03; image_yscale = height - ymax; if (!clicked) ys = Catalog.scroll_y * -0.03;", "gml", 0, 0, null, []),
            [MenuRuntime.FishalogHandleDrag] = new("Catalog.scroll_y = ys / -0.03;", "gml", 0, 0, null, []),
            [MenuRuntime.CatalogItemDraw] = new("draw_text_ext(220, Catalog.py + 40, desc, 14, 330); Catalog.py += sprite_height;", "gml", 0, 0, null, [])
        };
        var plan = new BuildPlan([], []) { Catalog = [new("Harbor Pack", "jmk_fish_glowfin", 2, 5, 67)] };

        var patched = MenuRuntime.PreparePlan(plan, baseline);

        Assert.Contains("boat_index = item_id", Assert.Single(patched.Assets, a => a.Name == MenuRuntime.BoatCardClick).Gml);
        string boats = Assert.Single(patched.Assets, a => a.Name == MenuRuntime.BoatDataCreate).Gml!;
        Assert.Contains("name[8] = \"Neon Boat\"", boats);
        Assert.Contains("object[8] = Boat8", boats);
        Assert.Contains("disp[8] = spr_boat_icon8", boats);
        Assert.DoesNotContain(patched.Assets, a => a.Name == waitCreate);
        string fishalog = Assert.Single(patched.Assets, a => a.Name == MenuRuntime.FishalogDraw).Gml!;
        Assert.Contains("surface_create(surfw, surfh)", fishalog);
        Assert.Contains("max(0, py - scroll_y", fishalog);
        Assert.Contains("x + 475, y + 10", fishalog);
        Assert.Contains("Catalog.surfh / max(1, Catalog.surfh + Catalog.scroll_height)", Assert.Single(patched.Assets, a => a.Name == MenuRuntime.FishalogHandleStep).Gml);
        Assert.Contains("Catalog.scroll_height : 0", Assert.Single(patched.Assets, a => a.Name == MenuRuntime.FishalogHandleDrag).Gml);
        string fish = Assert.Single(patched.Assets, a => a.Name == MenuRuntime.CatalogItemDraw).Gml!;
        Assert.Contains("size == 2 && index == 67", fish);
        Assert.Contains("Added by Harbor Pack", fish);
    }

    [Fact]
    public void MenuRuntimeComposesWithHudRuntimeInSameBuild()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteMenuModel(basePath, includeFont: true, includeHud: true, modEdit: null);
        WriteMenuModel(modPath, includeFont: true, includeHud: true, modEdit: d =>
        {
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("ExtraModObject") });
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "HudAndMenuMod");

        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);
        string chDraw = PatchBuilder.ReadCode(merged, MenuRuntime.CardHandlerDraw).Text;
        Assert.Contains("draw_triangle(", chDraw);
    }

    [Fact]
    public void PagingHidesHatButtonsCentersPageLabelNearGridAndSyncsPageTwoAnchor()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteMenuModel(basePath, includeFont: true, includeHud: true, modEdit: d =>
        {
            foreach (string icon in new[] { "icon_Hats", "icon_HatsNew", "icon_Cancel" })
                d.GameObjects.Add(new() { Name = d.Strings.MakeString(icon), Visible = true });
            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(d.GameObjects.ByName("icon_HatsNew").EventHandlerFor(EventType.Step, d), "visible = true; y = 3;");
            compiler.QueueCodeReplace(d.GameObjects.ByName("Handler").EventHandlerFor(EventType.Destroy, 0u, d), "with (Card) { instance_destroy(); }");
            compiler.QueueCodeReplace(d.GameObjects.ByName("CardHandler").EventHandlerFor(EventType.Destroy, 0u, d), "event_inherited();\nRod.swing_locked = 0;");
            Assert.True(compiler.Compile().Successful);
        });
        WriteMenuModel(modPath, includeFont: true, includeHud: true, modEdit: d =>
        {
            foreach (string icon in new[] { "icon_Hats", "icon_HatsNew", "icon_Cancel" })
                d.GameObjects.Add(new() { Name = d.Strings.MakeString(icon), Visible = true });
            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(d.GameObjects.ByName("icon_HatsNew").EventHandlerFor(EventType.Step, d), "visible = true; y = 3;");
            compiler.QueueCodeReplace(d.GameObjects.ByName("Handler").EventHandlerFor(EventType.Destroy, 0u, d), "with (Card) { instance_destroy(); }");
            compiler.QueueCodeReplace(d.GameObjects.ByName("CardHandler").EventHandlerFor(EventType.Destroy, 0u, d), "event_inherited();\nRod.swing_locked = 0;");
            Assert.True(compiler.Compile().Successful);
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "UiMenuMod");
        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);

        using var merged = GameFileLoader.Load(output);

        string umStep = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuStep).Text;
        Assert.Contains("\"icon_Hats\"", umStep);
        Assert.Contains("\"icon_HatsNew\"", umStep);
        Assert.Contains("jamm_menu_hidden = 1", umStep);
        Assert.Contains("\"icon_Rods\"", umStep);

        string hatStep = PatchBuilder.ReadCode(merged, "gml_Object_icon_HatsNew_Step_0").Text;
        Assert.StartsWith("visible = true;\ny = 3;", hatStep);
        Assert.Contains("y = jamm_shop_icon_hidden_y", hatStep);
        Assert.Contains("y = jamm_shop_icon_saved_y", hatStep);
        Assert.Contains("visible = jamm_shop_icon_saved_visible", hatStep);
        Assert.Contains("asset_get_index(\"UpgradeSlotMenu\")", hatStep);
        Assert.Contains("instance_number(jamm_shop_menu_type)", hatStep);
        Assert.Contains("|| !jamm_shop_icon_saved", hatStep);
        Assert.Contains("min(jamm_shop_anchor_instance.y, view_yview[0] - max(view_hview[0], sprite_height * 2) - 8)", hatStep);

        string umAlarm = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuAlarm).Text;
        Assert.Contains("window_pos == (scroll_row * visible_rows * card_width)", umAlarm);
        Assert.Contains("ORIGIN_X = jamm_page_anchor.ORIGIN_X", umAlarm);
        Assert.Contains("ORIGIN_Y = jamm_page_anchor.ORIGIN_Y", umAlarm);

        string umDestroy = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuDestroy).Text;
        Assert.Contains("event_inherited()", umDestroy);
        Assert.Contains("jamm_menu_hidden", umDestroy);

        string chDestroy = PatchBuilder.ReadCode(merged, MenuRuntime.CardHandlerDestroy).Text;
        Assert.Contains("Rod.swing_locked = 0", chDestroy);
        Assert.Contains("jamm_menu_hidden", chDestroy);

        string umDraw = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeMenuDraw).Text;
        Assert.Contains("ORIGIN_X + (card_width * X_GAP)", umDraw);
        Assert.Contains("view_wview[0]", umDraw);
        Assert.Contains("jamm_hud_set_halign(fa_center)", umDraw);
        Assert.Contains("jamm_hud_set_valign(fa_middle)", umDraw);
        Assert.Contains("jamm_hud_set_font(fnt_babySmall)", umDraw);
        Assert.DoesNotContain("display_get_gui_width() - 52", umDraw);
    }

    [Fact]
    public void MenuRuntimeHonorsOverwriteOrderingAndExplicitChoices()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string aPath = temp.PathFor("mod-a.win");
        string bPath = temp.PathFor("mod-b.win");

        WriteMenuModel(basePath, includeFont: true, includeHud: false, modEdit: null);
        WriteMenuModel(aPath, includeFont: true, includeHud: false, modEdit: d =>
        {
            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(d.Code.ByName(MenuRuntime.RodMenuDraw),
                """
                if (done_spawning)
                {
                    draw_set_color(Colors.ui_light);
                    draw_set_halign(fa_center);
                    draw_set_valign(fa_top);
                    draw_set_font(fnt_babySmall);
                    draw_text_shadowed(ORIGIN_X + 640, ORIGIN_Y + 220, "Hint A");
                }
                """);
            Assert.True(compiler.Compile().Successful);
        });
        WriteMenuModel(bPath, includeFont: true, includeHud: false, modEdit: d =>
        {
            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(d.Code.ByName(MenuRuntime.RodMenuDraw),
                """
                if (done_spawning)
                {
                    draw_set_color(Colors.ui_light);
                    draw_set_halign(fa_center);
                    draw_set_valign(fa_top);
                    draw_set_font(fnt_babySmall);
                    draw_text_shadowed(ORIGIN_X + 640, ORIGIN_Y + 220, "Hint B");
                }
                """);
            Assert.True(compiler.Compile().Successful);
        });

        using var orig = GameFileLoader.Load(basePath);
        using var modA = GameFileLoader.Load(aPath);
        using var modB = GameFileLoader.Load(bPath);
        var patchA = PatchBuilder.Create(orig, modA, "ModA");
        var patchB = PatchBuilder.Create(orig, modB, "ModB");

        string outAB = temp.PathFor("ab.win");
        ArchiveBuilder.Build(basePath, [new(patchA, aPath), new(patchB, bPath)], outAB);
        using (var merged = GameFileLoader.Load(outAB))
        {
            string rodDraw = PatchBuilder.ReadCode(merged, MenuRuntime.RodMenuDraw).Text;
            Assert.Contains("Hint B", rodDraw);
            Assert.Contains("event_inherited()", rodDraw);
        }

        string outBA = temp.PathFor("ba.win");
        ArchiveBuilder.Build(basePath, [new(patchB, bPath), new(patchA, aPath)], outBA);
        using (var merged = GameFileLoader.Load(outBA))
        {
            string rodDraw = PatchBuilder.ReadCode(merged, MenuRuntime.RodMenuDraw).Text;
            Assert.Contains("Hint A", rodDraw);
            Assert.Contains("event_inherited()", rodDraw);
        }

        string outResolved = temp.PathFor("resolved.win");
        ArchiveBuilder.Build(basePath, [new(patchA, aPath), new(patchB, bPath)], outResolved,
            resolutions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"code/{MenuRuntime.RodMenuDraw}"] = "ModA"
            });
        using (var merged = GameFileLoader.Load(outResolved))
        {
            string rodDraw = PatchBuilder.ReadCode(merged, MenuRuntime.RodMenuDraw).Text;
            Assert.Contains("Hint A", rodDraw);
            Assert.DoesNotContain("Hint B", rodDraw);
            Assert.Contains("event_inherited()", rodDraw);
        }
    }

    [Fact]
    public void UpgradeSlotMenuPagingHandlesMoreThanEightSlotsAndSkipsBrokenAnchors()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteMenuModel(basePath, includeFont: true, includeHud: false, modEdit: d =>
        {
            var slotObj = new UndertaleGameObject { Name = d.Strings.MakeString("UpgradeSlot"), Visible = true };
            slotObj.ParentId = d.GameObjects.ByName("Card");
            d.GameObjects.Add(slotObj);

            var slotMenu = new UndertaleGameObject { Name = d.Strings.MakeString("UpgradeSlotMenu"), Visible = true };
            slotMenu.ParentId = d.GameObjects.ByName("RowHandler");
            d.GameObjects.Add(slotMenu);

            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(slotMenu.EventHandlerFor(EventType.Alarm, 0u, d),
                """
                while (iCard < 8 && UpgradeData.upg[Rod.rod_id, iCard] == -2)
                {
                    iCard++;
                }
                if (iCard < 8)
                {
                    r = instance_create(x, y, UpgradeSlot);
                    r.slot_id = iCard;
                    if (((iCard + 1) % 2) == 0)
                    {
                        x = ORIGIN_X;
                        y += Y_GAP;
                    }
                    else
                    {
                        x += X_GAP;
                    }
                    iCard++;
                    alarm[0] = DELAY;
                }
                else
                {
                    done_spawning = 1;
                }
                """);
            Assert.True(compiler.Compile().Successful);
        });
        WriteMenuModel(modPath, includeFont: true, includeHud: false, modEdit: d =>
        {
            var slotObj = new UndertaleGameObject { Name = d.Strings.MakeString("UpgradeSlot"), Visible = true };
            slotObj.ParentId = d.GameObjects.ByName("Card");
            d.GameObjects.Add(slotObj);

            var slotMenu = new UndertaleGameObject { Name = d.Strings.MakeString("UpgradeSlotMenu"), Visible = true };
            slotMenu.ParentId = d.GameObjects.ByName("RowHandler");
            d.GameObjects.Add(slotMenu);

            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(slotMenu.EventHandlerFor(EventType.Alarm, 0u, d),
                """
                while (iCard < 8 && UpgradeData.upg[Rod.rod_id, iCard] == -2)
                {
                    iCard++;
                }
                if (iCard < 8)
                {
                    r = instance_create(x, y, UpgradeSlot);
                    r.slot_id = iCard;
                    if (((iCard + 1) % 2) == 0)
                    {
                        x = ORIGIN_X;
                        y += Y_GAP;
                    }
                    else
                    {
                        x += X_GAP;
                    }
                    iCard++;
                    alarm[0] = DELAY;
                }
                else
                {
                    done_spawning = 1;
                }
                """);
            Assert.True(compiler.Compile().Successful);
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod, "SlotMenuMod"), modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.Contains("UpgradeSlotMenu (4 rows)", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(output);
        var menuObj = merged.Data.GameObjects.ByName("UpgradeSlotMenu");
        Assert.NotNull(menuObj);
        Assert.Equal(MenuRuntime.UpgradeSlotMenuCreate, menuObj.Events[(int)EventType.Create].Single(e => e.EventSubtype == 0).Actions[0].CodeId.Name.Content);
        Assert.Equal(MenuRuntime.UpgradeSlotMenuStep, menuObj.Events[(int)EventType.Step].Single(e => e.EventSubtype == 0).Actions[0].CodeId.Name.Content);
        Assert.Equal(MenuRuntime.UpgradeSlotMenuDraw, menuObj.Events[(int)EventType.Draw].Single(e => e.EventSubtype == (uint)EventSubtypeDraw.DrawGUI).Actions[0].CodeId.Name.Content);

        string slotCreate = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeSlotMenuCreate).Text;
        Assert.Contains("card_width = 2", slotCreate);
        Assert.Contains("visible_rows = 4", slotCreate);
        Assert.Contains("ORIGIN_Y = y", slotCreate);

        string slotAlarm = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeSlotMenuAlarm).Text;
        Assert.Contains("array_length_2d(UpgradeData.upg, jamm_rod)", slotAlarm);
        Assert.Contains("!= -2", slotAlarm);
        Assert.Contains("r.slot_id = visible_items[window_pos]", slotAlarm);
        Assert.DoesNotContain("iCard < 8", slotAlarm);

        string slotStep = PatchBuilder.ReadCode(merged, MenuRuntime.UpgradeSlotMenuStep).Text;
        Assert.Contains("with (UpgradeSlot)", slotStep);

        var brokenBaseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [MenuRuntime.UpgradeSlotMenuAlarm] = new("// custom mod layout", "gml", 0, 0, null, [])
        };
        var skippedPlan = MenuRuntime.PreparePlan(new BuildPlan([], []), brokenBaseline);
        Assert.Empty(skippedPlan.Assets);
        Assert.Contains(skippedPlan.Decisions, d => d.Contains("Menu paging skipped for UpgradeSlotMenu", StringComparison.Ordinal));
    }

    private static void WriteMinimalModel(string path, Action<UndertaleData> edit)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        var code = UndertaleCode.CreateEmptyEntry(data, "test_code");
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(code, "x = 1;\ny = 2;\n");
        Assert.True(compiler.Compile().Successful);
        data.GameObjects.Add(new() { Name = data.Strings.MakeString("fish") });
        edit(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static void WriteMenuModel(string path, bool includeFont, bool includeHud, Action<UndertaleData>? modEdit)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;

        if (includeFont)
        {
            data.Fonts.Add(new()
            {
                Name = data.Strings.MakeString("fnt_babySmall"),
                DisplayName = data.Strings.MakeString("Small")
            });
            var shadow = UndertaleCode.CreateEmptyEntry(data, "gml_Script_draw_text_shadowed");
            data.Scripts.Add(new() { Name = data.Strings.MakeString("draw_text_shadowed"), Code = shadow });
            var shadowCompiler = new CompileGroup(data);
            shadowCompiler.QueueCodeReplace(shadow, "draw_text(argument0, argument1, argument2);");
            Assert.True(shadowCompiler.Compile().Successful);
        }

        foreach (string name in new[]
        {
            "icon_Rods", "Rod", "BoatData", "UpgradeData", "Card", "Handler",
            "CardHandler", "BoatMenu", "RodMenu", "RowHandler", "UpgradeCard", "UpgradeMenu", "Colors"
        })
        {
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        }

        var handler = data.GameObjects.ByName("Handler");
        var cardHandler = data.GameObjects.ByName("CardHandler");
        cardHandler.ParentId = handler;
        data.GameObjects.ByName("BoatMenu").ParentId = cardHandler;
        data.GameObjects.ByName("RodMenu").ParentId = cardHandler;
        var rowHandler = data.GameObjects.ByName("RowHandler");
        rowHandler.ParentId = handler;
        data.GameObjects.ByName("UpgradeMenu").ParentId = rowHandler;
        data.GameObjects.ByName("UpgradeCard").ParentId = data.GameObjects.ByName("Card");

        if (includeHud)
        {
            foreach (string name in new[] { "ExperienceBar", "Quest", "QuestEngine" })
                data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        }

        var compiler = new CompileGroup(data);

        if (includeHud)
        {
            compiler.QueueCodeReplace(data.GameObjects.ByName("ExperienceBar").EventHandlerFor(EventType.Draw, data),
                "draw_x = round(view_xview[view_current] + offset_x); draw_y = round(view_yview[view_current] + offset_y); draw_text(draw_x - 10, draw_y + 10, \"Level\");");
            compiler.QueueCodeReplace(data.GameObjects.ByName("Quest").EventHandlerFor(EventType.Draw, data),
                "draw_text_transformed(draw_x, draw_y, label, 0.5, 0.5, 0);");
        }

        compiler.QueueCodeReplace(cardHandler.EventHandlerFor(EventType.Create, data),
            """
            x = icon_Rods.ORIGIN_X;
            y = icon_Rods.ORIGIN_Y;
            DELAY = 2;
            ORIGIN_X = x;
            ORIGIN_Y = y;
            X_GAP = 160;
            Y_GAP = 230;
            Rod.swing_locked = 1;
            iCard = 0;
            alarm[0] = DELAY;
            done_spawning = 0;
            card_width = 3;
            """);

        compiler.QueueCodeReplace(cardHandler.EventHandlerFor(EventType.Alarm, 0u, data),
            """
            r = instance_create(x, y, card_type);
            r.item_id = iCard;
            if (((iCard + 1) % card_width) == 0)
            {
                x = ORIGIN_X;
                y += Y_GAP;
            }
            else
            {
                x += X_GAP;
            }
            iCard++;
            while (iCard < data_object.nItems && data_object.unlocked[iCard] < 0)
            {
                iCard++;
            }
            if (iCard < data_object.nItems)
            {
                alarm[0] = DELAY;
            }
            else
            {
                done_spawning = 1;
            }
            """);

        compiler.QueueCodeReplace(data.GameObjects.ByName("BoatMenu").EventHandlerFor(EventType.Create, data),
            """
            data_object = BoatData;
            card_type = Card;
            card_width = 4;
            """);

        if (includeFont)
        {
            compiler.QueueCodeReplace(data.GameObjects.ByName("RodMenu").EventHandlerFor(EventType.Draw, data),
                """
                if (done_spawning)
                {
                    draw_set_color(Colors.ui_light);
                    draw_set_halign(fa_center);
                    draw_set_valign(fa_top);
                    draw_set_font(fnt_babySmall);
                    draw_text_shadowed(ORIGIN_X + 640, ORIGIN_Y + 220, "Click the#equipped rod.");
                }
                """);
        }

        compiler.QueueCodeReplace(data.GameObjects.ByName("UpgradeData").EventHandlerFor(EventType.Create, data),
            """
            for (i = 0; i <= 17; i++)
            {
                unlocks[i] = 0;
            }
            unlocks[1] = 1;
            """);

        compiler.QueueCodeReplace(rowHandler.EventHandlerFor(EventType.Create, data),
            """
            x = icon_Rods.ORIGIN_X;
            y = icon_Rods.ORIGIN_Y;
            DELAY = 2;
            ORIGIN_X = x;
            X_GAP = 325;
            Y_GAP = 85;
            iCard = 0;
            alarm[0] = DELAY;
            done_spawning = 0;
            """);

        var upgradeMenu = data.GameObjects.ByName("UpgradeMenu");
        compiler.QueueCodeReplace(upgradeMenu.EventHandlerFor(EventType.Create, data),
            """
            Y_GAP = 40;
            for (i = 0; i <= 17; i++)
            {
                spawn_order[i] = i;
            }
            spawn_order[17] = 17;
            spawns = 0;
            """);

        compiler.QueueCodeReplace(upgradeMenu.EventHandlerFor(EventType.Alarm, 0u, data),
            """
            if (UpgradeData.unlocks[spawn_order[iCard]])
            {
                r = instance_create(x, y, UpgradeCard);
                r.card_id = spawn_order[iCard];
                spawns++;
                if ((spawns % 2) == 0)
                {
                    x = ORIGIN_X;
                    y += Y_GAP;
                }
                else
                {
                    x += X_GAP;
                }
            }
            iCard++;
            if (iCard <= 17)
            {
                alarm[0] = DELAY;
            }
            else
            {
                done_spawning = 1;
            }
            """);

        Assert.True(compiler.Compile().Successful);
        modEdit?.Invoke(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgfmm-menu-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => System.IO.Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
