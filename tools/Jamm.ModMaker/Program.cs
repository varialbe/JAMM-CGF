using System.Globalization;
using UndertaleModLib;

namespace Jamm.ModMaker;

/// <summary>Entry point: builds example JAMM mods (as complete data.win files) from an original data.win.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Mod creation failed: {ex.Message} Check the input file and choose a writable new output path.");
            if (args.Contains("--verbose")) Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("""
                jamm-modmaker — builds example mods for JAMM-CGF from YOUR original data.win
                  inspect <original.win>
                  perf    <original.win> <out.win> [--debug]
                  perf-census <original.win>
                  smooth  <original.win> <out.win> [--debug]
                  hd      <original.win> <out.win> [--upscaled-pages <dir>] [--debug] [--verbose]
                  console <original.win> <out.win>
                  bench   <silo.win> <out.win> --scene silo [--silo-mode cycle|save|reload] [--silo-video] [--silo-frames]
                  bench   <weather.win> <out.win> --scene weather
                  bench   <any.win> <out.win> --scene autoplay
                  bench   <legacy.win> <out.win> --scene maize
                  bench   <modded.win> <out.win> --scene money
                  bench   <endless-ocean.win> <out.win> --scene worldgen [--showcase]
                  bench   <weather-perf.win> <out.win> --scene stress [--duration <sec>, minimum 180]
                  bench   <in.win> <out.win> [--scene dock|sonar|deepwater] [--warmup <sec>] [--duration <sec>] [--max-samples <n>] [--out-file <name>] [--auto-exit|--no-exit] [--seed <n>] [--dock-x <x>] [--deep-x <x>] [--deep-y <y>] [--boat-slot <n>] [--deep-sonar] [--show-hud] [--profile] [--profile-scripts] [--trace-fish <ticks>] [--fish-grid]
                  export-pages <original.win> <dir>
                The original is opened read-only; output must be a new path.
                """);
            return 1;
        }
        string command = args[0];
        string original = Path.GetFullPath(args[1]);
        bool needsOutput = command is "perf" or "smooth" or "hd" or "console" or "bench" or "profile" or "export-pages";
        if (needsOutput && (args.Length < 3 || args[2].StartsWith("--", StringComparison.Ordinal)))
            throw new ArgumentException($"'{command}' requires an original file and a new output path before options.");
        string? output = needsOutput ? Path.GetFullPath(args[2]) : null;
        if (output == original)
            throw new ArgumentException("Output must be a different file than the original.");
        if (needsOutput && File.Exists(output))
            throw new IOException($"Output '{output}' already exists. Choose a new path to protect existing game files.");
        UndertaleData data = Load(original);
        switch (command)
        {
            case "perf-census": foreach (string line in PerfMode.Census(data)) Console.WriteLine(line); return 0;
            case "inspect": Inspector.Run(data); return 0;
            case "dump": Dump.Run(data, args.Skip(2)); return 0;
            case "names":
                if (args.Contains("--objects"))
                {
                    for (int i = 0; i < data.GameObjects.Count; i++)
                    {
                        var obj = data.GameObjects[i];
                        Console.WriteLine($"{i}\t{obj.Name.Content}\t{obj.ParentId?.Name.Content}");
                    }
                }
                else foreach (var c in data.Code) Console.WriteLine(c.Name.Content);
                return 0;
            case "census":
                {
                    var room = data.Rooms.ByName(args.Length > 2 ? args[2] : "NewWorld")!;
                    int drawType = (int)UndertaleModLib.Models.EventType.Draw;
                    bool HasDraw(UndertaleModLib.Models.UndertaleGameObject? o) { for (; o is not null; o = o.ParentId) if (o.Events[drawType].Count > 0) return true; return false; }
                    string Chain(UndertaleModLib.Models.UndertaleGameObject? o) { var l = new List<string>(); for (; o is not null; o = o.ParentId) l.Add(o.Name.Content); return string.Join(">", l); }
                    foreach (var g in room.GameObjects.GroupBy(i => i.ObjectDefinition).OrderByDescending(g => g.Count()).Take(30))
                        Console.WriteLine($"{g.Count(),5} {Chain(g.Key)} draw={(HasDraw(g.Key) ? "code" : "default")} sprite={g.Key?.Sprite?.Name.Content} visible={g.Key?.Visible}");
                    return 0;
                }
            case "rooms":
                foreach (var r in data.Rooms)
                    Console.WriteLine($"{r.Name.Content}: {r.GameObjects.Count} instances; mod instances: {string.Join(", ", r.GameObjects.Select((g, i) => (g, i)).Where(p => p.g.ObjectDefinition?.Name.Content.StartsWith("obj_jamm") == true).Select(p => $"{p.g.ObjectDefinition.Name.Content}@{p.i} id {p.g.InstanceID}"))}");
                return 0;
            case "export-pages": TexturePages.Export(data, output!, Console.WriteLine); return 0;
            case "perf":
                string[] changed = ["gml_Object_Fish_Step_2", "gml_Script_fish_check_trawler",
                    "gml_Script_fish_avoid_fish", "gml_Object_SceneItem_Step_2",
                    "gml_Object_Magmer_Draw_0", "gml_Object_Volteen_Other_13", "gml_Object_Volteen_Other_10"];
                var before = changed.ToDictionary(n => n, n => data.Code.ByName(n)?.Instructions.Count ?? 0);
                PerfMod.Apply(data, args.Contains("--debug"));
                foreach (string name in changed)
                    Console.WriteLine($"{name}: {before[name]} -> {data.Code.ByName(name)!.Instructions.Count} instructions (static size, not executed work).");
                Save(data, output!); return 0;
            case "smooth": SmoothMod.Apply(data, args.Contains("--debug")); Save(data, output!); return 0;
            case "console": ConsoleMod.Apply(data); Save(data, output!); return 0;
            case "bench" when MoneyScene.Requested(args): MoneyScene.Apply(data); Save(data, output!); return 0;
            case "bench" when AutoplayScene.Requested(args): AutoplayScene.Apply(data); Save(data, output!); return 0;
            case "bench" when WeatherScene.Requested(args): WeatherScene.Apply(data); Save(data, output!); return 0;
            case "bench" when OptionValue(args, "--scene") == "maize": MaizeScene.Apply(data); Save(data, output!); return 0;
            case "bench" when SiloScene.Requested(args): SiloScene.Apply(data, args); Save(data, output!); return 0;
            case "bench" when WorldgenScene.Requested(args): WorldgenScene.Apply(data, args.Contains("--showcase")); Save(data, output!); return 0;
            case "bench" when OptionValue(args, "--scene") == "stress":
                StressScene.Apply(data, OptionValue(args, "--duration"));
                Save(data, output!);
                return 0;
            case "bench":
                var benchOpts = ParseBenchOptions(args.Skip(3).ToArray());
                BenchmarkMod.Apply(data, benchOpts, Console.WriteLine);
                Save(data, output!, saved => VerifyBench(saved, benchOpts));
                return 0;
            case "profile":
                // Profiles a (possibly modded) data.win: instruments all object events.
                Profiler.Apply(data); Save(data, output!); return 0;
            case "hd":
                string? pages = OptionValue(args, "--upscaled-pages");
                if (output is null || args[2].StartsWith("--"))
                    throw new ArgumentException("hd needs <original.win> <out.win>; choose a new output path.");
                var baseline = TexturePages.Capture(data);
                HdMod.Apply(data, pages, args.Contains("--debug"));
                Save(data, output, saved => TexturePages.VerifyHd(baseline, saved, pages is not null, Console.WriteLine));
                return 0;
            default: Console.Error.WriteLine($"Unknown command '{command}'."); return 1;
        }
    }

    private static BenchmarkOptions ParseBenchOptions(string[] args)
    {
        var scene = BenchmarkScene.Dock;
        double warmup = 5.0;
        double duration = 15.0;
        int maxSamples = 50000;
        string? outFile = null;
        bool autoExit = true;
        int seed = 1337;
        double dockX = 815.0;
        double deepX = 21000.0;
        double deepY = 7200.0;
        int boatSlot = 7;
        bool deepSonar = false;
        bool showHud = false;
        bool profile = false;
        bool profileScripts = false;
        int traceFish = 0;
        bool fishGrid = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--verbose": break;
                case "--auto-exit": autoExit = true; break;
                case "--no-exit": autoExit = false; break;
                case "--deep-sonar": deepSonar = true; break;
                case "--show-hud": showHud = true; break;
                case "--profile": profile = true; break;
                case "--trace-fish":
                    traceFish = ParseInt(RequireValue(args, ref i, arg), arg);
                    if (traceFish < 1) throw new ArgumentException("--trace-fish requires at least one tick.");
                    break;
                case "--fish-grid": fishGrid = true; break;
                case "--profile-scripts": profile = true; profileScripts = true; break;
                case "--scene": scene = BenchmarkMod.ParseScene(RequireValue(args, ref i, arg)); break;
                case "--warmup": warmup = ParseDouble(RequireValue(args, ref i, arg), arg); break;
                case "--duration": duration = ParseDouble(RequireValue(args, ref i, arg), arg); break;
                case "--max-samples": maxSamples = ParseInt(RequireValue(args, ref i, arg), arg); break;
                case "--out-file": outFile = RequireValue(args, ref i, arg); break;
                case "--seed": seed = ParseInt(RequireValue(args, ref i, arg), arg); break;
                case "--dock-x": dockX = ParseDouble(RequireValue(args, ref i, arg), arg); break;
                case "--deep-x": deepX = ParseDouble(RequireValue(args, ref i, arg), arg); break;
                case "--deep-y": deepY = ParseDouble(RequireValue(args, ref i, arg), arg); break;
                case "--boat-slot": boatSlot = ParseInt(RequireValue(args, ref i, arg), arg); break;
                default: throw new ArgumentException($"Unknown bench option '{arg}'.");
            }
        }

        return new BenchmarkOptions(scene, warmup, duration, maxSamples, outFile, autoExit, seed, dockX, deepX, deepY, boatSlot, deepSonar, showHud, profile, profileScripts, traceFish, fishGrid);
    }

    private static string RequireValue(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} requires a value.");
        return args[++i];
    }

    private static double ParseDouble(string text, string option)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new ArgumentException($"Invalid number '{text}' for {option}.");

    private static int ParseInt(string text, string option)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new ArgumentException($"Invalid integer '{text}' for {option}.");

    private static void VerifyBench(UndertaleData saved, BenchmarkOptions opts)
    {
        var ctrl = saved.GameObjects.ByName(BenchmarkMod.ObjectName)
            ?? throw new InvalidDataException($"Saved archive missing '{BenchmarkMod.ObjectName}'.");
        if (!ctrl.Persistent)
            throw new InvalidDataException($"Saved '{BenchmarkMod.ObjectName}' is not persistent.");
        var newWorld = saved.Rooms.ByName("NewWorld")
            ?? throw new InvalidDataException("Saved archive missing 'NewWorld'.");
        if (!newWorld.GameObjects.Any(g => g.ObjectDefinition == ctrl))
            throw new InvalidDataException($"Saved 'NewWorld' does not contain '{BenchmarkMod.ObjectName}'.");
        Console.WriteLine($"bench: scene={BenchmarkMod.SceneName(opts.Scene)}, warmup={opts.WarmupSeconds}s, duration={opts.CaptureSeconds}s, max_samples={opts.MaxSamples}, auto_exit={opts.AutoExit}");
    }

    private static string? OptionValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
            throw new ArgumentException($"{name} needs a directory path.");
        return args[index + 1];
    }

    private static UndertaleData Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return UndertaleIO.Read(stream, (warning, _) => { }, _ => { });
    }

    private static void Save(UndertaleData data, string path, Action<UndertaleData>? verify = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) throw new IOException($"Output '{path}' already exists. Choose a new output path.");
        string temp = path + ".tmp";
        // CreateNew also rejects an existing symlink/hardlink at the temporary output path.
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            UndertaleIO.Write(stream, data);
        try
        {
            if (verify is not null)
            {
                using var saved = Load(temp);
                verify(saved);
            }
            File.Move(temp, path, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Console.WriteLine($"Wrote {path} ({new FileInfo(path).Length:N0} bytes).");
    }
}
