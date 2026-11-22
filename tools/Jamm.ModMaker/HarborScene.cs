using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Captures each Harbor Town building at five levels and exercises its donation rewards.</summary>
internal static class HarborScene
{
    private const string Probe = "obj_jamm_bench_harbor";
    private const string Report = "jamm_bench_harbor.csv";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return args[0] == "bench" && index >= 3 && args.ElementAtOrDefault(index + 1) == "harbor";
    }

    internal static void Apply(UndertaleData data)
    {
        foreach (string name in new[] { "jmk_harbor_town_object_harbor", "Girl" })
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"The harbor scene needs '{name}'. Build and merge the Harbor Town ModKit package first.");
        if (data.Scripts.ByName("jmk_harbor_town_script_command") is null
            || data.Scripts.ByName("jmk_harbor_town_script_title") is null)
            throw new InvalidDataException("The harbor scene needs Harbor Town's command and title scripts. Rebuild the package before running it.");

        // The game leaves NewWorld after the menu is removed; keep the probe alive so it
        // can exercise Harbor Town from the loaded fishing room.
        var probe = ModObjects.Create(data, Probe, persistent: true);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            harbor_started = -1;
            harbor_bootstrapped = false;
            harbor_diag_next = 0;
            harbor_last_capture = 0;
            harbor_capture_index = 0;
            harbor_capture_pending = false;
            harbor_reward_phase = 0;
            harbor_reward_next = 0;
            harbor_balance_phase = 0;
            harbor_labels[0] = "lighthouse";
            harbor_labels[1] = "aquarium";
            harbor_labels[2] = "statue";
            harbor_labels[3] = "research";
            harbor_levels[0] = 0;
            harbor_levels[1] = 1;
            harbor_levels[2] = 5;
            harbor_levels[3] = 20;
            harbor_levels[4] = 100;
            var output = file_text_open_write("jamm_bench_harbor.csv");
            file_text_write_string(output, "building,level,command_result,screenshot");
            file_text_writeln(output);
            file_text_close(output);
            var diag = file_text_open_write("jamm_bench_harbor_state.csv");
            file_text_write_string(diag, "ms,room,menu,girl,save,harbor");
            file_text_writeln(diag);
            file_text_close(diag);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), """
            if (current_time >= harbor_diag_next)
            {
                var diag = file_text_open_append("jamm_bench_harbor_state.csv");
                file_text_write_string(diag, string(current_time) + "," + string(room) + ","
                    + string(instance_exists(GameModeMenu)) + "," + string(instance_exists(Girl)) + ","
                    + string(instance_exists(SaveManager)) + "," + string(instance_exists(jmk_harbor_town_object_harbor)));
                file_text_writeln(diag);
                file_text_close(diag);
                harbor_diag_next = current_time + 2000;
            }
            if (instance_exists(GameModeMenu))
            {
                with (GameModeMenu) instance_destroy();
                exit;
            }
            if (!instance_exists(Girl)) exit;
            var harbor_instance = instance_find(jmk_harbor_town_object_harbor, 0);
            if (!instance_exists(harbor_instance))
                harbor_instance = instance_create(Girl.x - 53, Girl.y, jmk_harbor_town_object_harbor);
            if (!harbor_bootstrapped)
            {
                with (harbor_instance)
                {
                    harbor_ready = true;
                    harbor_menu = false;
                    harbor_donated = 0;
                    harbor_fish_refresh = 0;
                    harbor_fish_count = 0;
                    for (var i = 0; i < 4; i += 1) harbor_level[i] = 0;
                }
                var harbor_x = harbor_instance.x + 53;
                var harbor_y = harbor_instance.y;
                Girl.x = harbor_x;
                Girl.y = harbor_y;
                if (instance_exists(Camera))
                {
                    Camera.x = harbor_x;
                    Camera.y = harbor_y;
                }
                harbor_bootstrapped = true;
            }
            if (harbor_reward_phase < 5 && current_time >= harbor_reward_next)
            {
                if (harbor_reward_phase == 0) Girl.dollars = 1000000000000000;
                if (harbor_reward_phase == 1)
                {
                    harbor_instance.harbor_donated = 1000000;
                    Girl.dollars -= 1000000;
                }
                if (harbor_reward_phase == 2)
                {
                    harbor_instance.harbor_donated = 1000000000;
                    Girl.dollars -= 999000000;
                }
                if (harbor_reward_phase == 3)
                {
                    harbor_instance.harbor_donated = 1000000000000;
                    Girl.dollars -= 999000000000;
                }
                if (harbor_reward_phase == 4)
                {
                    harbor_instance.harbor_donated = 1000000000000000;
                    Girl.dollars -= 999000000000000;
                }
                harbor_reward_phase += 1;
                harbor_reward_next = current_time + 1000;
            }
            if (harbor_reward_phase < 5) exit;
            if (harbor_started < 0) harbor_started = current_time;
            if (harbor_capture_index >= 20)
            {
                if (harbor_balance_phase == 0 && current_time >= harbor_reward_next)
                {
                    harbor_instance.harbor_level[3] = 200;
                    Girl.dollars = 1000000000;
                    var research_fn = asset_get_index("jmk_harbor_town_script_research");
                    var result = "sale=" + string_format(100 * script_execute(research_fn, 200, 0), 2, 1)
                        + "% bite=" + string_format(100 * script_execute(research_fn, 200, 1), 2, 1)
                        + "% luck=" + string_format(100 * script_execute(research_fn, 200, 2), 2, 1);
                    var output = file_text_open_append("jamm_bench_harbor.csv");
                    file_text_write_string(output, "balance,1000000000," + string(Girl.dollars) + "," + result);
                    file_text_writeln(output);
                    file_text_close(output);
                    harbor_balance_phase = 1;
                    harbor_reward_next = current_time + 1200;
                }
                else if (harbor_balance_phase == 1 && current_time >= harbor_reward_next)
                {
                    Girl.dollars = 1000000000000;
                    var result = "Wallet=" + string(Girl.dollars) + "; research level 200 remains capped";
                    var output = file_text_open_append("jamm_bench_harbor.csv");
                    file_text_write_string(output, "balance,1000000000000," + string(Girl.dollars) + "," + result);
                    file_text_writeln(output);
                    file_text_close(output);
                    harbor_balance_phase = 2;
                    harbor_reward_next = current_time + 1200;
                }
                else if (harbor_balance_phase == 2 && current_time >= harbor_reward_next)
                {
                    game_end();
                }
                exit;
            }
            if (current_time - harbor_last_capture < 2600 || harbor_capture_pending) exit;

            var building_index = floor(harbor_capture_index / 5);
            var level_index = harbor_capture_index mod 5;
            var building = harbor_labels[building_index];
            var level = harbor_levels[level_index];
            harbor_instance.harbor_level[building_index] = level;
            harbor_instance.harbor_fish_refresh = 0;
            var result = building + " level " + string(level);
            if (building == "research")
            {
                var research_fn = asset_get_index("jmk_harbor_town_script_research");
                result += "; sale=" + string_format(100 * script_execute(research_fn, level, 0), 2, 1)
                    + "% bite=" + string_format(100 * script_execute(research_fn, level, 1), 2, 1)
                    + "% luck=" + string_format(100 * script_execute(research_fn, level, 2), 2, 1);
            }
            result += "; title=" + script_execute(asset_get_index("jmk_harbor_town_script_title"),
                instance_find(jmk_harbor_town_object_harbor, 0).harbor_donated);
            harbor_capture_building = building;
            harbor_capture_level = level;
            harbor_capture_result = result;
            harbor_capture_filename = "harbor_art_" + building + "_" + string(level) + ".png";
            harbor_capture_pending = true;
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), """
            var harbor_instance = instance_find(jmk_harbor_town_object_harbor, 0);
            if (instance_exists(harbor_instance))
            {
                harbor_instance.harbor_near = true;
                harbor_instance.harbor_menu = true;
            }
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), """
            if (!harbor_capture_pending) exit;
            screen_save(harbor_capture_filename);
            var output = file_text_open_append("jamm_bench_harbor.csv");
            file_text_write_string(output, harbor_capture_building + "," + string(harbor_capture_level) + ","
                + harbor_capture_result + "," + harbor_capture_filename);
            file_text_writeln(output);
            file_text_close(output);
            harbor_capture_pending = false;
            harbor_capture_index += 1;
            harbor_last_capture = current_time;
            """);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);
        ModObjects.Compile(compiler);
    }
}
