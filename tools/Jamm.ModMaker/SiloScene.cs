using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class SiloScene
{
    private const string Probe = "obj_jamm_bench_silo";
    private const string Report = "jamm_bench_silo.csv";
    private const string HatchController = "jmk_jamm_silo_object_hatch";
    private static readonly string[] FishNames = ["veyl", "radik", "ferrex", "garrusk"];
    private static readonly string[] ReferenceFish = ["Garder", "Sizzer", "Magmer", "Shark"];

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return args[0] == "bench" && index >= 3 && args.ElementAtOrDefault(index + 1) == "silo";
    }

    internal static void Apply(UndertaleData data, string[] args)
    {
        int modeIndex = Array.IndexOf(args, "--silo-mode");
        string mode = modeIndex < 0 ? "cycle" : args.ElementAtOrDefault(modeIndex + 1) ?? "";
        if (mode is not ("cycle" or "save" or "reload"))
            throw new ArgumentException("Choose --silo-mode cycle, save, or reload.");

        string[] species = FishNames.Select(key => $"jmk_jamm_silo_{key}").ToArray();
        foreach (string name in species.Concat(ReferenceFish).Concat([HatchController, "MissileSilo", "BoatData", "Lure", "Collidable", "CatalogData", "BigHook2", "Camera"]))
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"The silo scene needs '{name}'. Merge the Silo ModKit package before instrumenting.");

        var probe = ModObjects.Create(data, Probe, persistent: true);
        var compiler = new CompileGroup(data);
        string setup = string.Join('\n', species.Select((name, i) => $"silo_species[{i}] = {name};"));
        string references = string.Join('\n', ReferenceFish.Select((name, i) => $"silo_reference[{i}] = {name};"));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), $$"""
            {{setup}}
            {{references}}
            silo_mode = {{(mode == "reload" ? 2 : mode == "save" ? 1 : 0)}};
            silo_stage = 0;
            silo_ticks = 0;
            silo_index = 0;
            silo_shot = "";
            silo_row = "";
            silo_blocked = 0;
            silo_recorded_mid = false;
            silo_recorded_open = false;
            silo_glow_ticks = 0;
            silo_art_show = false;
            var file = file_text_open_write("{{Report}}");
            file_text_write_string(file, "stage,room,x,y,object,population,fishalog_count,fish_in_boat,blocked_samples,hatch_x,hatch_y,progress,glow,passable");
            file_text_writeln(file);
            file_text_close(file);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), Steps(data));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), """
            if (room != NewWorld || silo_stage == 0 || !instance_exists(Camera)) exit;

            // Wine's long frames can overshoot the native camera's interpolation.
            // Frame the scripted lure or boat directly while capturing the scene.
            if (silo_stage == 1 || silo_stage == 7)
            {
                Camera.x = silo_view_x;
                Camera.y = silo_view_y;
            }
            else if (silo_stage < 5)
            {
                Camera.x = silo_x;
                Camera.y = silo_y;
            }
            else if (instance_exists(Boat))
            {
                Camera.x = Boat.x;
                Camera.y = Boat.y + 50;
            }
            Camera.ORIGIN_X = Camera.x;
            Camera.ORIGIN_Y = Camera.y;
            Camera.atOrigin = 1;
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User0, data), Snapshot);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User1, data), """
            // The runner supplies its coordinates on the test object because events have their own local scopes.
            Rod.state = 3;
            Lure.state = 2;
            Lure.x = silo_x;
            Lure.y = silo_y;
            Lure.spd = 0;
            Lure.tether_length = point_distance(Rod.x, Rod.y, Lure.x, Lure.y) + 25;
            Lure.canBite = 0;
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), """
            if (silo_art_show)
            {
                // Photograph the live sprite frame and the donor at 1x in identical light.
                // Stack the subjects so the largest pair fits inside the capture window.
                draw_set_alpha(1);
                draw_set_color(make_color_rgb(23, 29, 39));
                draw_rectangle(0, 0, 1200, 720, false);
                draw_set_color(c_white);
                draw_sprite(silo_art_live, silo_art_frame,
                    600 - sprite_get_width(silo_art_live) / 2 + sprite_get_xoffset(silo_art_live),
                    180 - sprite_get_height(silo_art_live) / 2 + sprite_get_yoffset(silo_art_live));
                draw_sprite(silo_art_sprite, 0,
                    600 - sprite_get_width(silo_art_sprite) / 2 + sprite_get_xoffset(silo_art_sprite),
                    540 - sprite_get_height(silo_art_sprite) / 2 + sprite_get_yoffset(silo_art_sprite));
            }
            if (silo_shot != "")
            {
                screen_save("jamm_silo_" + silo_shot + ".png");
                silo_shot = "";
            }
            """);
        ModObjects.Compile(compiler);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: true);
    }

    private static string Steps(UndertaleData data)
    {
        string dismiss = string.Join('\n', new[] { "GameModeMenu", "GM_Classic", "GM_Realism" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));

        return $$"""
            if (room != NewWorld || !instance_exists(SaveManager) || !SaveManager.loaded) exit;
            var control, hatch, vessel, species, target, dx, dy, px, py, turn;
            control = instance_find({{HatchController}}, 0);
            if (!instance_exists(control) || !control.silo_ready) exit;
            hatch = control.hatch;
            if (!instance_exists(hatch)) exit;
            species = silo_species[silo_index];
            silo_ticks += 1;

            if (silo_stage == 0)
            {
                if (silo_ticks < 10 || !variable_instance_exists(CatalogData.id, "jamm_fish_names")) exit;
                window_set_fullscreen(false);
                window_set_size(1200, 720);
                display_set_gui_size(1200, 720);
                {{dismiss}}
                if (silo_mode == 2)
                {
                    for (silo_index = 0; silo_index < 4; silo_index += 1)
                    {
                        silo_row = "reload";
                        event_user(0);
                    }
                    silo_index = 0;
                    silo_view_x = hatch.x + 55;
                    silo_view_y = hatch.y + 95;
                    silo_stage = 7;
                    silo_ticks = 0;
                    exit;
                }
                if (!instance_exists(Boat))
                {
                    vessel = instance_create(Girl.x, 1294, BoatData.object[1]);
                    vessel.boat_index = 1;
                }
                Girl.level = max(25, Girl.level);
                Girl.boatMessageShown = 1;
                Girl.startMessageShown = 1;
                Boat.moveLocked = 0;
                Rod.swing_locked = 0;
                Boat.x = hatch.x + 45;
                Girl.x = Boat.x;
                Boat.y = 1294;
                silo_stage = 1;
                silo_ticks = 0;
            }

            if (silo_stage == 7)
            {
                if (silo_ticks == 4)
                {
                    silo_row = "reload-hatch";
                    event_user(0);
                    silo_shot = "reload-hatch";
                }
                if (silo_ticks >= 8)
                {
                    silo_shot = "reload";
                    silo_stage = 6;
                    silo_ticks = 0;
                }
                exit;
            }

            if (silo_stage == 1)
            {
                // Keep the closed and moving shutters in the same framing as the lure approaches.
                turn = hatch.image_angle * pi / 180;
                dx = 56;
                dy = silo_ticks < 16 ? -350 : -88;
                silo_x = hatch.x + dx * cos(turn) + dy * sin(turn);
                silo_y = hatch.y - dx * sin(turn) + dy * cos(turn);
                silo_view_x = hatch.x + 55;
                silo_view_y = hatch.y + 95;
                event_user(1);
                if (silo_ticks == 8) silo_row = "closed";
                else if (!silo_recorded_mid && control.silo_progress >= 0.37 && control.silo_progress < 0.72)
                {
                    silo_recorded_mid = true;
                    silo_row = "mid";
                }
                else if (!silo_recorded_open && control.silo_progress == 1)
                {
                    silo_recorded_open = true;
                    silo_row = "opened";
                }
                else if (silo_recorded_open && control.silo_glow == 1)
                {
                    silo_view_x = hatch.x + 65;
                    silo_view_y = hatch.y + 335;
                    silo_glow_ticks += 1;
                    if (silo_glow_ticks == 3) silo_row = "glow";
                    if (silo_glow_ticks == 5)
                    {
                        silo_stage = 2;
                        silo_ticks = 0;
                    }
                }
                if (silo_row != "")
                {
                    event_user(0);
                    silo_shot = silo_row;
                    silo_row = "";
                }
                exit;
            }

            if (silo_stage == 2)
            {
                // Follow the opened sprite's shaft in GameMaker's local coordinate system.
                turn = hatch.image_angle * pi / 180;
                var along, into;
                along = clamp(silo_ticks / 66, 0, 1);
                dx = lerp(56, 174, along);
                dy = lerp(-88, 410, along);
                px = hatch.x + dx * cos(turn) + dy * sin(turn);
                py = hatch.y - dx * sin(turn) + dy * cos(turn);
                if (silo_ticks > 66)
                {
                    into = clamp((silo_ticks - 66) / 24, 0, 1);
                    px = lerp(px, hatch.x + 85, into);
                    py = lerp(py, hatch.y + 640, into);
                }
                if (collision_point(px, py, Collidable, 1, 0) != noone) silo_blocked += 1;
                silo_x = px;
                silo_y = py;
                event_user(1);
                if (silo_ticks == 90)
                {
                    silo_row = "shaft";
                    event_user(0);
                    silo_shot = "chamber";
                }
                if (silo_ticks >= 94)
                {
                    silo_stage += 1;
                    silo_ticks = 0;
                }
                exit;
            }

            if (silo_stage == 3)
            {
                target = instance_nearest(hatch.x + 85, hatch.y + 640, species);
                if (!instance_exists(target))
                {
                    silo_row = "missing_spawn";
                    event_user(0);
                    game_end();
                    exit;
                }
                px = target.x;
                py = target.y - 85;
                silo_x = px;
                silo_y = py;
                event_user(1);
                if (silo_ticks == 8)
                {
                    silo_row = "swimming";
                    event_user(0);
                    silo_shot = object_get_name(species);
                }
                silo_art_show = silo_ticks >= 10 && silo_ticks <= 14;
                if (silo_art_show)
                {
                    silo_art_sprite = object_get_sprite(silo_reference[silo_index]);
                    silo_art_live = target.sprite_index;
                    silo_art_frame = floor(target.image_index);
                    if (silo_ticks == 13) silo_shot = "compare_" + object_get_name(species);
                }
                if (silo_ticks >= 16)
                {
                    silo_ticks = 0;
                    silo_index += 1;
                    if (silo_index == 4)
                    {
                        silo_index = 0;
                        silo_stage = 4;
                    }
                }
                exit;
            }

            if (silo_stage == 4)
            {
                // Catch the four subjects in one step so unrelated bites and later casts cannot
                // disturb the boat inventory between scripted deposits.
                for (silo_index = 0; silo_index < 4; silo_index += 1)
                {
                    target = instance_find(silo_species[silo_index], 0);
                    if (!instance_exists(target))
                    {
                        silo_row = "missing_spawn";
                        event_user(0);
                        continue;
                    }
                    if (Lure.fish != target || target.state != 3)
                    {
                        if (Lure.fish > 0 && instance_exists(Lure.fish))
                        {
                            with (Lure.fish)
                            {
                                state = 2;
                                full_reset = 1;
                            }
                        }
                        Rod.state = 3;
                        Lure.state = 2;
                        Lure.fish = -1;
                        Lure.hookedFishSize = 0;
                        Lure.size = target.size;
                        if (target.display_size == 4 && !instance_exists(BigHook2))
                            instance_create(Lure.x, Lure.y, BigHook2);
                        Lure.canBite = 1;
                        with (target) event_user(7);
                    }
                    if (Lure.fish != target || target.state != 3)
                    {
                        silo_row = "bite_failed";
                        event_user(0);
                        continue;
                    }
                    silo_row = "bite";
                    event_user(0);
                    Rod.state = 0;
                    Lure.state = 0;
                    Lure.x = Boat.x - 20 + 12 * silo_index;
                    Lure.y = Boat.y;
                    target.x = Lure.x;
                    target.y = Lure.y;
                    with (target) fish_check_caught();
                    Lure.collision_with = Boat.id;
                    Lure.bounces = 6;
                    with (Lure) event_user(10);
                    silo_row = target.hasGivenExperience && target.onBoat && target.stuck && target.state == 4 ? "caught" : "catch_failed";
                    event_user(0);
                }

                silo_index = 0;
                if (silo_mode == 1)
                {
                    with (SaveManager) event_user(1);
                    silo_row = "saved";
                    event_user(0);
                }
                silo_stage = 5;
                silo_ticks = 0;
                exit;
            }

            if (silo_stage == 5 && silo_ticks == 6)
            {
                silo_shot = "boat";
                silo_stage = 6;
                silo_ticks = 0;
            }
            if (silo_stage == 6 && silo_ticks >= 6) game_end();
            """;
    }

    private static readonly string Snapshot = $$"""
        var species, slot, tab, caught, row, file, control, hatch, angle, px, py;
        species = silo_species[silo_index];
        slot = jamm_catalog_find(species);
        tab = jamm_catalog_area(species);
        caught = -99;
        if (slot >= 0 && tab >= 0) caught = CatalogData.count[tab, slot];
        row = silo_row + "," + room_get_name(room) + "," + string(Lure.x) + "," + string(Lure.y);
        row += "," + object_get_name(species) + "," + string(instance_number(species)) + "," + string(caught);
        row += "," + string(instance_exists(Boat) ? Boat.fish_count : 0) + "," + string(silo_blocked);
        control = instance_find({{HatchController}}, 0);
        hatch = control.hatch;
        angle = hatch.image_angle;
        px = hatch.x + lengthdir_x(129, angle) + lengthdir_x(220, angle - 90);
        py = hatch.y + lengthdir_y(129, angle) + lengthdir_y(220, angle - 90);
        row += "," + string(hatch.x) + "," + string(hatch.y) + "," + string(control.silo_progress) + "," + string(control.silo_glow);
        row += "," + string(collision_point(px, py, hatch, 1, 0) == noone);
        file = file_text_open_append("{{Report}}");
        file_text_write_string(file, row);
        file_text_writeln(file);
        file_text_close(file);
        show_debug_message("Silo scene: " + row);
        """;
}
