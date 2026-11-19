using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class WorldgenScene
{
    private const string Report = "jamm_bench_worldgen.csv";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "worldgen";
    }

    internal static void Apply(UndertaleData data)
    {
        const string controller = "obj_jmk_endless_ocean";
        if (data.GameObjects.ByName(controller) is null)
            throw new InvalidDataException("The worldgen scene needs Endless Ocean. Build the recipe before instrumenting the archive.");

        var probe = ModObjects.Create(data, "obj_jamm_bench_worldgen", persistent: false);
        var compiler = new CompileGroup(data);
        Queue(EventType.Create, 0, $$"""
            stage = 0;
            ticks = 0;
            shot = "";
            target = 0;
            lamp = noone;
            var file = file_text_open_write("{{Report}}");
            file_text_write_string(file, "stage,boat_x,view_x,view_y,chunks,owned,instances,rock_hit,seed,first,last,fish,writes");
            file_text_writeln(file);
            file_text_close(file);
            """);

        string dismiss = string.Join("\n", new[] { "GameModeMenu", "GM_Classic", "GM_Realism" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));

        Queue(EventType.Step, (uint)EventSubtypeStep.BeginStep, $$"""
            ticks += 1;
            var ocean = instance_find({{controller}}, 0);
            if (stage == 0)
            {
                if (!instance_exists(SaveManager) || !SaveManager.loaded || ticks < 6) exit;
                {{dismiss}}
                window_set_fullscreen(false);
                window_set_size(1200, 720);
                with (Boat) instance_destroy();
                var boat = instance_create(0, 1294, Boat7);
                stock_limit = boat.MAX_X;
                Girl.level = max(25, Girl.level);
                Girl.boatMessageShown = 1;
                Girl.startMessageShown = 1;
                Boat.moveLocked = 0;
                Rod.swing_locked = 0;
                target = stock_limit - 20;
                event_user(1);
                stage = 1;
                ticks = 0;
            }
            if (stage == 1)
            {
                Boat.move_right = 1;
                if (Girl.x > stock_limit + 35)
                {
                    label = "stock_limit_crossed";
                    event_user(0);
                    target = ocean.ocean_start - 15;
                    event_user(1);
                    stage = 2;
                    ticks = 0;
                }
            }
            else if (stage == 2)
            {
                Boat.move_right = 1;
                if (Girl.x > ocean.ocean_start + 35)
                {
                    label = "generated_edge_crossed";
                    event_user(0);
                    stage = 3;
                    ticks = 0;
                }
            }
            else if (stage >= 3 && stage <= 5)
            {
                var chunk = 1;
                if (stage == 4) chunk = 117;
                if (stage == 5) chunk = ocean.ocean_chunk_count - 1;
                var layout = jmk_endless_ocean_layout(ocean.ocean_seed, chunk, ocean.ocean_start, 1244);
                // Frame terrain at the first and last stops, and the deep fish at the middle stop.
                rock_x = layout[4] + 330;
                floor_y = layout[5];
                target = stage == 4 ? layout[24] : rock_x;
                var camera_y = stage == 4 ? layout[25] + 45 : floor_y - 40;
                if (ticks == 1) event_user(1);
                if (!instance_exists(CatSwim)) instance_create(target, camera_y, CatSwim);
                CatSwim.x = target;
                CatSwim.y = camera_y;
                CatSwim.tracker.x = target;
                CatSwim.tracker.y = camera_y;
                CatSwim.collider.x = target;
                CatSwim.collider.y = camera_y;
                BreathBar.breath = BreathBar.MAX_BREATH;
                Camera.x = target;
                Camera.y = camera_y;
                Camera.target_x = target;
                Camera.target_y = camera_y;
                Camera.atOrigin = 1;
                Camera.hold_position = 1;
                if (lamp == noone)
                {
                    lamp = instance_create(target, camera_y, Light);
                    lamp.image_scale = 5;
                    lamp.sub_blend = c_white;
                }
                lamp.x = target;
                lamp.y = camera_y + 10;
                if (ticks == 8)
                {
                    label = "point_" + string(stage - 2);
                    event_user(0);
                    shot = label;
                }
                if (ticks == 15)
                {
                    stage += 1;
                    ticks = 0;
                }
            }
            else if (stage == 6)
            {
                if (ticks == 2)
                {
                    label = "save";
                    event_user(0);
                }
                if (ticks == 5)
                {
                    with (CatSwim) instance_destroy();
                    if (instance_exists(lamp)) with (lamp) instance_destroy();
                    Camera.hold_position = 0;
                    target = stock_limit + 5;
                    event_user(1);
                }
                if (ticks == 13)
                {
                    label = "returned";
                    event_user(0);
                    game_end();
                }
            }

            if (ticks > 180)
            {
                label = "stalled_" + string(stage);
                event_user(0);
                game_end();
            }
            """);
        Queue(EventType.Other, (uint)EventSubtypeOther.User1, """
            Girl.x = target;
            Girl.hspeed_base = 0;
            Girl.hspeed = 0;
            Boat.x = target;
            Lure.x = target;
            Camera.x = target;
            Camera.y = Girl.y + Camera.RELATIVE_ANCHOR_Y;
            Camera.target_x = target;
            Camera.target_y = Camera.y;
            """);
        Queue(EventType.Other, (uint)EventSubtypeOther.User0, $$"""
            var ocean = instance_find({{controller}}, 0);
            var map = ocean.ocean_chunks;
            var owned = 0;
            var generated_fish = ds_list_create();
            var key = ds_map_find_first(map);
            while (!is_undefined(key))
            {
                owned += ds_list_size(ds_map_find_value(map, key));
                key = ds_map_find_next(map, key);
            }
            with (Fish)
            {
                if (variable_instance_exists(id, "jmk_endless_ocean_fish"))
                    ds_list_add(generated_fish, id);
            }
            var hit = false;
            if (stage >= 3 && stage <= 6)
            {
                with (Lure)
                    hit = position_meeting(other.rock_x, other.floor_y + 430, CaveRock);
            }

            var writes = 0;
            if (label == "save")
            {
                SaveManager.saveflag = 7;
                with (SaveManager)
                {
                    event_user(4);
                    event_user(5);
                }
                for (var i = 0; i < 2; i += 1)
                {
                    ini_open(i == 0 ? "sdata7n_beta.ini" : "sdata7n-backup.ini");
                    for (var j = 0; j < ds_list_size(generated_fish); j += 1)
                    {
                        if (ini_key_exists("Fishes", string(ds_list_find_value(generated_fish, j)))) writes += 1;
                    }
                    ini_close();
                }
            }

            var file = file_text_open_append("{{Report}}");
            var row = label + "," + string(round(Girl.x)) + "," + string(round(view_xview[0])) + "," + string(round(view_yview[0]));
            row += "," + string(ds_map_size(map)) + "," + string(owned) + "," + string(instance_count) + "," + string(hit);
            row += "," + string(ocean.ocean_seed) + "," + string(ocean.ocean_first) + "," + string(ocean.ocean_last);
            row += "," + string(ds_list_size(generated_fish)) + "," + string(writes);
            file_text_write_string(file, row);
            file_text_writeln(file);
            file_text_close(file);
            ds_list_destroy(generated_fish);
            """);
        Queue(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, """
            if (shot != "")
            {
                screen_save("jamm_worldgen_" + shot + ".png");
                shot = "";
            }
            """);
        ModObjects.Compile(compiler);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);

        void Queue(EventType type, uint subtype, string gml) =>
            compiler.QueueCodeReplace(probe.EventHandlerFor(type, subtype, data), gml);
    }
}
