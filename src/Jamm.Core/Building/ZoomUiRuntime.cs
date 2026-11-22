using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;

namespace Jamm.Core.Building;

/// <summary>
/// Keeps vanilla view-relative HUD elements, catch popups, buttons, and modal menus at their
/// 800x480 screen positions and proportions whenever a mod zooms <c>view[0]</c>, while leaving
/// unzoomed (<c>800x480</c>) vanilla behaviour unchanged.
/// </summary>
internal static partial class ZoomUiRuntime
{
    /// <summary>Prefix for ModKit capability scripts requesting view-zoom UI adaptation.</summary>
    internal const string CapabilityScriptPrefix = "jamm_cap_view_zoom_";

    private const string ZxScript = "jamm_ui_zx";
    private const string ZyScript = "jamm_ui_zy";
    private const string CxScript = "jamm_ui_cx";
    private const string CyScript = "jamm_ui_cy";
    private const string ShadowScript = "jamm_draw_text_shadowed_scaled";
    private const string Shadow2Script = "jamm_draw_text_shadowed2_scaled";

    private static readonly IReadOnlyDictionary<string, string> Scripts = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ZxScript] = """
            var jamm_w;
            jamm_w = view_wview[0];
            if (jamm_w <= 0) return 1;
            return jamm_w / 800;
            """,
        [ZyScript] = """
            var jamm_h;
            jamm_h = view_hview[0];
            if (jamm_h <= 0) return 1;
            return jamm_h / 480;
            """,
        [CxScript] = """
            if (view_wview[0] == 800 && view_hview[0] == 480) return Camera.x;
            return view_xview[0] + (view_wview[0] * 0.5);
            """,
        [CyScript] = """
            if (view_wview[0] == 800 && view_hview[0] == 480) return Camera.y;
            return view_yview[0] + (view_hview[0] * 0.5);
            """,
        [ShadowScript] = """
            if (argument3 == 1 && argument4 == 1)
            {
                draw_text_shadowed(argument0, argument1, argument2);
            }
            else
            {
                var jamm_c;
                jamm_c = draw_get_color();
                draw_set_color(Colors.ui_dark);
                draw_text_transformed(argument0 + (2 * argument3), argument1 + (2 * argument4), argument2, argument3, argument4, 0);
                draw_text_transformed(argument0 + argument3, argument1 + argument4, argument2, argument3, argument4, 0);
                draw_set_color(Colors.ui_light);
                draw_text_transformed(argument0, argument1, argument2, argument3, argument4, 0);
                draw_set_color(jamm_c);
            }
            """,
        [Shadow2Script] = """
            if (argument3 == 1 && argument4 == 1)
            {
                draw_text_shadowed2(argument0, argument1, argument2);
            }
            else
            {
                var jamm_c;
                jamm_c = draw_get_color();
                draw_set_color(Colors.ui_dark);
                draw_text_transformed(argument0 + (2 * argument3), argument1 + (2 * argument4), argument2, argument3, argument4, 0);
                draw_set_color(Colors.ui_light2);
                draw_text_transformed(argument0 - argument3, argument1 - argument4, argument2, argument3, argument4, 0);
                draw_text_transformed(argument0 - argument3, argument1, argument2, argument3, argument4, 0);
                draw_text_transformed(argument0 - argument3, argument1 + argument4, argument2, argument3, argument4, 0);
                draw_text_transformed(argument0 + argument3, argument1 - argument4, argument2, argument3, argument4, 0);
                draw_text_transformed(argument0 + argument3, argument1, argument2, argument3, argument4, 0);
                draw_text_transformed(argument0 + argument3, argument1 + argument4, argument2, argument3, argument4, 0);
                draw_text_transformed(argument0, argument1 - argument4, argument2, argument3, argument4, 0);
                draw_text_transformed(argument0, argument1 + argument4, argument2, argument3, argument4, 0);
                draw_set_color(c_white);
                draw_text_transformed(argument0, argument1, argument2, argument3, argument4, 0);
                draw_set_color(jamm_c);
            }
            """
    };

    private static readonly string[] CardDrawEntries =
    [
        "gml_Object_BoatCard_Other_11",
        "gml_Object_ToolCard_Other_11",
        "gml_Object_RodCard_Other_11",
        "gml_Object_LureCard_Other_11",
        "gml_Object_SaveCard_Other_11",
        "gml_Object_UpgradeCard_Other_11",
        "gml_Object_UpgradeSlot_Other_11",
        "gml_Object_GearCard_Other_11",
        "gml_Object_GearCardBlank_Other_11"
    ];

    internal static readonly string[] TargetCodeEntries =
    [
        "gml_Object_ExperienceBar_Draw_0",
        "gml_Object_BreathBar_Draw_0",
        "gml_Object_UpgradeData_Draw_0",
        "gml_Object_BoatData_Draw_0",
        "gml_Object_CatalogData_Draw_0",
        "gml_Object_Message_Other_10",
        "gml_Object_Message_Draw_0",
        "gml_Object_MessageTwoSize_Draw_0",
        "gml_Object_Icons_Step_2",
        "gml_Object_Icons_Draw_0",
        "gml_Object_Wallet_Draw_0",
        "gml_Object_RadarButton_Step_0",
        "gml_Object_SaveMenu_Create_0",
        "gml_Object_Catalog_Create_0",
        "gml_Object_CardHandler_Create_0",
        "gml_Object_RowHandler_Create_0",
        "gml_Object_UpgradeMenu_Create_0",
        "gml_Object_Card_Create_0",
        "gml_Object_Card_Step_0",
        "gml_Object_Card_Draw_0",
        "gml_Object_SaveCard_Create_0",
        "gml_Object_SaveCard_Step_0",
        "gml_Object_SCB_Draw_0",
        "gml_Object_CatalogTab_Draw_0",
        "gml_Object_Controls_Create_0",
        "gml_Object_RodMenu_Draw_0",
        "gml_Object_GearMenu_Create_0",
        "gml_Object_GearMenu_Alarm_0",
        "gml_Object_GearMenu_Draw_0",
        "gml_Object_GameModeMenu_Create_0",
        "gml_Object_GM_Classic_Draw_0",
        "gml_Object_GM_Realism_Draw_0",
        "gml_Object_Fishalog_Create_0",
        "gml_Object_Fishalog_Step_0",
        "gml_Object_Fishalog_Draw_0",
        "gml_Object_Hatalog_Create_0",
        "gml_Object_Hatalog_Step_0",
        "gml_Object_Hatalog_Draw_0",
        "gml_Object_HatalogItem_Draw_0",
        "gml_Object_Potion_Draw_73",
        .. CardDrawEntries
    ];

    /// <summary>Returns whether any mod in the archive declared the <c>view-zoom</c> capability.</summary>
    internal static bool IsRequested(UndertaleData data) =>
        data.Scripts.Any(s => s.Name?.Content.StartsWith(CapabilityScriptPrefix, StringComparison.Ordinal) == true);

    /// <summary>Applies zoom-aware HUD, icon, popup, and modal menu adaptations when requested and vanilla targets are present.</summary>
    internal static void Apply(
        UndertaleData data,
        IReadOnlyDictionary<string, CodeText> baseline,
        Dictionary<string, string> recompile,
        Dictionary<string, string> generated,
        List<string> decisions)
    {
        if (!IsRequested(data))
            return;
        if (data.GameObjects.ByName("Camera") is null || data.GameObjects.ByName("Girl") is null)
            return;

        int patched = 0;

        TryPatch("gml_Object_ExperienceBar_Draw_0", text => PatchMeterBar(text, "Lvl. \" + string(level)"));
        TryPatch("gml_Object_BreathBar_Draw_0", text => PatchMeterBar(text, "O2\""));
        TryPatch("gml_Object_UpgradeData_Draw_0", PatchUpgradeDataDraw);
        TryPatch("gml_Object_BoatData_Draw_0", PatchBoatDataDraw);
        TryPatch("gml_Object_CatalogData_Draw_0", PatchCatalogDataDraw);
        TryPatch("gml_Object_Message_Other_10", PatchMessageOther10);
        TryPatch("gml_Object_Message_Draw_0", text => PatchDrawTextShadowed(text));
        TryPatch("gml_Object_MessageTwoSize_Draw_0", text => PatchDrawTextShadowed(text));
        TryPatch("gml_Object_Icons_Step_2", PatchIconsStep2);
        TryPatch("gml_Object_Icons_Draw_0", PatchIconsDraw0);
        TryPatch("gml_Object_Wallet_Draw_0", PatchWalletDraw0);
        TryPatch("gml_Object_RadarButton_Step_0", PatchRadarButtonStep0);
        TryPatch("gml_Object_SaveMenu_Create_0", PatchSaveMenuCreate);
        TryPatch("gml_Object_Catalog_Create_0", PatchCatalogCreate);
        TryPatch("gml_Object_CardHandler_Create_0", PatchHandlerGaps);
        TryPatch("gml_Object_RowHandler_Create_0", PatchHandlerGaps);
        TryPatch("gml_Object_UpgradeMenu_Create_0", PatchUpgradeMenuCreate);
        TryPatch("gml_Object_Card_Create_0", PatchCardCreate);
        TryPatch("gml_Object_Card_Step_0", PatchCardAnchorStep);
        TryPatch("gml_Object_Card_Draw_0", PatchCardAnchorStep);
        TryPatch("gml_Object_SaveCard_Create_0", PatchSaveCardCreate);
        TryPatch("gml_Object_SaveCard_Step_0", PatchSaveCardStep);
        TryPatch("gml_Object_SCB_Draw_0", PatchScbDraw);
        TryPatch("gml_Object_CatalogTab_Draw_0", PatchCatalogTabDraw);
        TryPatch("gml_Object_Controls_Create_0", PatchControlsCreate);
        TryPatch("gml_Object_RodMenu_Draw_0", PatchRodMenuDraw);
        TryPatch("gml_Object_GearMenu_Create_0", PatchGearMenuCreate);
        TryPatch("gml_Object_GearMenu_Alarm_0", PatchGearMenuAlarm);
        TryPatch("gml_Object_GearMenu_Draw_0", PatchGearMenuDraw);
        TryPatch("gml_Object_GameModeMenu_Create_0", PatchGameModeMenuCreate);
        TryPatch("gml_Object_GM_Classic_Draw_0", PatchGameModeButtonDraw);
        TryPatch("gml_Object_GM_Realism_Draw_0", PatchGameModeButtonDraw);
        TryPatch("gml_Object_Fishalog_Create_0", PatchCatalogBrowserCreate);
        TryPatch("gml_Object_Fishalog_Step_0", PatchCatalogBrowserStep);
        TryPatch("gml_Object_Fishalog_Draw_0", PatchCatalogBrowserDraw);
        TryPatch("gml_Object_Hatalog_Create_0", PatchCatalogBrowserCreate);
        TryPatch("gml_Object_Hatalog_Step_0", PatchCatalogBrowserStep);
        TryPatch("gml_Object_Hatalog_Draw_0", PatchCatalogBrowserDraw);
        TryPatch("gml_Object_HatalogItem_Draw_0", PatchHoverTooltipDraw);
        TryPatch("gml_Object_Potion_Draw_73", PatchHoverTooltipDraw);

        foreach (string cardDraw in CardDrawEntries)
        {
            TryPatch(cardDraw, text => WrapCardDrawWithZoomBranch(cardDraw, text));
        }

        if (patched == 0)
            return;

        foreach (var (name, body) in Scripts)
            ValueGuard.PutScript(data, name, body, recompile, generated);

        decisions.Add($"Zoom UI: adapted {patched} view-relative HUD/menu entry/entries so 800x480 proportions hold under view zoom.");

        void TryPatch(string entry, Func<string, string?> transform)
        {
            if (data.Code.ByName(entry) is null)
                return;
            string? current = recompile.GetValueOrDefault(entry) ?? baseline.GetValueOrDefault(entry)?.Text;
            if (current is null)
                return;
            string? updated = transform(current);
            if (updated is not null && updated != current)
            {
                generated[entry] = recompile[entry] = updated;
                patched++;
            }
        }
    }

    private static string? PatchMeterBar(string text, string labelSuffix)
    {
        string target = $"draw_text(draw_x - 10, draw_y + 10, \"{labelSuffix});\n"
            + "draw_rectangle(draw_x, draw_y, draw_x + (428 * fill_percent), draw_y + 20, false);\n"
            + "draw_sprite_ext(sprite_index, image_index, draw_x, draw_y, 1, 1, 0, c_white, 1);";
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy, jamm_bx, jamm_by;
                jamm_zx = {{ZxScript}}();
                jamm_zy = {{ZyScript}}();
                if (jamm_zx == 1 && jamm_zy == 1)
                {
                    {{matched}}
                }
                else
                {
                    jamm_bx = round(view_xview[view_current] + (offset_x * jamm_zx));
                    jamm_by = round(view_yview[view_current] + (offset_y * jamm_zy));
                    draw_text_transformed(jamm_bx - (10 * jamm_zx), jamm_by + (10 * jamm_zy), "{{labelSuffix}}, jamm_zx, jamm_zy, 0);
                    draw_rectangle(jamm_bx, jamm_by, jamm_bx + (428 * fill_percent * jamm_zx), jamm_by + (20 * jamm_zy), false);
                    draw_sprite_ext(sprite_index, image_index, jamm_bx, jamm_by, jamm_zx, jamm_zy, 0, c_white, 1);
                }
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchUpgradeDataDraw(string text)
    {
        const string anchor = """
            base_x = round(view_xview[view_current] + 10);
            base_y = round((view_yview[view_current] + view_hview[view_current]) - 35);
            gap = 30;
            icon_scale = 0.5;
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, anchor, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy;
                jamm_zx = {{ZxScript}}();
                jamm_zy = {{ZyScript}}();
                base_x = round(view_xview[view_current] + (10 * jamm_zx));
                base_y = round((view_yview[view_current] + view_hview[view_current]) - (35 * jamm_zy));
                gap = 30 * jamm_zx;
                icon_scale = 0.5 * jamm_zx;
            """;
        string updated = normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
        return updated.Replace("icon_y = base_y - (iy * gap);", "icon_y = base_y - (iy * 30 * jamm_zy);", StringComparison.Ordinal);
    }

    private static string? PatchBoatDataDraw(string text)
    {
        const string anchor = """
            base_x = round(view_xview[view_current] + 300);
            base_y = round(view_yview[view_current] + 8);
            """;
        if (!text.Contains("base_x = round(view_xview[view_current] + 300);", StringComparison.Ordinal))
            return null;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, anchor, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy;
                jamm_zx = {{ZxScript}}();
                jamm_zy = {{ZyScript}}();
                base_x = round(view_xview[view_current] + (300 * jamm_zx));
                base_y = round(view_yview[view_current] + (8 * jamm_zy));
            """;
        string updated = normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
        updated = ScaleDrawTextShadowedCalls(updated, "jamm_zx", "jamm_zy");
        updated = ScaleCoordOffsets(updated, "base_x", "jamm_zx");
        updated = ScaleCoordOffsets(updated, "base_y", "jamm_zy");
        return updated;
    }

    private static string? PatchCatalogDataDraw(string text)
    {
        if (!text.Contains("draw_text_shadowed(Camera.x,", StringComparison.Ordinal))
            return null;
        string updated = $$"""
            var jamm_zx, jamm_zy, jamm_cx, jamm_cy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            jamm_cx = {{CxScript}}();
            jamm_cy = {{CyScript}}();
            """ + "\n" + text;
        updated = ScaleDrawTextShadowedCalls(updated, "jamm_zx", "jamm_zy");
        updated = updated.Replace("Camera.x", "jamm_cx", StringComparison.Ordinal);
        updated = ScaleCoordOffsets(updated, "Camera.y", "jamm_zy").Replace("Camera.y", "jamm_cy", StringComparison.Ordinal);
        return updated;
    }

    private static string? PatchMessageOther10(string text)
    {
        if (!text.Contains("Camera.x", StringComparison.Ordinal))
            return null;
        return text.Replace("Camera.x", $"{CxScript}()", StringComparison.Ordinal);
    }

    private static string? PatchDrawTextShadowed(string text)
    {
        if (!text.Contains("draw_text_shadowed(", StringComparison.Ordinal))
            return null;
        return ScaleDrawTextShadowedCalls(text, $"{ZxScript}()", $"{ZyScript}()");
    }

    private static string? PatchIconsStep2(string text)
    {
        const string anchor = """
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
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, anchor, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            if (!variable_instance_exists(id, "jamm_base_xscale"))
            {
                jamm_base_xscale = image_xscale;
                jamm_base_yscale = image_yscale;
            }
            var jamm_zx, jamm_zy, jamm_cx, jamm_cy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            jamm_cx = {{CxScript}}();
            jamm_cy = {{CyScript}}();
            image_xscale = jamm_base_xscale * jamm_zx;
            image_yscale = jamm_base_yscale * jamm_zy;
            ORIGIN_X = (OFFSET_X * jamm_zx) + jamm_cx;
            ORIGIN_Y = (OFFSET_Y * jamm_zy) + jamm_cy;
            if (Rod.state == 0 && !hidden)
            {
                visible = true;
                x += ((ORIGIN_X - x) * 0.6);
                y += ((ORIGIN_Y - y) * 0.6);
            }
            else
            {
                x += (((ORIGIN_X + (hidden_x * jamm_zx)) - x) * 0.4);
                y += (((ORIGIN_Y + (hidden_y * jamm_zy)) - y) * 0.4);
            }
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchIconsDraw0(string text)
    {
        const string target = "draw_text(x + label_x, y + label_y, label);";
        if (!text.Contains(target, StringComparison.Ordinal))
            return null;
        string replacement = $$"""
            var jamm_zx, jamm_zy;
                    jamm_zx = {{ZxScript}}();
                    jamm_zy = {{ZyScript}}();
                    if (jamm_zx == 1 && jamm_zy == 1)
                    {
                        draw_text(x + label_x, y + label_y, label);
                    }
                    else
                    {
                        draw_text_transformed(x + (label_x * jamm_zx), y + (label_y * jamm_zy), label, jamm_zx, jamm_zy, 0);
                    }
            """;
        return text.Replace(target, replacement, StringComparison.Ordinal);
    }

    private static string? PatchWalletDraw0(string text)
    {
        const string target = """
            draw_sprite_ext(sprite_index, image_index, x - xoffset, y - 4, image_xscale, image_yscale, image_angle, image_blend, image_alpha);
            draw_text_ext_transformed(x + 55, y - 2, dollars_string, 100, 800, 0.5, 0.5, 0);
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy;
                jamm_zx = {{ZxScript}}();
                jamm_zy = {{ZyScript}}();
                if (jamm_zx == 1 && jamm_zy == 1)
                {
                    {{matched}}
                }
                else
                {
                    draw_sprite_ext(sprite_index, image_index, x - (xoffset * jamm_zx), y - (4 * jamm_zy), image_xscale, image_yscale, image_angle, image_blend, image_alpha);
                    draw_text_ext_transformed(x + (55 * jamm_zx), y - (2 * jamm_zy), dollars_string, 100, 800, 0.5 * jamm_zx, 0.5 * jamm_zy, 0);
                }
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchRadarButtonStep0(string text)
    {
        const string target = """
            x = Camera.x + OFFSET_X;
            y = Camera.y + OFFSET_Y;
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            if (!variable_instance_exists(id, "jamm_base_xscale"))
            {
                jamm_base_xscale = image_xscale;
                jamm_base_yscale = image_yscale;
            }
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            image_xscale = jamm_base_xscale * jamm_zx;
            image_yscale = jamm_base_yscale * jamm_zy;
            x = {{CxScript}}() + (OFFSET_X * jamm_zx);
            y = {{CyScript}}() + (OFFSET_Y * jamm_zy);
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchSaveMenuCreate(string text)
    {
        const string target = """
            var spawn_x = 681 + icon_Rods.x;
            control_icon = instance_create(spawn_x, 940, icon_Controls);
            exit_icon = instance_create(spawn_x, 1050, icon_Exit);
            fps_icon = instance_create(spawn_x, 900, icon_Fps);
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy, jamm_cx, jamm_cy, spawn_x;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            jamm_cx = {{CxScript}}();
            jamm_cy = {{CyScript}}();
            spawn_x = jamm_cx + (288 * jamm_zx);
            control_icon = instance_create(spawn_x, jamm_cy - (122 * jamm_zy), icon_Controls);
            control_icon.OFFSET_X = 288;
            control_icon.OFFSET_Y = -122;
            control_icon.hidden_x = 300;
            control_icon.hidden_y = 0;
            exit_icon = instance_create(spawn_x, jamm_cy - (12 * jamm_zy), icon_Exit);
            exit_icon.OFFSET_X = 288;
            exit_icon.OFFSET_Y = -12;
            exit_icon.hidden_x = 300;
            exit_icon.hidden_y = 0;
            fps_icon = instance_create(spawn_x, jamm_cy - (162 * jamm_zy), icon_Fps);
            fps_icon.OFFSET_X = 288;
            fps_icon.OFFSET_Y = -162;
            fps_icon.hidden_x = 300;
            fps_icon.hidden_y = 0;
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchCatalogCreate(string text)
    {
        const string target = """
            var spawn_x = 681 + icon_Rods.x;
            reset_icon = instance_create(spawn_x, 1050, icon_Reset);
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy, jamm_cx, jamm_cy, spawn_x;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            jamm_cx = {{CxScript}}();
            jamm_cy = {{CyScript}}();
            spawn_x = jamm_cx + (288 * jamm_zx);
            reset_icon = instance_create(spawn_x, jamm_cy - (12 * jamm_zy), icon_Reset);
            reset_icon.OFFSET_X = 288;
            reset_icon.OFFSET_Y = -12;
            reset_icon.hidden_x = 300;
            reset_icon.hidden_y = 0;
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchHandlerGaps(string text)
    {
        var match = Regex.Match(text, @"X_GAP\s*=\s*(\d+)\s*;\s*Y_GAP\s*=\s*(\d+)\s*;");
        if (!match.Success)
            return null;
        string updated = text[..match.Index]
            + $"X_GAP = {match.Groups[1].Value} * {ZxScript}();\nY_GAP = {match.Groups[2].Value} * {ZyScript}();"
            + text[(match.Index + match.Length)..];
        return updated;
    }

    private static string? PatchUpgradeMenuCreate(string text)
    {
        const string target = "Y_GAP = 40;";
        if (!text.Contains(target, StringComparison.Ordinal))
            return null;
        return text.Replace(target, $"Y_GAP = 40 * {ZyScript}();", StringComparison.Ordinal);
    }

    private static string? PatchCardCreate(string text)
    {
        const string target = """
            ORIGIN_X = x - Girl.x;
            ORIGIN_Y = y - Girl.y;
            x += 2000;
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            image_xscale *= jamm_zx;
            image_yscale *= jamm_zy;
            if (jamm_zx == 1 && jamm_zy == 1)
            {
                ORIGIN_X = x - Girl.x;
                ORIGIN_Y = y - Girl.y;
                x += 2000;
            }
            else
            {
                ORIGIN_X = x - {{CxScript}}();
                ORIGIN_Y = y - {{CyScript}}();
                x += 2000 * jamm_zx;
            }
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchCardAnchorStep(string text)
    {
        const string target = """
            target_x = ORIGIN_X + Girl.x;
            target_y = ORIGIN_Y + Girl.y;
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            if ({{ZxScript}}() == 1 && {{ZyScript}}() == 1)
            {
                target_x = ORIGIN_X + Girl.x;
                target_y = ORIGIN_Y + Girl.y;
            }
            else
            {
                target_x = ORIGIN_X + {{CxScript}}();
                target_y = ORIGIN_Y + {{CyScript}}();
            }
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchSaveCardCreate(string text)
    {
        const string target = """
            btn_x_offset = 10.666666666666666;
            btn_y_offset = 140;
            btn_gap = 37.33333333333333;
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            btn_x_offset = 10.666666666666666 * jamm_zx;
            btn_y_offset = 140 * jamm_zy;
            btn_gap = 37.33333333333333 * jamm_zy;
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchSaveCardStep(string text)
    {
        const string target = """
            btn1.x = x + btn_x_offset;
            btn1.y = y + btn_y_offset;
            btn2.x = x + btn_x_offset;
            btn2.y = y + btn_y_offset + btn_gap;
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            btn1.image_xscale = (2 / 3) * jamm_zx;
            btn1.image_yscale = (2 / 3) * jamm_zy;
            btn2.image_xscale = (2 / 3) * jamm_zx;
            btn2.image_yscale = (2 / 3) * jamm_zy;
            btn1.x = x + btn_x_offset;
            btn1.y = y + btn_y_offset;
            btn2.x = x + btn_x_offset;
            btn2.y = y + btn_y_offset + btn_gap;
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchScbDraw(string text)
    {
        const string target = "draw_text(x + (sprite_width * 0.5), y + (sprite_height * 0.5), label);";
        if (!text.Contains(target, StringComparison.Ordinal))
            return null;
        string replacement = $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            if (jamm_zx == 1 && jamm_zy == 1)
            {
                draw_text(x + (sprite_width * 0.5), y + (sprite_height * 0.5), label);
            }
            else
            {
                draw_text_transformed(x + (sprite_width * 0.5), y + (sprite_height * 0.5), label, jamm_zx, jamm_zy, 0);
            }
            """;
        return text.Replace(target, replacement, StringComparison.Ordinal);
    }

    private static string? PatchCatalogTabDraw(string text)
    {
        const string target = "draw_text(x + (sprite_width * 0.5), y + (sprite_height * 0.5), label);";
        if (!text.Contains(target, StringComparison.Ordinal))
            return null;
        string replacement = $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            if (jamm_zx == 1 && jamm_zy == 1)
            {
                draw_text(x + (sprite_width * 0.5), y + (sprite_height * 0.5), label);
            }
            else
            {
                draw_text_transformed(x + (sprite_width * 0.5), y + (sprite_height * 0.5), label, jamm_zx, jamm_zy, 0);
            }
            """;
        return text.Replace(target, replacement, StringComparison.Ordinal);
    }

    private static string? PatchControlsCreate(string text)
    {
        const string target = """
            x = icon_Rods.ORIGIN_X + 70;
            y = icon_Rods.ORIGIN_Y + 30;
            image_alpha = 0.8;
            image_xscale = 0.5;
            image_yscale = 0.5;
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            x = icon_Rods.ORIGIN_X + (70 * jamm_zx);
            y = icon_Rods.ORIGIN_Y + (30 * jamm_zy);
            image_alpha = 0.8;
            image_xscale = 0.5 * jamm_zx;
            image_yscale = 0.5 * jamm_zy;
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchRodMenuDraw(string text)
    {
        const string target = "draw_text_shadowed(ORIGIN_X + 640, ORIGIN_Y + 220, \"Click the#equipped rod to#purchase upgrades.\");";
        if (!text.Contains(target, StringComparison.Ordinal))
            return null;
        string replacement = $$"""
            var jamm_zx, jamm_zy;
                jamm_zx = {{ZxScript}}();
                jamm_zy = {{ZyScript}}();
                {{ShadowScript}}(ORIGIN_X + (640 * jamm_zx), ORIGIN_Y + (220 * jamm_zy), "Click the#equipped rod to#purchase upgrades.", jamm_zx, jamm_zy);
            """;
        return text.Replace(target, replacement, StringComparison.Ordinal);
    }

    private static string? PatchGearMenuCreate(string text)
    {
        if (!text.Contains("spawn_base_x = Girl.x + Camera.RELATIVE_ANCHOR_X;", StringComparison.Ordinal))
            return null;
        return $$"""
            action_inherited();
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            gear_card_width = 90 * jamm_zx;
            gear_card_gap = 60 * jamm_zx;
            if (jamm_zx == 1 && jamm_zy == 1)
            {
                spawn_base_x = Girl.x + Camera.RELATIVE_ANCHOR_X;
                spawn_base_y = Girl.y + Camera.RELATIVE_ANCHOR_Y;
            }
            else
            {
                spawn_base_x = {{CxScript}}();
                spawn_base_y = {{CyScript}}();
            }
            if (Girl.gear > 0)
            {
                card1 = instance_create(spawn_base_x - gear_card_gap - gear_card_width, spawn_base_y - (80 * jamm_zy), GearCard);
                card1.gear = Girl.gear;
                card1.equipped = 1;
            }
            alarm[0] = 10;
            """;
    }

    private static string? PatchGearMenuAlarm(string text)
    {
        const string target = "card2 = instance_create(spawn_base_x + gear_card_gap, spawn_base_y - 80, GearCard);";
        if (!text.Contains(target, StringComparison.Ordinal))
            return null;
        return text.Replace(target, $"card2 = instance_create(spawn_base_x + gear_card_gap, spawn_base_y - (80 * {ZyScript}()), GearCard);", StringComparison.Ordinal);
    }

    private static string? PatchGearMenuDraw(string text)
    {
        if (!text.Contains("draw_text(spawn_base_x, spawn_base_y - 180", StringComparison.Ordinal))
            return null;
        return $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            draw_set_color(Colors.ui_light);
            draw_set_halign(fa_center);
            draw_set_font(fnt_babyMedium);
            draw_set_valign(fa_bottom);
            if (jamm_zx == 1 && jamm_zy == 1)
            {
                draw_text(spawn_base_x, spawn_base_y - 180, "Choose a Hat:");
                draw_set_font(fnt_babySmall);
                draw_set_valign(fa_top);
                draw_text(spawn_base_x, spawn_base_y - 180, "(The other hat will be sold.)");
            }
            else
            {
                draw_text_transformed(spawn_base_x, spawn_base_y - (180 * jamm_zy), "Choose a Hat:", jamm_zx, jamm_zy, 0);
                draw_set_font(fnt_babySmall);
                draw_set_valign(fa_top);
                draw_text_transformed(spawn_base_x, spawn_base_y - (180 * jamm_zy), "(The other hat will be sold.)", jamm_zx, jamm_zy, 0);
            }
            """;
    }

    private static string? PatchGameModeMenuCreate(string text)
    {
        const string target = """
            sep = 200;
            instance_create(Camera.x - sep, Camera.y, GM_Classic);
            instance_create(Camera.x + sep, Camera.y, GM_Realism);
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy, jamm_cx, jamm_cy, jamm_b;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            jamm_cx = {{CxScript}}();
            jamm_cy = {{CyScript}}();
            sep = 200 * jamm_zx;
            jamm_b = instance_create(jamm_cx - sep, jamm_cy, GM_Classic);
            jamm_b.image_xscale *= jamm_zx;
            jamm_b.image_yscale *= jamm_zy;
            jamm_b = instance_create(jamm_cx + sep, jamm_cy, GM_Realism);
            jamm_b.image_xscale *= jamm_zx;
            jamm_b.image_yscale *= jamm_zy;
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? PatchGameModeButtonDraw(string text)
    {
        var match = Regex.Match(text, @"draw_sprite_ext\(spr_button_icons,\s*(\d+),\s*x\s*\+\s*0,\s*y\s*\+\s*180,\s*0\.5,\s*0\.5,\s*0,\s*c_white,\s*1\);");
        if (!match.Success)
            return null;
        string idxExpr = match.Groups[1].Value;
        return text[..match.Index]
            + $"draw_sprite_ext(spr_button_icons, {idxExpr}, x, y + (180 * {ZyScript}()), 0.5 * {ZxScript}(), 0.5 * {ZyScript}(), 0, c_white, 1);"
            + text[(match.Index + match.Length)..];
    }

    private static string? PatchCatalogBrowserCreate(string text)
    {
        if (!text.Contains("x = icon_Rods.ORIGIN_X + 40;", StringComparison.Ordinal))
            return null;

        string updated = NormalizeLineEndings(text);
        updated = $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            """ + "\n" + updated;
        updated = updated.Replace("x = icon_Rods.ORIGIN_X + 40;", "x = icon_Rods.ORIGIN_X + (40 * jamm_zx);", StringComparison.Ordinal);
        updated = updated.Replace("y = icon_Rods.ORIGIN_Y + 40;", "y = icon_Rods.ORIGIN_Y + (40 * jamm_zy);", StringComparison.Ordinal);
        updated = updated.Replace("y = icon_Rods.ORIGIN_Y + 50;", "y = icon_Rods.ORIGIN_Y + (50 * jamm_zy);", StringComparison.Ordinal);
        updated = updated.Replace("ORIGIN_X = x - Girl.x;", $"ORIGIN_X = (jamm_zx == 1 && jamm_zy == 1) ? (x - Girl.x) : (x - {CxScript}());", StringComparison.Ordinal);
        updated = updated.Replace("tab[i] = instance_create(x + 4 + (i * 115), y + 4, CatalogTab);", "tab[i] = instance_create(x + (4 * jamm_zx) + (i * 115 * jamm_zx), y + (4 * jamm_zy), CatalogTab);\n    tab[i].image_xscale *= jamm_zx;\n    tab[i].image_yscale *= jamm_zy;", StringComparison.Ordinal);
        updated = updated.Replace("cup = instance_create((x + width) - 28, y + 66, CatalogUp);", "cup = instance_create(x + ((width - 28) * jamm_zx), y + (66 * jamm_zy), CatalogUp);\ncup.image_xscale *= jamm_zx;\ncup.image_yscale *= jamm_zy;", StringComparison.Ordinal);
        updated = updated.Replace("cdown = instance_create((x + width) - 28, (y + height) - 28, CatalogDown);", "cdown = instance_create(x + ((width - 28) * jamm_zx), y + ((height - 28) * jamm_zy), CatalogDown);\ncdown.image_xscale *= jamm_zx;\ncdown.image_yscale *= jamm_zy;", StringComparison.Ordinal);
        updated = updated.Replace("handle = instance_create((x + width) - 28, y + 66, CatalogHandle);", "handle = instance_create(x + ((width - 28) * jamm_zx), y + (66 * jamm_zy), CatalogHandle);", StringComparison.Ordinal);
        updated = updated.Replace("handle.image_xscale = 24;", "handle.image_xscale = 24 * jamm_zx;", StringComparison.Ordinal);
        updated = updated.Replace("handle.image_yscale = 50;", "handle.image_yscale = 50 * jamm_zy;", StringComparison.Ordinal);
        updated = updated.Replace("cs = instance_create(x + surfx, y + surfy, CatalogSurface);", "cs = instance_create(x + (surfx * jamm_zx), y + (surfy * jamm_zy), CatalogSurface);", StringComparison.Ordinal);
        updated = updated.Replace("cs.image_xscale = surfw;", "cs.image_xscale = surfw * jamm_zx;", StringComparison.Ordinal);
        updated = updated.Replace("cs.image_yscale = surfh;", "cs.image_yscale = surfh * jamm_zy;", StringComparison.Ordinal);
        return updated;
    }

    private static string? PatchCatalogBrowserStep(string text)
    {
        if (!text.Contains("x = ORIGIN_X + Girl.x;", StringComparison.Ordinal))
            return null;
        return text.Replace("x = ORIGIN_X + Girl.x;", $"x = ORIGIN_X + (({ZxScript}() == 1 && {ZyScript}() == 1) ? Girl.x : {CxScript}());", StringComparison.Ordinal);
    }

    private static string? PatchCatalogBrowserDraw(string text)
    {
        if (!text.Contains("draw_rectangle(x, y, x + width, y + height, false);", StringComparison.Ordinal))
            return null;
        string normalized = NormalizeLineEndings(text);
        string updated = $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            """ + "\n" + normalized;
        updated = updated.Replace("draw_rectangle(x, y, x + width, y + height, false);", "draw_rectangle(x, y, x + (width * jamm_zx), y + (height * jamm_zy), false);", StringComparison.Ordinal);
        updated = updated.Replace("draw_sprite_ext(spr_catalog_bar, 0, CatalogUp.x, CatalogUp.y, 1, CatalogDown.y - CatalogUp.y, 0, c_white, 1);", "draw_sprite_ext(spr_catalog_bar, 0, CatalogUp.x, CatalogUp.y, jamm_zx, CatalogDown.y - CatalogUp.y, 0, c_white, 1);", StringComparison.Ordinal);
        updated = updated.Replace("draw_surface(csurf, x + surfx, y + surfy);", "draw_surface_stretched(csurf, x + (surfx * jamm_zx), y + (surfy * jamm_zy), surfw * jamm_zx, surfh * jamm_zy);", StringComparison.Ordinal);
        updated = updated.Replace("draw_text(x + 475, y + 10, \"Completion: \" + string(floor(Catalog.fish_completion[selected] * 100)) + \"%\");", "draw_text_transformed(x + (475 * jamm_zx), y + (10 * jamm_zy), \"Completion: \" + string(floor(Catalog.fish_completion[selected] * 100)) + \"%\", jamm_zx, jamm_zy, 0);", StringComparison.Ordinal);
        updated = updated.Replace("draw_text(x + 475, y + 10, \"Completion: \" + string(floor(completion * 100)) + \"%\");", "draw_text_transformed(x + (475 * jamm_zx), y + (10 * jamm_zy), \"Completion: \" + string(floor(completion * 100)) + \"%\", jamm_zx, jamm_zy, 0);", StringComparison.Ordinal);
        return updated;
    }

    private static string? PatchHoverTooltipDraw(string text)
    {
        const string target = """
            box_height = 20 + string_height_ext(desc, 20, 280);
            draw_set_color(Colors.ui_dark);
            draw_rectangle(mouse_x + 1, mouse_y + 1, mouse_x + 299, (mouse_y + box_height) - 1, false);
            draw_set_color(Colors.ui_light);
            draw_rectangle(mouse_x, mouse_y, mouse_x + 300, mouse_y + box_height, true);
            draw_text_ext(mouse_x + 10, mouse_y + 10, desc, 20, 280);
            """;
        string normalized = NormalizeLineEndings(text);
        int idx = IndexOfIgnoringIndentation(normalized, target, out string? matched);
        if (idx < 0 || matched is null)
            return null;

        string replacement = $$"""
            var jamm_zx, jamm_zy;
                jamm_zx = {{ZxScript}}();
                jamm_zy = {{ZyScript}}();
                if (jamm_zx == 1 && jamm_zy == 1)
                {
                    {{matched}}
                }
                else
                {
                    box_height = (20 + string_height_ext(desc, 20, 280)) * jamm_zy;
                    draw_set_color(Colors.ui_dark);
                    draw_rectangle(mouse_x + jamm_zx, mouse_y + jamm_zy, mouse_x + (299 * jamm_zx), (mouse_y + box_height) - jamm_zy, false);
                    draw_set_color(Colors.ui_light);
                    draw_rectangle(mouse_x, mouse_y, mouse_x + (300 * jamm_zx), mouse_y + box_height, true);
                    draw_text_ext_transformed(mouse_x + (10 * jamm_zx), mouse_y + (10 * jamm_zy), desc, 20, 280, jamm_zx, jamm_zy, 0);
                }
            """;
        return normalized[..idx] + replacement + normalized[(idx + matched.Length)..];
    }

    private static string? WrapCardDrawWithZoomBranch(string entry, string text)
    {
        string normalized = NormalizeLineEndings(text);
        if (normalized.Contains(ZxScript, StringComparison.Ordinal))
            return null;

        string scaled = ScaleCardBody(entry, normalized);
        if (scaled == normalized)
            return null;

        return $$"""
            var jamm_zx, jamm_zy;
            jamm_zx = {{ZxScript}}();
            jamm_zy = {{ZyScript}}();
            if (jamm_zx == 1 && jamm_zy == 1)
            {
            {{normalized}}
            }
            else
            {
            {{scaled}}
            }
            """;
    }

    private static string ScaleCardBody(string entry, string body)
    {
        string s = body;
        // Scale card-local layout variables where defined.
        s = s.Replace("icon_scale = 0.5;", "icon_scale = 0.5 * jamm_zx;", StringComparison.Ordinal);
        s = s.Replace("scale = 0.5;", "scale = 0.5 * jamm_zx;", StringComparison.Ordinal);
        s = s.Replace("icon_x = x + 5;", "icon_x = x + (5 * jamm_zx);", StringComparison.Ordinal);
        s = s.Replace("icon_y = y + 5;", "icon_y = y + (5 * jamm_zy);", StringComparison.Ordinal);
        s = s.Replace("icon_x = (x + sprite_width) - 35;", "icon_x = (x + sprite_width) - (35 * jamm_zx);", StringComparison.Ordinal);
        s = s.Replace("icon_y = (y + sprite_height) - 45;", "icon_y = (y + sprite_height) - (45 * jamm_zy);", StringComparison.Ordinal);

        if (entry == "gml_Object_UpgradeCard_Other_11")
        {
            s = s.Replace("box_height = 20 + string_height_ext(UpgradeData.desc[card_id], 20, 280);",
                "box_height = (20 + string_height_ext(UpgradeData.desc[card_id], 20, 280)) * jamm_zy;", StringComparison.Ordinal);
            s = s.Replace("draw_rectangle(mouse_x + 1, mouse_y + 1, mouse_x + 299, (mouse_y + box_height) - 1, false);",
                "draw_rectangle(mouse_x + jamm_zx, mouse_y + jamm_zy, mouse_x + (299 * jamm_zx), (mouse_y + box_height) - jamm_zy, false);", StringComparison.Ordinal);
            s = s.Replace("draw_rectangle(mouse_x, mouse_y, mouse_x + 300, mouse_y + box_height, true);",
                "draw_rectangle(mouse_x, mouse_y, mouse_x + (300 * jamm_zx), mouse_y + box_height, true);", StringComparison.Ordinal);
            s = s.Replace("draw_text_ext(mouse_x + 10, mouse_y + 10, UpgradeData.desc[card_id], 20, 280);",
                "draw_text_ext_transformed(mouse_x + (10 * jamm_zx), mouse_y + (10 * jamm_zy), UpgradeData.desc[card_id], 20, 280, jamm_zx, jamm_zy, 0);", StringComparison.Ordinal);
        }

        if (entry == "gml_Object_RodCard_Other_11")
        {
            s = Regex.Replace(s,
                @"draw_rectangle_colour\(x \+ 42,\s*y \+ (\d+),\s*x \+ 42 \+ \(94 \* ([a-z_]+)\),\s*y \+ \1 \+ 10,",
                "draw_rectangle_colour(x + (42 * jamm_zx), y + ($1 * jamm_zy), x + ((42 + (94 * $2)) * jamm_zx), y + (($1 + 10) * jamm_zy),");
        }

        // Scale x +/- K and y +/- K (and center_x + K) outside sprite_width/height expressions.
        s = ScaleCoordOffsets(s, "x", "jamm_zx");
        s = ScaleCoordOffsets(s, "y", "jamm_zy");
        s = ScaleCoordOffsets(s, "center_x", "jamm_zx");
        s = s.Replace("y + (height * 0.5) + 10", "y + ((height * 0.5) * jamm_zy) + (10 * jamm_zy)", StringComparison.Ordinal);

        // Scale draw_sprite, draw_sprite_ext, draw_text, draw_text_ext, draw_text_shadowed, draw_text_shadowed2.
        s = ScaleDrawSpriteCalls(s);
        s = ScaleDrawSpriteExtCalls(s);
        s = ScaleDrawTextCalls(s);
        s = ScaleDrawTextExtCalls(s);
        s = ScaleDrawTextShadowedCalls(s, "jamm_zx", "jamm_zy");
        s = ScaleDrawTextShadowed2Calls(s, "jamm_zx", "jamm_zy");

        // UpgradeCard tooltip already handled above; undo double-scaling on mouse_x/mouse_y if any (none matched).
        return s;
    }

    private static string ScaleCoordOffsets(string gml, string axis, string scaleExpr)
    {
        var tokens = GmlReferences.Tokens(gml);
        var edits = new List<(int Start, int Length, string Replacement)>();
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != axis)
                continue;
            if (i > 0 && tokens[i - 1].Value == ".")
                continue;
            string op = tokens[i + 1].Value;
            if (op is not ("+" or "-"))
                continue;
            if (tokens[i + 2].Kind != 'n')
                continue;
            // Do not touch `x + sprite_width` or `x + 42 + (94 * ...)` already rewritten.
            if (i + 3 < tokens.Count && tokens[i + 3].Value is "*" or "/")
                continue;
            string num = tokens[i + 2].Value;
            int start = tokens[i + 2].Start;
            int length = tokens[i + 2].Length;
            edits.Add((start, length, $"({num} * {scaleExpr})"));
        }
        if (edits.Count == 0)
            return gml;
        var sb = new StringBuilder(gml);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            sb.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        return sb.ToString();
    }

    private static string ScaleDrawTextShadowedCalls(string gml, string sx, string sy) =>
        RewriteCalls(gml, "draw_text_shadowed", args => args.Count == 3
            ? $"{ShadowScript}({args[0]}, {args[1]}, {args[2]}, {sx}, {sy})"
            : null);

    private static string ScaleDrawTextShadowed2Calls(string gml, string sx, string sy) =>
        RewriteCalls(gml, "draw_text_shadowed2", args => args.Count == 3
            ? $"{Shadow2Script}({args[0]}, {args[1]}, {args[2]}, {sx}, {sy})"
            : null);

    private static string ScaleDrawTextCalls(string gml) =>
        RewriteCalls(gml, "draw_text", args => args.Count == 3
            ? $"draw_text_transformed({args[0]}, {args[1]}, {args[2]}, jamm_zx, jamm_zy, 0)"
            : null);

    private static string ScaleDrawTextExtCalls(string gml) =>
        RewriteCalls(gml, "draw_text_ext", args => args.Count == 5
            ? $"draw_text_ext_transformed({args[0]}, {args[1]}, {args[2]}, {args[3]}, {args[4]}, jamm_zx, jamm_zy, 0)"
            : null);

    private static string ScaleDrawSpriteCalls(string gml) =>
        RewriteCalls(gml, "draw_sprite", args => args.Count == 4
            ? $"draw_sprite_ext({args[0]}, {args[1]}, {args[2]}, {args[3]}, jamm_zx, jamm_zy, 0, c_white, 1)"
            : null);

    private static string ScaleDrawSpriteExtCalls(string gml) =>
        RewriteCalls(gml, "draw_sprite_ext", args =>
        {
            if (args.Count != 9)
                return null;
            // UpgradeCard/Slot BuildUpgradeIconDraw uses jamm_sc (which already includes icon_scale * jamm_zx).
            if (args[4].Contains("jamm_sc", StringComparison.Ordinal) || args[4].Contains("icon_scale", StringComparison.Ordinal) || args[4].Contains("scale", StringComparison.Ordinal))
                return null;
            return $"draw_sprite_ext({args[0]}, {args[1]}, {args[2]}, {args[3]}, ({args[4]}) * jamm_zx, ({args[5]}) * jamm_zy, {args[6]}, {args[7]}, {args[8]})";
        });

    private static string RewriteCalls(string gml, string fn, Func<List<string>, string?> format)
    {
        var t = GmlReferences.Tokens(gml);
        var edits = new List<(int Start, int Length, string Replacement)>();
        for (int i = 0; i + 1 < t.Count; i++)
        {
            if (t[i].Kind != 'i' || t[i].Value != fn || t[i + 1].Value != "(")
                continue;
            if (i > 0 && t[i - 1].Value == ".")
                continue;
            int depth = 0, start = i + 2, end = start;
            var args = new List<string>();
            for (; end < t.Count; end++)
            {
                if (t[end].Kind == 'p')
                {
                    if (t[end].Value == ")" && depth == 0) { AddArg(end); break; }
                    if (t[end].Value == "," && depth == 0) { AddArg(end); start = end + 1; }
                    else if (t[end].Value is "(" or "[" or "{") depth++;
                    else if (t[end].Value is ")" or "]" or "}") depth--;
                }
            }
            if (end == t.Count)
                continue;
            string? replacement = format(args);
            if (replacement is not null)
                edits.Add((t[i].Start, t[end].Start + t[end].Length - t[i].Start, replacement));
            i = end;

            void AddArg(int stop)
            {
                if (start < stop)
                    args.Add(gml[t[start].Start..t[stop].Start].Trim());
            }
        }
        if (edits.Count == 0)
            return gml;
        var sb = new StringBuilder(gml);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            sb.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        return sb.ToString();
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int IndexOfIgnoringIndentation(string haystack, string needle, out string? matched)
    {
        var lines = needle.Split('\n');
        var pattern = string.Join(@"\s*\n\s*", lines.Select(l => Regex.Escape(l.Trim())));
        var m = Regex.Match(haystack, pattern);
        if (!m.Success)
        {
            matched = null;
            return -1;
        }
        matched = m.Value;
        return m.Index;
    }
}
