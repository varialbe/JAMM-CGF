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
        foreach (string name in new[] { "jmk_harbor_town_object_harbor", "Girl", "SaveManager", "Camera" })
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"The harbor scene needs '{name}'. Build and merge the Harbor Town ModKit package first.");
        if (data.Scripts.ByName("jmk_harbor_town_script_command") is null
            || data.Scripts.ByName("jmk_harbor_town_script_title") is null)
            throw new InvalidDataException("The harbor scene needs Harbor Town's command and title scripts. Rebuild the package before running it.");

        var probe = ModObjects.Create(data, Probe, persistent: false);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            harbor_started = -1;
            harbor_last_capture = 0;
            harbor_capture_index = 0;
            harbor_capture_pending = false;
            harbor_reward_phase = 0;
            harbor_reward_next = 0;
            harbor_balance_phase = 0;
            harbor_command = asset_get_index("jmk_harbor_town_script_command");
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
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), """
            if (room == NewWorld && instance_exists(GameModeMenu))
            {
                with (GameModeMenu) instance_destroy();
                exit;
            }
            if (room != NewWorld || !instance_exists(SaveManager) || !SaveManager.loaded
                || !instance_exists(Girl) || !instance_exists(jmk_harbor_town_object_harbor)) exit;
            if (harbor_reward_phase < 5 && current_time >= harbor_reward_next && !SaveManager.saving_locked)
            {
                if (harbor_reward_phase == 0) script_execute(harbor_command, "money 1000000000000000");
                if (harbor_reward_phase == 1) script_execute(harbor_command, "donate 1000000");
                if (harbor_reward_phase == 2) script_execute(harbor_command, "donate 999000000");
                if (harbor_reward_phase == 3) script_execute(harbor_command, "donate 999000000000");
                if (harbor_reward_phase == 4) script_execute(harbor_command, "donate 999000000000000");
                harbor_reward_phase += 1;
                harbor_reward_next = current_time + 1000;
            }
            if (harbor_reward_phase < 5) exit;
            if (harbor_started < 0) harbor_started = current_time;
            if (harbor_capture_index >= 20)
            {
                if (harbor_balance_phase == 0 && current_time >= harbor_reward_next && !SaveManager.saving_locked)
                {
                    var result = script_execute(harbor_command, "research 200");
                    var research_fn = asset_get_index("jmk_harbor_town_script_research");
                    result += "; sale=" + string_format(100 * script_execute(research_fn, 200, 0), 2, 1)
                        + "% bite=" + string_format(100 * script_execute(research_fn, 200, 1), 2, 1)
                        + "% luck=" + string_format(100 * script_execute(research_fn, 200, 2), 2, 1);
                    var output = file_text_open_append("jamm_bench_harbor.csv");
                    file_text_write_string(output, "balance,research-200," + string(Girl.dollars) + "," + result);
                    file_text_writeln(output);
                    file_text_close(output);
                    harbor_balance_phase = 1;
                    harbor_reward_next = current_time + 1200;
                }
                else if (harbor_balance_phase == 1 && current_time >= harbor_reward_next && !SaveManager.saving_locked)
                {
                    script_execute(harbor_command, "money 1000000000");
                    var result = "Wallet=" + string(Girl.dollars) + "; Research level 200 remains capped";
                    var output = file_text_open_append("jamm_bench_harbor.csv");
                    file_text_write_string(output, "balance,1000000000," + string(Girl.dollars) + "," + result);
                    file_text_writeln(output);
                    file_text_close(output);
                    harbor_balance_phase = 2;
                    harbor_reward_next = current_time + 1200;
                }
                else if (harbor_balance_phase == 2 && current_time >= harbor_reward_next && !SaveManager.saving_locked)
                {
                    script_execute(harbor_command, "money 999000000000");
                    var result = "Wallet=" + string(Girl.dollars) + "; Research level 200 remains capped";
                    var output = file_text_open_append("jamm_bench_harbor.csv");
                    file_text_write_string(output, "balance,1000000000000," + string(Girl.dollars) + "," + result);
                    file_text_writeln(output);
                    file_text_close(output);
                    harbor_balance_phase = 3;
                    harbor_reward_next = current_time + 1200;
                }
                else if (harbor_balance_phase == 3 && current_time >= harbor_reward_next && !SaveManager.saving_locked)
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
            var result = script_execute(harbor_command, building + " " + string(level));
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
