using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// Debug-only build: every object event and script records how long ran since the previous event started
/// (including engine work in between) and attributes it to that previous event. Every 10 seconds the
/// totals are appended to jamm_profile.txt in the game's save folder. Never ship this.
/// </summary>
public static class Profiler
{
    public static void Apply(UndertaleData data)
    {
        var targets = data.Code.Where(c => c.ParentEntry is null && (c.Name.Content.StartsWith("gml_Object_") || c.Name.Content.StartsWith("gml_Script_")) && !c.Name.Content.Contains("obj_jamm")).ToList();
        var sources = new Dictionary<UndertaleCode, string>();
        foreach (var code in targets)
        {
            try { sources[code] = ModObjects.Decompile(data, code); }
            catch { }
        }
        var failed = new HashSet<UndertaleCode>();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var compiler = new CompileGroup(data);
            foreach (var (code, gml) in sources)
                if (!failed.Contains(code))
                    compiler.QueueCodeReplace(code, Prelude(code.Name.Content) + gml);
            var result = compiler.Compile();
            if (result.Successful) break;
            foreach (var error in result.Errors) if (error.Code is not null) failed.Add(error.Code);
            Console.WriteLine($"profile: {failed.Count} entries left uninstrumented after attempt {attempt + 1}");
        }
        // Restore entries that failed so they keep their original bytecode semantics.
        var restore = new CompileGroup(data);
        foreach (var code in failed) restore.QueueCodeReplace(code, sources[code]);
        if (failed.Count > 0) ModObjects.Compile(restore);

        var obj = ModObjects.Create(data, "obj_jamm_profiler", persistent: true);
        var compile = new CompileGroup(data);
        compile.QueueCodeReplace(obj.EventHandlerFor(EventType.Create, 0u, data), """
            if (instance_number(object_index) > 1) { instance_destroy(); exit; }
            global.__pf = ds_map_create();
            global.__pf_last = "start";
            global.__pf_t = get_timer();
            global.__pf_on = 1;
            jamm_pf_time = current_time;
            """);
        compile.QueueCodeReplace(obj.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, data), """
            jamm_n = get_timer();
            jamm_old = ds_map_find_value(global.__pf, global.__pf_last);
            if (is_undefined(jamm_old)) jamm_old = 0;
            ds_map_replace(global.__pf, global.__pf_last, jamm_old + (jamm_n - global.__pf_t));
            global.__pf_last = "frame-end (present, sleep, engine)";
            global.__pf_t = jamm_n;
            if ((current_time - jamm_pf_time) >= 10000)
            {
                jamm_f = file_text_open_append("jamm_profile.txt");
                file_text_write_string(jamm_f, "=== window " + string(current_time - jamm_pf_time) + " ms, fps " + string(fps) + ", instances " + string(instance_count));
                file_text_writeln(jamm_f);
                jamm_k = ds_map_find_first(global.__pf);
                while (!is_undefined(jamm_k))
                {
                    file_text_write_string(jamm_f, string(ds_map_find_value(global.__pf, jamm_k) div 1000) + " " + string(jamm_k));
                    file_text_writeln(jamm_f);
                    jamm_k = ds_map_find_next(global.__pf, jamm_k);
                }
                file_text_close(jamm_f);
                ds_map_clear(global.__pf);
                jamm_pf_time = current_time;
            }
            """);
        ModObjects.Compile(compile);
        foreach (string room in new[] { "NewWorld", "room11" })
            ModObjects.PlaceInstance(data, room, obj, first: true);
        Console.WriteLine($"profile: instrumented {sources.Count - failed.Count} of {targets.Count} object events.");
    }

    private static string Prelude(string name) => $$"""
        if (variable_global_exists("__pf_on"))
        {
            var __pf_n = get_timer();
            var __pf_o = ds_map_find_value(global.__pf, global.__pf_last);
            if (is_undefined(__pf_o)) __pf_o = 0;
            ds_map_replace(global.__pf, global.__pf_last, __pf_o + (__pf_n - global.__pf_t));
            global.__pf_last = "{{name}}";
            global.__pf_t = __pf_n;
        }

        """;
}
