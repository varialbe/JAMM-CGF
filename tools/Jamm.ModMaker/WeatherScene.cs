using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class WeatherScene
{
    private const string Probe = "obj_jamm_bench_weather";
    private const string Controller = "obj_jamm_weather";
    private static readonly string[] Scenarios = ["clear", "super-sunny", "rainy", "windy", "fog", "clear", "fog", "fog"];
    private static readonly string[] Captures = ["day", "super-sunny", "rainy", "windy", "fog", "clear-restored", "before-dusk", "night"];

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "weather";
    }

    internal static void Apply(UndertaleData data)
    {
        if (data.GameObjects.ByName(Controller) is null)
            throw new InvalidDataException("The weather scene needs a merged Weather Pack. Build a profile containing the package before instrumenting.");

        var world = data.Rooms.ByName("NewWorld")
            ?? throw new InvalidDataException("The dock scene requires the legacy NewWorld room.");
        var view = world.Views[0];
        // Frame the sky and both surface schools in one capture.
        view.ViewWidth = 1200;
        view.ViewHeight = 720;
        view.ViewX = 352;
        view.ViewY = 800;

        var probe = ModObjects.Create(data, Probe, persistent: false);
        var compiler = new CompileGroup(data);
        string setup = string.Join('\n', Scenarios.Select((id, i) => $$"""
            bench_weather[{{i}}] = "{{id}}";
            bench_capture[{{i}}] = "{{Captures[i]}}";
            """));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), $$"""
            {{setup}}
            global.jamm_weather_bench_night = false;
            bench_stage = -1;
            bench_start = current_time;
            bench_minute = 25;
            bench_second = 0;
            bench_shot = "";
            bench_daylight = 1;
            var log = file_text_open_write("jamm_bench_weather.csv");
            file_text_write_string(log, "capture,active,night,vanilla_surface,weather_surface,parked,catalog,playing,seed,day,forecast,start,end,elapsed_ms,minute,lock_day,vanilla_night");
            file_text_writeln(log);
            file_text_close(log);
            """);
        string dismiss = string.Join('\n', new[] { "GameModeMenu", "GM_Classic", "GM_Realism", "Message_Start" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), $$"""
            if (room != NewWorld || !instance_exists(SaveManager) || !SaveManager.loaded
                || !instance_exists({{Controller}})) exit;
            if (bench_stage < 0)
            {
                if (current_time - bench_start < 4500) exit;
                window_set_fullscreen(false);
                window_set_size(1200, 720);
                {{dismiss}}
                Girl.x = 856;
                Girl.hspeed = 0;
                Girl.hspeed_base = 0;
                if (instance_exists(Boat)) Boat.x = Girl.x;
                Girl.level = max(25, Girl.level);
                Girl.nightfallUnlocked = 1;
                Girl.startMessageShown = 1;
                Girl.boatMessageShown = 1;
                bench_start = current_time;
                bench_stage = 0;
                jamm_weather_command(bench_weather[0]);
            }
            if (current_time - bench_start < 5900) exit;
            if (bench_stage >= {{Scenarios.Length}})
            {
                game_end();
                exit;
            }
            bench_shot = bench_capture[bench_stage];
            event_user(0);
            bench_stage += 1;
            bench_start = current_time;
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), """
            if (bench_stage < 0 || !instance_exists(Camera)) exit;
            Camera.x = 952;
            Camera.y = 1160;
            Camera.ORIGIN_X = Camera.x;
            Camera.ORIGIN_Y = Camera.y;
            Camera.atOrigin = 1;
            with (Message) instance_destroy();
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), $$"""
            if (bench_shot == "") exit;
            screen_save("jamm_weather_" + bench_shot + ".png");
            bench_shot = "";
            if (bench_stage >= {{Scenarios.Length}}) exit;
            if (bench_stage == {{Scenarios.Length - 1}})
            {
                bench_minute = 56;
                bench_daylight = 0;
                global.jamm_weather_bench_night = true;
            }
            jamm_weather_command(bench_weather[bench_stage]);
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User0, data), $$"""
            var vanilla = 0;
            var custom = 0;
            var catalog = 0;
            var base_fish = {{Controller}}.weather_vanilla_fish;
            with (Fish)
            {
                if (y < 1294 || y > 1950 || abs(x - Camera.x) > 600 || !visible) continue;
                if (variable_instance_exists(id, "jamm_weather_owner"))
                {
                    custom += 1;
                    if (ds_map_exists(CatalogData.jamm_fish_names, object_index)) catalog += 1;
                }
                else if (!stayOutAtNight && TRUE_START_Y <= 1950 && ds_map_exists(base_fish, object_index))
                    vanilla += 1;
            }
            var probe_stage = bench_capture[bench_stage];
            var elapsed = current_time;
            var log = file_text_open_append("jamm_bench_weather.csv");
            with ({{Controller}})
            {
                var key = "";
                if (weather_plan >= 0) key = weather_key[weather_plan];
                var voices = 0;
                for (var i = 0; i < 2; i += 1)
                    if (weather_voice[i] >= 0 && audio_is_playing(weather_voice[i])) voices += 1;
                file_text_write_string(log, probe_stage + "," + jamm_weather_current() + "," + string(weather_night)
                    + "," + string(vanilla) + "," + string(custom) + "," + string(ds_list_size(weather_parked))
                    + "," + string(catalog) + "," + string(voices) + "," + string(weather_seed)
                    + "," + string(weather_day) + "," + key + "," + string(weather_begin) + "," + string(weather_finish)
                    + "," + string(elapsed) + "," + string(AmbianceController.minute) + "," + string(AmbianceController.lockDay)
                    + "," + string(is_it_night()));
                file_text_writeln(log);
            }
            file_text_close(log);
            """);
        var ambiance = data.Code.ByName("gml_Object_AmbianceController_Step_0")
            ?? throw new InvalidDataException("The dock scene requires the legacy AmbianceController Step event.");
        string body = ModObjects.Decompile(data, ambiance);
        const string anchor = "isStorming = is_it_storming();";
        int offset = body.IndexOf(anchor, StringComparison.Ordinal);
        if (offset < 0 || body.IndexOf(anchor, offset + anchor.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("The dock scene could not locate the legacy clock anchor.");
        body = body.Insert(offset, $$"""
            if (instance_exists({{Probe}}))
            {
                minute = {{Probe}}.bench_minute;
                second = {{Probe}}.bench_second;
                lockDay = 0;
            }

            """);
        compiler.QueueCodeReplace(ambiance, body + $$"""

            if (instance_exists({{Probe}}))
            {
                LightingSystem.day_coefficient = {{Probe}}.bench_daylight;
                LightingSystem.overcast_coefficient = max(minimumOvercast, {{Probe}}.bench_daylight);
            }
            """);
        QueueNightOverride(data, compiler);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: true);
        ModObjects.Compile(compiler);
    }

    internal static void QueueNightOverride(UndertaleData data, CompileGroup compiler)
    {
        // Legacy gear and daylight locks use the same phase as the benchmark weather.
        var night = data.Code.ByName("gml_Script_is_it_night")
            ?? throw new InvalidDataException("The weather benchmark requires the legacy day/night predicate.");
        compiler.QueueCodeReplace(night, """
            if (room == NewWorld && variable_global_exists("jamm_weather_bench_night"))
                return global.jamm_weather_bench_night;
            """ + "\n" + ModObjects.Decompile(data, night));
    }
}
