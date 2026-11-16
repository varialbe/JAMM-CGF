using UndertaleModLib;

namespace Jamm.ModMaker;

/// <summary>Entry point: builds example JAMM mods (as complete data.win files) from an original data.win.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
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
                  perf    <original.win> <out.win>
                  smooth  <original.win> <out.win> [--debug]
                  hd      <original.win> <out.win> [--upscaled-pages <dir>]
                  export-pages <original.win> <dir>
                The original is opened read-only; output must be a new path.
                """);
            return 1;
        }
        string command = args[0];
        string original = Path.GetFullPath(args[1]);
        string? output = command is not ("dump" or "names" or "rooms" or "census") && args.Length > 2 ? Path.GetFullPath(args[2]) : null;
        if (output is not null && output == original)
        {
            Console.Error.WriteLine("Output must be a different file than the original.");
            return 1;
        }
        if (command is "perf" or "smooth" or "hd" or "profile")
        {
            if (output is null)
            {
                Console.Error.WriteLine($"'{command}' requires an original file and a new output file path.");
                return 1;
            }
            if (File.Exists(output))
            {
                Console.Error.WriteLine($"Output '{output}' already exists. Choose a new path to protect existing game files.");
                return 1;
            }
        }
        UndertaleData data = Load(original);
        switch (command)
        {
            case "inspect": Inspector.Run(data); return 0;
            case "dump": Dump.Run(data, args.Skip(2)); return 0;
            case "names": foreach (var c in data.Code) Console.WriteLine(c.Name.Content); return 0;
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
            case "export-pages": TexturePages.Export(data, output!); return 0;
            case "perf":
                string[] changed = ["gml_Object_Fish_Step_2", "gml_Script_fish_check_trawler",
                    "gml_Script_fish_avoid_fish", "gml_Object_SceneItem_Step_2",
                    "gml_Object_Magmer_Draw_0", "gml_Object_Volteen_Other_13", "gml_Object_Volteen_Other_10"];
                var before = changed.ToDictionary(n => n, n => data.Code.ByName(n)?.Instructions.Count ?? 0);
                PerfMod.Apply(data);
                foreach (string name in changed)
                    Console.WriteLine($"{name}: {before[name]} -> {data.Code.ByName(name)!.Instructions.Count} instructions (static size, not executed work).");
                Save(data, output!); return 0;
            case "smooth": SmoothMod.Apply(data, args.Contains("--debug")); Save(data, output!); return 0;
            case "profile":
                // Profiles a (possibly modded) data.win: instruments all object events.
                Profiler.Apply(data); Save(data, output!); return 0;
            case "hd":
                string? pages = OptionValue(args, "--upscaled-pages");
                HdMod.Apply(data, pages, args.Contains("--debug")); Save(data, output!); return 0;
            default: Console.Error.WriteLine($"Unknown command '{command}'."); return 1;
        }
    }

    private static string? OptionValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static UndertaleData Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return UndertaleIO.Read(stream, (warning, _) => { }, _ => { });
    }

    private static void Save(UndertaleData data, string path)
    {
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
            UndertaleIO.Write(stream, data);
        File.Move(temp, path, overwrite: true);
        Console.WriteLine($"Wrote {path} ({new FileInfo(path).Length:N0} bytes).");
    }
}
