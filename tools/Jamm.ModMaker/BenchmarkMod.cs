using System.Globalization;
using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Supported deterministic benchmark scenes.</summary>
public enum BenchmarkScene
{
    /// <summary>Player on the dock looking at shallow water with sonar off.</summary>
    Dock,
    /// <summary>Player on the dock with sonar viewport active.</summary>
    Sonar,
    /// <summary>Boat and camera positioned in deep trench water with sonar off.</summary>
    Deepwater
}

/// <summary>Configuration baked into the benchmark instrumentation controller (overridable at runtime via <c>jamm_bench.ini</c>).</summary>
public sealed record BenchmarkOptions(
    BenchmarkScene Scene = BenchmarkScene.Dock,
    double WarmupSeconds = 5.0,
    double CaptureSeconds = 15.0,
    int MaxSamples = 50000,
    string? OutputFile = null,
    bool AutoExit = true,
    int Seed = 1337,
    double DockX = 815.0,
    double DeepX = 21000.0,
    double DeepY = 7200.0,
    int BoatSlot = 7,
    bool DeepSonar = false,
    bool ShowHud = false,
    bool Profile = false,
    bool ProfileScripts = false,
    int TraceFish = 0,
    bool FishGrid = false);

/// <summary>
/// Test-only identical instrumentation pass for local frame-time benchmarking.
/// Adds a single persistent controller (<see cref="ObjectName"/>) that measures frame-to-frame
/// <c>get_timer()</c> across <c>DrawGUIEnd</c> and active <c>BeginStep</c>-to-<c>DrawGUIEnd</c>
/// game-loop duration, warms up, batches samples in memory during a bounded recording, writes one
/// CSV report per run in the runner sandbox, locks <c>SaveManager</c> so no user saves are read or
/// modified, and applies deterministic scene setup for dock, sonar, and deepwater.
/// </summary>
public static partial class BenchmarkMod
{
    /// <summary>Name of the persistent benchmark controller object.</summary>
    public const string ObjectName = "obj_jamm_bench";

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeFileNameRegex();

    /// <summary>Parses a scene name (<c>dock</c>, <c>sonar</c>, or <c>deepwater</c>).</summary>
    public static BenchmarkScene ParseScene(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Scene must be 'dock', 'sonar', or 'deepwater'.");

        return value.Trim().ToLowerInvariant() switch
        {
            "dock" => BenchmarkScene.Dock,
            "sonar" => BenchmarkScene.Sonar,
            "deepwater" => BenchmarkScene.Deepwater,
            _ => throw new ArgumentException($"Unknown benchmark scene '{value}'. Choose 'dock', 'sonar', or 'deepwater'.")
        };
    }

    /// <summary>Formats a <see cref="BenchmarkScene"/> as its canonical lowercase identifier.</summary>
    public static string SceneName(BenchmarkScene scene) => scene switch
    {
        BenchmarkScene.Dock => "dock",
        BenchmarkScene.Sonar => "sonar",
        BenchmarkScene.Deepwater => "deepwater",
        _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unsupported benchmark scene.")
    };

    /// <summary>Applies the benchmark controller pass to an original or already-modded in-memory archive.</summary>
    public static void Apply(UndertaleData data, BenchmarkOptions? options = null, Action<string>? decision = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new BenchmarkOptions();
        ValidateOptions(options);

        if (options.FishGrid)
        {
            string result = FishGrid.Apply(data);
            decision?.Invoke(result);
        }

        if (options.TraceFish > 0) FishTrace.Instrument(data, options.Seed);
        if (options.Profile)
            GmlCpuProfiler.Instrument(data, options.ProfileScripts);

        if (data.Rooms.ByName("NewWorld") is null)
            throw new InvalidDataException("Required room 'NewWorld' was not found: use a Cat Goes Fishing legacy archive (original or modded).");

        var bench = ModObjects.Create(data, ObjectName, persistent: true);
        // Run DrawGUIEnd after normal draws/GUI and just before SmoothMod's Depth=-1000000 freeze.
        bench.Depth = -999999;

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Create, 0u, data), CreateGml(options));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Destroy, 0u, data), DestroyGml(options.TraceFish > 0));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.RoomStart, data), RoomStartGml(data));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), BeginStepGml(data, options.TraceFish > 0));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), EndStepGml(data));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, data), DrawGuiGml(data));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), DrawGuiEndGml(options));
        ModObjects.Compile(compiler);

        ModObjects.PlaceInstance(data, "NewWorld", bench, first: true);
        if (data.Rooms.ByName("room11") is not null)
            ModObjects.PlaceInstance(data, "room11", bench, first: true);
    }

    private static void ValidateOptions(BenchmarkOptions options)
    {
        _ = SceneName(options.Scene);
        if (double.IsNaN(options.WarmupSeconds) || double.IsInfinity(options.WarmupSeconds) || options.WarmupSeconds < 0 || options.WarmupSeconds > 3600)
            throw new ArgumentOutOfRangeException(nameof(options), "WarmupSeconds must be between 0 and 3600.");
        if (double.IsNaN(options.CaptureSeconds) || double.IsInfinity(options.CaptureSeconds) || options.CaptureSeconds <= 0 || options.CaptureSeconds > 3600)
            throw new ArgumentOutOfRangeException(nameof(options), "CaptureSeconds must be > 0 and <= 3600.");
        if (options.MaxSamples < 1 || options.MaxSamples > 200000)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSamples must be between 1 and 200000.");
        if (options.TraceFish is < 0 or > 200000)
            throw new ArgumentOutOfRangeException(nameof(options), "TraceFish must be between 0 (disabled) and 200000.");
        if (options.BoatSlot < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "BoatSlot must be >= 1.");
        if (double.IsNaN(options.DockX) || double.IsInfinity(options.DockX)
            || double.IsNaN(options.DeepX) || double.IsInfinity(options.DeepX)
            || double.IsNaN(options.DeepY) || double.IsInfinity(options.DeepY))
            throw new ArgumentException("Scene coordinates must be finite numbers.");
        if (options.OutputFile is not null)
            ValidateOutputFileName(options.OutputFile);
    }

    /// <summary>Validates a relative sandbox filename so benchmark output can never touch real saves or archives.</summary>
    public static void ValidateOutputFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !SafeFileNameRegex().IsMatch(fileName)
            || fileName.Contains("..", StringComparison.Ordinal)
            || fileName.StartsWith("sdata", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "meta.ini", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".win", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid benchmark output filename '{fileName}'. Use a simple relative filename (e.g. 'jamm_bench_dock.csv') that is not a save or .win archive.");
        }
    }

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string CreateGml(BenchmarkOptions o)
    {
        string scene = SceneName(o.Scene);
        string defaultOutput = o.OutputFile ?? $"jamm_bench_{scene}.csv";
        int customOutput = o.OutputFile is null ? 0 : 1;

        return $$"""
            if (instance_number(object_index) > 1)
            {
                instance_destroy();
                exit;
            }
            persistent = true;
            jamm_b_owner = true;

            jamm_b_scene = "{{scene}}";
            jamm_b_scene_req = jamm_b_scene;
            jamm_b_warn = "";
            jamm_b_warmup_sec = {{Num(o.WarmupSeconds)}};
            jamm_b_capture_sec = {{Num(o.CaptureSeconds)}};
            jamm_b_max_samples = {{o.MaxSamples}};
            jamm_b_output_file = "{{defaultOutput}}";
            jamm_b_output_custom = {{customOutput}};
            jamm_b_auto_exit = {{(o.AutoExit ? 1 : 0)}};
            jamm_b_seed = {{o.Seed}};
            jamm_b_dock_x = {{Num(o.DockX)}};
            jamm_b_deep_x = {{Num(o.DeepX)}};
            jamm_b_deep_y = {{Num(o.DeepY)}};
            jamm_b_boat_slot = {{o.BoatSlot}};
            jamm_b_deep_sonar = {{(o.DeepSonar ? 1 : 0)}};
            jamm_b_show_hud = {{(o.ShowHud ? 1 : 0)}};
            {{(o.Profile ? "jamm_b_profile = 1;" : "")}}

            if (file_exists("jamm_bench.ini"))
            {
                ini_open("jamm_bench.ini");
                jamm_b_scene = string_lower(ini_read_string("Bench", "Scene", jamm_b_scene));
                jamm_b_scene_req = jamm_b_scene;
                jamm_b_warmup_sec = clamp(ini_read_real("Bench", "WarmupSeconds", jamm_b_warmup_sec), 0, 3600);
                jamm_b_capture_sec = clamp(ini_read_real("Bench", "CaptureSeconds", jamm_b_capture_sec), 0.1, 3600);
                jamm_b_max_samples = clamp(round(ini_read_real("Bench", "MaxSamples", jamm_b_max_samples)), 1, 200000);
                if (ini_key_exists("Bench", "OutputFile"))
                {
                    jamm_b_output_file = ini_read_string("Bench", "OutputFile", jamm_b_output_file);
                    jamm_b_output_custom = 1;
                }
                jamm_b_auto_exit = ini_read_real("Bench", "AutoExit", jamm_b_auto_exit) != 0;
                jamm_b_seed = round(ini_read_real("Bench", "Seed", jamm_b_seed));
                jamm_b_dock_x = ini_read_real("Bench", "DockX", jamm_b_dock_x);
                jamm_b_deep_x = ini_read_real("Bench", "DeepX", jamm_b_deep_x);
                jamm_b_deep_y = ini_read_real("Bench", "DeepY", jamm_b_deep_y);
                jamm_b_boat_slot = max(1, round(ini_read_real("Bench", "BoatSlot", jamm_b_boat_slot)));
                jamm_b_deep_sonar = ini_read_real("Bench", "DeepSonar", jamm_b_deep_sonar) != 0;
                jamm_b_show_hud = ini_read_real("Bench", "ShowHud", jamm_b_show_hud) != 0;
                ini_close();
            }

            if (jamm_b_scene != "dock" && jamm_b_scene != "sonar" && jamm_b_scene != "deepwater")
            {
                jamm_b_warn = "unknown_scene_" + jamm_b_scene + "_fallback_dock";
                jamm_b_scene = "dock";
            }
            if (!jamm_b_output_custom)
                jamm_b_output_file = "jamm_bench_" + jamm_b_scene + ".csv";
            if (jamm_b_output_file == "" || jamm_b_output_file == "meta.ini" || string_pos("sdata", string_lower(jamm_b_output_file)) == 1 || string_pos(".win", string_lower(jamm_b_output_file)) > 0 || string_pos("..", jamm_b_output_file) > 0 || string_pos("/", jamm_b_output_file) > 0 || string_pos(chr(92), jamm_b_output_file) > 0 || string_pos(":", jamm_b_output_file) > 0)
                jamm_b_output_file = "jamm_bench_" + jamm_b_scene + ".csv";

            {{(o.TraceFish > 0 ? FishTrace.Create : "")}}
            jamm_b_frame_list = ds_list_create();
            jamm_b_loop_list = ds_list_create();
            jamm_b_state = 0;
            jamm_b_init_ticks = 0;
            jamm_b_scene_applied = 0;
            jamm_b_deep_light = -1;
            jamm_b_room_jump_done = 0;
            jamm_b_step_t = 0;
            jamm_b_has_step = 0;
            jamm_b_prev_gui_t = get_timer();
            jamm_b_phase_start = jamm_b_prev_gui_t;
            jamm_b_capture_start = jamm_b_prev_gui_t;
            jamm_b_warmup_frames = 0;
            jamm_b_step_samples = 0;
            jamm_b_summary_line = "";
            {{(o.Profile ? "global.__jamm_prof_us = ds_map_create();\nglobal.__jamm_prof_n = ds_map_create();\nglobal.__jamm_prof_on = 0;" : "")}}
            """;
    }

    private static string DestroyGml(bool trace) => $$"""
        if (variable_instance_exists(id, "jamm_b_owner") && jamm_b_owner)
        {
            {{(trace ? "buffer_delete(jamm_trace_buffer);" : "")}}
            ds_list_destroy(jamm_b_frame_list);
            ds_list_destroy(jamm_b_loop_list);
        }
        """;

    private static string LockSaveManagerGml(UndertaleData data, bool clearSlotFlags)
    {
        if (data.GameObjects.ByName("SaveManager") is null) return "";
        string clear = clearSlotFlags ? " SaveManager.saveflag = 0; SaveManager.loadflag = 0;" : "";
        return $$"""
            if (instance_exists(SaveManager))
            {
                SaveManager.saving_locked = 1;{{clear}}
            }
            """;
    }

    private static string DismissMenusGml(UndertaleData data)
    {
        var lines = new List<string>();
        foreach (string menuObj in new[] { "GameModeMenu", "GM_Classic", "GM_Realism" })
        {
            if (data.GameObjects.ByName(menuObj) is not null)
                lines.Add($"if (instance_exists({menuObj})) with ({menuObj}) instance_destroy();");
        }
        if (data.GameObjects.ByName("Rod") is not null)
            lines.Add("if (instance_exists(Rod)) Rod.swing_locked = 0;");
        if (data.GameObjects.ByName("Boat") is not null)
            lines.Add("if (instance_exists(Boat)) Boat.moveLocked = 0;");
        return string.Join("\n", lines);
    }

    private static string RoomStartGml(UndertaleData data) => $$"""
        {{LockSaveManagerGml(data, clearSlotFlags: true)}}
        if (room != NewWorld && !jamm_b_room_jump_done)
        {
            jamm_b_room_jump_done = 1;
            room_goto(NewWorld);
        }
        """;

    private static string BeginStepGml(UndertaleData data, bool trace)
    {
        bool hasSaveManager = data.GameObjects.ByName("SaveManager") is not null;
        string saveLock = hasSaveManager
            ? """
              if (instance_exists(SaveManager))
              {
                  SaveManager.saving_locked = 1;
                  if (jamm_b_state == 0)
                  {
                      SaveManager.saveflag = 0;
                      SaveManager.loadflag = 0;
                  }
              }
              """
            : "";
        string waitSaveLoaded = hasSaveManager
            ? "if (instance_exists(SaveManager) && variable_instance_exists(SaveManager.id, \"loaded\") && !SaveManager.loaded) exit;"
            : "";

        return $$"""
            {{(trace ? "global.jamm_trace_tick += 1;" : "")}}
            jamm_b_step_t = get_timer();
            jamm_b_has_step = 1;
            if (jamm_b_state == 2) jamm_b_step_samples += 1;
            {{saveLock}}
            if (room != NewWorld)
            {
                if (!jamm_b_room_jump_done)
                {
                    jamm_b_room_jump_done = 1;
                    room_goto(NewWorld);
                }
                exit;
            }
            if (jamm_b_state == 0)
            {
                jamm_b_init_ticks += 1;
                if (jamm_b_init_ticks < 3) exit;
                {{waitSaveLoaded}}
                {{DismissMenusGml(data)}}
                {{ApplySceneGml(data)}}
                {{NormalizeRenderSizeGml()}}
                jamm_b_scene_applied = 1;
                jamm_b_state = 1;
                jamm_b_phase_start = get_timer();
                jamm_b_warmup_frames = 0;
            }
            """;
    }

    private static string ApplySceneGml(UndertaleData data)
    {
        bool hasAmbiance = data.GameObjects.ByName("AmbianceController") is not null;
        bool hasGirl = data.GameObjects.ByName("Girl") is not null;
        bool hasBoat = data.GameObjects.ByName("Boat") is not null;
        bool hasBoatData = data.GameObjects.ByName("BoatData") is not null;
        bool hasToolData = data.GameObjects.ByName("ToolData") is not null;
        bool hasSonar = data.GameObjects.ByName("SonarPanel") is not null;
        bool hasDeepSonar = data.GameObjects.ByName("SonarPanelAdvanced") is not null;
        bool hasRadar = data.GameObjects.ByName("RadarButton") is not null;
        bool hasCamera = data.GameObjects.ByName("Camera") is not null;
        string? deepLight = FindBaseDeepwaterLight(data);

        string ambianceDay = hasAmbiance
            ? "if (instance_exists(AmbianceController)) AmbianceController.lockDay = 1;"
            : "";

        string sonarOff = string.Join("\n    ", new[]
        {
            "view_visible[3] = 0;",
            hasSonar ? "if (instance_exists(SonarPanel)) { SonarPanel.active = 0; SonarPanel.shouldDraw = 0; }" : "",
            hasDeepSonar ? "if (instance_exists(SonarPanelAdvanced)) SonarPanelAdvanced.active = 0;" : "",
            hasRadar ? "if (instance_exists(RadarButton)) RadarButton.visible = 0;" : ""
        }.Where(s => s.Length > 0));

        string sonarOn = BuildSonarOnGml(hasToolData, hasSonar, hasDeepSonar);

        string dockSetup = string.Join("\n    ", new[]
        {
            hasBoat ? "if (instance_exists(Boat)) with (Boat) instance_destroy();" : "",
            hasGirl ? "if (instance_exists(Girl)) Girl.x = jamm_b_dock_x;" : "",
            sonarOff
        }.Where(s => s.Length > 0));

        string sonarSetup = string.Join("\n    ", new[]
        {
            hasBoat ? "if (instance_exists(Boat)) with (Boat) instance_destroy();" : "",
            hasGirl ? "if (instance_exists(Girl)) Girl.x = jamm_b_dock_x;" : "",
            sonarOn
        }.Where(s => s.Length > 0));

        string deepBoat = (hasBoat && hasBoatData)
            ? $$"""
                if (instance_exists(BoatData) && BoatData.nItems > 1)
                {
                    var jamm_b_bslot = clamp(jamm_b_boat_slot, 1, BoatData.nItems - 1);
                    if (BoatData.object[jamm_b_bslot] > 0)
                    {
                        BoatData.unlocked[jamm_b_bslot] = 1;
                        if (instance_exists(Boat)) with (Boat) instance_destroy();
                        var jamm_b_boat = instance_create(0, 0, BoatData.object[jamm_b_bslot]);
                        jamm_b_boat.boat_index = jamm_b_bslot;
                        jamm_b_boat.x = jamm_b_deep_x;
                        {{(hasGirl ? "if (instance_exists(Girl)) Girl.bestBoat = max(Girl.bestBoat, jamm_b_bslot);" : "")}}
                    }
                }
                """
            : "";

        string deepSetup = string.Join("\n    ", new[]
        {
            sonarOff,
            deepBoat,
            hasGirl ? "if (instance_exists(Girl)) Girl.x = jamm_b_deep_x;" : "",
            hasCamera ? "if (instance_exists(Camera)) { Camera.x = jamm_b_deep_x; Camera.y = jamm_b_deep_y; }" : "",
            "view_xview[0] = clamp(jamm_b_deep_x - (view_wview[0] * 0.5), 0, max(0, room_width - view_wview[0]));",
            "view_yview[0] = clamp(jamm_b_deep_y - (view_hview[0] * 0.5), 0, max(0, room_height - view_hview[0]));",
            deepLight is not null
                ? $$"""
                    if (jamm_b_deep_light == -1 || !instance_exists(jamm_b_deep_light))
                    {
                        jamm_b_deep_light = instance_create(view_xview[0] + view_wview[0] * 0.5, view_yview[0] + view_hview[0] * 0.5, {{deepLight}});
                        jamm_b_deep_light.image_scale = 4;
                        jamm_b_deep_light.image_xscale = 4;
                        jamm_b_deep_light.image_yscale = 4;
                        jamm_b_deep_light.image_alpha = 0.85;
                    }
                    """
                : ""
        }.Where(s => s.Length > 0));

        return $$"""
            random_set_seed(jamm_b_seed);
            {{ambianceDay}}
            if (jamm_b_scene == "dock")
            {
                {{dockSetup}}
            }
            else if (jamm_b_scene == "sonar")
            {
                {{sonarSetup}}
            }
            else
            {
                {{deepSetup}}
            }
            """;
    }

    private static string? FindBaseDeepwaterLight(UndertaleData data)
    {
        foreach (string name in new[] { "Light", "Light2" })
        {
            var light = data.GameObjects.ByName(name);
            if (light?.ParentId?.Name.Content == "AbstractLight" && light.Sprite?.Name.Content == "spr_light_radgrad")
                return name;
        }

        return null;
    }

    private static string NormalizeRenderSizeGml() => """
        // Apply once after game and scene initialization, before warmup begins.
        window_set_fullscreen(false);
        window_set_size(1200, 720);
        if (surface_exists(application_surface)) surface_resize(application_surface, 1200, 720);
        """;

    private static string BuildSonarOnGml(bool hasToolData, bool hasSonar, bool hasDeepSonar)
    {
        var lines = new List<string>();
        if (hasToolData)
        {
            string matchSonar = hasSonar ? "if (ToolData.item[jamm_b_ti] == SonarPanel) jamm_b_sslot = jamm_b_ti;" : "";
            string matchDeep = hasDeepSonar ? "if (ToolData.item[jamm_b_ti] == SonarPanelAdvanced) jamm_b_dslot = jamm_b_ti;" : "";
            lines.Add($$"""
                if (instance_exists(ToolData))
                {
                    var jamm_b_sslot = 1;
                    var jamm_b_dslot = 2;
                    for (var jamm_b_ti = 0; jamm_b_ti < ToolData.nItems; jamm_b_ti += 1)
                    {
                        {{matchSonar}}
                        {{matchDeep}}
                    }
                    if (jamm_b_sslot >= 0 && jamm_b_sslot < ToolData.nItems) ToolData.unlocked[jamm_b_sslot] = 1;
                    if (jamm_b_deep_sonar && jamm_b_dslot >= 0 && jamm_b_dslot < ToolData.nItems) ToolData.unlocked[jamm_b_dslot] = 1;
                }
                """);
        }
        if (hasSonar)
        {
            lines.Add("""
                if (!instance_exists(SonarPanel)) instance_create(0, 0, SonarPanel);
                if (instance_exists(SonarPanel))
                {
                    SonarPanel.active = 1;
                    SonarPanel.shouldDraw = 1;
                }
                """);
        }
        if (hasDeepSonar)
        {
            lines.Add("""
                if (jamm_b_deep_sonar)
                {
                    if (!instance_exists(SonarPanelAdvanced)) instance_create(0, 0, SonarPanelAdvanced);
                    if (instance_exists(SonarPanelAdvanced)) SonarPanelAdvanced.active = 1;
                }
                else if (instance_exists(SonarPanelAdvanced))
                {
                    SonarPanelAdvanced.active = 0;
                }
                """);
        }
        lines.Add("view_visible[3] = 1;");
        return string.Join("\n", lines);
    }

    private static string EndStepGml(UndertaleData data)
    {
        bool hasAmbiance = data.GameObjects.ByName("AmbianceController") is not null;
        bool hasGirl = data.GameObjects.ByName("Girl") is not null;
        bool hasBoat = data.GameObjects.ByName("Boat") is not null;
        bool hasCamera = data.GameObjects.ByName("Camera") is not null;
        bool hasSonar = data.GameObjects.ByName("SonarPanel") is not null;
        bool hasDeepSonar = data.GameObjects.ByName("SonarPanelAdvanced") is not null;
        bool hasDeepLight = FindBaseDeepwaterLight(data) is not null;

        string deepPin = string.Join("\n        ", new[]
        {
            hasBoat ? "if (instance_exists(Boat)) Boat.x = jamm_b_deep_x;" : "",
            hasGirl ? "if (instance_exists(Girl)) Girl.x = jamm_b_deep_x;" : "",
            hasCamera ? "if (instance_exists(Camera)) { Camera.x = jamm_b_deep_x; Camera.y = jamm_b_deep_y; }" : "",
            "view_xview[0] = clamp(jamm_b_deep_x - (view_wview[0] * 0.5), 0, max(0, room_width - view_wview[0]));",
            "view_yview[0] = clamp(jamm_b_deep_y - (view_hview[0] * 0.5), 0, max(0, room_height - view_hview[0]));",
            hasDeepLight ? "if (jamm_b_deep_light != -1 && instance_exists(jamm_b_deep_light)) { jamm_b_deep_light.x = clamp(view_xview[0] + view_wview[0] * 0.5, 0, room_width); jamm_b_deep_light.y = clamp(view_yview[0] + view_hview[0] * 0.5, 0, room_height); }" : ""
        }.Where(s => s.Length > 0));

        string sonarPin = string.Join("\n        ", new[]
        {
            hasSonar ? "if (instance_exists(SonarPanel)) { SonarPanel.active = 1; SonarPanel.shouldDraw = 1; }" : "",
            hasDeepSonar ? "if (jamm_b_deep_sonar && instance_exists(SonarPanelAdvanced)) SonarPanelAdvanced.active = 1;" : "",
            "view_visible[3] = 1;"
        }.Where(s => s.Length > 0));

        string dockPin = string.Join("\n        ", new[]
        {
            "view_visible[3] = 0;",
            hasSonar ? "if (instance_exists(SonarPanel)) SonarPanel.active = 0;" : "",
            hasDeepSonar ? "if (instance_exists(SonarPanelAdvanced)) SonarPanelAdvanced.active = 0;" : ""
        }.Where(s => s.Length > 0));

        return $$"""
            {{LockSaveManagerGml(data, clearSlotFlags: false)}}
            if (room != NewWorld) exit;
            if (jamm_b_state <= 1)
            {
                {{DismissMenusGml(data)}}
            }
            {{(hasAmbiance ? "if (instance_exists(AmbianceController)) AmbianceController.lockDay = 1;" : "")}}
            if (jamm_b_scene_applied)
            {
                if (jamm_b_scene == "deepwater")
                {
                    {{deepPin}}
                }
                else if (jamm_b_scene == "sonar")
                {
                    {{sonarPin}}
                }
                else
                {
                    {{dockPin}}
                }
            }
            """;
    }

    private static string DrawGuiGml(UndertaleData data)
    {
        string fontSetup = data.Fonts.ByName("fnt_babySmall") is not null
            ? "draw_set_font(fnt_babySmall);"
            : "draw_set_font(-1);";

        return $$"""
            // Never draw status text during active capture so benchmark overhead stays minimal.
            if (!jamm_b_show_hud || jamm_b_state == 2) exit;
            var jamm_b_msg = "Bench [" + jamm_b_scene + "] init";
            if (jamm_b_state == 1)
                jamm_b_msg = "Bench [" + jamm_b_scene + "] warmup...";
            else if (jamm_b_state == 3)
                jamm_b_msg = jamm_b_summary_line + " (F9: rerun)";
            {{fontSetup}}
            draw_set_halign(fa_left);
            draw_set_valign(fa_top);
            draw_set_alpha(0.8);
            draw_set_color(c_black);
            draw_rectangle(8, 8, 20 + string_width(jamm_b_msg), 20 + string_height(jamm_b_msg), false);
            draw_set_alpha(1);
            draw_set_color(c_white);
            draw_text(14, 14, jamm_b_msg);
            """;
    }

    private static string DrawGuiEndGml(BenchmarkOptions options)
    {
        bool profile = options.Profile;
        bool trace = options.TraceFish > 0;
        string warmedUp = trace
            ? "true"
            : "(jamm_b_now - jamm_b_phase_start) >= (jamm_b_warmup_sec * 1000000) && jamm_b_warmup_frames >= 10";
        string finished = trace
            ? $"jamm_trace_n >= {options.TraceFish}"
            : "(jamm_b_now - jamm_b_capture_start) >= (jamm_b_capture_sec * 1000000) || ds_list_size(jamm_b_frame_list) >= jamm_b_max_samples";

        return $$"""
        var jamm_b_now = get_timer();
        if (jamm_b_state == 1)
        {
            jamm_b_warmup_frames += 1;
            if ({{warmedUp}})
            {
                ds_list_clear(jamm_b_frame_list);
                ds_list_clear(jamm_b_loop_list);
                jamm_b_step_samples = 0;
                jamm_b_capture_start = jamm_b_now;
                jamm_b_prev_gui_t = jamm_b_now;
                jamm_b_has_step = 0;
                {{(profile ? "ds_map_clear(global.__jamm_prof_us);\nds_map_clear(global.__jamm_prof_n);\nglobal.__jamm_prof_on = 1;" : "")}}
                {{(trace ? FishTrace.Start : "")}}
                jamm_b_state = 2;
            }
        }
        else if (jamm_b_state == 2)
        {
            var jamm_b_frame_us = max(0, jamm_b_now - jamm_b_prev_gui_t);
            jamm_b_prev_gui_t = jamm_b_now;
            var jamm_b_loop_us = -1;
            if (jamm_b_has_step)
            {
                jamm_b_loop_us = max(0, jamm_b_now - jamm_b_step_t);
                jamm_b_has_step = 0;
                {{(trace ? FishTrace.Sample() : "")}}
            }
            ds_list_add(jamm_b_frame_list, jamm_b_frame_us);
            ds_list_add(jamm_b_loop_list, jamm_b_loop_us);

            if ({{finished}})
            {
                {{(profile ? "global.__jamm_prof_on = 0;" : "")}}
                var jamm_b_wall_us = max(1, jamm_b_now - jamm_b_capture_start);
                var jamm_b_n = ds_list_size(jamm_b_frame_list);

                var jamm_b_sorted = ds_list_create();
                ds_list_copy(jamm_b_sorted, jamm_b_frame_list);
                ds_list_sort(jamm_b_sorted, true);

                var jamm_b_sum = 0;
                var jamm_b_loop_sum = 0;
                var jamm_b_loop_n = 0;
                var jamm_b_pres_sum = 0;
                var jamm_b_loop_sorted = ds_list_create();
                var jamm_b_i, jamm_b_fu, jamm_b_lu, jamm_b_pu;
                for (jamm_b_i = 0; jamm_b_i < jamm_b_n; jamm_b_i += 1)
                {
                    jamm_b_fu = ds_list_find_value(jamm_b_frame_list, jamm_b_i);
                    jamm_b_lu = ds_list_find_value(jamm_b_loop_list, jamm_b_i);
                    jamm_b_sum += jamm_b_fu;
                    if (jamm_b_lu >= 0)
                    {
                        jamm_b_loop_sum += jamm_b_lu;
                        jamm_b_loop_n += 1;
                        jamm_b_pres_sum += max(0, jamm_b_fu - jamm_b_lu);
                        ds_list_add(jamm_b_loop_sorted, jamm_b_lu);
                    }
                }
                ds_list_sort(jamm_b_loop_sorted, true);

                var jamm_b_f_min = ds_list_find_value(jamm_b_sorted, 0);
                var jamm_b_f_p50 = ds_list_find_value(jamm_b_sorted, clamp(floor((jamm_b_n - 1) * 0.50), 0, jamm_b_n - 1));
                var jamm_b_f_p95 = ds_list_find_value(jamm_b_sorted, clamp(floor((jamm_b_n - 1) * 0.95), 0, jamm_b_n - 1));
                var jamm_b_f_p99 = ds_list_find_value(jamm_b_sorted, clamp(floor((jamm_b_n - 1) * 0.99), 0, jamm_b_n - 1));
                var jamm_b_f_max = ds_list_find_value(jamm_b_sorted, jamm_b_n - 1);
                var jamm_b_f_mean = round(jamm_b_sum / max(1, jamm_b_n));

                var jamm_b_l_mean = -1;
                var jamm_b_l_p50 = -1;
                var jamm_b_l_p95 = -1;
                var jamm_b_l_max = -1;
                var jamm_b_p_mean = -1;
                if (jamm_b_loop_n > 0)
                {
                    jamm_b_l_mean = round(jamm_b_loop_sum / jamm_b_loop_n);
                    jamm_b_p_mean = round(jamm_b_pres_sum / jamm_b_loop_n);
                    jamm_b_l_p50 = ds_list_find_value(jamm_b_loop_sorted, clamp(floor((jamm_b_loop_n - 1) * 0.50), 0, jamm_b_loop_n - 1));
                    jamm_b_l_p95 = ds_list_find_value(jamm_b_loop_sorted, clamp(floor((jamm_b_loop_n - 1) * 0.95), 0, jamm_b_loop_n - 1));
                    jamm_b_l_max = ds_list_find_value(jamm_b_loop_sorted, jamm_b_loop_n - 1);
                }

                var jamm_b_avg_fps = (jamm_b_n * 1000000) / jamm_b_wall_us;
                var jamm_b_window_w = window_get_width();
                var jamm_b_window_h = window_get_height();
                var jamm_b_app_w = -1;
                var jamm_b_app_h = -1;
                if (surface_exists(application_surface))
                {
                    jamm_b_app_w = surface_get_width(application_surface);
                    jamm_b_app_h = surface_get_height(application_surface);
                }
                var jamm_b_fullscreen = window_get_fullscreen();
                var jamm_b_size_ok = !jamm_b_fullscreen && jamm_b_window_w == 1200 && jamm_b_window_h == 720 && jamm_b_app_w == 1200 && jamm_b_app_h == 720;
                var jamm_b_f = file_text_open_write(jamm_b_output_file);
                if (jamm_b_f >= 0)
                {
                    file_text_write_string(jamm_b_f, "# jamm_bench v1");
                    file_text_writeln(jamm_b_f);
                    if (variable_global_exists("jamm_fg_enabled"))
                    {
                        file_text_write_string(jamm_b_f, "# fish_grid=" + string(global.jamm_fg_enabled));
                        file_text_writeln(jamm_b_f);
                    }
                    file_text_write_string(jamm_b_f, "# scene=" + jamm_b_scene + " requested=" + jamm_b_scene_req + " room=" + room_get_name(room) + " seed=" + string(jamm_b_seed) + " warmup_s=" + string(jamm_b_warmup_sec) + " capture_s=" + string(jamm_b_capture_sec) + " max_samples=" + string(jamm_b_max_samples));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, "# samples=" + string(jamm_b_n) + " wall_us=" + string(round(jamm_b_wall_us)) + " avg_fps=" + string(jamm_b_avg_fps) + " room_speed=" + string(room_speed) + " step_samples=" + string(jamm_b_step_samples) + " draw_samples=" + string(jamm_b_n) + " draws_per_step=" + string(jamm_b_n / max(1, jamm_b_step_samples)) + " instances=" + string(instance_count) + " window=" + string(jamm_b_window_w) + "x" + string(jamm_b_window_h) + " application_surface=" + string(jamm_b_app_w) + "x" + string(jamm_b_app_h) + " fullscreen=" + string(jamm_b_fullscreen) + " render_size_normalized=" + string(jamm_b_size_ok));
                    file_text_writeln(jamm_b_f);
                    if (!jamm_b_size_ok)
                    {
                        file_text_write_string(jamm_b_f, "# warning=render_size_normalization_not_applied_or_overridden_requested_window=1200x720_application_surface=1200x720_fullscreen=0");
                        file_text_writeln(jamm_b_f);
                    }
                    if (jamm_b_step_samples <= 0 || jamm_b_n < jamm_b_step_samples)
                    {
                        file_text_write_string(jamm_b_f, "# warning=draw_step_sampling_mismatch_check_step_samples_and_draw_samples");
                        file_text_writeln(jamm_b_f);
                    }
                    file_text_write_string(jamm_b_f, "# frame_us min=" + string(round(jamm_b_f_min)) + " p50=" + string(round(jamm_b_f_p50)) + " p95=" + string(round(jamm_b_f_p95)) + " p99=" + string(round(jamm_b_f_p99)) + " max=" + string(round(jamm_b_f_max)) + " mean=" + string(jamm_b_f_mean));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, "# loop_us samples=" + string(jamm_b_loop_n) + " mean=" + string(round(jamm_b_l_mean)) + " p50=" + string(round(jamm_b_l_p50)) + " p95=" + string(round(jamm_b_l_p95)) + " max=" + string(round(jamm_b_l_max)) + " present_sleep_us_mean=" + string(round(jamm_b_p_mean)));
                    file_text_writeln(jamm_b_f);
                    if (jamm_b_warn != "")
                    {
                        file_text_write_string(jamm_b_f, "# warning=" + jamm_b_warn);
                        file_text_writeln(jamm_b_f);
                    }
                    file_text_write_string(jamm_b_f, "frame_index,frame_us,loop_us,present_sleep_us");
                    file_text_writeln(jamm_b_f);
                    for (jamm_b_i = 0; jamm_b_i < jamm_b_n; jamm_b_i += 1)
                    {
                        jamm_b_fu = ds_list_find_value(jamm_b_frame_list, jamm_b_i);
                        jamm_b_lu = ds_list_find_value(jamm_b_loop_list, jamm_b_i);
                        jamm_b_pu = -1;
                        if (jamm_b_lu >= 0) jamm_b_pu = max(0, jamm_b_fu - jamm_b_lu);
                        file_text_write_string(jamm_b_f, string(jamm_b_i) + "," + string(round(jamm_b_fu)) + "," + string(round(jamm_b_lu)) + "," + string(round(jamm_b_pu)));
                        file_text_writeln(jamm_b_f);
                    }
                    file_text_close(jamm_b_f);
                }

                {{(profile ? ProfileWriterGml() : "")}}

                ds_list_destroy(jamm_b_sorted);
                ds_list_destroy(jamm_b_loop_sorted);
                jamm_b_summary_line = "Bench " + jamm_b_scene + ": n=" + string(jamm_b_n) + " fps=" + string(jamm_b_avg_fps) + " p50_us=" + string(round(jamm_b_f_p50)) + " p95_us=" + string(round(jamm_b_f_p95)) + " file=" + jamm_b_output_file;
                show_debug_message(jamm_b_summary_line);
                jamm_b_state = 3;
                if (jamm_b_auto_exit) game_end();
            }
        }
        else if (jamm_b_state == 3)
        {
            if (!jamm_b_auto_exit && keyboard_check_pressed(vk_f9))
            {
                jamm_b_state = 1;
                jamm_b_phase_start = jamm_b_now;
                jamm_b_warmup_frames = 0;
            }
        }
        """;

    }

    private static string ProfileWriterGml() => """
        var jamm_p_name = "jamm_profile_" + jamm_b_scene + ".csv";
        var jamm_p_f = file_text_open_write(jamm_p_name);
        if (jamm_p_f >= 0)
        {
            file_text_write_string(jamm_p_f, "# ticks=" + string(jamm_b_loop_n) + " loop_us_mean=" + string(round(jamm_b_l_mean)) + " overhead=get_timer_start_and_end_per_invocation; relative_cpu_only; draw_entries_render_affected");
            file_text_writeln(jamm_p_f);
            file_text_write_string(jamm_p_f, "code_entry,calls,total_us,us_per_tick,share_of_loop");
            file_text_writeln(jamm_p_f);
            var jamm_p_keys = ds_list_create();
            var jamm_p_key = ds_map_find_first(global.__jamm_prof_us);
            while (!is_undefined(jamm_p_key))
            {
                ds_list_add(jamm_p_keys, jamm_p_key);
                jamm_p_key = ds_map_find_next(global.__jamm_prof_us, jamm_p_key);
            }
            var jamm_p_i, jamm_p_j, jamm_p_left, jamm_p_right;
            for (jamm_p_i = 0; jamm_p_i < ds_list_size(jamm_p_keys); jamm_p_i += 1)
            {
                for (jamm_p_j = jamm_p_i + 1; jamm_p_j < ds_list_size(jamm_p_keys); jamm_p_j += 1)
                {
                    jamm_p_left = ds_list_find_value(jamm_p_keys, jamm_p_i);
                    jamm_p_right = ds_list_find_value(jamm_p_keys, jamm_p_j);
                    if (ds_map_find_value(global.__jamm_prof_us, jamm_p_right) > ds_map_find_value(global.__jamm_prof_us, jamm_p_left))
                    {
                        ds_list_replace(jamm_p_keys, jamm_p_i, jamm_p_right);
                        ds_list_replace(jamm_p_keys, jamm_p_j, jamm_p_left);
                    }
                }
            }
            for (jamm_p_i = 0; jamm_p_i < ds_list_size(jamm_p_keys); jamm_p_i += 1)
            {
                jamm_p_key = ds_list_find_value(jamm_p_keys, jamm_p_i);
                var jamm_p_us = ds_map_find_value(global.__jamm_prof_us, jamm_p_key);
                var jamm_p_calls = ds_map_find_value(global.__jamm_prof_n, jamm_p_key);
                file_text_write_string(jamm_p_f, string(jamm_p_key) + "," + string(jamm_p_calls) + "," + string(round(jamm_p_us)) + "," + string(round(jamm_p_us / max(1, jamm_b_loop_n))) + "," + string(jamm_p_us / max(1, jamm_b_loop_sum)));
                file_text_writeln(jamm_p_f);
            }
            ds_list_destroy(jamm_p_keys);
            file_text_close(jamm_p_f);
        }
        """;
}
