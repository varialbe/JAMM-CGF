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

/// <summary>Fish simulation/rendering mode applied after warmup in benchmark mode.</summary>
public enum BenchmarkFishMode
{
    /// <summary>Normal fish simulation and rendering (default).</summary>
    Full,
    /// <summary>Skip fish BeginStep/Step/EndStep logic while keeping drawing enabled.</summary>
    NoStep,
    /// <summary>Simulate fish normally while skipping all fish Draw events.</summary>
    NoDraw,
    /// <summary>Deactivate all Fish instances after warmup.</summary>
    Deactivate
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
    bool FishGrid = false,
    bool DecorSleep = false,
    bool FishSleep = false,
    bool Screenshot = false,
    BenchmarkFishMode FishMode = BenchmarkFishMode.Full,
    int FishCap = -1,
    double FishScale = 1.0);

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

    /// <summary>Parses a fish experiment mode (<c>full</c>, <c>nostep</c>, <c>nodraw</c>, or <c>deactivate</c>).</summary>
    public static BenchmarkFishMode ParseFishMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Fish mode must be 'full', 'nostep', 'nodraw', or 'deactivate'.");

        return value.Trim().ToLowerInvariant() switch
        {
            "full" => BenchmarkFishMode.Full,
            "nostep" => BenchmarkFishMode.NoStep,
            "nodraw" => BenchmarkFishMode.NoDraw,
            "deactivate" => BenchmarkFishMode.Deactivate,
            _ => throw new ArgumentException($"Unknown fish mode '{value}'. Choose 'full', 'nostep', 'nodraw', or 'deactivate'.")
        };
    }

    /// <summary>Formats a <see cref="BenchmarkFishMode"/> as its canonical lowercase identifier.</summary>
    public static string FishModeName(BenchmarkFishMode mode) => mode switch
    {
        BenchmarkFishMode.Full => "full",
        BenchmarkFishMode.NoStep => "nostep",
        BenchmarkFishMode.NoDraw => "nodraw",
        BenchmarkFishMode.Deactivate => "deactivate",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported benchmark fish mode.")
    };

    /// <summary>Applies the benchmark controller pass to an original or already-modded in-memory archive.</summary>
    public static void Apply(UndertaleData data, BenchmarkOptions? options = null, Action<string>? decision = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new BenchmarkOptions();
        ValidateOptions(options);

        if (data.Rooms.ByName("NewWorld") is null)
            throw new InvalidDataException("Required room 'NewWorld' was not found: use a Cat Goes Fishing legacy archive (original or modded).");
        if (data.GameObjects.ByName(ObjectName) is not null)
            throw new InvalidDataException($"Object '{ObjectName}' already exists: build the mod from the ORIGINAL data.win, not from a modded one.");

        BenchmarkFishExperiments.GuardFishReferences(data);

        if (options.FishGrid)
        {
            string result = FishGrid.Apply(data);
            decision?.Invoke(result);
        }
        if (options.DecorSleep)
        {
            foreach (string line in DecorSleep.Apply(data))
                decision?.Invoke(line);
        }
        if (options.FishSleep)
        {
            foreach (string line in FishSleep.Apply(data))
                decision?.Invoke(line);
        }

        if (options.TraceFish > 0) FishTrace.Instrument(data, options.Seed);
        BenchmarkFishExperiments.InstrumentFishAndSpawners(data);
        if (options.Profile)
            GmlCpuProfiler.Instrument(data, options.ProfileScripts);

        var bench = ModObjects.Create(data, ObjectName, persistent: true);
        // Run DrawGUIEnd after normal draws/GUI and just before SmoothMod's Depth=-1000000 freeze.
        bench.Depth = -999999;

        var compiler = new CompileGroup(data);
        AddBenchScript(data, compiler, "jamm_b_stats_line", StatsLineScriptGml());
        if (data.GameObjects.ByName("Fish") is not null)
            AddBenchScript(data, compiler, "jamm_b_apply_fish_cap", ApplyFishCapScriptGml(data));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Create, 0u, data), CreateGml(options));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Destroy, 0u, data), DestroyGml(options.TraceFish > 0));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.RoomStart, data), RoomStartGml(data));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), BeginStepGml(data, options.TraceFish > 0));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), EndStepGml(data));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.PreDraw, data), PreDrawGml());
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, data), DrawGuiGml(data));
        compiler.QueueCodeReplace(bench.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), DrawGuiEndGml(data, options));
        ModObjects.Compile(compiler);

        ModObjects.PlaceInstance(data, "NewWorld", bench, first: true);
        if (data.Rooms.ByName("room11") is not null)
            ModObjects.PlaceInstance(data, "room11", bench, first: true);
    }

    private static void AddBenchScript(UndertaleData data, CompileGroup compiler, string name, string body)
    {
        if (data.Scripts.ByName(name) is not null)
            throw new InvalidDataException($"'{name}' already exists: use an uninstrumented input archive.");
        var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
        compiler.QueueCodeReplace(code, body);
    }

    internal static void ValidateOptions(BenchmarkOptions options)
    {
        _ = SceneName(options.Scene);
        _ = FishModeName(options.FishMode);
        if (double.IsNaN(options.WarmupSeconds) || double.IsInfinity(options.WarmupSeconds) || options.WarmupSeconds < 0 || options.WarmupSeconds > 3600)
            throw new ArgumentOutOfRangeException(nameof(options), "WarmupSeconds must be between 0 and 3600.");
        if (double.IsNaN(options.CaptureSeconds) || double.IsInfinity(options.CaptureSeconds) || options.CaptureSeconds <= 0 || options.CaptureSeconds > 3600)
            throw new ArgumentOutOfRangeException(nameof(options), "CaptureSeconds must be > 0 and <= 3600.");
        if (options.MaxSamples < 1 || options.MaxSamples > 200000)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSamples must be between 1 and 200000.");
        if (options.TraceFish is < 0 or > 200000)
            throw new ArgumentOutOfRangeException(nameof(options), "TraceFish must be between 0 (disabled) and 200000.");
        if (options.FishCap is < -1 or > 200000)
            throw new ArgumentOutOfRangeException(nameof(options), "FishCap must be -1 (disabled) or between 0 and 200000.");
        if (double.IsNaN(options.FishScale) || double.IsInfinity(options.FishScale) || options.FishScale < 1.0 || options.FishScale > 20.0)
            throw new ArgumentOutOfRangeException(nameof(options), "FishScale must be between 1 and 20.");
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
        string fishMode = FishModeName(o.FishMode);
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

            global.jamm_b_fish_nostep = 0;
            global.jamm_b_fish_nodraw = 0;
            global.jamm_b_freeze_spawners = 0;
            global.jamm_b_no_thin = 0;
            global.jamm_b_fish_depth = 0;
            global.jamm_b_fish_t0 = 0;
            global.jamm_b_fish_step_us = 0;

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
            jamm_b_screenshot = {{(o.Screenshot ? 1 : 0)}};
            jamm_b_fish_mode = "{{fishMode}}";
            jamm_b_fish_mode_req = jamm_b_fish_mode;
            jamm_b_fish_cap = {{o.FishCap}};
            jamm_b_fish_scale = {{Num(o.FishScale)}};
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
                jamm_b_screenshot = ini_read_real("Bench", "Screenshot", jamm_b_screenshot) != 0;
                jamm_b_fish_mode = string_lower(ini_read_string("Bench", "FishMode", jamm_b_fish_mode));
                jamm_b_fish_mode_req = jamm_b_fish_mode;
                jamm_b_fish_cap = round(ini_read_real("Bench", "FishCap", jamm_b_fish_cap));
                if (jamm_b_fish_cap < 0)
                    jamm_b_fish_cap = -1;
                else
                    jamm_b_fish_cap = clamp(jamm_b_fish_cap, 0, 200000);
                jamm_b_fish_scale = clamp(ini_read_real("Bench", "FishScale", jamm_b_fish_scale), 1, 20);
                ini_close();
            }

            if (jamm_b_scene != "dock" && jamm_b_scene != "sonar" && jamm_b_scene != "deepwater")
            {
                jamm_b_warn = "unknown_scene_" + jamm_b_scene + "_fallback_dock";
                jamm_b_scene = "dock";
            }
            if (jamm_b_fish_mode != "full" && jamm_b_fish_mode != "nostep" && jamm_b_fish_mode != "nodraw" && jamm_b_fish_mode != "deactivate")
            {
                if (jamm_b_warn != "") jamm_b_warn += ";";
                jamm_b_warn += "unknown_fish_mode_" + jamm_b_fish_mode + "_fallback_full";
                jamm_b_fish_mode = "full";
            }
            if (jamm_b_fish_scale > 1)
                global.jamm_b_no_thin = 1;
            if (!jamm_b_output_custom)
                jamm_b_output_file = "jamm_bench_" + jamm_b_scene + ".csv";
            if (jamm_b_output_file == "" || jamm_b_output_file == "meta.ini" || string_pos("sdata", string_lower(jamm_b_output_file)) == 1 || string_pos(".win", string_lower(jamm_b_output_file)) > 0 || string_pos("..", jamm_b_output_file) > 0 || string_pos("/", jamm_b_output_file) > 0 || string_pos(chr(92), jamm_b_output_file) > 0 || string_pos(":", jamm_b_output_file) > 0)
                jamm_b_output_file = "jamm_bench_" + jamm_b_scene + ".csv";

            {{(o.TraceFish > 0 ? FishTrace.Create : "")}}
            jamm_b_frame_list = ds_list_create();
            jamm_b_loop_list = ds_list_create();
            jamm_b_step_list = ds_list_create();
            jamm_b_draw_list = ds_list_create();
            jamm_b_fish_step_list = ds_list_create();
            jamm_b_fish_count_list = ds_list_create();
            jamm_b_state = 0;
            jamm_b_init_ticks = 0;
            jamm_b_scene_applied = 0;
            jamm_b_deep_light = -1;
            jamm_b_room_jump_done = 0;
            jamm_b_step_t = 0;
            jamm_b_draw_start_t = 0;
            jamm_b_has_step = 0;
            jamm_b_has_draw = 0;
            jamm_b_prev_gui_t = get_timer();
            jamm_b_phase_start = jamm_b_prev_gui_t;
            jamm_b_capture_start = jamm_b_prev_gui_t;
            jamm_b_warmup_frames = 0;
            jamm_b_step_samples = 0;
            jamm_b_fish_total = 0;
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
            ds_list_destroy(jamm_b_step_list);
            ds_list_destroy(jamm_b_draw_list);
            ds_list_destroy(jamm_b_fish_step_list);
            ds_list_destroy(jamm_b_fish_count_list);
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
            jamm_b_has_draw = 0;
            global.jamm_b_fish_step_us = 0;
            global.jamm_b_fish_depth = 0;
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
                {{ApplyFishScaleGml(data)}}
                jamm_b_scene_applied = 1;
                jamm_b_state = 1;
                jamm_b_phase_start = get_timer();
                jamm_b_warmup_frames = 0;
            }
            """;
    }

    private static string ApplyFishScaleGml(UndertaleData data)
    {
        if (data.GameObjects.ByName("Fish") is null) return "";

        bool hasSmall = data.GameObjects.ByName("FishSmall") is not null;
        bool hasMedium = data.GameObjects.ByName("FishMedium") is not null;
        string sizeCheck = (hasSmall || hasMedium)
            ? "(" + string.Join(" || ", new[]
            {
                hasSmall ? "object_is_ancestor(object_index, FishSmall)" : "",
                hasMedium ? "object_is_ancestor(object_index, FishMedium)" : ""
            }.Where(s => s.Length > 0)) + ")"
            : "true";

        var excluded = new[]
        {
            "Kingfish", "Torby", "Curby", "Goldfish", "Thermo",
            "Bishop", "Caval", "Volteen", "Flash", "Maskfish"
        }.Where(n => data.GameObjects.ByName(n) is not null).ToArray();
        string specialExclusion = excluded.Length > 0
            ? string.Join(" && ", excluded.Select(n => $"object_index != {n} && !object_is_ancestor(object_index, {n})"))
            : "true";

        bool hasPathInit = data.Scripts.ByName("path_init") is not null;

        return $$"""
            if (jamm_b_fish_scale > 1)
            {
                var jamm_scale_src = ds_list_create();
                with (Fish)
                {
                    if (variable_instance_exists(id, "hollow") && hollow) continue;
                    if (variable_instance_exists(id, "state") && (state == 3 || state == 4 || state == 6)) continue;
                    if (!{{sizeCheck}}) continue;
                    if (!({{specialExclusion}})) continue;
                    if (variable_instance_exists(id, "neck") || variable_instance_exists(id, "tail") || variable_instance_exists(id, "eye") || variable_instance_exists(id, "hasGuard")) continue;
                    if (variable_instance_exists(id, "light") && light > 0) continue;
                    ds_list_add(jamm_scale_src, id);
                }
                var jamm_src_n = ds_list_size(jamm_scale_src);
                var jamm_total_copies = max(0, round((jamm_b_fish_scale - 1) * jamm_src_n));
                for (var jamm_ci = 0; jamm_ci < jamm_total_copies && jamm_src_n > 0; jamm_ci += 1)
                {
                    var jamm_orig = ds_list_find_value(jamm_scale_src, jamm_ci mod jamm_src_n);
                    var jamm_layer = (jamm_ci div jamm_src_n) + 1;
                    with (jamm_orig)
                    {
                        var jamm_copy = instance_copy(false);
                        with (jamm_copy)
                        {
                            hollow = 0;
                            if (variable_instance_exists(id, "light")) light = -1;
                            if (variable_instance_exists(id, "light2")) light2 = -1;
                            var jamm_ox = ((jamm_ci mod 5) - 2) * 24;
                            var jamm_oy = (((jamm_ci div 5) mod 3) - 1) * 16 + (jamm_layer * 8);
                            x = clamp(x + jamm_ox, 740, room_width - 100);
                            y = clamp(y + jamm_oy, 1320, room_height - 100);
                            start_x = x;
                            start_y = y;
                            TRUE_START_X = x;
                            TRUE_START_Y = y;
                            {{(hasPathInit ? "if (variable_instance_exists(id, \"hasPath\") && hasPath) path_init();" : "")}}
                        }
                    }
                }
                ds_list_destroy(jamm_scale_src);
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
        bool hasFish = data.GameObjects.ByName("Fish") is not null;

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

        string captureEnforce = hasFish
            ? """
              if (jamm_b_state == 2)
              {
                  if (jamm_b_fish_mode == "deactivate" && instance_number(Fish) > 0)
                      instance_deactivate_object(Fish);
                  else if (jamm_b_fish_cap >= 0 && instance_number(Fish) > jamm_b_fish_cap)
                      jamm_b_apply_fish_cap(jamm_b_fish_cap);
              }
              """
            : "";

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
            {{captureEnforce}}
            """;
    }

    private static string PreDrawGml() => """
        jamm_b_draw_start_t = get_timer();
        jamm_b_has_draw = 1;
        """;

    private static string StatsLineScriptGml() => """
        var jamm_s_list = argument0;
        var jamm_s_label = argument1;
        var jamm_s_total = ds_list_size(jamm_s_list);
        var jamm_s_valid = ds_list_create();
        var jamm_s_sum = 0;
        var jamm_s_i, jamm_s_val;
        for (jamm_s_i = 0; jamm_s_i < jamm_s_total; jamm_s_i += 1)
        {
            jamm_s_val = ds_list_find_value(jamm_s_list, jamm_s_i);
            if (jamm_s_val >= 0)
            {
                ds_list_add(jamm_s_valid, jamm_s_val);
                jamm_s_sum += jamm_s_val;
            }
        }
        var jamm_s_cnt = ds_list_size(jamm_s_valid);
        if (jamm_s_cnt <= 0)
        {
            ds_list_destroy(jamm_s_valid);
            return "# " + jamm_s_label + " samples=0 min=-1 p50=-1 p95=-1 p99=-1 max=-1 mean=-1";
        }
        ds_list_sort(jamm_s_valid, true);
        var jamm_s_min = ds_list_find_value(jamm_s_valid, 0);
        var jamm_s_p50 = ds_list_find_value(jamm_s_valid, clamp(floor((jamm_s_cnt - 1) * 0.50), 0, jamm_s_cnt - 1));
        var jamm_s_p95 = ds_list_find_value(jamm_s_valid, clamp(floor((jamm_s_cnt - 1) * 0.95), 0, jamm_s_cnt - 1));
        var jamm_s_p99 = ds_list_find_value(jamm_s_valid, clamp(floor((jamm_s_cnt - 1) * 0.99), 0, jamm_s_cnt - 1));
        var jamm_s_max = ds_list_find_value(jamm_s_valid, jamm_s_cnt - 1);
        var jamm_s_mean = round(jamm_s_sum / jamm_s_cnt);
        ds_list_destroy(jamm_s_valid);
        return "# " + jamm_s_label + " samples=" + string(jamm_s_cnt) + " min=" + string(round(jamm_s_min)) + " p50=" + string(round(jamm_s_p50)) + " p95=" + string(round(jamm_s_p95)) + " p99=" + string(round(jamm_s_p99)) + " max=" + string(round(jamm_s_max)) + " mean=" + string(round(jamm_s_mean));
        """;

    private static string ApplyFishCapScriptGml(UndertaleData data)
    {
        bool hasLure = data.GameObjects.ByName("Lure") is not null;
        bool hasCatSwim = data.GameObjects.ByName("CatSwim") is not null;
        bool hasNet = data.GameObjects.ByName("Net") is not null;
        bool hasFishSmall = data.GameObjects.ByName("FishSmall") is not null;
        bool hasFishMedium = data.GameObjects.ByName("FishMedium") is not null;
        bool hasFishLarge = data.GameObjects.ByName("FishLarge") is not null;
        bool hasFishLargePather = data.GameObjects.ByName("FishLargePather") is not null;
        bool hasFishHuge = data.GameObjects.ByName("FishHuge") is not null;
        bool hasFishPart = data.GameObjects.ByName("FishPart") is not null;

        string hookedCheck = string.Join("\n    ", new[]
        {
            hasLure ? "if (instance_exists(Lure) && ((Lure.fish > 0 && id == Lure.fish) || (variable_instance_exists(Lure.id, \"stolenBy\") && Lure.stolenBy > 0 && id == Lure.stolenBy))) jamm_hooked = true;" : "",
            hasCatSwim ? "if (instance_exists(CatSwim) && CatSwim.holding > 0 && id == CatSwim.holding) jamm_hooked = true;" : ""
        }.Where(s => s.Length > 0));

        string ordExprParts = string.Join(" || ", new[]
        {
            hasFishSmall ? "object_index == FishSmall || object_is_ancestor(object_index, FishSmall)" : "",
            hasFishMedium ? "object_index == FishMedium || object_is_ancestor(object_index, FishMedium)" : "",
            hasFishLarge ? "(object_index == FishLarge || object_is_ancestor(object_index, FishLarge))" + (hasFishLargePather ? " && object_index != FishLargePather && !object_is_ancestor(object_index, FishLargePather)" : "") : ""
        }.Where(s => s.Length > 0));
        string ordCheck = ordExprParts.Length > 0 ? $"({ordExprParts})" : "true";
        if (hasFishHuge)
            ordCheck += " && object_index != FishHuge && !object_is_ancestor(object_index, FishHuge)";

        string unlinkExtra = string.Join("\n        ", new[]
        {
            hasLure ? "with (Lure) { if (fish > 0 && ds_map_exists(jamm_dead, fish)) { fish = -1; hookedFishSize = 0; } if (variable_instance_exists(id, \"stolenBy\") && stolenBy > 0 && ds_map_exists(jamm_dead, stolenBy)) stolenBy = -1; }" : "",
            hasCatSwim ? "with (CatSwim) { if (holding > 0 && ds_map_exists(jamm_dead, holding)) holding = -1; }" : "",
            hasNet ? "with (Net) { if (variable_instance_exists(id, \"fish\") && fish > 0 && ds_map_exists(jamm_dead, fish)) fish = -1; }" : "",
            hasFishPart ? "with (FishPart) { if (variable_instance_exists(id, \"parent\") && parent > 0 && ds_map_exists(jamm_dead, parent)) { ds_map_replace(jamm_dead, id, 1); instance_destroy(); } }" : ""
        }.Where(s => s.Length > 0));

        return $$"""
            var jamm_cap = argument0;
            if (jamm_cap < 0) exit;
            var jamm_cur = instance_number(Fish);
            if (jamm_cur <= jamm_cap) exit;
            var jamm_need = jamm_cur - jamm_cap;
            var jamm_out_ord = ds_list_create();
            var jamm_out_rem = ds_list_create();
            var jamm_view_ord = ds_list_create();
            var jamm_view_rem = ds_list_create();
            with (Fish)
            {
                var jamm_hooked = false;
                if (state != 0 && state != 12) jamm_hooked = true;
                if (isDragging || onBoat) jamm_hooked = true;
                {{hookedCheck}}
                if (jamm_hooked) continue;
                var jamm_in_view = false;
                for (var jamm_v = 0; jamm_v < 8 && !jamm_in_view; jamm_v += 1)
                {
                    if (view_visible[jamm_v])
                    {
                        if (x >= view_xview[jamm_v] && x <= view_xview[jamm_v] + view_wview[jamm_v] && y >= view_yview[jamm_v] && y <= view_yview[jamm_v] + view_hview[jamm_v])
                            jamm_in_view = true;
                    }
                }
                var jamm_is_ord = {{ordCheck}};
                if (jamm_is_ord && (variable_instance_exists(id, "body") || variable_instance_exists(id, "pather") || variable_instance_exists(id, "partner") || variable_instance_exists(id, "piercer") || variable_instance_exists(id, "chaw") || variable_instance_exists(id, "next") || variable_instance_exists(id, "arm_l") || variable_instance_exists(id, "master") || variable_instance_exists(id, "mate")))
                    jamm_is_ord = false;
                if (jamm_in_view)
                {
                    if (jamm_is_ord) ds_list_add(jamm_view_ord, id);
                    else ds_list_add(jamm_view_rem, id);
                }
                else
                {
                    if (jamm_is_ord) ds_list_add(jamm_out_ord, id);
                    else ds_list_add(jamm_out_rem, id);
                }
            }
            var jamm_dead = ds_map_create();
            var jamm_buckets = 4;
            var jamm_lists;
            jamm_lists[0] = jamm_out_ord;
            jamm_lists[1] = jamm_out_rem;
            jamm_lists[2] = jamm_view_ord;
            jamm_lists[3] = jamm_view_rem;
            for (var jamm_bi = 0; jamm_bi < jamm_buckets && jamm_need > 0; jamm_bi += 1)
            {
                var jamm_bl = jamm_lists[jamm_bi];
                for (var jamm_di = 0; jamm_di < ds_list_size(jamm_bl) && jamm_need > 0; jamm_di += 1)
                {
                    with (ds_list_find_value(jamm_bl, jamm_di))
                    {
                        ds_map_replace(jamm_dead, id, 1);
                        if (variable_instance_exists(id, "light") && is_real(light) && light > 0 && instance_exists(light)) { ds_map_replace(jamm_dead, light, 1); instance_destroy(light); }
                        if (variable_instance_exists(id, "light2") && is_real(light2) && light2 > 0 && instance_exists(light2)) { ds_map_replace(jamm_dead, light2, 1); instance_destroy(light2); }
                        if (variable_instance_exists(id, "pather") && is_real(pather) && pather > 0 && instance_exists(pather)) { ds_map_replace(jamm_dead, pather, 1); instance_destroy(pather); }
                        if (variable_instance_exists(id, "lure_inst") && is_real(lure_inst) && lure_inst > 0 && instance_exists(lure_inst)) { ds_map_replace(jamm_dead, lure_inst, 1); instance_destroy(lure_inst); }
                        if (variable_instance_exists(id, "body") && is_real(body) && body > 0 && instance_exists(body)) { ds_map_replace(jamm_dead, body, 1); instance_destroy(body); }
                        if (variable_instance_exists(id, "tail") && is_real(tail) && tail > 0 && instance_exists(tail)) { ds_map_replace(jamm_dead, tail, 1); instance_destroy(tail); }
                        if (variable_instance_exists(id, "eye") && is_real(eye) && eye > 0 && instance_exists(eye)) { ds_map_replace(jamm_dead, eye, 1); instance_destroy(eye); }
                        if (variable_instance_exists(id, "jaw") && is_real(jaw) && jaw > 0 && instance_exists(jaw)) { ds_map_replace(jamm_dead, jaw, 1); instance_destroy(jaw); }
                        if (variable_instance_exists(id, "neck") && is_real(neck) && neck > 0 && instance_exists(neck)) { ds_map_replace(jamm_dead, neck, 1); instance_destroy(neck); }
                        if (variable_instance_exists(id, "fin1") && is_real(fin1) && fin1 > 0 && instance_exists(fin1)) { ds_map_replace(jamm_dead, fin1, 1); instance_destroy(fin1); }
                        if (variable_instance_exists(id, "fin2") && is_real(fin2) && fin2 > 0 && instance_exists(fin2)) { ds_map_replace(jamm_dead, fin2, 1); instance_destroy(fin2); }
                        if (variable_instance_exists(id, "arm_l") && is_real(arm_l) && arm_l > 0 && instance_exists(arm_l)) { ds_map_replace(jamm_dead, arm_l, 1); instance_destroy(arm_l); }
                        if (variable_instance_exists(id, "arm_r") && is_real(arm_r) && arm_r > 0 && instance_exists(arm_r)) { ds_map_replace(jamm_dead, arm_r, 1); instance_destroy(arm_r); }
                        if (variable_instance_exists(id, "next") && is_real(next) && next > 0 && instance_exists(next)) { ds_map_replace(jamm_dead, next, 1); instance_destroy(next); }
                        instance_destroy();
                    }
                    jamm_need -= 1;
                }
            }
            if (ds_map_size(jamm_dead) > 0)
            {
                {{unlinkExtra}}
                with (all)
                {
                    if (variable_instance_exists(id, "pal") && is_real(pal) && pal > 0 && ds_map_exists(jamm_dead, pal)) pal = -1;
                    if (variable_instance_exists(id, "target") && is_real(target) && target > 0 && ds_map_exists(jamm_dead, target)) target = -1;
                    if (variable_instance_exists(id, "partner") && is_real(partner) && partner > 0 && ds_map_exists(jamm_dead, partner)) partner = -1;
                    if (variable_instance_exists(id, "parent") && is_real(parent) && parent > 0 && ds_map_exists(jamm_dead, parent)) parent = -1;
                    if (variable_instance_exists(id, "mate") && is_real(mate) && mate > 0 && ds_map_exists(jamm_dead, mate)) mate = -1;
                    if (variable_instance_exists(id, "leader") && is_real(leader) && leader > 0 && ds_map_exists(jamm_dead, leader)) leader = -1;
                    if (variable_instance_exists(id, "follower") && is_real(follower) && follower > 0 && ds_map_exists(jamm_dead, follower)) follower = -1;
                    if (variable_instance_exists(id, "bro") && is_real(bro) && bro > 0 && ds_map_exists(jamm_dead, bro)) bro = -1;
                    if (variable_instance_exists(id, "nightbro") && is_real(nightbro) && nightbro > 0 && ds_map_exists(jamm_dead, nightbro)) nightbro = -1;
                    if (variable_instance_exists(id, "master") && is_real(master) && master > 0 && ds_map_exists(jamm_dead, master)) master = -1;
                    if (variable_instance_exists(id, "piercer") && is_real(piercer) && piercer > 0 && ds_map_exists(jamm_dead, piercer)) piercer = -1;
                    if (variable_instance_exists(id, "chaw") && is_real(chaw) && chaw > 0 && ds_map_exists(jamm_dead, chaw)) { chaw = -1; hasChaw = 0; }
                    if (variable_instance_exists(id, "hit_fish") && is_real(hit_fish) && hit_fish > 0 && ds_map_exists(jamm_dead, hit_fish)) hit_fish = -1;
                    if (variable_instance_exists(id, "inhale_target") && is_real(inhale_target) && inhale_target > 0 && ds_map_exists(jamm_dead, inhale_target)) inhale_target = -1;
                    if (variable_instance_exists(id, "nearest_big") && is_real(nearest_big) && nearest_big > 0 && ds_map_exists(jamm_dead, nearest_big)) nearest_big = -1;
                    if (variable_instance_exists(id, "nearest_glacier") && is_real(nearest_glacier) && nearest_glacier > 0 && ds_map_exists(jamm_dead, nearest_glacier)) nearest_glacier = -1;
                    if (variable_instance_exists(id, "nearest_piercer") && is_real(nearest_piercer) && nearest_piercer > 0 && ds_map_exists(jamm_dead, nearest_piercer)) nearest_piercer = -1;
                    if (variable_instance_exists(id, "nearby_magmer") && is_real(nearby_magmer) && nearby_magmer > 0 && ds_map_exists(jamm_dead, nearby_magmer)) nearby_magmer = -1;
                    if (variable_instance_exists(id, "nearest_swarmer") && is_real(nearest_swarmer) && nearest_swarmer > 0 && ds_map_exists(jamm_dead, nearest_swarmer)) nearest_swarmer = -1;
                    if (variable_instance_exists(id, "respawn_target") && is_real(respawn_target) && respawn_target > 0 && ds_map_exists(jamm_dead, respawn_target)) respawn_target = -1;
                    if (variable_instance_exists(id, "flee_target") && is_real(flee_target) && flee_target > 0 && ds_map_exists(jamm_dead, flee_target)) flee_target = -1;
                    if (variable_instance_exists(id, "large_target") && is_real(large_target) && large_target > 0 && ds_map_exists(jamm_dead, large_target)) large_target = -1;
                    if (variable_instance_exists(id, "turgeon") && is_real(turgeon) && turgeon > 0 && ds_map_exists(jamm_dead, turgeon)) turgeon = -1;
                    if (variable_instance_exists(id, "nearest_target") && is_real(nearest_target) && nearest_target > 0 && ds_map_exists(jamm_dead, nearest_target)) nearest_target = -1;
                    if (variable_instance_exists(id, "thrall_target_fish") && is_real(thrall_target_fish) && thrall_target_fish > 0 && ds_map_exists(jamm_dead, thrall_target_fish)) thrall_target_fish = -1;
                    if (variable_instance_exists(id, "lure_inst") && is_real(lure_inst) && lure_inst > 0 && ds_map_exists(jamm_dead, lure_inst)) lure_inst = -1;
                    if (variable_instance_exists(id, "next") && is_real(next) && next > 0 && ds_map_exists(jamm_dead, next)) next = -1;
                    if (variable_instance_exists(id, "prev") && is_real(prev) && prev > 0 && ds_map_exists(jamm_dead, prev)) prev = -1;
                    if (variable_instance_exists(id, "arm_l") && is_real(arm_l) && arm_l > 0 && ds_map_exists(jamm_dead, arm_l)) arm_l = -1;
                    if (variable_instance_exists(id, "arm_r") && is_real(arm_r) && arm_r > 0 && ds_map_exists(jamm_dead, arm_r)) arm_r = -1;
                    if (variable_instance_exists(id, "hope") && is_real(hope) && hope > 0 && ds_map_exists(jamm_dead, hope)) hope = -1;
                    if (variable_instance_exists(id, "snob") && is_real(snob) && snob > 0 && ds_map_exists(jamm_dead, snob)) snob = -1;
                    if (variable_instance_exists(id, "escort_target") && is_real(escort_target) && escort_target > 0 && ds_map_exists(jamm_dead, escort_target)) escort_target = -1;
                    if (variable_instance_exists(id, "potential") && is_real(potential) && potential > 0 && ds_map_exists(jamm_dead, potential)) potential = -1;
                    if (variable_instance_exists(id, "potential_target") && is_real(potential_target) && potential_target > 0 && ds_map_exists(jamm_dead, potential_target)) potential_target = -1;
                    if (variable_instance_exists(id, "mp3") && is_real(mp3) && mp3 > 0 && ds_map_exists(jamm_dead, mp3)) mp3 = -1;
                }
            }
            ds_map_destroy(jamm_dead);
            ds_list_destroy(jamm_out_ord);
            ds_list_destroy(jamm_out_rem);
            ds_list_destroy(jamm_view_ord);
            ds_list_destroy(jamm_view_rem);
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

    private static string DrawGuiEndGml(UndertaleData data, BenchmarkOptions options)
    {
        bool profile = options.Profile;
        bool trace = options.TraceFish > 0;
        bool hasFish = data.GameObjects.ByName("Fish") is not null;
        bool hasLure = data.GameObjects.ByName("Lure") is not null;
        bool hasCatSwim = data.GameObjects.ByName("CatSwim") is not null;
        string warmedUp = trace
            ? "true"
            : "(jamm_b_now - jamm_b_phase_start) >= (jamm_b_warmup_sec * 1000000) && jamm_b_warmup_frames >= 10";
        string finished = trace
            ? $"jamm_trace_n >= {options.TraceFish}"
            : "(jamm_b_now - jamm_b_capture_start) >= (jamm_b_capture_sec * 1000000) || ds_list_size(jamm_b_frame_list) >= jamm_b_max_samples";

        string applyFishCap = hasFish
            ? "if (jamm_b_fish_cap >= 0) jamm_b_apply_fish_cap(jamm_b_fish_cap);"
            : "";
        string deactivateHookClear = string.Join("\n                    ", new[]
        {
            hasLure ? "if (instance_exists(Lure) && Lure.fish > 0) { Lure.fish = -1; Lure.hookedFishSize = 0; }" : "",
            hasCatSwim ? "if (instance_exists(CatSwim) && CatSwim.holding > 0) CatSwim.holding = -1;" : "",
            hasFish ? "instance_deactivate_object(Fish);" : ""
        }.Where(s => s.Length > 0));
        string fishCountExpr = hasFish ? "instance_number(Fish)" : "0";

        return $$"""
        var jamm_b_now = get_timer();
        if (jamm_b_state == 1)
        {
            jamm_b_warmup_frames += 1;
            if ({{warmedUp}})
            {
                ds_list_clear(jamm_b_frame_list);
                ds_list_clear(jamm_b_loop_list);
                ds_list_clear(jamm_b_step_list);
                ds_list_clear(jamm_b_draw_list);
                ds_list_clear(jamm_b_fish_step_list);
                ds_list_clear(jamm_b_fish_count_list);
                jamm_b_step_samples = 0;
                jamm_b_fish_total = 0;

                if (jamm_b_fish_cap >= 0 || jamm_b_fish_mode == "deactivate")
                    global.jamm_b_freeze_spawners = 1;
                {{applyFishCap}}
                if (jamm_b_fish_mode == "nostep")
                {
                    global.jamm_b_fish_nostep = 1;
                    global.jamm_b_fish_nodraw = 0;
                }
                else if (jamm_b_fish_mode == "nodraw")
                {
                    global.jamm_b_fish_nostep = 0;
                    global.jamm_b_fish_nodraw = 1;
                }
                else if (jamm_b_fish_mode == "deactivate")
                {
                    global.jamm_b_fish_nostep = 0;
                    global.jamm_b_fish_nodraw = 0;
                    {{deactivateHookClear}}
                }
                else
                {
                    global.jamm_b_fish_nostep = 0;
                    global.jamm_b_fish_nodraw = 0;
                }

                global.jamm_b_fish_step_us = 0;
                global.jamm_b_fish_depth = 0;
                jamm_b_capture_start = jamm_b_now;
                jamm_b_prev_gui_t = jamm_b_now;
                jamm_b_has_step = 0;
                jamm_b_has_draw = 0;
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
            var jamm_b_step_us = -1;
            var jamm_b_draw_us = -1;
            var jamm_b_fstep_us = -1;
            var jamm_b_fcount = {{fishCountExpr}};
            if (jamm_b_has_draw)
            {
                jamm_b_draw_us = max(0, jamm_b_now - jamm_b_draw_start_t);
            }
            if (jamm_b_has_step)
            {
                jamm_b_loop_us = max(0, jamm_b_now - jamm_b_step_t);
                if (jamm_b_has_draw)
                    jamm_b_step_us = max(0, jamm_b_draw_start_t - jamm_b_step_t);
                jamm_b_fstep_us = max(0, global.jamm_b_fish_step_us);
                jamm_b_has_step = 0;
                jamm_b_fish_total += jamm_b_fcount;
                {{(trace ? FishTrace.Sample() : "")}}
            }
            jamm_b_has_draw = 0;
            ds_list_add(jamm_b_frame_list, jamm_b_frame_us);
            ds_list_add(jamm_b_loop_list, jamm_b_loop_us);
            ds_list_add(jamm_b_step_list, jamm_b_step_us);
            ds_list_add(jamm_b_draw_list, jamm_b_draw_us);
            ds_list_add(jamm_b_fish_step_list, jamm_b_fstep_us);
            ds_list_add(jamm_b_fish_count_list, jamm_b_fcount);

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
                var jamm_b_pres_list = ds_list_create();
                var jamm_b_i, jamm_b_fu, jamm_b_lu, jamm_b_pu, jamm_b_su, jamm_b_du, jamm_b_fsu, jamm_b_fc;
                for (jamm_b_i = 0; jamm_b_i < jamm_b_n; jamm_b_i += 1)
                {
                    jamm_b_fu = ds_list_find_value(jamm_b_frame_list, jamm_b_i);
                    jamm_b_lu = ds_list_find_value(jamm_b_loop_list, jamm_b_i);
                    jamm_b_sum += jamm_b_fu;
                    if (jamm_b_lu >= 0)
                    {
                        jamm_b_pu = max(0, jamm_b_fu - jamm_b_lu);
                        jamm_b_loop_sum += jamm_b_lu;
                        jamm_b_loop_n += 1;
                        jamm_b_pres_sum += jamm_b_pu;
                        ds_list_add(jamm_b_loop_sorted, jamm_b_lu);
                        ds_list_add(jamm_b_pres_list, jamm_b_pu);
                    }
                    else
                    {
                        ds_list_add(jamm_b_pres_list, -1);
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
                    var jamm_b_fish_grid = 0;
                    if (variable_global_exists("jamm_fg_enabled"))
                        jamm_b_fish_grid = global.jamm_fg_enabled;
                    file_text_write_string(jamm_b_f, "# fish_grid=" + string(jamm_b_fish_grid));
                    file_text_writeln(jamm_b_f);
                    if (variable_global_exists("jamm_ds_enabled"))
                    {
                        var ds_deact_n = 0;
                        if (variable_global_exists("jamm_ds_deact_n")) ds_deact_n = global.jamm_ds_deact_n;
                        var ds_n = 0;
                        if (variable_global_exists("jamm_ds_count")) ds_n = global.jamm_ds_count;
                        file_text_write_string(jamm_b_f, "# decor_sleep=" + string(global.jamm_ds_enabled) + " tracked=" + string(ds_n) + " sleeping_deact=" + string(ds_deact_n));
                        file_text_writeln(jamm_b_f);
                    }
                    if (variable_global_exists("jamm_perf_sleep"))
                    {
                        var fs_asleep = 0;
                        if (variable_global_exists("jamm_sleep_asleep_count")) fs_asleep = global.jamm_sleep_asleep_count;
                        var fs_wakes = 0;
                        if (variable_global_exists("jamm_sleep_wake_count")) fs_wakes = global.jamm_sleep_wake_count;
                        file_text_write_string(jamm_b_f, "# fish_sleep=" + string(global.jamm_perf_sleep) + " asleep=" + string(fs_asleep) + " wakes=" + string(fs_wakes));
                        file_text_writeln(jamm_b_f);
                    }
                    file_text_write_string(jamm_b_f, "# scene=" + jamm_b_scene + " requested=" + jamm_b_scene_req + " room=" + room_get_name(room) + " seed=" + string(jamm_b_seed) + " warmup_s=" + string(jamm_b_warmup_sec) + " capture_s=" + string(jamm_b_capture_sec) + " max_samples=" + string(jamm_b_max_samples));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, "# fish_mode=" + jamm_b_fish_mode + " fish_mode_requested=" + jamm_b_fish_mode_req + " fish_cap=" + string(jamm_b_fish_cap) + " fish_scale=" + string(jamm_b_fish_scale));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, "# samples=" + string(jamm_b_n) + " wall_us=" + string(round(jamm_b_wall_us)) + " avg_fps=" + string(jamm_b_avg_fps) + " room_speed=" + string(room_speed) + " step_samples=" + string(jamm_b_step_samples) + " draw_samples=" + string(jamm_b_n) + " draws_per_step=" + string(jamm_b_n / max(1, jamm_b_step_samples)) + " instances=" + string(instance_count) + " window=" + string(jamm_b_window_w) + "x" + string(jamm_b_window_h) + " application_surface=" + string(jamm_b_app_w) + "x" + string(jamm_b_app_h) + " fullscreen=" + string(jamm_b_fullscreen) + " render_size_normalized=" + string(jamm_b_size_ok));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, "# active_fish_mean=" + string(jamm_b_fish_total / max(1, jamm_b_loop_n)));
                    file_text_writeln(jamm_b_f);
                    var jamm_b_perf_mode = 0;
                    var jamm_b_perf_interval = 4;
                    var jamm_b_perf_eligible = 0;
                    var jamm_b_perf_skipped = 0;
                    if (variable_global_exists("jamm_perf_mode"))
                    {
                        jamm_b_perf_mode = global.jamm_perf_mode;
                        jamm_b_perf_interval = global.jamm_perf_interval;
                        jamm_b_perf_eligible = global.jamm_perf_eligible;
                        jamm_b_perf_skipped = global.jamm_perf_skipped;
                    }
                    file_text_write_string(jamm_b_f, "# perf_mode=" + string(jamm_b_perf_mode) + " interval=" + string(jamm_b_perf_interval) + " eligible_events=" + string(jamm_b_perf_eligible) + " skipped_events=" + string(jamm_b_perf_skipped));
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
                    file_text_write_string(jamm_b_f, jamm_b_stats_line(jamm_b_pres_list, "present_sleep_us"));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, jamm_b_stats_line(jamm_b_step_list, "step_us"));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, jamm_b_stats_line(jamm_b_draw_list, "draw_us"));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, jamm_b_stats_line(jamm_b_fish_step_list, "fish_step_us"));
                    file_text_writeln(jamm_b_f);
                    file_text_write_string(jamm_b_f, jamm_b_stats_line(jamm_b_fish_count_list, "fish_count"));
                    file_text_writeln(jamm_b_f);
                    if (jamm_b_warn != "")
                    {
                        file_text_write_string(jamm_b_f, "# warning=" + jamm_b_warn);
                        file_text_writeln(jamm_b_f);
                    }
                    file_text_write_string(jamm_b_f, "frame_index,frame_us,loop_us,present_sleep_us,step_us,draw_us,fish_step_us,fish_count");
                    file_text_writeln(jamm_b_f);
                    for (jamm_b_i = 0; jamm_b_i < jamm_b_n; jamm_b_i += 1)
                    {
                        jamm_b_fu = ds_list_find_value(jamm_b_frame_list, jamm_b_i);
                        jamm_b_lu = ds_list_find_value(jamm_b_loop_list, jamm_b_i);
                        jamm_b_pu = ds_list_find_value(jamm_b_pres_list, jamm_b_i);
                        jamm_b_su = ds_list_find_value(jamm_b_step_list, jamm_b_i);
                        jamm_b_du = ds_list_find_value(jamm_b_draw_list, jamm_b_i);
                        jamm_b_fsu = ds_list_find_value(jamm_b_fish_step_list, jamm_b_i);
                        jamm_b_fc = ds_list_find_value(jamm_b_fish_count_list, jamm_b_i);
                        file_text_write_string(jamm_b_f, string(jamm_b_i) + "," + string(round(jamm_b_fu)) + "," + string(round(jamm_b_lu)) + "," + string(round(jamm_b_pu)) + "," + string(round(jamm_b_su)) + "," + string(round(jamm_b_du)) + "," + string(round(jamm_b_fsu)) + "," + string(round(jamm_b_fc)));
                        file_text_writeln(jamm_b_f);
                    }
                    file_text_close(jamm_b_f);
                }
                if (jamm_b_screenshot)
                    screen_save("jamm_bench_shot_" + jamm_b_scene + ".png");

                {{(profile ? ProfileWriterGml() : "")}}

                ds_list_destroy(jamm_b_sorted);
                ds_list_destroy(jamm_b_loop_sorted);
                ds_list_destroy(jamm_b_pres_list);
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
                global.jamm_b_fish_nostep = 0;
                global.jamm_b_fish_nodraw = 0;
                global.jamm_b_freeze_spawners = 0;
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
