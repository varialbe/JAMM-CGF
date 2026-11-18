using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

/// <summary>Injects row-windowed paging into legacy CardHandler and UpgradeMenu UI handlers.</summary>
internal static class MenuRuntime
{
    internal const string CardHandlerCreate = "gml_Object_CardHandler_Create_0";
    internal const string CardHandlerAlarm = "gml_Object_CardHandler_Alarm_0";
    internal const string CardHandlerStep = "gml_Object_CardHandler_Step_0";
    internal const string CardHandlerDraw = "gml_Object_CardHandler_Draw_0";
    internal const string RodMenuDraw = "gml_Object_RodMenu_Draw_0";
    internal const string BoatCardClick = "gml_Object_BoatCard_Other_10";
    internal const string BoatDataCreate = "gml_Object_BoatData_Create_0";
    internal const string FishalogDraw = "gml_Object_Fishalog_Draw_0";
    internal const string FishalogHandleStep = "gml_Object_CatalogHandle_Step_2";
    internal const string FishalogHandleDrag = "gml_Object_CatalogHandle_Mouse_50";
    internal const string CatalogItemDraw = "gml_Object_CatalogItem_Other_10";

    internal const string UpgradeMenuCreate = "gml_Object_UpgradeMenu_Create_0";
    internal const string UpgradeMenuAlarm = "gml_Object_UpgradeMenu_Alarm_0";
    internal const string UpgradeMenuStep = "gml_Object_UpgradeMenu_Step_0";
    internal const string UpgradeMenuDraw = "gml_Object_UpgradeMenu_Draw_0";

    internal static BuildPlan PreparePlan(BuildPlan plan, IReadOnlyDictionary<string, CodeText> baseline, UndertaleData? original = null)
    {
        bool hasCardHandler = baseline.ContainsKey(CardHandlerCreate);
        bool hasUpgradeMenu = baseline.ContainsKey(UpgradeMenuCreate);
        var assets = plan.Assets.ToList();
        var decisions = plan.Decisions.ToList();
        bool hasSmallFont = original?.Fonts.ByName("fnt_babySmall") is not null;
        List<string> patchedMenus = [];

        if (hasCardHandler)
            TryPatch("CardHandler (2 rows)", () => PatchCardHandler(assets, baseline, hasSmallFont));
        if (hasUpgradeMenu)
            TryPatch("UpgradeMenu (9 rows)", () => PatchUpgradeMenu(assets, baseline, hasSmallFont));

        TryPatchOptional("Boat card equip index", () => PatchBoatCard(assets, baseline));
        TryPatchOptional("invalid Dark Boat registration", () => PatchInvalidDarkBoat(assets, baseline));
        TryPatchOptional("Fishalog layout", () => PatchFishalog(assets, baseline, plan.Catalog));

        if (patchedMenus.Count > 0)
            decisions.Add($"Menu paging: injected row-windowed scrolling for {string.Join(" and ", patchedMenus)}.");

        // Paging is an enhancement: when a selected mod rewrote a menu so the patch points no longer fit,
        // keep that menu exactly as the mods built it instead of failing the whole build.
        void TryPatch(string label, Action patch)
        {
            var snapshot = assets.ToList();
            try { patch(); patchedMenus.Add(label); }
            catch (InvalidDataException e)
            {
                assets.Clear(); assets.AddRange(snapshot);
                decisions.Add($"Menu paging skipped for {label}: {e.Message} The menu keeps its unpaged layout.");
            }
        }
        void TryPatchOptional(string label, Action patch)
        {
            var snapshot = assets.ToList();
            try { patch(); }
            catch (InvalidDataException e)
            {
                assets.Clear(); assets.AddRange(snapshot);
                decisions.Add($"Menu layout skipped for {label}: {e.Message}");
            }
        }
        return plan with { Assets = assets, Decisions = decisions };
    }

    internal static void Inject(UndertaleData data, Dictionary<string, string> recompile)
    {
        if (recompile.ContainsKey(CardHandlerStep) && data.GameObjects.ByName("CardHandler") is { } cardHandler)
        {
            Attach(cardHandler, EventType.Step, (uint)EventSubtypeStep.Step, CardHandlerStep);
            Attach(cardHandler, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, CardHandlerDraw);
        }

        if (recompile.ContainsKey(UpgradeMenuStep) && data.GameObjects.ByName("UpgradeMenu") is { } upgradeMenu)
        {
            Attach(upgradeMenu, EventType.Step, (uint)EventSubtypeStep.Step, UpgradeMenuStep);
            Attach(upgradeMenu, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, UpgradeMenuDraw);
        }

        void Attach(UndertaleGameObject obj, EventType type, uint subtype, string codeName)
        {
            var code = data.Code.ByName(codeName)
                ?? throw new InvalidDataException($"Cannot attach '{codeName}': code entry is missing from the merged archive.");
            var events = obj.Events[(int)type];
            var ev = events.FirstOrDefault(e => e.EventSubtype == subtype);
            if (ev is null) { ev = new() { EventSubtype = subtype }; events.Add(ev); }
            if (ev.Actions.Count == 0) ev.Actions.Add(new() { ActionName = data.Strings.MakeString("") });
            ev.Actions[0].CodeId = code;
        }
    }

    private static void PatchCardHandler(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, bool hasSmallFont)
    {
        string create = Current(assets, baseline, CardHandlerCreate);
        RequireAnchor(CardHandlerCreate, create, "card_width = 3;");
        Put(assets, baseline, CardHandlerCreate, ReplaceOnce(CardHandlerCreate, create, "card_width = 3;",
            """
            card_width = 3;
            visible = true;
            visible_rows = 2;
            scroll_row = 0;
            max_scroll_row = 0;
            n_visible = 0;
            window_pos = 0;
            window_end = 0;
            jamm_menu_init = 0;
            """));

        string alarm = RequireExisting(assets, baseline, CardHandlerAlarm);
        RequireAnchor(CardHandlerAlarm, alarm, "r = instance_create(x, y, card_type);");
        RequireAnchor(CardHandlerAlarm, alarm, "done_spawning = 1;");
        Put(assets, baseline, CardHandlerAlarm, BuildCardHandlerAlarmGml());

        AppendOrPut(assets, baseline, CardHandlerStep, BuildStepGml("Card"));
        AppendOrPut(assets, baseline, CardHandlerDraw, BuildDrawGml(hasSmallFont));

        if (HasCode(assets, baseline, RodMenuDraw))
        {
            string rodDraw = Current(assets, baseline, RodMenuDraw);
            RequireAnchor(RodMenuDraw, rodDraw, "if (done_spawning)");
            if (!rodDraw.Contains("event_inherited", StringComparison.Ordinal) && !rodDraw.Contains("action_inherited", StringComparison.Ordinal))
                Put(assets, baseline, RodMenuDraw, "event_inherited();\n" + rodDraw);
        }
    }

    private static void PatchUpgradeMenu(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, bool hasSmallFont)
    {
        string create = Current(assets, baseline, UpgradeMenuCreate);
        RequireAnchor(UpgradeMenuCreate, create, "spawn_order[");
        RequireAnchor(UpgradeMenuCreate, create, "spawns = 0;");
        Put(assets, baseline, UpgradeMenuCreate, ReplaceOnce(UpgradeMenuCreate, create, "spawns = 0;",
            """
            spawns = 0;
            visible = true;
            ORIGIN_Y = y;
            card_width = 2;
            visible_rows = 9;
            scroll_row = 0;
            max_scroll_row = 0;
            n_visible = 0;
            window_pos = 0;
            window_end = 0;
            jamm_menu_init = 0;
            """));

        string alarm = RequireExisting(assets, baseline, UpgradeMenuAlarm);
        RequireAnchor(UpgradeMenuAlarm, alarm, "UpgradeData.unlocks[spawn_order[iCard]]");
        if (!System.Text.RegularExpressions.Regex.IsMatch(alarm, @"iCard <= \d+"))
            throw new InvalidDataException($"Cannot patch '{UpgradeMenuAlarm}': expected anchor 'iCard <= N' was not found.");
        Put(assets, baseline, UpgradeMenuAlarm, BuildUpgradeMenuAlarmGml());

        AppendOrPut(assets, baseline, UpgradeMenuStep, BuildStepGml("UpgradeCard"));
        AppendOrPut(assets, baseline, UpgradeMenuDraw, BuildDrawGml(hasSmallFont));
    }

    private static void PatchBoatCard(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline)
    {
        if (!HasCode(assets, baseline, BoatCardClick)) return;
        string text = Current(assets, baseline, BoatCardClick);
        const string create = "instance_create(0, 0, BoatData.object[item_id]);";
        if (text.Contains("boat_index = item_id", StringComparison.Ordinal)) return;
        if (!text.Contains(create, StringComparison.Ordinal))
            throw new InvalidDataException($"Cannot patch '{BoatCardClick}': expected the BoatData object spawn call.");
        Put(assets, baseline, BoatCardClick, ReplaceOnce(BoatCardClick, text, create,
            "var jamm_menu_boat;\njamm_menu_boat = instance_create(0, 0, BoatData.object[item_id]);\njamm_menu_boat.boat_index = item_id;"));
    }

    private static void PatchInvalidDarkBoat(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline)
    {
        if (!HasCode(assets, baseline, BoatDataCreate)) return;
        string text = Current(assets, baseline, BoatDataCreate);
        // This legacy Boat8 registration points at scenery; the archive also contains the actual Neon Boat assets.
        const string badRegistration = "name[8] = \"Dark Boat\";\nobject[8] = BoatBackground;\ndisp[8] = bg_parallax;\ndesc[8] = \"The most op Boat\";";
        if (!text.Contains(badRegistration, StringComparison.Ordinal)) return;
        const string correctedRegistration = "name[8] = \"Neon Boat\";\nobject[8] = Boat8;\ndisp[8] = spr_boat_icon8;\ndesc[8] = \"Travels with flash speed and#holds many fish.\";";
        Put(assets, baseline, BoatDataCreate, ReplaceOnce(BoatDataCreate, text, badRegistration, correctedRegistration));
    }

    private static void PatchFishalog(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, IReadOnlyList<CatalogMapping> catalog)
    {
        if (!HasCode(assets, baseline, FishalogDraw)) return;
        string draw = Current(assets, baseline, FishalogDraw);
        const string fallback = "csurf = surface_create(570, 274);";
        if (draw.Contains(fallback, StringComparison.Ordinal))
        {
            draw = ReplaceOnce(FishalogDraw, draw, fallback,
                "csurf = surface_create(surfw, surfh);");
        }
        const string height = "scroll_height = py - scroll_y - CatalogItem.sprite_height;";
        if (draw.Contains(height, StringComparison.Ordinal))
            draw = ReplaceOnce(FishalogDraw, draw, height,
                "scroll_height = max(0, py - scroll_y - CatalogItem.sprite_height);");
        if (draw != Current(assets, baseline, FishalogDraw)) Put(assets, baseline, FishalogDraw, draw);

        if (HasCode(assets, baseline, FishalogHandleStep))
        {
            string handle = Current(assets, baseline, FishalogHandleStep);
            string patched = "var jamm_track, jamm_thumb;\njamm_track = CatalogDown.y - CatalogUp.y;\njamm_thumb = max(12, jamm_track * Catalog.surfh / max(1, Catalog.surfh + Catalog.scroll_height));\nymax = max(0, jamm_track - jamm_thumb);\nimage_yscale = jamm_thumb / max(1, sprite_get_height(sprite_index));\nif (!clicked) ys = (Catalog.scroll_height > 0) ? (-Catalog.scroll_y / Catalog.scroll_height) * ymax : 0;\nys = clamp(ys, 0, ymax);\ny = yoffset + ys;";
            Put(assets, baseline, FishalogHandleStep, patched);
        }
        if (HasCode(assets, baseline, FishalogHandleDrag))
        {
            string drag = Current(assets, baseline, FishalogHandleDrag);
            const string oldDrag = "Catalog.scroll_y = ys / -0.03;";
            if (drag.Contains(oldDrag, StringComparison.Ordinal))
                Put(assets, baseline, FishalogHandleDrag, ReplaceOnce(FishalogHandleDrag, drag, oldDrag,
                    "Catalog.scroll_y = (ymax > 0) ? -(ys / ymax) * Catalog.scroll_height : 0;"));
        }

        if (catalog.Count == 0 || !HasCode(assets, baseline, CatalogItemDraw)) return;
        string item = Current(assets, baseline, CatalogItemDraw);
        const string itemEnd = "Catalog.py += sprite_height;";
        if (!item.Contains(itemEnd, StringComparison.Ordinal))
            throw new InvalidDataException($"Cannot add fish source labels: expected the row advance in '{CatalogItemDraw}'.");
        System.Text.StringBuilder labels = new();
        foreach (var mapping in catalog.DistinctBy(m => (m.Area, m.Slot)))
        {
            string displaySource = mapping.Mod.Length > 22 ? mapping.Mod[..21] + "…" : mapping.Mod;
            string source = displaySource.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
            labels.AppendLine($"if (size == {mapping.Area} && index == {mapping.Slot})");
            labels.AppendLine("{");
            labels.AppendLine("    draw_set_color(Colors.ui_dark);");
            labels.AppendLine("    draw_text(520, Catalog.py + 8, \"Mod\");");
            labels.AppendLine("    if (mouse_x - (Catalog.x + Catalog.surfx) >= 520 && mouse_x - (Catalog.x + Catalog.surfx) <= 560 && mouse_y - (Catalog.y + Catalog.surfy) >= Catalog.py && mouse_y - (Catalog.y + Catalog.surfy) <= Catalog.py + sprite_height)");
            labels.AppendLine("    {");
            labels.AppendLine("        draw_set_color(Colors.ui_light);");
            labels.AppendLine("        draw_rectangle(365, Catalog.py + 3, 558, Catalog.py + 36, false);");
            labels.AppendLine($"        draw_text_ext(372, Catalog.py + 8, \"Added by {source}\", 12, 180);");
            labels.AppendLine("    }");
            labels.AppendLine("}");
        }
        Put(assets, baseline, CatalogItemDraw, ReplaceOnce(CatalogItemDraw, item, itemEnd,
            labels + itemEnd));
    }

    private static string BuildCardHandlerAlarmGml() => BuildWindowedAlarmGml(
        extraLocals: "",
        initPrefix: """
                for (jamm_i = 0; jamm_i < data_object.nItems; jamm_i += 1)
                {
                    if (data_object.unlocked[jamm_i] >= 0)
                    {
                        visible_items[n_visible] = jamm_i;
                        n_visible += 1;
                    }
                }
            """,
        spawnCard: """
                iCard = visible_items[window_pos];
                r = instance_create(x, y, card_type);
                r.item_id = iCard;
            """);

    private static string BuildUpgradeMenuAlarmGml() => BuildWindowedAlarmGml(
        extraLocals: ", jamm_id",
        initPrefix: """
                for (jamm_i = array_length_1d(spawn_order); jamm_i < array_length_1d(UpgradeData.unlocks); jamm_i += 1)
                {
                    spawn_order[jamm_i] = jamm_i;
                }
                for (jamm_i = 0; jamm_i < array_length_1d(UpgradeData.unlocks); jamm_i += 1)
                {
                    jamm_id = spawn_order[jamm_i];
                    if (UpgradeData.unlocks[jamm_id])
                    {
                        visible_items[n_visible] = jamm_id;
                        n_visible += 1;
                    }
                }
            """,
        spawnCard: """
                r = instance_create(x, y, UpgradeCard);
                r.card_id = visible_items[window_pos];
            """);

    private static string BuildWindowedAlarmGml(string extraLocals, string initPrefix, string spawnCard) =>
        $$"""
        if (!jamm_menu_init)
        {
            var jamm_i, jamm_total_rows{{extraLocals}};
            jamm_menu_init = 1;
            n_visible = 0;
        {{initPrefix}}
            jamm_total_rows = ceil(n_visible / card_width);
            max_scroll_row = max(0, ceil(jamm_total_rows / visible_rows) - 1);
            if (scroll_row > max_scroll_row)
            {
                scroll_row = max_scroll_row;
            }
            window_pos = scroll_row * visible_rows * card_width;
            window_end = min(n_visible, (scroll_row + 1) * visible_rows * card_width);
            x = ORIGIN_X;
            y = ORIGIN_Y;
        }
        if (window_pos < window_end)
        {
            var jamm_col;
        {{spawnCard}}
            jamm_col = (window_pos - (scroll_row * visible_rows * card_width)) + 1;
            if ((jamm_col % card_width) == 0)
            {
                x = ORIGIN_X;
                y += Y_GAP;
            }
            else
            {
                x += X_GAP;
            }
            window_pos += 1;
        }
        if (window_pos < window_end)
        {
            alarm[0] = DELAY;
        }
        else
        {
            done_spawning = 1;
        }
        """;

    private static string BuildStepGml(string cardObject) =>
        $$"""
        if (jamm_menu_init && max_scroll_row > 0)
        {
            var jamm_delta, jamm_bx, jamm_by_up, jamm_by_down, jamm_bw, jamm_bh, jamm_next, jamm_mx, jamm_my;
            jamm_delta = 0;
            jamm_bx = display_get_gui_width() - 52;
            jamm_by_up = max(12, (display_get_gui_height() * 0.5) - 52);
            jamm_bw = 36;
            jamm_bh = 28;
            jamm_by_down = jamm_by_up + jamm_bh + 36;
            jamm_mx = device_mouse_x_to_gui(0);
            jamm_my = device_mouse_y_to_gui(0);
            if (mouse_wheel_up() || keyboard_check_pressed(vk_up))
            {
                jamm_delta -= 1;
            }
            if (mouse_wheel_down() || keyboard_check_pressed(vk_down))
            {
                jamm_delta += 1;
            }
            if (mouse_check_button_pressed(mb_left))
            {
                if (jamm_mx >= jamm_bx && jamm_mx <= (jamm_bx + jamm_bw))
                {
                    if (jamm_my >= jamm_by_up && jamm_my <= (jamm_by_up + jamm_bh))
                    {
                        jamm_delta -= 1;
                    }
                    else if (jamm_my >= jamm_by_down && jamm_my <= (jamm_by_down + jamm_bh))
                    {
                        jamm_delta += 1;
                    }
                }
            }
            if (jamm_delta != 0)
            {
                jamm_next = clamp(scroll_row + jamm_delta, 0, max_scroll_row);
                if (jamm_next != scroll_row)
                {
                    scroll_row = jamm_next;
                    with ({{cardObject}})
                    {
                        instance_destroy();
                    }
                    x = ORIGIN_X;
                    y = ORIGIN_Y;
                    done_spawning = 0;
                    window_pos = scroll_row * visible_rows * card_width;
                    window_end = min(n_visible, (scroll_row + 1) * visible_rows * card_width);
                    alarm[0] = 1;
                }
            }
        }
        """;

    private static string BuildDrawGml(bool hasSmallFont)
    {
        _ = hasSmallFont;
        return $$"""
        if (jamm_menu_init && max_scroll_row > 0)
        {
            var jamm_bx, jamm_by_up, jamm_by_down, jamm_bw, jamm_bh, jamm_cx;
            var jamm_bg, jamm_fg, jamm_dim;
            jamm_bx = display_get_gui_width() - 52;
            jamm_by_up = max(12, (display_get_gui_height() * 0.5) - 52);
            jamm_bw = 36;
            jamm_bh = 28;
            jamm_by_down = jamm_by_up + jamm_bh + 36;
            jamm_bg = make_color_rgb(69, 77, 89);
            jamm_fg = make_color_rgb(254, 255, 189);
            jamm_dim = make_color_rgb(130, 136, 145);
            jamm_cx = jamm_bx + (jamm_bw * 0.5);

            draw_set_alpha(0.85);
            draw_set_color(jamm_bg);
            draw_rectangle(jamm_bx, jamm_by_up, jamm_bx + jamm_bw, jamm_by_up + jamm_bh, false);
            draw_rectangle(jamm_bx, jamm_by_down, jamm_bx + jamm_bw, jamm_by_down + jamm_bh, false);
            draw_set_alpha(1);

            if (scroll_row > 0)
            {
                draw_set_color(jamm_fg);
            }
            else
            {
                draw_set_color(jamm_dim);
            }
            draw_rectangle(jamm_bx, jamm_by_up, jamm_bx + jamm_bw, jamm_by_up + jamm_bh, true);
            draw_triangle(jamm_cx, jamm_by_up + 6, jamm_bx + 8, (jamm_by_up + jamm_bh) - 6, (jamm_bx + jamm_bw) - 8, (jamm_by_up + jamm_bh) - 6, false);

            if (scroll_row < max_scroll_row)
            {
                draw_set_color(jamm_fg);
            }
            else
            {
                draw_set_color(jamm_dim);
            }
            draw_rectangle(jamm_bx, jamm_by_down, jamm_bx + jamm_bw, jamm_by_down + jamm_bh, true);
            draw_triangle(jamm_cx, (jamm_by_down + jamm_bh) - 6, jamm_bx + 8, jamm_by_down + 6, (jamm_bx + jamm_bw) - 8, jamm_by_down + 6, false);

            draw_set_color(jamm_fg);
            draw_text(jamm_bx - 4, jamm_by_up + jamm_bh + 18, "Page " + string(scroll_row + 1) + "/" + string(max_scroll_row + 1));
        }
        """;
    }

    private static bool HasCode(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name) =>
        assets.Any(a => a.Family == "code" && a.Name == name) || baseline.ContainsKey(name);

    private static string Current(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name) =>
        assets.FirstOrDefault(a => a.Family == "code" && a.Name == name)?.Gml
        ?? baseline.GetValueOrDefault(name)?.Text
        ?? "";

    private static string RequireExisting(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name)
    {
        if (!HasCode(assets, baseline, name))
            throw new InvalidDataException($"Cannot patch '{name}': expected code entry is missing.");
        return Current(assets, baseline, name);
    }

    private static void RequireAnchor(string entryName, string text, string anchor)
    {
        if (!text.Contains(anchor, StringComparison.Ordinal))
            throw new InvalidDataException($"Cannot patch '{entryName}': expected anchor '{anchor}' was not found.");
    }

    private static string ReplaceOnce(string entryName, string text, string anchor, string replacement)
    {
        int index = text.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidDataException($"Cannot patch '{entryName}': expected anchor '{anchor}' was not found.");
        return string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + anchor.Length));
    }

    private static void AppendOrPut(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name, string body)
    {
        string existing = Current(assets, baseline, name);
        Put(assets, baseline, name, string.IsNullOrWhiteSpace(existing) ? body : existing.TrimEnd() + "\n" + body);
    }

    private static void Put(List<PlannedAsset> assets, IReadOnlyDictionary<string, CodeText> baseline, string name, string text)
    {
        int index = assets.FindIndex(a => a.Family == "code" && a.Name == name);
        if (index >= 0) { assets[index] = assets[index] with { Gml = text }; return; }
        var before = baseline.GetValueOrDefault(name);
        var after = before is null ? new CodeText(text, "gml", 0, 0, null, []) : before with { Text = text };
        var snapshot = AssetFields.Json(new { });
        assets.Add(new("code", name, 0, new AssetPatch(name, before is null ? null : snapshot, snapshot, [], before, after), snapshot, text));
    }
}
