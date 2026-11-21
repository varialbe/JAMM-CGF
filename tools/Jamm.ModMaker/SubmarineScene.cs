using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class SubmarineScene
{
    private const string Sub = "jmk_submarine_submarine";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "submarine";
    }

    internal static void Apply(UndertaleData data)
    {
        if (data.GameObjects.ByName(Sub) is null || data.GameObjects.ByName(ConsoleMod.ObjectName) is null
            || data.Scripts.ByName("jamm_item_find") is null)
            throw new InvalidDataException("The submarine scene needs Submarine and JAMM Console in a merged profile.");

        var probe = ModObjects.Create(data, "obj_jamm_bench_submarine", persistent: false);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            stage = 0;
            stage_at = current_time;
            capture = "";
            slot = -1;
            launch_x = -1;
            depths[0] = 450;
            depths[1] = 1800;
            depths[2] = 4000;
            depth_index = 0;
            file = file_text_open_write("jamm_bench_submarine.csv");
            file_text_write_string(file, "stage,ms,boat_x,boat_y,depth,oxygen,lamp,lure_state,rod_state,lure_x,lure_y,tether_x,tether_y,port_x,port_y,drop,camera_x,camera_y,cash,owned,deep");
            file_text_writeln(file);
            file_text_close(file);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), Steps);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), Capture);
        ModObjects.Compile(compiler);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: true);
    }

    private const string Steps = $$"""
        if (!instance_exists(SaveManager) || !SaveManager.loaded || !instance_exists(Girl)) exit;
        if (current_time - stage_at > 30000)
        {
            capture = "timeout_" + string(stage);
            stage = 99;
            exit;
        }
        switch (stage)
        {
            case 0:
                if (current_time - stage_at < 2500) exit;
                slot = jamm_item_find("boat", "submarine:submarine");
                if (slot < 0) exit;
                // Start from a clean shop state even when the runner uses a seeded save.
                with (Handler) instance_destroy();
                with (Message) instance_destroy();
                with (Boat) instance_destroy();
                with (Net) instance_destroy();
                with (Lure)
                {
                    if (state != 0) line_snap();
                    line_reset();
                }
                Girl.dollars = 0;
                Girl.startMessageShown = 1;
                Girl.boatMessageShown = 2;
                Girl.nightfallUnlocked = 1;
                Girl.level = max(Girl.level, 30);
                BoatData.unlocked[slot] = 0;
                keyboard_key_press(vk_f12);
                break;
            case 1:
                if (!{{ConsoleMod.ObjectName}}.jamm_console_open) exit;
                keyboard_key_release(vk_f12);
                keyboard_string = "money 150000000";
                keyboard_key_press(vk_enter);
                break;
            case 2:
                if (Girl.dollars != 150000000) exit;
                keyboard_key_release(vk_enter);
                capture = "console";
                break;
            case 3:
                if (current_time - stage_at < 600) exit;
                keyboard_key_press(vk_f12);
                break;
            case 4:
                if ({{ConsoleMod.ObjectName}}.jamm_console_open) exit;
                keyboard_key_release(vk_f12);
                with (icon_Boats) event_user(0);
                break;
            case 5:
                if (!instance_exists(BoatMenu) || !BoatMenu.done_spawning) exit;
                var card = noone;
                with (BoatCard)
                    if (item_id == other.slot) card = id;
                if (card == noone)
                {
                    if (current_time - stage_at < 400) exit;
                    keyboard_key_release(vk_down);
                    keyboard_key_press(vk_down);
                    exit;
                }
                keyboard_key_release(vk_down);
                shop_card = card;
                capture = "shop";
                break;
            case 6:
                if (current_time - stage_at < 900) exit;
                with (shop_card) event_user(0);
                break;
            case 7:
                if (!instance_exists({{Sub}}) || BoatData.unlocked[slot] != 1 || Girl.dollars != 25000000) exit;
                capture = "purchased";
                break;
            case 8:
                // Select a clear vertical lane, including room for the line to deploy from the bow.
                for (var px = 12500; px < 41000 && launch_x < 0; px += 350)
                    if (collision_rectangle(px - 115, 1310, px + 220, 5500, Collidable, true, true) == noone)
                        launch_x = px;
                if (launch_x < 0)
                {
                    capture = "no_clear_water";
                    stage = 99;
                    exit;
                }
                Girl.x = launch_x;
                Girl.hspeed = 0;
                Girl.hspeed_base = 0;
                Camera.x = launch_x + Camera.RELATIVE_ANCHOR_X;
                Camera.y = 1294 + Camera.RELATIVE_ANCHOR_Y_FROM_SEA;
                capture = "at_sea";
                break;
            case 9:
                if (current_time - stage_at < 600) exit;
                keyboard_key_press(ord("V"));
                break;
            case 10:
                if (!{{Sub}}.sub_diving) exit;
                keyboard_key_release(ord("V"));
                keyboard_key_press(vk_down);
                break;
            case 11:
                if ({{Sub}}.sub_y - 1294 < depths[depth_index]) exit;
                keyboard_key_release(vk_down);
                capture = "depth_" + string(depth_index + 1);
                break;
            case 12:
                if (current_time - stage_at < 550) exit;
                depth_index += 1;
                if (depth_index < 3)
                {
                    keyboard_key_press(vk_down);
                    stage = 11;
                    stage_at = current_time;
                    exit;
                }
                keyboard_key_press(ord("L"));
                break;
            case 13:
                if ({{Sub}}.sub_lamp_on) exit;
                keyboard_key_release(ord("L"));
                capture = "light_off";
                break;
            case 14:
                if (current_time - stage_at < 900) exit;
                keyboard_key_press(ord("L"));
                break;
            case 15:
                if (!{{Sub}}.sub_lamp_on) exit;
                keyboard_key_release(ord("L"));
                capture = "light_on";
                break;
            case 16:
                if (current_time - stage_at < 500) exit;
                keyboard_key_press(vk_space);
                break;
            case 17:
                if (current_time - stage_at < 250) exit;
                keyboard_key_release(vk_space);
                break;
            case 18:
                if ({{Sub}}.sub_drop <= 0) exit;
                capture = "cast_start";
                break;
            case 19:
                if (Lure.state != 2) exit;
                capture = "cast";
                break;
            case 20:
                if (current_time - stage_at < 700) exit;
                keyboard_key_press(ord("V"));
                break;
            case 21:
                if (!{{Sub}}.sub_ascending) exit;
                keyboard_key_release(ord("V"));
                capture = "reel_to_surface";
                break;
            case 22:
                if (current_time - stage_at < 450) exit;
                keyboard_key_press(vk_space);
                break;
            case 23:
                if (Lure.state != 0) exit;
                keyboard_key_release(vk_space);
                capture = "reeled";
                break;
            case 24:
                if ({{Sub}}.sub_diving || {{Sub}}.sub_oxygen != {{Sub}}.sub_oxygen_max
                    || instance_exists({{Sub}}.sub_lamp)) exit;
                capture = "surfaced";
                break;
            case 25:
                if (current_time - stage_at < 850) exit;
                capture = "passed";
                stage = 99;
                exit;
            default:
                exit;
        }
        stage += 1;
        stage_at = current_time;
        """;

    private const string Capture = $$"""
        if (capture == "") exit;
        var telemetry = capture + "," + string(current_time);
        if (instance_exists({{Sub}}))
        {
            var sub = instance_find({{Sub}}, 0);
            var lit = instance_exists(sub.sub_lamp) && sub.sub_lamp.visible;
            var dive_depth = sub.sub_diving ? sub.sub_y - 1294 : 0;
            var port_x = sub.sub_diving ? sub.sub_port_x : 0;
            var port_y = sub.sub_diving ? sub.sub_port_y : 0;
            telemetry += "," + string(sub.x) + "," + string(sub.y) + "," + string(dive_depth)
                + "," + string(sub.sub_oxygen) + "," + string(lit) + "," + string(Lure.state)
                + "," + string(Rod.state) + "," + string(Lure.x) + "," + string(Lure.y)
                + "," + string(Lure.tether_x) + "," + string(Lure.tether_y) + "," + string(port_x)
                + "," + string(port_y) + "," + string(sub.sub_drop);
        }
        else telemetry += ",,,,,,,,,,,,,,";
        telemetry += "," + string(Camera.x) + "," + string(Camera.y) + "," + string(Girl.dollars)
            + "," + string(slot >= 0 && BoatData.unlocked[slot]) + "," + string(Lure.deep);
        file = file_text_open_append("jamm_bench_submarine.csv");
        file_text_write_string(file, telemetry);
        file_text_writeln(file);
        file_text_close(file);
        if (capture != "passed") screen_save("jamm_submarine_" + capture + ".png");
        capture = "";
        if (stage == 99) game_end();
        """;
}
