using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class SiloScene
{
    private const string Probe = "obj_jamm_bench_silo";
    private const string Report = "jamm_bench_silo.csv";
    private static readonly string[] FishNames = ["veyl", "radik", "ferrex", "garrusk"];

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
        foreach (string name in species.Concat(["MissileSilo", "BoatData", "Lure", "Collidable", "CatalogData", "BigHook2", "Camera"]))
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"The silo scene needs '{name}'. Merge the Silo ModKit package before instrumenting.");

        var probe = ModObjects.Create(data, Probe, persistent: true);
        var compiler = new CompileGroup(data);
        string setup = string.Join('\n', species.Select((name, i) => $"silo_species[{i}] = {name};"));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), $$"""
            {{setup}}
            silo_mode = {{(mode == "reload" ? 2 : mode == "save" ? 1 : 0)}};
            silo_stage = 0;
            silo_ticks = 0;
            silo_index = 0;
            silo_shot = "";
            silo_row = "";
            silo_blocked = 0;
            var file = file_text_open_write("{{Report}}");
            file_text_write_string(file, "stage,room,x,y,object,population,fishalog_count,fish_in_boat,blocked_samples");
            file_text_writeln(file);
            file_text_close(file);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), Steps(data));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), """
            if (room != NewWorld || silo_stage == 0 || !instance_exists(Camera)) exit;

            // Wine's long frames can overshoot the native camera's interpolation.
            // Frame the scripted lure or boat directly while capturing the scene.
            if (silo_stage < 5)
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
            if (room != NewWorld || !instance_exists(SaveManager) || !SaveManager.loaded || !instance_exists(MissileSilo)) exit;
            var hatch, vessel, species, target, dx, dy, px, py, turn;
            hatch = instance_find(MissileSilo, 0);
            species = silo_species[silo_index];
            silo_ticks += 1;

            if (silo_stage == 0)
            {
                if (silo_ticks < 10 || !variable_instance_exists(CatalogData.id, "jamm_fish_names")) exit;
                window_set_fullscreen(false);
                window_set_size(1200, 720);
                {{dismiss}}
                if (silo_mode == 2)
                {
                    for (silo_index = 0; silo_index < 4; silo_index += 1)
                    {
                        silo_row = "reload";
                        event_user(0);
                    }
                    silo_index = 0;
                    silo_shot = "reload";
                    silo_stage = 6;
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
                silo_row = hatch.canOpen && hatch.sprite_index != object_get_sprite(MissileSilo) ? "opened" : "closed";
                event_user(0);
                silo_stage = 1;
                silo_ticks = 0;
            }

            if (silo_stage == 1 || silo_stage == 2)
            {
                // Follow the opened sprite's shaft in GameMaker's local coordinate system.
                turn = hatch.image_angle * pi / 180;
                var along, into;
                along = silo_stage == 1 ? 0 : clamp(silo_ticks / 66, 0, 1);
                dx = lerp(56, 174, along);
                dy = lerp(-88, 410, along);
                px = hatch.x + dx * cos(turn) + dy * sin(turn);
                py = hatch.y - dx * sin(turn) + dy * cos(turn);
                if (silo_stage == 2 && silo_ticks > 66)
                {
                    into = clamp((silo_ticks - 66) / 24, 0, 1);
                    px = lerp(px, hatch.x + 85, into);
                    py = lerp(py, hatch.y + 640, into);
                }
                if (silo_stage == 2 && collision_point(px, py, Collidable, 1, 0) != noone) silo_blocked += 1;
                silo_x = px;
                silo_y = py;
                event_user(1);
                if (silo_stage == 1 && silo_ticks == 8) silo_shot = "opened";
                if (silo_stage == 2 && silo_ticks == 90)
                {
                    silo_row = "shaft";
                    event_user(0);
                    silo_shot = "chamber";
                }
                if ((silo_stage == 1 && silo_ticks >= 14) || (silo_stage == 2 && silo_ticks >= 94))
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
                if (silo_ticks >= 14)
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
        var species, slot, tab, caught, row, file;
        species = silo_species[silo_index];
        slot = jamm_catalog_find(species);
        tab = jamm_catalog_area(species);
        caught = -99;
        if (slot >= 0 && tab >= 0) caught = CatalogData.count[tab, slot];
        row = silo_row + "," + room_get_name(room) + "," + string(Lure.x) + "," + string(Lure.y);
        row += "," + object_get_name(species) + "," + string(instance_number(species)) + "," + string(caught);
        row += "," + string(instance_exists(Boat) ? Boat.fish_count : 0) + "," + string(silo_blocked);
        file = file_text_open_append("{{Report}}");
        file_text_write_string(file, row);
        file_text_writeln(file);
        file_text_close(file);
        show_debug_message("Silo scene: " + row);
        """;
}
