using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// Optional in-game console overlay (<c>obj_jamm_console</c>) toggled with F12 (or backtick/grave),
/// supporting <c>help</c>, <c>unlockall</c>, <c>unlockgear</c>, <c>maxlevel</c>, <c>level</c>,
/// <c>catalog all</c>/<c>unlockfish</c>, and <c>money</c> without any hotkey cheat outside the console.
/// </summary>
public static class ConsoleMod
{
    /// <summary>Name of the persistent console runtime controller.</summary>
    public const string ObjectName = "obj_jamm_console";

    /// <summary>Adds the persistent in-game console controller to an original legacy game archive.</summary>
    public static void Apply(UndertaleData data)
    {
        var compiler = new CompileGroup(data);
        foreach (var (entry, body) in Install(data))
            compiler.QueueCodeReplace(entry, body);
        var guardTemplate = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jamm_console_input_guard");
        compiler.QueueCodeReplace(guardTemplate, InputGuard);
        ModObjects.Compile(compiler);

        var guard = guardTemplate.Instructions.ToArray();
        data.Code.Remove(guardTemplate);
        if (guard.Length > 0 && guard[^1].Kind == UndertaleModLib.Models.UndertaleInstruction.Opcode.Ret)
            guard = guard[..^1];

        foreach (var code in GameplayInputCode(data))
        {
            if (!LegacyCodeHooks.Supports(code))
                throw new InvalidDataException($"Cannot safely guard gameplay input event '{code.Name.Content}': unsupported legacy bytecode layout.");
            LegacyCodeHooks.Insert(code, _ => default, enterInline: guard);
        }
    }

    private const string InputGuard = "if ((variable_global_exists(\"jamm_console_open\") && global.jamm_console_open) || keyboard_check_pressed(192) || keyboard_check_pressed(vk_f12)) exit; jamm_console_guard_marker = 0;";

    private static IEnumerable<UndertaleCode> GameplayInputCode(UndertaleData data)
    {
        var inputTypes = new[] { EventType.Keyboard, EventType.KeyPress, EventType.KeyRelease };
        var seen = new HashSet<UndertaleCode>();
        foreach (var obj in data.GameObjects)
        foreach (var type in inputTypes)
        foreach (var ev in obj.Events[(int)type])
        foreach (var action in ev.Actions)
            if (action.CodeId is { } code && seen.Add(code))
                yield return code;
    }

    internal static IReadOnlyList<(UndertaleCode Entry, string Body)> Install(UndertaleData data)
    {
        foreach (string required in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
        {
            if (data.GameObjects.ByName(required) is null)
                throw new InvalidDataException($"Required object '{required}' is missing: build the console mod from the original legacy 2023 data.win.");
        }

        var console = ModObjects.Create(data, ObjectName, persistent: true);
        foreach (string room in new[] { "NewWorld", "room11" })
            ModObjects.PlaceInstance(data, room, console, first: true);
        return
        [
            (console.EventHandlerFor(EventType.Create, 0u, data), Create),
            (console.EventHandlerFor(EventType.Destroy, 0u, data), Destroy),
            (console.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), BeginStep(data)),
            (console.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), EndStep(data)),
            (console.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, data), DrawGui(data))
        ];
    }

    private const string SaveWarning = "WARNING: The next autosave (or opening the Esc save menu) will keep these changes in your save file.";

    private const string Create = """
        if (instance_number(object_index) > 1)
        {
            instance_destroy();
            exit;
        }
        persistent = true;
        jamm_console_owner = true;
        global.jamm_console_open = false;
        jamm_console_open = false; // legacy instance mirror: published ModKit mods (harbor-town, submarine) read obj_jamm_console.jamm_console_open
        jamm_console_input = "";
        jamm_console_history = ds_list_create();
        jamm_console_hist_pos = 0;
        jamm_console_log = ds_list_create();
        jamm_console_unlocked_all = false;
        jamm_console_slot_owner = -1;
        jamm_console_slot = -1;
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
        bool hasExperienceBar = data.GameObjects.ByName("ExperienceBar") is not null;
        bool hasCatalogData = data.GameObjects.ByName("CatalogData") is not null;
        bool hasSaveManager = data.GameObjects.ByName("SaveManager") is not null;
        string unlockSwing = hasRod
            ? (hasHandler ? "if (instance_exists(Rod) && !instance_exists(Handler)) Rod.swing_locked = 0;" : "if (instance_exists(Rod)) Rod.swing_locked = 0;")
            : "";
        string lockSwing = hasRod ? "if (instance_exists(Rod)) Rod.swing_locked = 1;" : "";
        string slotSync = SlotSyncGml(hasSaveManager);
        string catalogCommandWarning = hasCatalogData ? $"ds_list_add(jamm_console_log, \"{SaveWarning}\");" : "";

        return $$"""
        {{slotSync}}
        jamm_console_open = global.jamm_console_open;
        if (keyboard_check_pressed(vk_f12) || keyboard_check_pressed(192))
        {
            global.jamm_console_open = !global.jamm_console_open;
            keyboard_string = "";
            jamm_console_input = "";
            jamm_console_hist_pos = ds_list_size(jamm_console_history);
            keyboard_clear(vk_f12);
            keyboard_clear(192);
            if (!global.jamm_console_open)
            {
                {{unlockSwing}}
            }
            else
            {
                {{ClearInputKeys}}
            }
            exit;
        }
        if (!global.jamm_console_open) exit;

        {{lockSwing}}
        if (keyboard_check_pressed(vk_escape))
        {
            global.jamm_console_open = false;
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
                while (string_pos("  ", jamm_cmd) > 0)
                    jamm_cmd = string_replace_all(jamm_cmd, "  ", " ");
                if (jamm_cmd == "help")
                {
                    ds_list_add(jamm_console_log, "  help        : list commands");
                    ds_list_add(jamm_console_log, "  unlockall   : unlock gear, max level, and full fish catalog");
                    ds_list_add(jamm_console_log, "  unlockgear  : unlock equipment, upgrades, hats, HUD, nightfall");
                    ds_list_add(jamm_console_log, "  maxlevel    : set level to max");
                    ds_list_add(jamm_console_log, "  level <n>   : set level to n (0..max)");
                    ds_list_add(jamm_console_log, "  catalog all : mark all fish caught (alias: unlockfish)");
                    ds_list_add(jamm_console_log, "  money <n>   : set dollars (up to 1e15); money +<n> adds");
                    ds_list_add(jamm_console_log, "  clear       : clear console log");
                    ds_list_add(jamm_console_log, "  close/exit  : close console");
                }
                else if (jamm_cmd == "clear")
                {
                    ds_list_clear(jamm_console_log);
                }
                else if (jamm_cmd == "close" || jamm_cmd == "exit")
                {
                    global.jamm_console_open = false;
                    {{unlockSwing}}
                }
                else if (jamm_cmd == "unlockall")
                {
                    {{UnlockGearGml(hasAmbiance)}}
                    {{SetLevelGml(hasExperienceBar, "jamm_max_lvl")}}
                    {{CatalogAllGml(hasCatalogData, logIfMissing: false)}}
                    ds_list_add(jamm_console_log, "{{SaveWarning}}");
                }
                else if (jamm_cmd == "unlockgear")
                {
                    {{UnlockGearGml(hasAmbiance)}}
                    ds_list_add(jamm_console_log, "{{SaveWarning}}");
                }
                else if (jamm_cmd == "maxlevel")
                {
                    {{SetLevelGml(hasExperienceBar, "jamm_max_lvl")}}
                    ds_list_add(jamm_console_log, "{{SaveWarning}}");
                }
                else if (jamm_cmd == "level")
                {
                    ds_list_add(jamm_console_log, "Usage: level <n> (e.g. level 50)");
                }
                else if (string_pos("level ", jamm_cmd) == 1)
                {
                    var jamm_lvl_arg = string_delete(jamm_cmd, 1, 6);
                    {{DigitCheckGml("jamm_lvl_arg", "jamm_lvl_ok")}}
                    if (!jamm_lvl_ok)
                    {
                        ds_list_add(jamm_console_log, "Invalid level '" + jamm_lvl_arg + "'. Usage: level <n>");
                    }
                    else
                    {
                        {{SetLevelGml(hasExperienceBar, "real(jamm_lvl_arg)")}}
                        ds_list_add(jamm_console_log, "{{SaveWarning}}");
                    }
                }
                else if (jamm_cmd == "catalog all" || jamm_cmd == "unlockfish")
                {
                    {{CatalogAllGml(hasCatalogData, logIfMissing: true)}}
                    {{catalogCommandWarning}}
                }
                else if (jamm_cmd == "money")
                {
                    ds_list_add(jamm_console_log, "Usage: money <n> or money +<n>");
                }
                else if (string_pos("money ", jamm_cmd) == 1)
                {
                    {{MoneyGml(data.Scripts.ByName("number_to_dollar_string") is not null)}}
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
        {{ClearInputKeys}}
        """;
    }

    private const string ClearInputKeys = """
        for (var jamm_k = 48; jamm_k <= 90; jamm_k += 1)
            keyboard_clear(jamm_k);
        keyboard_clear(vk_space);
        keyboard_clear(vk_shift);
        keyboard_clear(vk_control);
        keyboard_clear(vk_tab);
        keyboard_clear(vk_left);
        keyboard_clear(vk_right);
        keyboard_clear(vk_enter);
        """;

    private static string SlotSyncGml(bool hasSaveManager)
    {
        if (!hasSaveManager) return "";
        return """
            if (instance_exists(SaveManager))
            {
                if (jamm_console_slot_owner != SaveManager.id || jamm_console_slot != SaveManager.saveflag)
                {
                    jamm_console_unlocked_all = false;
                    jamm_console_slot_owner = SaveManager.id;
                    jamm_console_slot = SaveManager.saveflag;
                }
            }
            """;
    }

    private static string DigitCheckGml(string strVar, string okVar) => $$"""
        var {{okVar}} = string_length({{strVar}}) > 0;
        for (var jamm_d = 1; jamm_d <= string_length({{strVar}}); jamm_d += 1)
        {
            if (string_pos(string_char_at({{strVar}}, jamm_d), "0123456789") == 0)
            {
                {{okVar}} = false;
                break;
            }
        }
        """;

    private static string UnlockGearGml(bool hasAmbiance)
    {
        string clearLockDay = hasAmbiance ? "if (instance_exists(AmbianceController)) AmbianceController.lockDay = 0;" : "";
        return $$"""
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
                jamm_rod_total = min(RodData.nItems, array_length_1d(RodData.unlocked));
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
                jamm_lure_total = min(LureData.nItems, array_length_1d(LureData.unlocked));
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
                jamm_boat_total = min(BoatData.nItems, array_length_1d(BoatData.unlocked));
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
                jamm_tool_total = min(ToolData.nItems, array_length_1d(ToolData.unlocked));
                for (var jamm_i = 0; jamm_i < jamm_tool_total; jamm_i += 1)
                {
                    if (ToolData.unlocked[jamm_i] <= 0) jamm_tool_new += 1;
                    ToolData.unlocked[jamm_i] = 1;
                    if (jamm_i < array_length_1d(ToolData.item))
                    {
                        if (instance_exists(ToolData.item[jamm_i])) ToolData.item[jamm_i].active = 1;
                    }
                }
            }
            var jamm_hat_total = 0;
            var jamm_hat_new = 0;
            if (instance_exists(GearData))
            {
                jamm_hat_total = min(GearData.n_gear, array_length_1d(GearData.unlocked));
                for (var jamm_i = 0; jamm_i < jamm_hat_total; jamm_i += 1)
                {
                    if (GearData.unlocked[jamm_i] <= 0) jamm_hat_new += 1;
                    GearData.unlocked[jamm_i] = 1;
                }
            }
            jamm_icon_new = 0; // instance variable: read via other.jamm_icon_new inside with (Icons)
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
            ds_list_add(jamm_console_log, "Unlocked: " + string(jamm_tool_total) + " tools/gear (" + string(jamm_tool_new) + " new), " + string(jamm_hat_total) + " hats (" + string(jamm_hat_new) + " new), " + string(jamm_icon_new) + " HUD menus, nightfall=1.");
            """;
    }

    private static string SetLevelGml(bool hasExperienceBar, string targetExpr)
    {
        string readMax = hasExperienceBar
            ? "if (instance_exists(ExperienceBar)) jamm_max_lvl = ExperienceBar.max_level;"
            : "";
        string resetBar = hasExperienceBar
            ? "if (instance_exists(ExperienceBar)) ExperienceBar.experience = 0;"
            : "";
        return $$"""
            var jamm_max_lvl = 100;
            {{readMax}}
            var jamm_target_lvl = clamp({{targetExpr}}, 0, jamm_max_lvl);
            if (instance_exists(Girl))
            {
                Girl.level = jamm_target_lvl;
                Girl.experience = 0;
            }
            {{resetBar}}
            ds_list_add(jamm_console_log, "Level set to " + string(jamm_target_lvl) + " (max " + string(jamm_max_lvl) + ").");
            """;
    }

    private static string CatalogAllGml(bool hasCatalogData, bool logIfMissing)
    {
        if (!hasCatalogData)
            return logIfMissing
                ? "ds_list_add(jamm_console_log, \"Fish catalog not available in this game build.\");"
                : "";
        return """
            var jamm_cat_total = 0;
            var jamm_cat_new = 0;
            if (instance_exists(CatalogData))
            {
                var jamm_cat_rows = array_height_2d(CatalogData.object);
                for (var jamm_i = 0; jamm_i < jamm_cat_rows; jamm_i += 1)
                {
                    var jamm_cat_cols = array_length_2d(CatalogData.object, jamm_i);
                    for (var jamm_j = 0; jamm_j < jamm_cat_cols; jamm_j += 1)
                    {
                        var jamm_fish_obj = CatalogData.object[jamm_i, jamm_j];
                        if (is_real(jamm_fish_obj))
                        {
                            if (jamm_fish_obj >= 0 && object_exists(jamm_fish_obj))
                            {
                                jamm_cat_total += 1;
                                if (CatalogData.count[jamm_i, jamm_j] < 1 || CatalogData.life[jamm_i, jamm_j] < 3)
                                    jamm_cat_new += 1;
                                CatalogData.count[jamm_i, jamm_j] = max(CatalogData.count[jamm_i, jamm_j], 1);
                                CatalogData.life[jamm_i, jamm_j] = max(CatalogData.life[jamm_i, jamm_j], 3);
                                CatalogData.hide[jamm_i, jamm_j] = 0;
                            }
                        }
                    }
                }
                CatalogData.all_fish_revealed = 1;
                if (variable_instance_exists(CatalogData.id, "weight"))
                {
                    with (CatalogData) event_perform(ev_other, ev_user0);
                }
            }
            ds_list_add(jamm_console_log, "Catalog: " + string(jamm_cat_total) + " fish (" + string(jamm_cat_new) + " newly caught).");
            """;
    }

    private static string MoneyGml(bool hasFormatter) => $$"""
        var jamm_m_arg = string_delete(jamm_cmd, 1, 6);
        var jamm_m_add = false;
        if (string_length(jamm_m_arg) > 0)
        {
            if (string_char_at(jamm_m_arg, 1) == "+")
            {
                jamm_m_add = true;
                jamm_m_arg = string_delete(jamm_m_arg, 1, 1);
            }
        }
        {{DigitCheckGml("jamm_m_arg", "jamm_m_ok")}}
        if (!jamm_m_ok)
        {
            ds_list_add(jamm_console_log, "Invalid money amount. Usage: money <n> or money +<n>");
        }
        else
        {
            var jamm_m_val = real(jamm_m_arg);
            if (instance_exists(Girl))
            {
                if (jamm_m_add)
                    Girl.dollars = clamp(Girl.dollars + jamm_m_val, 0, 1000000 * 1000000000);
                else
                    Girl.dollars = clamp(jamm_m_val, 0, 1000000 * 1000000000);
                ds_list_add(jamm_console_log, "Money set to " + {{(hasFormatter ? "number_to_dollar_string(Girl.dollars)" : "\"$\" + string(Girl.dollars)")}} + ".");
                ds_list_add(jamm_console_log, "{{SaveWarning}}");
            }
            else
            {
                ds_list_add(jamm_console_log, "Girl instance not found; money unchanged.");
            }
        }
        """;

    private static string EndStep(UndertaleData data)
    {
        bool hasAmbiance = data.GameObjects.ByName("AmbianceController") is not null;
        bool hasSaveManager = data.GameObjects.ByName("SaveManager") is not null;
        return $$"""
        if (jamm_console_unlocked_all)
        {
            {{SlotSyncGml(hasSaveManager)}}
            if (jamm_console_unlocked_all)
            {
                if (instance_exists(LureData) && array_length_1d(LureData.unlocked) > 5)
                    LureData.unlocked[5] = 1;
                {{(hasAmbiance ? "if (instance_exists(AmbianceController)) AmbianceController.lockDay = 0;" : "")}}
            }
        }
        """;
    }

    private static string DrawGui(UndertaleData data)
    {
        string fontSetup = data.Fonts.ByName("fnt_babySmall") is not null
            ? "draw_set_font(fnt_babySmall);"
            : "draw_set_font(-1);";
        return $$"""
        jamm_console_open = global.jamm_console_open;
        if (!global.jamm_console_open)
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
