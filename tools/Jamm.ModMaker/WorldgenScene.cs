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
        if (data.GameObjects.ByName(Controller) is null)
            throw new InvalidDataException("The worldgen scene needs Endless Ocean. Build the recipe before instrumenting the archive.");

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

    private static void QueueValidation(Action<EventType, uint, string> queue, string dismiss)
    {
        queue(EventType.Create, 0, $$"""
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

        queue(EventType.Step, (uint)EventSubtypeStep.BeginStep, $$"""
            ticks += 1;
            var ocean = instance_find({{Controller}}, 0);
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
    }

    private static void QueueShowcase(Action<EventType, uint, string> queue, string dismiss)
    {
        const int chunkWidth = 2048;
        const int floorY = 11008;
        const int surfaceWidth = 2200;
        const int wideWidth = 2 * chunkWidth;
        const int panoramaFirst = 26;
        const int panoramaCount = 5;

        var frames = new List<(string Name, int X, int Y, int Width, int LampX, int LampWidth)>();
        AddFrame("surface", ChunkCenter(117), 1040, surfaceWidth);
        AddFrame("transition", 0, floorY, wideWidth);
        foreach (int chunk in new[] { 3, 117, 486 })
        {
            AddFrame($"floor_{chunk}", ChunkCenter(chunk), floorY, wideWidth);
            AddFrame($"mid_{chunk}", ChunkCenter(chunk), 5794, wideWidth);
            AddFrame($"upper_{chunk}", ChunkCenter(chunk), 2494, wideWidth);
        }

        // Keep the light in world coordinates so the adjoining panorama crops share its falloff.
        int panoramaLampX = ChunkCenter(panoramaFirst + panoramaCount / 2);
        for (int i = 0; i < panoramaCount; i++)
            AddFrame($"panorama_{i + 1}", ChunkCenter(panoramaFirst + i), floorY, wideWidth,
                panoramaLampX, chunkWidth * panoramaCount);

        string route = string.Join("\n", frames.Select((frame, i) => FormattableString.Invariant($"""
            name_at[{i}] = "{frame.Name}";
            x_at[{i}] = {frame.X};
            y_at[{i}] = {frame.Y};
            width_at[{i}] = {frame.Width};
            lamp_x_at[{i}] = {frame.LampX};
            lamp_width_at[{i}] = {frame.LampWidth};
            """)));

        queue(EventType.Create, 0, $$"""
            {{route}}
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
            var frame_width = width_at[frame];
            var frame_height = round(frame_width * 3 / 5);
            Camera.x = ocean.ocean_start + x_at[frame];
            Camera.y = y_at[frame];
            view_wview[0] = frame_width;
            view_hview[0] = frame_height;
            view_xview[0] = Camera.x - frame_width / 2;
            view_yview[0] = Camera.y - frame_height / 2;
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
            lamp.image_scale = 4 * lamp_width_at[frame] / sprite_get_width(lamp.sprite_index);
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
                if (frame == {{frames.Count}}) game_end();
            }
            """);

        static int ChunkCenter(int chunk) => chunk * chunkWidth + chunkWidth / 2;

        void AddFrame(string name, int x, int y, int width, int? lampX = null, int? lampWidth = null) =>
            frames.Add((name, x, y, width, lampX ?? x, lampWidth ?? width));
    }
}
