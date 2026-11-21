using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class WorldgenScene
{
    private const string Report = "jamm_bench_worldgen.csv";
    private const string Controller = "obj_jmk_endless_ocean";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "worldgen";
    }

    internal static void Apply(UndertaleData data, bool showcase)
    {
        if (data.Scripts.ByName("jmk_endless_ocean_setup") is null)
            throw new InvalidDataException("The worldgen scene needs Endless Ocean 0.2.0. Build the current recipe before instrumenting the archive.");

        var probe = ModObjects.Create(data, "obj_jamm_bench_worldgen", persistent: false);
        var compiler = new CompileGroup(data);
        string dismiss = string.Join("\n", new[] { "GameModeMenu", "GM_Classic", "GM_Realism" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));

        if (showcase)
        {
            var world = data.Rooms.ByName("NewWorld");
            if (world?.Views.FirstOrDefault() is not { Enabled: true } view)
                throw new InvalidDataException("The worldgen showcase needs an enabled primary view in NewWorld. Build the recipe on a compatible legacy archive.");

            // The showcase positions the primary view directly in its End Step.
            view.ObjectId = null!;
            QueueShowcase(Queue, dismiss);
        }
        else
        {
            QueueValidation(Queue, dismiss);
        }

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

    private const string FindCheckpoints = """
        route[0] = 0;
        route[1] = -1;
        route[2] = -1;
        route[3] = -1;
        route[4] = -1;
        route[5] = ocean.ocean_chunk_count - 1;
        for (var chunk = ocean.ocean_content_chunk + 4; chunk < ocean.ocean_chunk_count - 1; chunk += 1)
        {
            var layout = jmk_endless_ocean_layout(ocean.ocean_seed, chunk, ocean.ocean_anchor_y, room_height, ocean.ocean_content_chunk);
            var biome = layout[5] + 1;
            if (route[biome] == -1) route[biome] = chunk;
            if (route[4] == -1 && layout[6]) route[4] = chunk;
            if (route[1] >= 0 && route[2] >= 0 && route[3] >= 0 && route[4] >= 0) break;
        }
        """;

    private static void QueueValidation(Action<EventType, uint, string> queue, string dismiss)
    {
        queue(EventType.Create, 0, $$"""
            stage = 0;
            ticks = 0;
            shot = "";
            target = 0;
            chunk_at_stop = -1;
            lamp = noone;
            var file = file_text_open_write("{{Report}}");
            file_text_write_string(file, "stage,boat_x,view_x,view_y,chunks,owned,instances,rock_hits,seed,first,last,fish,writes,clear_hits,bottom_hits,seam_hits");
            file_text_writeln(file);
            file_text_close(file);
            """);

        queue(EventType.Step, (uint)EventSubtypeStep.BeginStep, $$"""
            ticks += 1;
            var ocean = instance_find({{Controller}}, 0);
            if (stage == 0)
            {
                if (!instance_exists(SaveManager) || !SaveManager.loaded || ticks < 6) exit;
                {{dismiss}}
                {{FindCheckpoints}}
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
            else if (stage >= 3 && stage <= 8)
            {
                var chunk = route[stage - 3];
                if (chunk < 0)
                {
                    label = "missing_point_" + string(stage - 2);
                    event_user(0);
                    game_end();
                    exit;
                }
                var offset_x = chunk * 2048 + 1024;
                target = ocean.ocean_start + offset_x;
                var floor_y = jmk_endless_ocean_height(ocean.ocean_seed, offset_x, ocean.ocean_anchor_y, room_height, ocean.ocean_content_chunk);
                var camera_y = floor_y - 200;
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
                    lamp.image_scale = 4;
                    lamp.sub_blend = c_white;
                }
                lamp.x = target;
                lamp.y = camera_y;
                if (ticks == 8)
                {
                    chunk_at_stop = chunk;
                    label = "point_" + string(stage - 2);
                    event_user(0);
                    shot = label;
                }
                if (ticks == 11)
                {
                    stage += 1;
                    ticks = 0;
                }
            }
            else if (stage == 9)
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
                    target = ocean.ocean_start - 4096 + 80;
                    event_user(1);
                    chunk_at_stop = -1;
                }
                if (ticks >= 6)
                {
                    Boat.move_left = 1;
                    if (Girl.x < ocean.ocean_start - 4100 && ds_map_size(ocean.ocean_chunks) == 0)
                    {
                        label = "returned";
                        event_user(0);
                        game_end();
                    }
                }
            }

            if (ticks > 180)
            {
                label = "stalled_" + string(stage);
                event_user(0);
                game_end();
            }
            """);
        queue(EventType.Other, (uint)EventSubtypeOther.User1, """
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
        queue(EventType.Other, (uint)EventSubtypeOther.User0, $$"""
            var ocean = instance_find({{Controller}}, 0);
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
            var rock_hits = 0;
            var clear_hits = 0;
            var bottom_hits = 0;
            var seam_hits = 0;
            if (chunk_at_stop >= 0 && label != "save")
            {
                for (var i = 0; i < 8; i += 1)
                {
                    var offset_x = chunk_at_stop * 2048 + i * 256;
                    var probe_x = ocean.ocean_start + offset_x;
                    var floor_y = jmk_endless_ocean_height(ocean.ocean_seed, offset_x, ocean.ocean_anchor_y, room_height, ocean.ocean_content_chunk);
                    with (Lure)
                    {
                        if (position_meeting(probe_x, min(room_height - 1, floor_y + 14), CaveRock)) rock_hits += 1;
                        if (!position_meeting(probe_x, floor_y - 850, CaveRock)) clear_hits += 1;
                        if (position_meeting(probe_x, room_height - 1, obj_jmk_ocean_ground)) bottom_hits += 1;
                    }
                }
                if (chunk_at_stop == 0)
                {
                    var sx = ocean.ocean_start;
                    var sy = ocean.ocean_anchor_y;
                    if (collision_line(sx - 1, sy - 4, sx - 1, sy + 12, Collidable, true, false) != noone) seam_hits += 1;
                    if (position_meeting(sx, min(room_height - 1, sy + 12), obj_jmk_ocean_ground)) seam_hits += 1;
                }
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
            row += "," + string(ds_map_size(map)) + "," + string(owned) + "," + string(instance_count) + "," + string(rock_hits);
            row += "," + string(ocean.ocean_seed) + "," + string(ocean.ocean_first) + "," + string(ocean.ocean_last);
            row += "," + string(ds_list_size(generated_fish)) + "," + string(writes);
            row += "," + string(clear_hits) + "," + string(bottom_hits) + "," + string(seam_hits);
            file_text_write_string(file, row);
            file_text_writeln(file);
            file_text_close(file);
            ds_list_destroy(generated_fish);
            """);
    }

    private static void QueueShowcase(Action<EventType, uint, string> queue, string dismiss)
    {
        const int frameCount = 15;
        queue(EventType.Create, 0, $$"""
            frame = 0;
            ticks = 0;
            ready = false;
            shot = "";
            lamp = noone;
            var file = file_text_open_write("{{Report}}");
            file_text_write_string(file, "shot,seed,view_x,view_y,view_width,view_height,chunks,visible_fish");
            file_text_writeln(file);
            file_text_close(file);
            """);

        queue(EventType.Step, (uint)EventSubtypeStep.BeginStep, $$"""
            if (ready) exit;
            ticks += 1;
            if (!instance_exists(SaveManager) || !SaveManager.loaded || ticks < 6) exit;
            {{dismiss}}
            window_set_fullscreen(false);
            window_set_size(1200, 720);
            var ocean = instance_find({{Controller}}, 0);
            {{FindCheckpoints}}
            name_at[0] = "surface";
            x_at[0] = (ocean.ocean_content_chunk + 12) * 2048 + 1024;
            y_at[0] = 1040;
            for (var i = 0; i < 4; i += 1)
            {
                var cx = i == 0 ? 0 : route[i] * 2048 + 1024;
                var cy = jmk_endless_ocean_height(ocean.ocean_seed, cx, ocean.ocean_anchor_y, room_height, ocean.ocean_content_chunk) - 180;
                for (var j = 0; j < 3; j += 1)
                {
                    var slot = 1 + 3 * i + j;
                    name_at[slot] = "pan_" + string(i) + "_" + string(j);
                    x_at[slot] = cx + (j - 1) * 2048;
                    y_at[slot] = cy;
                    lamp_x_at[slot] = cx;
                }
            }
            name_at[13] = "wreck";
            x_at[13] = route[4] * 2048 + 1040;
            name_at[14] = "seed_comparison";
            x_at[14] = x_at[0];
            for (var i = 13; i <= 14; i += 1)
            {
                y_at[i] = jmk_endless_ocean_height(ocean.ocean_seed, x_at[i], ocean.ocean_anchor_y, room_height, ocean.ocean_content_chunk) - 140;
                lamp_x_at[i] = x_at[i];
            }
            var boat_x = ocean.ocean_start + x_at[0];
            with (Boat) instance_destroy();
            instance_create(boat_x, 1294, Boat7);
            Girl.x = boat_x;
            Girl.hspeed_base = 0;
            Girl.hspeed = 0;
            Girl.level = max(25, Girl.level);
            Girl.boatMessageShown = 1;
            Girl.startMessageShown = 1;
            Girl.hide_ui_lock = 1;
            Lure.x = boat_x;
            lamp = instance_create(boat_x, y_at[0], Light);
            lamp.sub_blend = c_white;
            lamp.add_blend = make_color_rgb(16, 25, 39);
            ready = true;
            ticks = 0;
            """);

        queue(EventType.Step, (uint)EventSubtypeStep.EndStep, $$"""
            if (!ready) exit;
            ticks += 1;
            var ocean = instance_find({{Controller}}, 0);
            var frame_width = frame == 0 ? 2200 : 4096;
            var frame_height = round(frame_width * 3 / 5);
            Camera.x = ocean.ocean_start + x_at[frame];
            Camera.y = y_at[frame];
            view_wview[0] = frame_width;
            view_hview[0] = frame_height;
            view_xview[0] = Camera.x - frame_width / 2;
            view_yview[0] = clamp(Camera.y - frame_height / 2, 0, room_height - frame_height);
            with (Message) visible = false;

            // The vanilla renderer allocates its light surface at the original view size.
            with (LightingSystem)
            {
                if (width != frame_width || height != frame_height)
                {
                    if (surface_exists(light_surface)) surface_free(light_surface);
                    width = frame_width;
                    height = frame_height;
                    light_surface = surface_create(width, height);
                }
                day_coefficient = 1;
                overcast_coefficient = 1;
                lightning_flash = 0;
            }
            lamp.visible = frame > 0;
            lamp.x = ocean.ocean_start + lamp_x_at[frame];
            lamp.y = Camera.y;
            lamp.image_scale = (frame < 13 ? 22000 : 12500) / sprite_get_width(lamp.sprite_index);
            lamp.special_scale = lamp.image_scale;

            // Allow the normal streamer and fish steps to settle before saving the rendered frame.
            if (ticks == 9)
            {
                shot = name_at[frame];
                var fish_count = 0;
                with (Fish)
                {
                    if (variable_instance_exists(id, "jmk_endless_ocean_fish") &&
                        point_in_rectangle(x, y, view_xview[0], view_yview[0],
                            view_xview[0] + view_wview[0], view_yview[0] + view_hview[0]))
                        fish_count += 1;
                }
                var file = file_text_open_append("{{Report}}");
                var row = shot + "," + string(ocean.ocean_seed) + "," + string(view_xview[0]) + "," + string(view_yview[0]);
                row += "," + string(frame_width) + "," + string(frame_height) + "," + string(ds_map_size(ocean.ocean_chunks));
                file_text_write_string(file, row + "," + string(fish_count));
                file_text_writeln(file);
                file_text_close(file);
            }
            if (ticks == 10)
            {
                frame += 1;
                ticks = 0;
                if (frame == {{frameCount}}) game_end();
            }
            """);

    }
}
