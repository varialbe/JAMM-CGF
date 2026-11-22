using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies zoom-aware HUD, icon, popup, and modal menu adaptations.</summary>
public sealed class ZoomUiRuntimeTests
{
    [Fact]
    public void SyntheticArchiveWithViewRelativeHudAndMenusCompilesAndPreservesUnzoomedPath()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteModel(basePath, includeModEdit: false);
        WriteModel(modPath, includeModEdit: true);

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "ZoomMod");

        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.StartsWith("Zoom UI: adapted", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(output);
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_ui_zx"));
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_ui_zy"));
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_ui_cx"));
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_ui_cy"));
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_draw_text_shadowed_scaled"));
        Assert.NotNull(merged.Data.Scripts.ByName("jamm_draw_text_shadowed2_scaled"));

        string hudBegin = PatchBuilder.ReadCode(merged, "gml_Script_jamm_hud_begin").Text;
        Assert.Contains("jamm_vw / 800", hudBegin);
        Assert.Contains("ExperienceBar.offset_y < 480", hudBegin);

        string hudFlush = PatchBuilder.ReadCode(merged, "gml_Script_jamm_hud_flush").Text;
        Assert.Contains("jamm_hud_zx", hudFlush);
        Assert.Contains("draw_text_transformed", hudFlush);

        string xpDraw = PatchBuilder.ReadCode(merged, "gml_Object_ExperienceBar_Draw_0").Text;
        Assert.Contains("jamm_ui_zx()", xpDraw);
        Assert.Contains("draw_text(draw_x - 10, draw_y + 10, \"Lvl. \" + string(level))", xpDraw);
        Assert.Contains("draw_text_transformed(", xpDraw);

        string iconsStep = PatchBuilder.ReadCode(merged, "gml_Object_Icons_Step_2").Text;
        Assert.Contains("jamm_base_xscale", iconsStep);
        Assert.Contains("(OFFSET_X * jamm_zx) + jamm_cx", iconsStep);

        string saveMenu = PatchBuilder.ReadCode(merged, "gml_Object_SaveMenu_Create_0").Text;
        Assert.Contains("control_icon.OFFSET_Y = -122", saveMenu);
        Assert.Contains("exit_icon.OFFSET_Y = -12", saveMenu);

        string saveCard = PatchBuilder.ReadCode(merged, "gml_Object_SaveCard_Other_11").Text;
        Assert.Contains("if (jamm_zx == 1 && jamm_zy == 1)", saveCard);
        Assert.Contains("draw_text_transformed(", saveCard);
    }

    private static void WriteModel(string path, bool includeModEdit)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.GeneralInfo.Build = 1773;

        foreach (string name in new[]
        {
            "Camera", "Girl", "Colors", "Rod", "Lure", "Boat", "Handler", "CardHandler", "RowHandler",
            "ExperienceBar", "BreathBar", "UpgradeData", "BoatData", "CatalogData", "Quest", "QuestEngine",
            "Icons", "Wallet", "RadarButton", "SaveManager", "SaveMenu", "SaveCard", "SCB", "SCB_Load",
            "SCB_Delete", "SCB_New", "SCB_Copy", "Card", "BoatCard", "ToolCard", "RodCard", "LureCard",
            "UpgradeCard", "UpgradeSlot", "UpgradeMenu", "RodMenu", "GearMenu", "GearCard", "GearCardBlank",
            "Controls", "Fishalog", "Hatalog", "Catalog", "CatalogTab", "CatalogUp", "CatalogDown",
            "CatalogHandle", "CatalogSurface", "HatalogItem", "Potion", "Message", "MessageTwoSize",
            "icon_Rods", "icon_Controls", "icon_Exit", "icon_Fps", "icon_Reset", "icon_Cancel",
            "icon_Hats", "icon_HatsNew", "BoatDataMarker", "GM_Classic", "GM_Realism", "GameModeMenu"
        })
        {
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        }

        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babySmall"), DisplayName = data.Strings.MakeString("Small") });
        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babyVerySmall"), DisplayName = data.Strings.MakeString("VerySmall") });
        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babyTiny"), DisplayName = data.Strings.MakeString("Tiny") });
        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babyMedium"), DisplayName = data.Strings.MakeString("Medium") });
        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babyBig"), DisplayName = data.Strings.MakeString("Big") });
        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babyMoney"), DisplayName = data.Strings.MakeString("Money") });

        foreach (string spr in new[] { "spr_upgradeicons", "spr_upgradeicon_bg", "spr_icon_labels", "spr_rod", "spr_lock", "spr_catalog_bar", "spr_button_icons" })
            data.Sprites.Add(new() { Name = data.Strings.MakeString(spr) });

        var compiler = new CompileGroup(data);
        AddScript("draw_text_shadowed", "draw_text(argument0, argument1, argument2);");
        AddScript("draw_text_shadowed2", "draw_text(argument0, argument1, argument2);");
        AddScript("number_to_dollar_string", "return \"$\" + string(argument0);");

        AddCode("ExperienceBar", EventType.Draw, 0, """
            if (view_current == 0 && !Girl.hide_ui)
            {
                draw_x = round(view_xview[view_current] + offset_x);
                draw_y = round(view_yview[view_current] + offset_y);
                draw_set_color(Colors.ui_light);
                draw_set_font(fnt_babySmall);
                draw_set_halign(fa_right);
                draw_set_valign(fa_middle);
                draw_text(draw_x - 10, draw_y + 10, "Lvl. " + string(level));
                draw_rectangle(draw_x, draw_y, draw_x + (428 * fill_percent), draw_y + 20, false);
                draw_sprite_ext(sprite_index, image_index, draw_x, draw_y, 1, 1, 0, c_white, 1);
            }
            """);
        AddCode("Quest", EventType.Draw, 0, """
            draw_set_font(fnt_babyVerySmall);
            draw_set_color(Colors.ui_light);
            draw_set_halign(fa_right);
            draw_set_valign(fa_middle);
            draw_x = round((view_xview[view_current] + view_wview[view_current]) - 25);
            draw_y = round((view_yview[view_current] + 452) - (20 * QuestEngine.quest_counter));
            draw_text_transformed(draw_x, draw_y, label, 0.5, 0.5, 0);
            """);
        AddCode("Icons", EventType.Step, (uint)EventSubtypeStep.EndStep, """
            event_perform(ev_other, ev_user1);
            ORIGIN_X = OFFSET_X + Camera.x;
            ORIGIN_Y = OFFSET_Y + Camera.y;
            if (Rod.state == 0 && !hidden)
            {
                visible = true;
                x += ((ORIGIN_X - x) * 0.6);
                y += ((ORIGIN_Y - y) * 0.6);
            }
            else
            {
                x += (((ORIGIN_X + hidden_x) - x) * 0.4);
                y += (((ORIGIN_Y + hidden_y) - y) * 0.4);
            }
            """);
        AddCode("Icons", EventType.Draw, 0, """
            with (Icons)
            {
                if (view_current == 0 && object_index != Wallet)
                {
                    draw_self();
                    draw_set_font(font);
                    draw_set_color(Colors.ui_dark);
                    draw_set_halign(fa_center);
                    draw_set_valign(fa_middle);
                    draw_text(x + label_x, y + label_y, label);
                    draw_sprite_ext(spr_icon_labels, img_label, x + (sprite_width * 0.5), y + (sprite_height * 0.5), image_xscale, image_yscale, image_angle, image_blend, image_alpha);
                }
            }
            """);
        AddCode("Wallet", EventType.Draw, 0, """
            if (view_current == 0 && !Girl.hide_ui)
            {
                dollars_string = number_to_dollar_string(min(floor(dollars), 999999999));
                draw_set_font(fnt_babyMoney);
                draw_set_color(Colors.ui_dark);
                draw_set_halign(fa_right);
                draw_set_valign(fa_top);
                draw_sprite_ext(sprite_index, image_index, x - xoffset, y - 4, image_xscale, image_yscale, image_angle, image_blend, image_alpha);
                draw_text_ext_transformed(x + 55, y - 2, dollars_string, 100, 800, 0.5, 0.5, 0);
            }
            """);
        AddCode("SaveMenu", EventType.Create, 0, """
            with (SaveManager) { event_perform(ev_other, ev_user1); }
            var spawn_x = 681 + icon_Rods.x;
            control_icon = instance_create(spawn_x, 940, icon_Controls);
            exit_icon = instance_create(spawn_x, 1050, icon_Exit);
            fps_icon = instance_create(spawn_x, 900, icon_Fps);
            action_inherited();
            """);
        AddCode("SaveCard", EventType.Other, 11, """
            draw_self();
            draw_set_font(fnt_babySmall);
            draw_set_color(Colors.ui_light2);
            draw_set_halign(fa_left);
            draw_set_valign(fa_top);
            draw_text_shadowed2(x + 8, y + 3, "Save File " + string(item_id + 1));
            draw_set_font(fnt_babyTiny);
            draw_text(x + 20, y + 60, "Level " + string(level));
            """);
        if (includeModEdit)
            AddCode("BoatDataMarker", EventType.Create, 0, "zoom_marker = 1;");

        var compiled = compiler.Compile();
        Assert.True(compiled.Successful, compiled.PrintAllErrors(true));
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);

        void AddScript(string name, string body)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new() { Name = data.Strings.MakeString(name), Code = code });
            compiler.QueueCodeReplace(code, body);
        }

        void AddCode(string objName, EventType type, uint subtype, string body)
        {
            var obj = data.GameObjects.ByName(objName)!;
            compiler.QueueCodeReplace(obj.EventHandlerFor(type, subtype, data), body);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgfmm-zoomui-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => System.IO.Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
