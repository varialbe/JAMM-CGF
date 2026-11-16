using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Opt-in scheduling for structurally standard, distant paused fish.</summary>
public static class PerfMode
{
    private const string Controller = "obj_jamm_perf";

    // Offline historical census only; runtime eligibility below does not use this list.
    // WHY: the original audit excluded unknown/custom species.
    // Custom Step/user logic can own partners, lights, quests or timers even while zoned.
    private static string[] Eligible(UndertaleData data)
    {
        var fish = data.GameObjects.ByName("Fish")!;
        return data.GameObjects.Where(obj =>
        {
            bool standard = false;
            for (var o = obj; o is not null && o != fish; o = o.ParentId)
            {
                if (new[] { "Dragon", "Noctis", "Torby", "Ghost", "Abyssal", "Kingfish", "Shaku", "Bagu", "Curby" }.Contains(o.Name.Content, StringComparer.Ordinal)) return false;
                if (o.Name.Content is "FishSmall" or "FishMedium") standard = true;
                if (o.Events[(int)EventType.Step].Count > 0) return false;
                if (o.Events[(int)EventType.Other].Any(e => e.EventSubtype == 22)) return false;
                // Only the base medium flee check is accepted in the ordinary swimming state.
                if (o.Name.Content != "FishMedium" && o.Events[(int)EventType.Other].Any(e => e.EventSubtype is not (11 or 12 or 13 or 17 or 21))) return false;
            }
            return standard;
        }).Select(o => o.Name.Content).ToArray();
    }

    /// <summary>Reports placement candidates, without pretending room data is runtime state.</summary>
    public static void Census(UndertaleData data)
    {
        var allowed = Eligible(data).ToHashSet(StringComparer.Ordinal);
        Console.WriteLine("Historical static audit (not runtime eligibility): " + string.Join(", ", allowed));
        var room = data.Rooms.ByName("NewWorld")!;
        double right = room.Views[3].ViewX + Math.Max(room.Views[3].ViewWidth, room.Views[3].ViewHeight) + 3000;
        var placements = room.GameObjects.Where(i => allowed.Contains(i.ObjectDefinition.Name.Content)).ToArray();
        Console.WriteLine($"Total: {placements.Length} placements; {placements.Count(i => i.X > right)} beyond initial view-3 right+3000 ({right}). Runtime eligibility cannot be read from room placements.");
        foreach (var g in room.GameObjects.Where(i => allowed.Contains(i.ObjectDefinition.Name.Content)).GroupBy(i => i.ObjectDefinition.Name.Content))
            Console.WriteLine($"{g.Key}: {g.Count()} placed, {g.Count(i => i.X > right)} beyond initial view-3 right+3000 (upper bound; runtime pause/state required)");
        foreach (var v in room.Views.Select((v, i) => (v, i)))
            Console.WriteLine($"view {v.i}: enabled={v.v.Enabled} x={v.v.ViewX} y={v.v.ViewY} width={v.v.ViewWidth} height={v.v.ViewHeight}");
    }

    /// <summary>Builds the narrow End Step prefix; Mode 0 falls through to phase 6a.</summary>
    public static string Prefix(UndertaleData data) => """
        if (global.jamm_perf_mode)
        {
            // WHY: cache runtime ancestry, so newly merged species need no original allowlist.
            if (!ds_map_exists(global.jamm_perf_types, object_index))
            {
                var jamm_type = object_index;
                var jamm_standard = false;
                var jamm_special = false;
                while (jamm_type != -1)
                {
                    if (jamm_type == FishSmall || jamm_type == FishMedium) jamm_standard = true;
                    if (jamm_type == FishLarge || jamm_type == FishHuge) jamm_special = true;
                    var jamm_name = object_get_name(jamm_type);
                    if (jamm_name == "Dragon" || jamm_name == "Noctis" || jamm_name == "Torby" || jamm_name == "Ghost" || jamm_name == "Abyssal" || jamm_name == "Kingfish" || jamm_name == "Shaku" || jamm_name == "Bagu" || jamm_name == "Curby") jamm_special = true;
                    jamm_type = object_get_parent(jamm_type);
                }
                ds_map_add(global.jamm_perf_types, object_index, jamm_standard && !jamm_special);
            }
            // WHY: GM1 has no event-override reflection. A per-instance sentinel requires
            // one full Fish End Step before skipping; instance changes force a fresh run.
            // An override calling event_inherited can still reach this body: see report.
            var jamm_ready = false;
            if (variable_instance_exists(id, "jamm_perf_seen_type")) jamm_ready = jamm_perf_seen_type == object_index;
            jamm_perf_seen_type = object_index;
            var jamm_perf_allowed = ds_map_find_value(global.jamm_perf_types, object_index) && jamm_ready;
            if (jamm_perf_allowed && !always_zoned && !netherFish && !deactivated && !hollow && life > 0 && !isDragging && !onBoat && !thrall_override)
            {
                if (state == 12 || (state == 0 && !isVisible && lungeCooldown <= 0))
                {
                    var jamm_near = false;
                    // WHY: gear can enlarge lunge ranges; a fixed margin alone is not safe.
                    var jamm_range = abs(base_lunge_offset) + abs(Lure.lunge_scale) * max(abs(LUNGE_RANGE_START), abs(LUNGE_RANGE_STOP)) * max(1, abs(base_lunge_mod)) + 500;
                    // WHY: check every active instance, not just an arbitrary first Boat/Net.
                    with (Lure) if (point_distance(x, y, other.x, other.y) < max(3000, jamm_range)) jamm_near = true;
                    with (CatSwim) if (point_distance(x, y, other.x, other.y) < max(6000, jamm_range)) jamm_near = true;
                    with (Boat) if (point_distance(x, y, other.x, other.y) < 3000) jamm_near = true;
                    with (Girl) if (abs(x - other.x) < 1000) jamm_near = true;
                    with (Net) if (point_distance(x, y, other.x, other.y) < 6000) jamm_near = true;
                    // WHY: the visibility script uses view 3 even when that view is disabled.
                    if (x >= view_xview[3] - 3000 && x <= view_xview[3] + max(view_wview[3], view_hview[3]) + 3000) jamm_near = true;
                    for (var jamm_v = 0; jamm_v < 8; jamm_v++)
                    {
                        if (view_visible[jamm_v])
                            if (x >= view_xview[jamm_v] - 3000 && x <= view_xview[jamm_v] + view_wview[jamm_v] + 3000 && y >= view_yview[jamm_v] - 3000 && y <= view_yview[jamm_v] + view_hview[jamm_v] + 3000) jamm_near = true;
                    }
                    if (!jamm_near)
                    {
                        global.jamm_perf_eligible += 1;
                        if ((global.jamm_perf_tick + id) mod global.jamm_perf_interval != 0)
                        {
                            // WHY: engine alarms, motion and Draw still run. Do not replay them.
                            // Preserve the zoned positional clamp and the End Step tick counter.
                            if (state == 12) { x = xprevious; y = yprevious; }
                            // WHY: standard user0 restarts path thrust even after user12 pauses
                            // speed. Mode 1 intentionally suppresses this unseen movement.
                            else { life = MAX_LIFE; speed = 0; spd = 0; if (path_index >= 0) path_speed = 0; }
                            frames_since_last_flip++;
                            global.jamm_perf_skipped += 1;
                            exit;
                        }
                    }
                }
            }
        }
        """ + "\n";

    /// <summary>Adds a persistent switch, toast and optional per-second scheduling counters.</summary>
    public static void Queue(UndertaleData data, CompileGroup compiler, bool debug)
    {
        var obj = ModObjects.Create(data, Controller, true);
        void Add(EventType type, uint subtype, string text) => compiler.QueueCodeReplace(obj.EventHandlerFor(type, subtype, data), text);
        Add(EventType.Create, 0, $$"""
            if (instance_number(object_index) > 1) { instance_destroy(); exit; }
            ini_open("jamm_perf.ini");
            global.jamm_perf_mode = ini_read_real("Perf", "Mode", 0) != 0;
            global.jamm_perf_interval = clamp(round(ini_read_real("Perf", "Interval", 4)), 1, 60);
            ini_write_real("Perf", "Mode", global.jamm_perf_mode);
            ini_write_real("Perf", "Interval", global.jamm_perf_interval);
            ini_close();
            global.jamm_perf_types = ds_map_create();
            jamm_perf_map_owner = true;
            global.jamm_perf_tick = 0;
            global.jamm_perf_eligible = 0;
            global.jamm_perf_skipped = 0;
            jamm_perf_toast = current_time + 4000;
            jamm_perf_log = current_time + 1000;
            jamm_perf_count_log = current_time + 1000;
            {{(debug ? "jamm_perf_ms = 0; jamm_perf_max = 0; jamm_perf_n = 0; jamm_perf_sample = -1; jamm_perf_begin = get_timer();" : "")}}
            """);
        Add(EventType.Destroy, 0, """
            if (variable_instance_exists(id, "jamm_perf_map_owner"))
                if (jamm_perf_map_owner) ds_map_destroy(global.jamm_perf_types);
            """);
        Add(EventType.Step, (uint)EventSubtypeStep.BeginStep, $$"""
            {{(debug ? "jamm_perf_begin = get_timer();" : "")}}
            global.jamm_perf_tick += 1;
            if (keyboard_check_pressed(vk_f8))
            {
                global.jamm_perf_mode = !global.jamm_perf_mode;
                ini_open("jamm_perf.ini");
                ini_write_real("Perf", "Mode", global.jamm_perf_mode);
                ini_close();
                jamm_perf_toast = current_time + 2500;
                {{(debug ? "global.jamm_perf_eligible = 0; global.jamm_perf_skipped = 0; jamm_perf_ms = 0; jamm_perf_max = 0; jamm_perf_n = 0; jamm_perf_log = current_time + 1000; jamm_perf_count_log = current_time + 1000;" : "")}}
            }
            {{(debug ? "if (current_time >= jamm_perf_count_log) { jamm_perf_count_log = current_time + 1000; " + DebugLog("\"Perf mode=\" + string(global.jamm_perf_mode) + \" interval=\" + string(global.jamm_perf_interval) + \" eligible_events=\" + string(global.jamm_perf_eligible) + \" skipped_events=\" + string(global.jamm_perf_skipped)") + " global.jamm_perf_eligible = 0; global.jamm_perf_skipped = 0; }" : "")}}
            """);
        if (debug)
            Add(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, $$"""
                if (jamm_perf_sample != global.jamm_perf_tick)
                {
                    jamm_perf_sample = global.jamm_perf_tick;
                    var jamm_ms = (get_timer() - jamm_perf_begin) / 1000;
                    jamm_perf_ms += jamm_ms;
                    jamm_perf_max = max(jamm_perf_max, jamm_ms);
                    jamm_perf_n += 1;
                    if (current_time >= jamm_perf_log)
                    {
                        {{DebugLog("\"Perf mode=\" + string(global.jamm_perf_mode) + \" tick_ms_avg=\" + string(jamm_perf_ms / max(1, jamm_perf_n)) + \" tick_ms_max=\" + string(jamm_perf_max)")}}
                        jamm_perf_ms = 0; jamm_perf_max = 0; jamm_perf_n = 0; jamm_perf_log = current_time + 1000;
                    }
                }
                """);
        string fontSetup = data.Fonts.ByName("fnt_babySmall") is not null ? "draw_set_font(fnt_babySmall);" : "draw_set_font(-1);";
        Add(EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, $$"""
            if (current_time < jamm_perf_toast)
            {
                var jamm_perf_text = "Perf mode (experimental): " + string(global.jamm_perf_mode) + " (F8: toggle)";
                {{fontSetup}}
                draw_set_halign(fa_left);
                draw_set_valign(fa_top);
                draw_set_alpha(min(1, (jamm_perf_toast - current_time) / 500));
                draw_set_color(c_black);
                draw_rectangle(8, 80, 20 + string_width(jamm_perf_text), 92 + string_height(jamm_perf_text), false);
                draw_set_color(c_white);
                draw_text(14, 86, jamm_perf_text);
                draw_set_alpha(1);
            }
            """);
        foreach (string room in new[] { "NewWorld", "room11" }) ModObjects.PlaceInstance(data, room, obj, true);
    }

    // Relative text-file writes use the runner's writable save sandbox, like Smooth's log.
    private static string DebugLog(string expression) => $$"""
        var jamm_message = {{expression}};
        show_debug_message(jamm_message);
        var jamm_file = file_text_open_append("jamm_perf_log.txt");
        if (jamm_file >= 0)
        {
            file_text_write_string(jamm_file, jamm_message);
            file_text_writeln(jamm_file);
            file_text_close(jamm_file);
        }
        """;

}
