using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Plays a paced submarine shop, dive, cast, and surface sequence.</summary>
internal static class ShowcaseSubmarineScene
{
    private const string Sub = "jmk_submarine_submarine";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "showcase-submarine";
    }

    internal static void Apply(UndertaleData data)
    {
        if (data.GameObjects.ByName(Sub) is null || data.Scripts.ByName("jamm_item_find") is null)
            throw new InvalidDataException("The submarine showcase needs the Submarine package and JAMM item lookup in the merged video archive.");

        var probe = ModObjects.Create(data, "obj_jamm_showcase_submarine", persistent: false);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            stage = 0;
            stage_at = current_time;
            slot = -1;
            capture = "";
            catch_logged = false;
            file = file_text_open_write("jamm_showcase_submarine.csv");
            file_text_write_string(file, "stage,ms,x,y,depth,oxygen,lamp,lure_state,cash");
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
        if (current_time - stage_at > 70000) { game_end(); exit; }
        switch (stage)
        {
            case 0:
                if (current_time - stage_at < 2200) exit;
                slot = jamm_item_find("boat", "submarine:submarine");
                if (slot < 0) { game_end(); exit; }
                with (Handler) instance_destroy();
                with (Message) instance_destroy();
                with (Boat) instance_destroy();
                with (Net) instance_destroy();
                with (Lure)
                {
                    if (state != 0) line_snap();
                    line_reset();
                }
                Girl.dollars = 150000000;
                Girl.startMessageShown = 1;
                Girl.boatMessageShown = 2;
                Girl.nightfallUnlocked = 1;
                Girl.level = max(Girl.level, 30);
                BoatData.unlocked[slot] = 0;
                with (icon_Boats) event_user(0);
                stage = 1;
                stage_at = current_time;
                break;
            case 1:
                if (!instance_exists(BoatMenu) || !BoatMenu.done_spawning) exit;
                var card = noone;
                with (BoatCard) if (item_id == other.slot) card = id;
                if (card == noone)
                {
                    if (current_time - stage_at < 500) exit;
                    keyboard_key_release(vk_down);
                    keyboard_key_press(vk_down);
                    stage_at = current_time;
                    exit;
                }
                keyboard_key_release(vk_down);
                shop_card = card;
                capture = "shop_card";
                stage = 2;
                stage_at = current_time;
                break;
            case 2:
                if (current_time - stage_at < 1700) exit;
                with (shop_card) event_user(0);
                stage = 3;
                stage_at = current_time;
                break;
            case 3:
                if (!instance_exists(jmk_submarine_submarine) || BoatData.unlocked[slot] != 1) exit;
                capture = "purchased";
                stage = 4;
                stage_at = current_time;
                break;
            case 4:
                // Sail east from the shop using normal steering input.
                if (current_time - stage_at < 8000)
                {
                    keyboard_key_press(vk_right);
                    exit;
                }
                keyboard_key_release(vk_right);
                capture = "sailed_east";
                stage = 5;
                stage_at = current_time;
                break;
            case 5:
                if (current_time - stage_at < 900) exit;
                keyboard_key_press(ord("V"));
                stage = 6;
                stage_at = current_time;
                break;
            case 6:
                if (!jmk_submarine_submarine.sub_diving) exit;
                keyboard_key_release(ord("V"));
                keyboard_key_press(vk_down);
                stage = 7;
                stage_at = current_time;
                break;
            case 7:
                if (current_time - stage_at < 4000) exit;
                keyboard_key_release(vk_down);
                capture = "dive_oxygen_hud";
                stage = 8;
                stage_at = current_time;
                break;
            case 8:
                if (current_time - stage_at < 4000) exit;
                keyboard_key_press(ord("L"));
                stage = 9;
                stage_at = current_time;
                break;
            case 9:
                if (jmk_submarine_submarine.sub_lamp_on) exit;
                keyboard_key_release(ord("L"));
                capture = "light_off";
                stage = 10;
                stage_at = current_time;
                break;
            case 10:
                if (current_time - stage_at < 1000) exit;
                keyboard_key_press(ord("L"));
                stage = 11;
                stage_at = current_time;
                break;
            case 11:
                if (!jmk_submarine_submarine.sub_lamp_on) exit;
                keyboard_key_release(ord("L"));
                capture = "light_on";
                stage = 12;
                stage_at = current_time;
                break;
            case 12:
                if (current_time - stage_at < 900) exit;
                keyboard_key_press(vk_space);
                stage = 13;
                stage_at = current_time;
                break;
            case 13:
                if (current_time - stage_at < 350) exit;
                keyboard_key_release(vk_space);
                stage = 14;
                stage_at = current_time;
                break;
            case 14:
                // Keep the oxygen HUD visible while a nearby fish gets time to take the lure.
                var caught = Lure.fish > 0 && Lure.hookedFishSize > 0;
                if (current_time - stage_at < 7000 || (!caught && current_time - stage_at < 12000)) exit;
                capture = caught ? "catch" : "cast_no_bite";
                stage = 15;
                stage_at = current_time;
                break;
            case 15:
                if (current_time - stage_at < 1500) exit;
                keyboard_key_press(vk_space);
                stage = 16;
                stage_at = current_time;
                break;
            case 16:
                if (!catch_logged && Lure.fish > 0 && Lure.hookedFishSize > 0)
                {
                    capture = "catch";
                    catch_logged = true;
                }
                if (Lure.state != 0 && current_time - stage_at < 12000) exit;
                keyboard_key_release(vk_space);
                capture = "reeled";
                stage = 17;
                stage_at = current_time;
                break;
            case 17:
                if (current_time - stage_at < 850) exit;
                keyboard_key_press(ord("V"));
                stage = 18;
                stage_at = current_time;
                break;
            case 18:
                if (!jmk_submarine_submarine.sub_ascending) exit;
                keyboard_key_release(ord("V"));
                capture = "surfacing";
                stage = 19;
                stage_at = current_time;
                break;
            case 19:
                if (jmk_submarine_submarine.sub_diving || jmk_submarine_submarine.sub_oxygen != jmk_submarine_submarine.sub_oxygen_max
                    || instance_exists(jmk_submarine_submarine.sub_lamp)) exit;
                capture = "surfaced";
                stage = 20;
                stage_at = current_time;
                break;
            case 20:
                if (current_time - stage_at < 5000) exit;
                game_end();
                break;
        }
        """;

    private const string Capture = $$"""
        if (!variable_instance_exists(id, "capture") || capture == "") exit;
        var telemetry = capture + "," + string(current_time);
        if (instance_exists(jmk_submarine_submarine))
        {
            var sub = instance_find(jmk_submarine_submarine, 0);
            telemetry += "," + string(sub.x) + "," + string(sub.y) + "," + string(sub.sub_diving ? sub.sub_y - 1294 : 0)
                + "," + string(sub.sub_oxygen) + "," + string(sub.sub_lamp_on) + "," + string(Lure.state)
                + "," + string(Girl.dollars);
        }
        else telemetry += ",,,,,,,";
        var file = file_text_open_append("jamm_showcase_submarine.csv");
        file_text_write_string(file, telemetry);
        file_text_writeln(file);
        file_text_close(file);
        capture = "";
        """;
}
