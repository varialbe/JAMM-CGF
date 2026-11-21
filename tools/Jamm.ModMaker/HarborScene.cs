using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Captures each Harbor Town building at levels 0, 1, 5, 20, and 100 in the live game.</summary>
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
        if (data.Scripts.ByName("jmk_harbor_town_script_command") is null)
            throw new InvalidDataException("The harbor scene needs Harbor Town's command script. Rebuild the package before running it.");

        var probe = ModObjects.Create(data, Probe, persistent: false);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            harbor_started = -1;
            harbor_last_capture = 0;
            harbor_capture_index = 0;
            harbor_capture_pending = false;
            harbor_command = asset_get_index("jmk_harbor_town_script_command");
            harbor_labels[0] = "lighthouse";
            harbor_labels[1] = "aquarium";
            harbor_labels[2] = "statue";
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
            if (harbor_started < 0) harbor_started = current_time;
            if (current_time - harbor_last_capture < 2600 || harbor_capture_pending) exit;

            var building_index = floor(harbor_capture_index / 5);
            var level_index = harbor_capture_index mod 5;
            var building = harbor_labels[building_index];
            var level = harbor_levels[level_index];
            var result = script_execute(harbor_command, building + " " + string(level));
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
            if (harbor_capture_index >= 15) game_end();
            """);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);
        ModObjects.Compile(compiler);
    }
}
