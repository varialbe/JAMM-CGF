using System.Globalization;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class StressScene
{
    private const string Report = "jamm_bench_stress.csv";

    internal static void Apply(UndertaleData data, string? duration)
    {
        int seconds = 180;
        if (duration is not null && (!int.TryParse(duration, NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
            || seconds < 180 || seconds > 3600))
            throw new ArgumentException("Set the stress duration to a whole number of seconds from 180 to 3600.");
        bool hasPerf = data.GameObjects.ByName("obj_jamm_perf") is not null;
        foreach (string name in new[] { "obj_jamm_weather", "Bo", "Gallina", "Skrimp", "Cowfish", "Grumper", "Fish" })
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"The stress scene needs '{name}'. Merge Weather Pack and the fish content package before instrumenting.");

        var probe = ModObjects.Create(data, "obj_jamm_bench_stress", persistent: false);
        var compiler = new CompileGroup(data);
        void Queue(EventType type, uint subtype, string gml) =>
            compiler.QueueCodeReplace(probe.EventHandlerFor(type, subtype, data), gml);
        string dismiss = string.Join('\n', new[] { "GameModeMenu", "GM_Classic", "GM_Realism", "Message_Start" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));
        Queue(EventType.Create, 0, """
            stress_start = -1;
            stress_next = 0;
            stress_restore_at = 0;
            stress_cycles = 0;
            stress_hits = 0;
            stress_reactivated = 0;
            stress_initial_bo = 0;
            stress_unavailable = 0;
            stress_skip_start = 0;
            stress_parked = ds_list_create();
            stress_hooked = noone;
            stress_attacker = noone;
            stress_offscreen = noone;
            """);
        Queue(EventType.Destroy, 0, "ds_list_destroy(stress_parked);");
        Queue(EventType.Step, (uint)EventSubtypeStep.BeginStep, $$"""
            if (!instance_exists(SaveManager) || !instance_exists(Girl) || !instance_exists(Lure)) exit;
            SaveManager.saving_locked = 1;
            if (!SaveManager.loaded || SaveManager.loadingFish || !instance_exists(obj_jamm_weather) {{(hasPerf ? "|| !instance_exists(obj_jamm_perf)" : "")}}) exit;
            if (stress_start < 0)
            {
                {{dismiss}}
                random_set_seed(9021);
                Girl.x = 1100;
                Girl.level = max(Girl.level, 25);
                Girl.boatMessageShown = 1;
                Girl.startMessageShown = 1;
                {{(hasPerf ? "global.jamm_perf_mode = true;\nglobal.jamm_perf_interval = 4;\nstress_skip_start = global.jamm_perf_skipped;" : "stress_skip_start = 0;")}}
                stress_initial_bo = instance_number(Bo);
                // Keep a control Bo outside the weather's surface band for repeated deactivate/activate checks.
                with (instance_create(1600, 2300, Bo)) base_lunge_mod = 1;
                // Keep the legacy bite handler's other type references available in the unguarded control.
                if (!instance_exists(Gallina))
                    with (instance_create(1600, 2300, Gallina)) base_lunge_mod = 1;
                if (!instance_exists(Skrimp))
                    with (instance_create(1600, 2300, Skrimp)) base_lunge_mod = 1;
                // Rod seeds the lunge multiplier for existing fish. These fish can step before Rod sees them.
                stress_attacker = instance_create(1650, 1380, Cowfish);
                with (stress_attacker) base_lunge_mod = 1;
                for (var school = 0; school < 24; school += 1)
                    with (instance_create(1460 + (school * 22), 1500 + ((school mod 4) * 38), Grumper)) base_lunge_mod = 1;
                stress_offscreen = instance_create(24000, 1500, Grumper);
                with (stress_offscreen) base_lunge_mod = 1;
                global.jamm_weather_bench_night = false;
                jamm_weather_command("super-sunny");
                show_debug_message("JAMM stress started; initial active Bo: " + string(stress_initial_bo));
                stress_start = current_time;
                stress_next = current_time + 3000;
            }

            if (instance_exists(stress_offscreen)) stress_offscreen.state = 12;
            if (stress_restore_at > 0 && current_time >= stress_restore_at)
            {
                for (var i = 0; i < ds_list_size(stress_parked); i += 1)
                {
                    var member = ds_list_find_value(stress_parked, i);
                    instance_activate_object(member);
                    if (instance_exists(member)) stress_reactivated += 1;
                }
                ds_list_clear(stress_parked);
                stress_restore_at = 0;
            }
            if (current_time - stress_start >= {{(seconds * 1000).ToString(CultureInfo.InvariantCulture)}} && stress_restore_at == 0)
            {
                var file = file_text_open_write("{{Report}}");
                file_text_write_string(file, "elapsed_ms,cycles,school_bites,initial_bo,bo_absent,reactivations,perf_hooks,perf,perf_skipped,fish,weather");
                file_text_writeln(file);
                file_text_write_string(file, string(current_time - stress_start) + "," + string(stress_cycles) + ","
                    + string(stress_hits) + "," + string(stress_initial_bo) + ","
                    + string(stress_unavailable) + "," + string(stress_reactivated) + "," + string({{(hasPerf ? "1" : "0")}})
                    + "," + {{(hasPerf ? "string(global.jamm_perf_mode)" : "\"0\"")}}
                    + "," + {{(hasPerf ? "string(global.jamm_perf_skipped - stress_skip_start)" : "\"0\"")}}
                    + "," + string(instance_number(Fish))
                    + "," + jamm_weather_current());
                file_text_writeln(file);
                file_text_close(file);
                game_end();
                exit;
            }
            if (current_time < stress_next || stress_restore_at > 0 || !instance_exists(stress_attacker)) exit;
            stress_next = current_time + 5000;
            event_user(0);
            """);
        Queue(EventType.Other, (uint)EventSubtypeOther.User0, """
            // Reset the fixture's hook before selecting an active small member of the sunny school.
            if (instance_exists(stress_hooked))
                if (stress_hooked.state == 3) stress_hooked.state = 0;
            stress_hooked = noone;
            with (Fish)
            {
                if (state == 0 && size == 1 && variable_instance_exists(id, "jamm_weather_owner"))
                {
                    other.stress_hooked = id;
                    break;
                }
            }
            if (!instance_exists(stress_hooked)) exit;

            with (Bo)
            {
                ds_list_add(other.stress_parked, id);
                instance_deactivate_object(id);
            }
            if (!instance_exists(Bo)) stress_unavailable += 1;
            stress_restore_at = current_time + 750;
            stress_cycles += 1;
            if (stress_cycles == 1)
                show_debug_message("JAMM stress first attack; active Bo: " + string(instance_number(Bo)));

            Lure.x = 1640;
            Lure.y = 1380;
            Lure.state = 2;
            Lure.near_the_water = 1;
            Lure.in_the_water = 1;
            Lure.canBite = 1;
            Lure.hasEgg = 0;
            Lure.hasEquipment = 0;
            Lure.size = 3;
            Lure.fish = stress_hooked;
            Lure.hookedFishSize = stress_hooked.size;
            stress_hooked.x = Lure.x;
            stress_hooked.y = Lure.y;
            stress_hooked.life = stress_hooked.MAX_LIFE;
            stress_hooked.state = 3;
            var before = stress_hooked.life;
            with (stress_attacker)
            {
                x = Lure.x + lengthdir_x(16, Lure.image_angle - 90);
                y = Lure.y + lengthdir_y(16, Lure.image_angle - 90);
                state = 1;
                base_lunge_mod = 1;
                fish_default_lunge();
            }
            if (instance_exists(stress_hooked))
                if (stress_hooked.life < before) stress_hits += 1;
            """);
        Queue(EventType.Step, (uint)EventSubtypeStep.EndStep, """
            if (stress_start < 0 || !instance_exists(Camera)) exit;
            Camera.x = 1510;
            Camera.y = 1230;
            Camera.ORIGIN_X = Camera.x;
            Camera.ORIGIN_Y = Camera.y;
            Camera.atOrigin = 1;
            with (Message) instance_destroy();
            """);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);
        WeatherScene.QueueNightOverride(data, compiler);
        ModObjects.Compile(compiler);
    }
}
