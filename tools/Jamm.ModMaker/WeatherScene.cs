using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class WeatherScene
{
    private const string Probe = "obj_jamm_bench_weather";
    private const string Controller = "obj_jamm_weather";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "weather";
    }

    internal static void Apply(UndertaleData data)
    {
        var world = data.Rooms.ByName("NewWorld")
            ?? throw new InvalidDataException("The dock scene requires the legacy NewWorld room.");
        var view = world.Views.FirstOrDefault()
            ?? throw new InvalidDataException("The dock scene requires a primary view in NewWorld.");
        // Frame the sky and both surface schools in one capture.
        view.ViewWidth = 1200;
        view.ViewHeight = 720;
        view.ViewX = 352;
        view.ViewY = 800;

        bool hasWeather = data.GameObjects.ByName(Controller) is not null
            && data.Scripts.ByName("jamm_weather_command") is not null
            && data.Scripts.ByName("jamm_weather_current") is not null
            && data.Scripts.ByName("jamm_weather_find") is not null;
        bool hasSonar = data.GameObjects.ByName("SonarPanel") is not null;
        bool hasToolData = data.GameObjects.ByName("ToolData") is not null;
        bool hasFish = data.GameObjects.ByName("Fish") is not null;
        bool hasCatalogData = data.GameObjects.ByName("CatalogData") is not null;
        bool hasSaveManager = data.GameObjects.ByName("SaveManager") is not null;
        bool hasGirl = data.GameObjects.ByName("Girl") is not null;
        bool hasBoat = data.GameObjects.ByName("Boat") is not null;
        bool hasCamera = data.GameObjects.ByName("Camera") is not null;
        bool hasAmbiance = data.GameObjects.ByName("AmbianceController") is not null;

        var probe = ModObjects.Create(data, Probe, persistent: false);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            global.jamm_weather_bench_night = false;
            bench_stage = -1;
            bench_total = 0;
            bench_start = current_time;
            bench_minute = 25;
            bench_second = 0;
            bench_shot = "";
            bench_daylight = 1;
            var log = file_text_open_write("jamm_bench_weather.csv");
            file_text_write_string(log, "capture,weather_id,active,night,vanilla_surface,weather_surface,fish_count,sonar_missing,parked,catalog,playing,seed,day,forecast,start,end,elapsed_ms,minute,lock_day,vanilla_night");
            file_text_writeln(log);
            file_text_close(log);
            """);

        string dismiss = string.Join('\n', new[] { "GameModeMenu", "GM_Classic", "GM_Realism", "Message_Start" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));

        string scenarioSetup = hasWeather
            ? $$"""
              bench_weather[0] = "clear";
              bench_capture[0] = "day";
              bench_night_flag[0] = 0;
              bench_total = 1;
              bench_weather_pref[0] = "rainy";
              bench_weather_pref[1] = "super-sunny";
              bench_weather_pref[2] = "windy";
              bench_weather_pref[3] = "fog";
              var jamm_w_seen = ds_map_create();
              for (var jamm_wi = 0; jamm_wi < 4; jamm_wi += 1)
              {
                  bench_weather_query = bench_weather_pref[jamm_wi];
                  bench_weather_lookup_result = -1;
                  with ({{Controller}}) other.bench_weather_lookup_result = jamm_weather_find(other.bench_weather_query);
                  var jamm_wt = bench_weather_lookup_result;
                  if (jamm_wt >= 0 && {{Controller}}.weather_species[jamm_wt] > 0)
                  {
                      ds_map_add(jamm_w_seen, jamm_wt, 1);
                      bench_weather[bench_total] = {{Controller}}.weather_key[jamm_wt];
                      bench_capture[bench_total] = bench_weather_pref[jamm_wi];
                      bench_night_flag[bench_total] = 0;
                      bench_total += 1;
                  }
              }
              for (var jamm_wt = 0; jamm_wt < {{Controller}}.weather_count; jamm_wt += 1)
              {
                  if ({{Controller}}.weather_species[jamm_wt] > 0 && !ds_map_exists(jamm_w_seen, jamm_wt))
                  {
                      bench_weather[bench_total] = {{Controller}}.weather_key[jamm_wt];
                      bench_capture[bench_total] = {{Controller}}.weather_short[jamm_wt];
                      bench_night_flag[bench_total] = 0;
                      bench_total += 1;
                  }
              }
              ds_map_destroy(jamm_w_seen);
              bench_weather[bench_total] = "clear";
              bench_capture[bench_total] = "clear-restored";
              bench_night_flag[bench_total] = 0;
              bench_total += 1;
              bench_weather_lookup_result = -1;
              with ({{Controller}}) other.bench_weather_lookup_result = jamm_weather_find("fog");
              var jamm_w_fog = bench_weather_lookup_result;
              if (jamm_w_fog >= 0)
              {
                  bench_weather[bench_total] = "fog";
                  bench_capture[bench_total] = "before-dusk";
                  bench_night_flag[bench_total] = 0;
                  bench_total += 1;
                  bench_weather[bench_total] = "fog";
                  bench_capture[bench_total] = "night";
                  bench_night_flag[bench_total] = 1;
                  bench_total += 1;
              }
              else
              {
                  bench_weather[bench_total] = "clear";
                  bench_capture[bench_total] = "night";
                  bench_night_flag[bench_total] = 1;
                  bench_total += 1;
              }
              jamm_weather_command(bench_weather[0]);
              """
            : """
              bench_weather[0] = "clear";
              bench_capture[0] = "day";
              bench_night_flag[0] = 0;
              bench_weather[1] = "clear";
              bench_capture[1] = "night";
              bench_night_flag[1] = 1;
              bench_total = 2;
              """;

        string sonarUnlock = hasToolData
            ? $$"""
              if (instance_exists(ToolData))
              {
                  var jamm_w_sslot = 1;
                  {{(hasSonar ? """
                  for (var jamm_w_ti = 0; jamm_w_ti < ToolData.nItems; jamm_w_ti += 1)
                  {
                      if (ToolData.item[jamm_w_ti] == SonarPanel) jamm_w_sslot = jamm_w_ti;
                  }
                  """ : "")}}
                  if (jamm_w_sslot >= 0 && jamm_w_sslot < ToolData.nItems) ToolData.unlocked[jamm_w_sslot] = 1;
              }
              """
            : "";

        string sonarEnable = hasSonar
            ? """
              if (!instance_exists(SonarPanel)) instance_create(0, 0, SonarPanel);
              if (instance_exists(SonarPanel))
              {
                  SonarPanel.active = 1;
                  SonarPanel.shouldDraw = 1;
              }
              view_visible[3] = 1;
              """
            : "view_visible[3] = 1;";

        string sonarPin = hasSonar
            ? """
              if (instance_exists(SonarPanel))
              {
                  SonarPanel.active = 1;
                  SonarPanel.shouldDraw = 1;
              }
              view_visible[3] = 1;
              """
            : "view_visible[3] = 1;";

        string readyGate = "";
        if (hasWeather)
        {
            string weatherFishWait = hasFish
                ? """
                  if (obj_jamm_weather.weather_species[jamm_w_target] > 0)
                  {
                      var jamm_w_fish = 0;
                      with (Fish)
                      {
                          if (variable_instance_exists(id, "jamm_weather_owner") && jamm_weather_owner == jamm_w_target)
                              jamm_w_fish += 1;
                      }
                      if (jamm_w_fish <= 0) exit;
                  }
                  """
                : "";
            readyGate = $$"""
                if (jamm_w_elapsed < 15000)
                {
                    var jamm_w_req = bench_weather[bench_stage];
                    var jamm_w_night = bench_night_flag[bench_stage];
                    if (jamm_w_req == "clear" || jamm_w_night)
                    {
                        if ({{Controller}}.weather_live >= 0) exit;
                    }
                    else
                    {
                        bench_weather_query = jamm_w_req;
                        bench_weather_lookup_result = -1;
                        with ({{Controller}}) other.bench_weather_lookup_result = jamm_weather_find(other.bench_weather_query);
                        var jamm_w_target = bench_weather_lookup_result;
                        if (jamm_w_target >= 0)
                        {
                            if ({{Controller}}.weather_live != jamm_w_target) exit;
                            {{weatherFishWait}}
                        }
                    }
                }
                """;
        }

        string readyGuards = string.Join(" || ", new[]
        {
            "room != NewWorld",
            hasSaveManager ? "!instance_exists(SaveManager) || !SaveManager.loaded" : "",
            hasWeather ? $"!instance_exists({Controller})" : ""
        }.Where(s => s.Length > 0));

        string girlInit = hasGirl
            ? $$"""
              if (instance_exists(Girl))
              {
                  Girl.x = 856;
                  Girl.hspeed = 0;
                  Girl.hspeed_base = 0;
                  {{(hasBoat ? "if (instance_exists(Boat)) Boat.x = Girl.x;" : "")}}
                  Girl.level = max(25, Girl.level);
                  Girl.nightfallUnlocked = 1;
                  Girl.startMessageShown = 1;
                  Girl.boatMessageShown = 1;
              }
              """
            : "";

        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), $$"""
            if ({{readyGuards}}) exit;
            bench_controller_ready = false;
            with ({{Controller}})
            {
                if (variable_instance_exists(id, "weather_count")
                    && variable_instance_exists(id, "weather_species")
                    && variable_instance_exists(id, "weather_key")
                    && variable_instance_exists(id, "weather_short")
                    && variable_instance_exists(id, "weather_live"))
                    other.bench_controller_ready = true;
            }
            if (!bench_controller_ready) exit;
            if (bench_stage < 0)
            {
                if (current_time - bench_start < 4500) exit;
                window_set_fullscreen(false);
                window_set_size(1200, 720);
                {{dismiss}}
                {{girlInit}}
                {{sonarUnlock}}
                {{sonarEnable}}
                bench_start = current_time;
                bench_stage = 0;
                {{scenarioSetup}}
            }
            if (bench_shot != "") exit;
            if (bench_stage >= bench_total)
            {
                game_end();
                exit;
            }
            var jamm_w_elapsed = current_time - bench_start;
            if (jamm_w_elapsed < 3500) exit;
            {{readyGate}}
            bench_shot = bench_capture[bench_stage];
            event_user(0);
            bench_stage += 1;
            bench_start = current_time;
            """);

        string cameraPin = hasCamera
            ? """
              if (instance_exists(Camera))
              {
                  Camera.x = 952;
                  Camera.y = 1160;
                  Camera.ORIGIN_X = Camera.x;
                  Camera.ORIGIN_Y = Camera.y;
                  Camera.atOrigin = 1;
              }
              """
            : "";
        string messageDismiss = data.GameObjects.ByName("Message") is not null
            ? "with (Message) instance_destroy();"
            : "";

        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), $$"""
            if (bench_stage < 0) exit;
            {{cameraPin}}
            {{messageDismiss}}
            {{sonarPin}}
            """);

        string nextWeatherCommand = hasWeather ? "jamm_weather_command(bench_weather[bench_stage]);" : "";
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), $$"""
            if (bench_shot == "") exit;
            screen_save("jamm_weather_" + bench_shot + ".png");
            bench_shot = "";
            if (bench_stage >= bench_total) exit;
            if (bench_night_flag[bench_stage])
            {
                bench_minute = 56;
                bench_daylight = 0;
                global.jamm_weather_bench_night = true;
            }
            else
            {
                bench_minute = 25;
                bench_daylight = 1;
                global.jamm_weather_bench_night = false;
            }
            {{nextWeatherCommand}}
            """);

        string fishScan = BuildFishScanGml(hasFish, hasWeather, hasCatalogData, hasCamera);
        string ambianceFields = hasAmbiance
            ? "string(AmbianceController.minute) + \",\" + string(AmbianceController.lockDay)"
            : "\"0,0\"";
        string nightExpr = data.Scripts.ByName("is_it_night") is not null
            ? "string(is_it_night())"
            : "string(global.jamm_weather_bench_night)";

        string weatherTelemetry = hasWeather
            ? $$"""
              with ({{Controller}})
              {
                  var key = "";
                  if (weather_plan >= 0) key = weather_key[weather_plan];
                  var voices = 0;
                  for (var i = 0; i < 2; i += 1)
                      if (weather_voice[i] >= 0 && audio_is_playing(weather_voice[i])) voices += 1;
                  var active_id = jamm_weather_current();
                  var weather_id = active_id;
                  if (weather_id == "") weather_id = other.bench_weather[other.bench_stage];
                  file_text_write_string(log, probe_stage + "," + weather_id + "," + active_id + "," + string(weather_night)
                      + "," + string(vanilla) + "," + string(custom) + "," + string(fish_count) + "," + string(sonar_missing)
                      + "," + string(ds_list_size(weather_parked))
                      + "," + string(catalog) + "," + string(voices) + "," + string(weather_seed)
                      + "," + string(weather_day) + "," + key + "," + string(weather_begin) + "," + string(weather_finish)
                      + "," + string(elapsed) + "," + {{ambianceFields}}
                      + "," + {{nightExpr}});
                  file_text_writeln(log);
              }
              """
            : $$"""
              var weather_id = bench_weather[bench_stage];
              file_text_write_string(log, probe_stage + "," + weather_id + ",," + string(global.jamm_weather_bench_night)
                  + "," + string(vanilla) + "," + string(custom) + "," + string(fish_count) + "," + string(sonar_missing)
                  + ",0," + string(catalog) + ",0,0,-1,,-1,-1," + string(elapsed) + "," + {{ambianceFields}}
                  + "," + {{nightExpr}});
              file_text_writeln(log);
              """;

        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User0, data), $$"""
            var vanilla = 0;
            var custom = 0;
            var catalog = 0;
            var fish_count = 0;
            var sonar_missing = 0;
            {{fishScan}}
            var probe_stage = bench_capture[bench_stage];
            var elapsed = current_time;
            var log = file_text_open_append("jamm_bench_weather.csv");
            {{weatherTelemetry}}
            file_text_close(log);
            """);

        if (data.Code.ByName("gml_Object_AmbianceController_Step_0") is { } ambiance)
        {
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
            string lightingPatch = data.GameObjects.ByName("LightingSystem") is not null
                ? $$"""

                  if (instance_exists({{Probe}}))
                  {
                      LightingSystem.day_coefficient = {{Probe}}.bench_daylight;
                      LightingSystem.overcast_coefficient = max(minimumOvercast, {{Probe}}.bench_daylight);
                  }
                  """
                : "";
            compiler.QueueCodeReplace(ambiance, body + lightingPatch);
        }

        if (data.Code.ByName("gml_Script_is_it_night") is not null)
            QueueNightOverride(data, compiler);

        ModObjects.PlaceInstance(data, "NewWorld", probe, first: true);
        ModObjects.Compile(compiler);
    }

    private static string BuildFishScanGml(bool hasFish, bool hasWeather, bool hasCatalogData, bool hasCamera)
    {
        if (!hasFish) return "";
        string baseFishInit = hasWeather ? $"var base_fish = {Controller}.weather_vanilla_fish;" : "";
        string camBandCheck = hasCamera
            ? "if (y >= 1294 && y <= 1950 && abs(x - Camera.x) <= 600 && visible)"
            : "if (y >= 1294 && y <= 1950 && visible)";
        string catalogCheck = hasCatalogData
            ? """
              if (instance_exists(CatalogData) && variable_instance_exists(CatalogData.id, "jamm_fish_names")
                  && ds_map_exists(CatalogData.jamm_fish_names, object_index))
                  catalog += 1;
              """
            : "";
        string weatherFishBranch = hasWeather
            ? $$"""
              if (variable_instance_exists(id, "jamm_weather_owner"))
              {
                  custom += 1;
                  {{catalogCheck}}
              }
              else if (!stayOutAtNight && TRUE_START_Y <= 1950 && ds_map_exists(base_fish, object_index))
                  vanilla += 1;
              """
            : """
              if (!stayOutAtNight && TRUE_START_Y <= 1950)
                  vanilla += 1;
              """;
        return $$"""
            {{baseFishInit}}
            with (Fish)
            {
                fish_count += 1;
                if (!variable_instance_exists(id, "s5")) sonar_missing += 1;
                {{camBandCheck}}
                {
                    {{weatherFishBranch}}
                }
            }
            """;
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
