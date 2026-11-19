using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class SiloScene
{
    private const string Probe = "obj_jamm_bench_silo";
    private const string Report = "jamm_bench_silo.csv";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return args[0] == "bench" && index >= 3 && args.ElementAtOrDefault(index + 1) == "silo";
    }

    internal static void Apply(UndertaleData data, string[] args)
    {
        int modeIndex = Array.IndexOf(args, "--silo-mode");
        string mode = modeIndex < 0 ? "cycle" : args.ElementAtOrDefault(modeIndex + 1) ?? "";
        int modeId = mode switch
        {
            "cycle" => 0,
            "save" => 1,
            "reload" => 2,
            "cycle-save" => 3,
            _ => throw new ArgumentException("Choose --silo-mode cycle, save, reload, or cycle-save.")
        };
        foreach (string type in new[] { "obj_jamm_silo_hatch", SiloMod.ControllerObjectName, SiloMod.AreaObjectName, "BoatData", "Fish", "CatSwim" })
        {
            if (data.GameObjects.ByName(type) is null)
                throw new InvalidDataException($"The silo scene needs '{type}'. Build The Silo before instrumenting the archive.");
        }

        var probe = ModObjects.Create(data, Probe, persistent: true);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), $$"""
            silo_test_mode = {{modeId}};
            silo_stage = 0;
            silo_ticks = 0;
            silo_shot = "";
            silo_row = "";
            silo_key = 0;
            silo_world_checks = 0;
            var file = file_text_open_write("{{Report}}");
            file_text_write_string(file, "stage,room,money,fish,quest_accepts,quests,quest_progress,boat,rod,persistent,cat_x,cat_y,save_locked,save_persistent,nav_edges,cat_breath,area_air");
            file_text_writeln(file);
            file_text_close(file);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), Steps(data));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), Capture);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User0, data), Snapshot(data));
        ModObjects.Compile(compiler);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: true);
    }

    private static string Steps(UndertaleData data)
    {
        string dismiss = string.Join("\n", new[] { "GameModeMenu", "GM_Classic", "GM_Realism" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));
        string seedFish = data.GameObjects.ByName("Catfin") is null ? "" : """
            if (Boat.fish_count == 0)
            {
                var caught = instance_find(Catfin, 0);
                if (caught == noone) caught = instance_create(Boat.x, Boat.y, Catfin);
                with (caught)
                {
                    fish_appraise();
                    fish_kill_path();
                    state = 4;
                    fish = id;
                    fish_stick(Boat.id);
                }
                Girl.nFishToSell += 1;
            }
            """;

        return $$"""
            silo_ticks += 1;
            if (silo_key != 0) { keyboard_key_release(silo_key); silo_key = 0; }

            var hatch = instance_find(obj_jamm_silo_hatch, 0);
            var ctrl = instance_find(obj_jamm_silo_ctrl, 0);
            var diver = instance_find(obj_jamm_silo_area, 0);
            if (room == NewWorld && silo_stage == 0)
            {
                if (!instance_exists(SaveManager) || !SaveManager.loaded || silo_ticks < 5) exit;
                window_set_fullscreen(false);
                window_set_size(1200, 720);
                if (silo_test_mode == 2)
                {
                    silo_row = instance_exists(CatSwim) && hatch != noone &&
                        point_distance(CatSwim.x, CatSwim.y, hatch.x, hatch.y - 62) < 110 ? "reloaded" : "reload_failed";
                    event_user(0);
                    silo_shot = "reloaded";
                    silo_stage = 11;
                    silo_ticks = 0;
                    exit;
                }

                {{dismiss}}
                if (!instance_exists(Boat))
                {
                    var boat = instance_create(Girl.x, 1294, BoatData.object[1]);
                    boat.boat_index = 1;
                }
                Boat.moveLocked = 0;
                Rod.swing_locked = 0;
                Girl.level = max(25, Girl.level);
                Girl.dollars = 123456;
                Girl.x = hatch.x;
                Girl.boatMessageShown = 1;
                Girl.startMessageShown = 1;
                Boat.x = hatch.x;
                Boat.y = 1294;
                QuestEngine.quests_accepted_today = 2;
                if (instance_number(Quest) == 0)
                {
                    var quest = start_quest(3, 1);
                    quest.progress = 0.75;
                }
                {{seedFish}}
                if (!instance_exists(CatSwim)) instance_create(hatch.x + 146, hatch.y - 54, CatSwim);
                silo_stage = 1;
                silo_ticks = 0;
            }

            if (room == NewWorld && (silo_stage == 1 || silo_stage == 2 || silo_stage == 7 || silo_stage == 12))
            {
                var offset = silo_stage == 1 ? 145 : 0;
                CatSwim.x = hatch.x + offset;
                CatSwim.y = hatch.y - 56;
                CatSwim.tracker.x = CatSwim.x;
                CatSwim.tracker.y = CatSwim.y;
                CatSwim.collider.x = CatSwim.x;
                CatSwim.collider.y = CatSwim.y;
                CatSwim.swim_x = 0; CatSwim.swim_y = 0;
                CatSwim.drift_x = 0; CatSwim.drift_y = 0;
                BreathBar.breath = BreathBar.MAX_BREATH;
                Camera.x = hatch.x + offset * 0.36;
                Camera.y = hatch.y - 27;
                Camera.target_x = Camera.x;
                Camera.target_y = Camera.y;
                Camera.atOrigin = 1;
                if (silo_stage == 1 && silo_ticks == 3) silo_shot = "hatch-closed";
                if (silo_stage == 1 && silo_ticks >= 5)
                {
                    silo_stage = 2;
                    silo_ticks = 0;
                }
                else if (silo_stage == 2 && hatch.silo_open >= 1)
                {
                    if (silo_world_checks == 0)
                    {
                        silo_row = "before";
                        event_user(0);
                        silo_shot = "hatch-open";
                        silo_world_checks = 1;
                    }
                    else
                    {
                        silo_key = ord("E");
                        keyboard_key_press(silo_key);
                        silo_stage = 3;
                        silo_ticks = 0;
                    }
                }
                else if ((silo_stage == 7 || silo_stage == 12) && ctrl.silo_cooldown <= 0)
                {
                    silo_key = ord("E");
                    keyboard_key_press(silo_key);
                    silo_stage = silo_stage == 7 ? 8 : 13;
                    silo_ticks = 0;
                }
            }

            if (room == room_jamm_silo && silo_stage == 3)
            {
                if (silo_ticks == 4) silo_shot = "area";
                if (silo_ticks >= 12)
                {
                    silo_row = instance_exists(SaveManager) || instance_exists(Girl) ? "inside_unexpected_world_instance" : "inside";
                    event_user(0);
                    silo_stage = 40;
                    silo_ticks = 0;
                }
            }
            if (room == room_jamm_silo && (silo_stage == 40 || silo_stage == 41))
            {
                if (silo_stage == 40 && diver.y >= 347)
                {
                    silo_stage = 41;
                    silo_ticks = 0;
                }
                if (silo_stage == 41 && point_distance(diver.x, diver.y, 1326, 358) < 37)
                {
                    silo_row = "swam_to_control";
                    event_user(0);
                    silo_stage = 4;
                    silo_ticks = 0;
                }
                else
                {
                    silo_key = silo_stage == 40 ? ord("S") : ord("D");
                    keyboard_key_press(silo_key);
                }
            }
            if (room == room_jamm_silo && silo_stage == 4)
            {
                if (silo_ticks == 4) silo_shot = "control-room";
                if (silo_ticks == 6)
                {
                    silo_key = ord("E");
                    keyboard_key_press(silo_key);
                }
                if (silo_ticks == 9)
                {
                    silo_row = ctrl.silo_earned == 3200 ? "salvage_recovered" : "salvage_failed";
                    event_user(0);
                    silo_key = vk_escape;
                    keyboard_key_press(silo_key);
                    silo_stage = 5;
                    silo_ticks = 0;
                }
            }
            if (room == room_jamm_silo && silo_stage == 5)
            {
                if (silo_ticks == 3 && diver.silo_pause) silo_shot = silo_world_checks == 2 ? "save-pause" : "pause";
                if (silo_ticks == 5 && diver.silo_pause)
                {
                    silo_row = (silo_test_mode == 1 || silo_test_mode == 3 && silo_world_checks == 2) ? "save_and_quit_requested" : "paused";
                    event_user(0);
                    silo_key = (silo_test_mode == 1 || silo_test_mode == 3 && silo_world_checks == 2) ? ord("S") : ord("E");
                    keyboard_key_press(silo_key);
                    silo_stage = 6;
                    silo_ticks = 0;
                }
            }
            if (room == NewWorld && silo_stage == 6 && silo_ticks >= 4)
            {
                var expected_quit = silo_test_mode == 1 || silo_test_mode == 3 && silo_world_checks == 2;
                silo_row = expected_quit ? "save_and_quit_failed" : "back";
                event_user(0);
                silo_shot = "newworld";
                silo_stage = expected_quit ? 11 : 7;
                silo_ticks = 0;
            }
            if (room == room_jamm_silo && silo_stage == 8 && silo_ticks >= 9)
            {
                silo_shot = "repeat-entry";
                silo_row = (ctrl.silo_claimed & 2) != 0 ? "repeat_claim_preserved" : "repeat_claim_missing";
                event_user(0);
                diver.x = 760; diver.y = 320;
                diver.silo_air = 0.01;
                silo_stage = 9;
                silo_ticks = 0;
            }
            if (room == NewWorld && silo_stage == 9 && silo_ticks >= 4)
            {
                silo_row = ctrl.silo_exit == 3 ? "emergency_return" : "emergency_failed";
                event_user(0);
                silo_shot = "emergency-return";
                silo_stage = silo_test_mode == 3 ? 12 : 11;
                silo_ticks = 0;
            }
            if (room == room_jamm_silo && silo_stage == 13)
            {
                if (silo_ticks == 5) { diver.x = 183; diver.y = 752; }
                if (silo_ticks == 8)
                {
                    silo_key = ord("E");
                    keyboard_key_press(silo_key);
                }
                if (silo_ticks == 11)
                {
                    silo_row = ctrl.silo_earned == 2400 ? "save_salvage_recovered" : "save_salvage_failed";
                    event_user(0);
                    silo_key = vk_escape;
                    keyboard_key_press(silo_key);
                    silo_world_checks = 2;
                    silo_stage = 5;
                    silo_ticks = 0;
                }
            }
            if (silo_stage == 11 && silo_ticks >= 4) game_end();
            if (silo_ticks > 180)
            {
                silo_row = "stalled_stage_" + string(silo_stage);
                event_user(0);
                game_end();
            }
            """;
    }

    private const string Capture = """
        if (silo_shot != "")
        {
            screen_save("jamm_silo_" + silo_shot + ".png");
            silo_shot = "";
        }
        """;

    private static string Snapshot(UndertaleData data) => $$"""
        var row = silo_row + "," + room_get_name(room);
        row += "," + string(instance_exists(Girl) ? Girl.dollars : -1);
        row += "," + string(instance_exists(Boat) ? Boat.fish_count : -1);
        row += "," + string(instance_exists(QuestEngine) ? QuestEngine.quests_accepted_today : -1);
        row += "," + string(instance_number(Quest));
        var total_progress = 0;
        with (Quest) total_progress += progress;
        row += "," + string(total_progress);
        row += "," + string(instance_exists(Boat) ? Boat.boat_index : -1);
        row += "," + string(instance_exists(Rod) ? Rod.rod_id : -1);
        row += "," + string(room_persistent);
        row += "," + string(instance_exists(CatSwim) ? CatSwim.x : -1);
        row += "," + string(instance_exists(CatSwim) ? CatSwim.y : -1);
        row += "," + string(instance_exists(SaveManager) ? SaveManager.saving_locked : -1);
        row += "," + string(instance_exists(SaveManager) ? SaveManager.persistent : -1);
        row += "," + string({{(data.GameObjects.ByName("NavEdge") is not null ? "instance_number(NavEdge)" : "-1")}});
        row += "," + string(instance_exists(BreathBar) ? BreathBar.breath : -1);
        row += "," + string(instance_exists(obj_jamm_silo_area) ? obj_jamm_silo_area.silo_air : -1);
        var file = file_text_open_append("{{Report}}");
        file_text_write_string(file, row);
        file_text_writeln(file);
        file_text_close(file);
        show_debug_message("Silo scene: " + row);
        """;
}
