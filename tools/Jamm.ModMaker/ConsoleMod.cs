using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// Optional in-game console overlay (<c>obj_jamm_console</c>) toggled with F12 (or backtick/grave),
/// supporting <c>help</c> and <c>unlockall</c> without any hotkey cheat outside the console.
/// </summary>
public static class ConsoleMod
{
    /// <summary>Name of the persistent console runtime controller.</summary>
    public const string ObjectName = "obj_jamm_console";

    /// <summary>Adds the persistent in-game console controller to an original legacy game archive.</summary>
    public static void Apply(UndertaleData data)
    {
        foreach (string required in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
        {
            if (data.GameObjects.ByName(required) is null)
                throw new InvalidDataException($"Required object '{required}' is missing: build the console mod from the original legacy 2023 data.win.");
        }

        var compiler = new CompileGroup(data);
        var console = ModObjects.Create(data, ObjectName, persistent: true);
        compiler.QueueCodeReplace(console.EventHandlerFor(EventType.Create, 0u, data), Create);
        compiler.QueueCodeReplace(console.EventHandlerFor(EventType.Destroy, 0u, data), Destroy);
        compiler.QueueCodeReplace(console.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), BeginStep(data));
        compiler.QueueCodeReplace(console.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), EndStep(data));
        compiler.QueueCodeReplace(console.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, data), DrawGui(data));
        ModObjects.Compile(compiler);

        foreach (string room in new[] { "NewWorld", "room11" })
            ModObjects.PlaceInstance(data, room, console, first: true);
    }

    private const string Create = """
        if (instance_number(object_index) > 1)
        {
            instance_destroy();
            exit;
        }
        persistent = true;
        jamm_console_owner = true;
        jamm_console_open = false;
        jamm_console_input = "";
        jamm_console_history = ds_list_create();
        jamm_console_hist_pos = 0;
        jamm_console_log = ds_list_create();
        jamm_console_unlocked_all = false;
        jamm_console_toast = current_time + 4000;
        ds_list_add(jamm_console_log, "JAMM Console ready (F12 or ` to toggle). Type 'help' for commands.");
        """;

    private const string Destroy = """
        if (variable_instance_exists(id, "jamm_console_owner") && jamm_console_owner)
        {
            ds_list_destroy(jamm_console_history);
            ds_list_destroy(jamm_console_log);
        }
        """;

    private static string BeginStep(UndertaleData data)
    {
        bool hasRod = data.GameObjects.ByName("Rod") is not null;
        bool hasHandler = data.GameObjects.ByName("Handler") is not null;
        bool hasAmbiance = data.GameObjects.ByName("AmbianceController") is not null;
        string unlockSwing = hasRod
            ? (hasHandler ? "if (instance_exists(Rod) && !instance_exists(Handler)) Rod.swing_locked = 0;" : "if (instance_exists(Rod)) Rod.swing_locked = 0;")
            : "";
        string lockSwing = hasRod ? "if (instance_exists(Rod)) Rod.swing_locked = 1;" : "";
        string clearLockDay = hasAmbiance ? "if (instance_exists(AmbianceController)) AmbianceController.lockDay = 0;" : "";

        return $$"""
        if (keyboard_check_pressed(vk_f12) || keyboard_check_pressed(192))
        {
            jamm_console_open = !jamm_console_open;
            keyboard_string = "";
            jamm_console_input = "";
            jamm_console_hist_pos = ds_list_size(jamm_console_history);
            keyboard_clear(vk_f12);
            keyboard_clear(192);
            if (!jamm_console_open)
            {
                {{unlockSwing}}
            }
            exit;
        }
        if (!jamm_console_open) exit;

        {{lockSwing}}
        if (keyboard_check_pressed(vk_escape))
        {
            jamm_console_open = false;
            keyboard_string = "";
            jamm_console_input = "";
            keyboard_clear(vk_escape);
            {{unlockSwing}}
            exit;
        }
        if (keyboard_check_pressed(vk_up) && ds_list_size(jamm_console_history) > 0)
        {
            jamm_console_hist_pos = max(0, jamm_console_hist_pos - 1);
            keyboard_string = ds_list_find_value(jamm_console_history, jamm_console_hist_pos);
            keyboard_clear(vk_up);
        }
        if (keyboard_check_pressed(vk_down))
        {
            var jamm_hsize = ds_list_size(jamm_console_history);
            if (jamm_console_hist_pos < jamm_hsize)
                jamm_console_hist_pos += 1;
            if (jamm_console_hist_pos < jamm_hsize)
                keyboard_string = ds_list_find_value(jamm_console_history, jamm_console_hist_pos);
            else
                keyboard_string = "";
            keyboard_clear(vk_down);
        }

        var jamm_clean = string_replace_all(keyboard_string, "`", "");
        jamm_clean = string_replace_all(jamm_clean, "~", "");
        jamm_clean = string_replace_all(jamm_clean, chr(13), "");
        jamm_clean = string_replace_all(jamm_clean, chr(10), "");
        if (string_length(jamm_clean) > 120)
            jamm_clean = string_copy(jamm_clean, 1, 120);
        keyboard_string = jamm_clean;
        jamm_console_input = jamm_clean;

        if (keyboard_check_pressed(vk_enter))
        {
            var jamm_raw = jamm_console_input;
            while (string_length(jamm_raw) > 0 && string_char_at(jamm_raw, 1) == " ")
                jamm_raw = string_delete(jamm_raw, 1, 1);
            while (string_length(jamm_raw) > 0 && string_char_at(jamm_raw, string_length(jamm_raw)) == " ")
                jamm_raw = string_delete(jamm_raw, string_length(jamm_raw), 1);

            keyboard_string = "";
            jamm_console_input = "";
            keyboard_clear(vk_enter);

            if (string_length(jamm_raw) > 0)
            {
                ds_list_add(jamm_console_history, jamm_raw);
                jamm_console_hist_pos = ds_list_size(jamm_console_history);
                ds_list_add(jamm_console_log, "> " + jamm_raw);
                var jamm_cmd = string_lower(jamm_raw);
                if (jamm_cmd == "help")
                {
                    ds_list_add(jamm_console_log, "Commands: help, unlockall, clear, close");
                    ds_list_add(jamm_console_log, "  unlockall : unlock all upgrades, rods, lures, boats, tools/gear, hats, HUD shop icons, and nightfall (no money/catalog changes)");
                }
                else if (jamm_cmd == "clear")
                {
                    ds_list_clear(jamm_console_log);
                }
                else if (jamm_cmd == "close" || jamm_cmd == "exit")
                {
                    jamm_console_open = false;
                    {{unlockSwing}}
                }
                else if (jamm_cmd == "unlockall")
                {
                    var jamm_upg_total = 0;
                    var jamm_upg_new = 0;
                    if (instance_exists(UpgradeData))
                    {
                        jamm_upg_total = array_length_1d(UpgradeData.unlocks);
                        for (var jamm_i = 0; jamm_i < jamm_upg_total; jamm_i += 1)
                        {
                            if (UpgradeData.unlocks[jamm_i] <= 0) jamm_upg_new += 1;
                            UpgradeData.unlocks[jamm_i] = 1;
                        }
                    }
                    var jamm_rod_total = 0;
                    var jamm_rod_new = 0;
                    if (instance_exists(RodData))
                    {
                        RodData.nItems = max(RodData.nItems, array_length_1d(RodData.name));
                        jamm_rod_total = RodData.nItems;
                        for (var jamm_i = 0; jamm_i < jamm_rod_total; jamm_i += 1)
                        {
                            if (RodData.unlocked[jamm_i] <= 0) jamm_rod_new += 1;
                            RodData.unlocked[jamm_i] = 1;
                        }
                        if (instance_exists(Girl)) Girl.bestRod = max(Girl.bestRod, jamm_rod_total - 1);
                    }
                    var jamm_lure_total = 0;
                    var jamm_lure_new = 0;
                    if (instance_exists(LureData))
                    {
                        LureData.nItems = max(LureData.nItems, array_length_1d(LureData.name));
                        jamm_lure_total = LureData.nItems;
                        for (var jamm_i = 0; jamm_i < jamm_lure_total; jamm_i += 1)
                        {
                            if (LureData.unlocked[jamm_i] <= 0) jamm_lure_new += 1;
                            LureData.unlocked[jamm_i] = 1;
                        }
                    }
                    var jamm_boat_total = 0;
                    var jamm_boat_new = 0;
                    if (instance_exists(BoatData))
                    {
                        BoatData.nItems = max(BoatData.nItems, array_length_1d(BoatData.name));
                        jamm_boat_total = BoatData.nItems;
                        for (var jamm_i = 0; jamm_i < jamm_boat_total; jamm_i += 1)
                        {
                            if (BoatData.unlocked[jamm_i] <= 0) jamm_boat_new += 1;
                            BoatData.unlocked[jamm_i] = 1;
                        }
                        if (instance_exists(Girl)) Girl.bestBoat = max(Girl.bestBoat, jamm_boat_total - 1);
                    }
                    var jamm_tool_total = 0;
                    var jamm_tool_new = 0;
                    if (instance_exists(ToolData))
                    {
                        ToolData.nItems = max(ToolData.nItems, array_length_1d(ToolData.name));
                        jamm_tool_total = ToolData.nItems;
                        for (var jamm_i = 0; jamm_i < jamm_tool_total; jamm_i += 1)
                        {
                            if (ToolData.unlocked[jamm_i] <= 0) jamm_tool_new += 1;
                            ToolData.unlocked[jamm_i] = 1;
                            if (instance_exists(ToolData.item[jamm_i])) ToolData.item[jamm_i].active = 1;
                        }
                    }
                    var jamm_hat_total = 0;
                    var jamm_hat_new = 0;
                    if (instance_exists(GearData))
                    {
                        jamm_hat_total = GearData.n_gear;
                        for (var jamm_i = 0; jamm_i < jamm_hat_total; jamm_i += 1)
                        {
                            if (GearData.unlocked[jamm_i] <= 0) jamm_hat_new += 1;
                            GearData.unlocked[jamm_i] = 1;
                        }
                    }
                    var jamm_icon_new = 0;
                    with (Icons)
                    {
                        if (unlocked <= 0) other.jamm_icon_new += 1;
                        unlocked = 1;
                    }
                    if (instance_exists(Girl))
                    {
                        Girl.nightfallUnlocked = 1;
                        Girl.NightfallUnlocked = 1;
                        if (Girl.quest_max < 3) Girl.quest_max = 3;
                    }
                    {{clearLockDay}}
                    jamm_console_unlocked_all = true;
                    ds_list_add(jamm_console_log, "Unlocked: " + string(jamm_upg_total) + " upgrades (" + string(jamm_upg_new) + " new), " + string(jamm_rod_total) + " rods (" + string(jamm_rod_new) + " new), " + string(jamm_lure_total) + " lures (" + string(jamm_lure_new) + " new), " + string(jamm_boat_total) + " boats (" + string(jamm_boat_new) + " new).");
                    ds_list_add(jamm_console_log, "Unlocked: " + string(jamm_tool_total) + " tools/gear (" + string(jamm_tool_new) + " new), " + string(jamm_hat_total) + " hats (" + string(jamm_hat_new) + " new), " + string(jamm_icon_new) + " HUD menus, nightfall=1. Money and catalog unchanged.");
                    ds_list_add(jamm_console_log, "WARNING: The next autosave (or opening the Esc save menu) will keep these unlocks in your save file.");
                }
                else
                {
                    ds_list_add(jamm_console_log, "Unknown command '" + jamm_raw + "'. Type 'help' for available commands.");
                }

                while (ds_list_size(jamm_console_log) > 10)
                    ds_list_delete(jamm_console_log, 0);
            }
        }

        // Prevent gameplay KeyPress/Keyboard handlers (B/H/L/R lure buys, S sell, Q swim, A/D boat, etc.) from firing while typing in console.
        for (var jamm_k = 48; jamm_k <= 90; jamm_k += 1)
            keyboard_clear(jamm_k);
        keyboard_clear(vk_space);
        keyboard_clear(vk_shift);
        keyboard_clear(vk_control);
        keyboard_clear(vk_tab);
        keyboard_clear(vk_left);
        keyboard_clear(vk_right);
        """;
    }

    private static string EndStep(UndertaleData data)
    {
        bool hasAmbiance = data.GameObjects.ByName("AmbianceController") is not null;
        return $$"""
        if (jamm_console_unlocked_all)
        {
            if (instance_exists(LureData) && array_length_1d(LureData.unlocked) > 5)
                LureData.unlocked[5] = 1;
            {{(hasAmbiance ? "if (instance_exists(AmbianceController)) AmbianceController.lockDay = 0;" : "")}}
        }
        """;
    }

    private static string DrawGui(UndertaleData data)
    {
        string fontSetup = data.Fonts.ByName("fnt_babySmall") is not null
            ? "draw_set_font(fnt_babySmall);"
            : "draw_set_font(-1);";
        return $$"""
        if (!jamm_console_open)
        {
            if (current_time < jamm_console_toast)
            {
                var jamm_toast = "Console mod: F12 or ` to open";
                {{fontSetup}}
                draw_set_halign(fa_left);
                draw_set_valign(fa_top);
                draw_set_alpha(min(1, (jamm_console_toast - current_time) / 500));
                draw_set_color(c_black);
                draw_rectangle(8, 116, 20 + string_width(jamm_toast), 128 + string_height(jamm_toast), false);
                draw_set_color(c_white);
                draw_text(14, 122, jamm_toast);
                draw_set_alpha(1);
            }
            exit;
        }

        {{fontSetup}}
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
        var jamm_gw = max(640, display_get_gui_width());
        var jamm_line_h = max(18, string_height("Ag") + 4);
        var jamm_lines = ds_list_size(jamm_console_log);
        var jamm_box_h = 44 + (jamm_lines * jamm_line_h);
        draw_set_alpha(0.88);
        draw_set_color(c_black);
        draw_rectangle(8, 8, jamm_gw - 8, 8 + jamm_box_h, false);
        draw_set_alpha(1);
        draw_set_color(c_gray);
        draw_rectangle(8, 8, jamm_gw - 8, 8 + jamm_box_h, true);
        var jamm_y = 14;
        for (var jamm_i = 0; jamm_i < jamm_lines; jamm_i += 1)
        {
            var jamm_entry = ds_list_find_value(jamm_console_log, jamm_i);
            if (string_pos("WARNING:", jamm_entry) == 1)
                draw_set_color(c_yellow);
            else if (string_pos(">", jamm_entry) == 1)
                draw_set_color(c_aqua);
            else
                draw_set_color(c_white);
            draw_text(16, jamm_y, jamm_entry);
            jamm_y += jamm_line_h;
        }
        draw_set_color(c_lime);
        draw_text(16, jamm_y + 4, "> " + jamm_console_input + "_");
        draw_set_color(c_white);
        """;
    }
}
