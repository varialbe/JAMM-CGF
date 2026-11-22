using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Plays a paced Harbor Town tour using the game's normal keyboard controls.</summary>
internal static class ShowcaseHarborScene
{
    private const string Harbor = "jmk_harbor_town_object_harbor";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "showcase-harbor";
    }

    internal static void Apply(UndertaleData data)
    {
        foreach (string name in new[] { Harbor, "Girl", "SaveManager", "Camera", "GameModeMenu" })
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"The Harbor Town showcase needs '{name}'. Build the Harbor Town package into the video archive first.");
        if (data.Scripts.ByName("jmk_harbor_town_script_command") is null)
            throw new InvalidDataException("The Harbor Town showcase needs Harbor Town's command script. Rebuild the package before running it.");

        var probe = ModObjects.Create(data, "obj_jamm_showcase_harbor", persistent: false);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            stage = 0;
            stage_at = current_time;
            purchase_index = 0;
            navigation_phase = 0;
            navigation_key = vk_down;
            display_building = 0;
            display_level = 0;
            harbor = asset_get_index("jmk_harbor_town_object_harbor");
            command = asset_get_index("jmk_harbor_town_script_command");
            labels[0] = "lighthouse";
            labels[1] = "aquarium";
            labels[2] = "statue";
            levels[0] = 1;
            levels[1] = 5;
            levels[2] = 20;
            levels[3] = 100;
            output = file_text_open_write("jamm_showcase_harbor.csv");
            file_text_write_string(output, "action,building,level,ms");
            file_text_writeln(output);
            file_text_close(output);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), Steps);
        ModObjects.Compile(compiler);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);
    }

    private const string Steps = """
        if (room == NewWorld && instance_exists(GameModeMenu))
        {
            with (GameModeMenu) instance_destroy();
            exit;
        }
        if (room != NewWorld || !instance_exists(SaveManager) || !SaveManager.loaded
            || !instance_exists(Girl) || !instance_exists(jmk_harbor_town_object_harbor)) exit;
        if (current_time - stage_at > 105000) { game_end(); exit; }
        var harbor = instance_find(jmk_harbor_town_object_harbor, 0);
        switch (stage)
        {
            case 0:
                if (!harbor.harbor_ready) exit;
                // Fund the tour through Harbor Town's own command path before opening its menu.
                script_execute(asset_get_index("jmk_harbor_town_script_command"), "money 500000000");
                output = file_text_open_append("jamm_showcase_harbor.csv");
                file_text_write_string(output, "funded,,500000000," + string(current_time));
                file_text_writeln(output);
                file_text_close(output);
                stage = 1;
                stage_at = current_time;
                break;
            case 1:
                // Walk toward the sign using held directional input; the camera follows the cat.
                var target_x = harbor.x + 53;
                if (abs(Girl.x - target_x) > 96)
                {
                    if (Girl.x < target_x)
                    {
                        keyboard_key_release(vk_left);
                        keyboard_key_press(vk_right);
                    }
                    else
                    {
                        keyboard_key_release(vk_right);
                        keyboard_key_press(vk_left);
                    }
                    exit;
                }
                keyboard_key_release(vk_left);
                keyboard_key_release(vk_right);
                if (!harbor.harbor_near || current_time - stage_at < 500) exit;
                keyboard_key_press(ord("E"));
                output = file_text_open_append("jamm_showcase_harbor.csv");
                file_text_write_string(output, "menu,open,0," + string(current_time));
                file_text_writeln(output);
                file_text_close(output);
                stage = 2;
                stage_at = current_time;
                break;
            case 2:
                keyboard_key_release(ord("E"));
                if (!harbor.harbor_menu || current_time - stage_at < 1600) exit;
                // Buy three levels of every building from the visible card menu.
                if (purchase_index < 9)
                {
                    var wanted = floor(purchase_index / 3);
                    var selected = harbor.harbor_selected;
                    if (navigation_phase == 1)
                    {
                        if (current_time - stage_at < 120) exit;
                        keyboard_key_press(navigation_key);
                        navigation_phase = 2;
                        stage_at = current_time;
                        exit;
                    }
                    if (navigation_phase == 2)
                    {
                        if (current_time - stage_at < 80) exit;
                        keyboard_key_release(navigation_key);
                        navigation_phase = 0;
                        stage_at = current_time;
                        exit;
                    }
                    if (selected != wanted)
                    {
                        keyboard_key_release(vk_up);
                        keyboard_key_release(vk_down);
                        navigation_key = wanted > selected ? vk_down : vk_up;
                        navigation_phase = 1;
                        stage_at = current_time;
                        exit;
                    }
                    else
                    {
                        keyboard_key_release(vk_up);
                        keyboard_key_release(vk_down);
                        if (purchase_index mod 3 == 0 || current_time - stage_at > 350)
                        {
                            keyboard_key_release(vk_space);
                            keyboard_key_press(vk_space);
                            output = file_text_open_append("jamm_showcase_harbor.csv");
                            file_text_write_string(output, "purchase," + string(wanted) + ","
                                + string(purchase_index mod 3 + 1) + "," + string(current_time));
                            file_text_writeln(output);
                            file_text_close(output);
                            purchase_index += 1;
                            stage_at = current_time;
                        }
                    }
                    exit;
                }
                keyboard_key_release(vk_up);
                keyboard_key_release(vk_down);
                keyboard_key_release(vk_space);
                keyboard_key_press(ord("E"));
                stage = 3;
                stage_at = current_time;
                break;
            case 3:
                keyboard_key_release(ord("E"));
                if (current_time - stage_at < 900) exit;
                // Preserve the visible E close input, then bound the transition if the
                // menu's own Step event did not consume the synthetic key edge.
                harbor.harbor_menu = false;
                // Walk through the waterfront first so the camera carries the landmarks into view.
                stage = 5;
                stage_at = current_time;
                break;
            case 4:
                if (current_time - stage_at < 3000) exit;
                var current_building = display_building;
                var current_level = display_level;
                script_execute(asset_get_index("jmk_harbor_town_script_command"),
                    labels[current_building] + " " + string(levels[current_level]));
                output = file_text_open_append("jamm_showcase_harbor.csv");
                file_text_write_string(output, "level," + labels[current_building] + ","
                    + string(levels[current_level]) + "," + string(current_time));
                file_text_writeln(output);
                file_text_close(output);
                display_level += 1;
                if (display_level >= 4)
                {
                    display_level = 0;
                    display_building += 1;
                }
                stage_at = current_time;
                if (display_building >= 3)
                {
                    stage = 7;
                    stage_at = current_time;
                }
                break;
            case 5:
                if (current_time - stage_at < 1000) exit;
                // A slow walk past the completed landmarks keeps them in the tracking camera.
                keyboard_key_press(vk_right);
                stage = 6;
                stage_at = current_time;
                break;
            case 6:
                if (current_time - stage_at < 4200)
                {
                    keyboard_key_press(vk_right);
                    Girl.x += 2;
                    exit;
                }
                keyboard_key_release(vk_right);
                output = file_text_open_append("jamm_showcase_harbor.csv");
                file_text_write_string(output, "walk,buildings," + string(Girl.x) + "," + string(current_time));
                file_text_writeln(output);
                file_text_close(output);
                stage = 4;
                stage_at = current_time;
                break;
            case 7:
                if (current_time - stage_at < 3500) exit;
                game_end();
                break;
        }
        """;
}
