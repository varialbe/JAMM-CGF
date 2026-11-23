using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using System.Text.RegularExpressions;

namespace Jamm.ModMaker;

/// <summary>Samples inclusive event-entry time on every Nth frame, optionally filtering to selected entries.</summary>
public static class GmlCpuProfiler
{
    /// <summary>Instruments object event entries and, optionally, scripts in place.</summary>
    public static int Instrument(UndertaleData data, bool includeScripts = false, int sampleRate = 16, string? onlyEntries = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (sampleRate < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Profile sample rate must be at least one.");
        var entries = new Dictionary<UndertaleCode, bool>();
        foreach (var obj in data.GameObjects)
        {
            for (int eventType = 0; eventType < obj.Events.Count; eventType++)
            {
                // Create events contain initialization work and are intentionally excluded.
                if (eventType == (int)EventType.Create) continue;
                foreach (var ev in obj.Events[eventType])
                foreach (var action in ev.Actions)
                {
                    if (action.CodeId is not { ParentEntry: null } code || code.Name.Content.Contains("obj_jamm", StringComparison.Ordinal)) continue;
                    bool renderAffected = eventType == (int)EventType.Draw;
                    entries[code] = entries.TryGetValue(code, out bool existing) ? existing || renderAffected : renderAffected;
                }
            }
        }

        foreach (var script in data.Scripts)
        {
            if (!includeScripts && script.Name.Content is not ("jamm_fg_build" or "jamm_fg_collect")) continue;
            var code = script.Code;
            if (code is null || code.ParentEntry is not null || code.Name.Content.Contains("obj_jamm", StringComparison.Ordinal)) continue;
            entries.TryAdd(code, false);
        }

        if (onlyEntries is not null)
        {
            var selected = onlyEntries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
            entries = entries.Where(pair => selected.Contains(pair.Key.Name.Content))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        if (entries.Count == 0) return 0;

        var compiler = new CompileGroup(data);
        int index = 0;
        foreach (var (original, drawAffected) in entries.OrderBy(pair => pair.Key.Name.Content, StringComparer.Ordinal))
        {
            string oldName = original.Name.Content;
            string label = oldName + (drawAffected ? " [render-affected]" : "");
            if (!oldName.StartsWith("gml_Script_", StringComparison.Ordinal))
            {
                string originalSource = ModObjects.Decompile(data, original);
                compiler.QueueCodeReplace(original, InlineEventGml(originalSource, label, sampleRate, index));
                index++;
                continue;
            }

            string helperName = $"__jamm_prof_original_{index}";
            string helperCodeName = $"gml_Script_{helperName}";

            // Keep the original body as a callable script. The event/script entry becomes a
            // wrapper, so its timer is accumulated even when the body executes exit or return.
            original.Name = data.Strings.MakeString(helperCodeName);
            if (data.CodeLocals.ByName(oldName) is { } locals) locals.Name = original.Name;
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(helperName), Code = original });

            var wrapper = UndertaleCode.CreateEmptyEntry(data, oldName);
            if (oldName.StartsWith("gml_Script_", StringComparison.Ordinal))
            {
                var resourceName = oldName["gml_Script_".Length..];
                var existingScript = data.Scripts.FirstOrDefault(script => script.Name.Content == resourceName && script.Code == original);
                if (existingScript is not null)
                    existingScript.Code = wrapper;
                else
                    data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(resourceName), Code = wrapper });
            }
            else
            {
                var attached = data.GameObjects.SelectMany(obj => obj.Events).SelectMany(ev => ev).SelectMany(ev => ev.Actions)
                    .Where(action => action.CodeId == original).ToList();
                foreach (var action in attached) action.CodeId = wrapper;
            }

            compiler.QueueCodeReplace(wrapper, WrapperGml(helperName, label, oldName.StartsWith("gml_Script_", StringComparison.Ordinal), sampleRate));
            index++;
        }

        ModObjects.Compile(compiler);
        return entries.Count;
    }

    private static string WrapperGml(string helperName, string label, bool script, int sampleRate)
    {
        string call;
        string result;
        if (script)
        {
            var cases = new List<string>();
            for (int count = 0; count <= 16; count++)
            {
                string arguments = string.Join(", ", Enumerable.Range(0, count).Select(i => $"argument[{i}]"));
                cases.Add($"case {count}: __jamm_prof_result = {helperName}({arguments}); break;");
            }
            call = "switch (argument_count) {\n" + string.Join("\n", cases) + "\n default: __jamm_prof_result = " + helperName + "(argument[0], argument[1], argument[2], argument[3], argument[4], argument[5], argument[6], argument[7], argument[8], argument[9], argument[10], argument[11], argument[12], argument[13], argument[14], argument[15]); break;\n}";
            result = "return __jamm_prof_result;";
        }
        else
        {
            call = helperName + "();";
            result = "";
        }

        return $$"""
            var __jamm_prof_key = "{{label}}";
            var __jamm_prof_sample = global.__jamm_prof_on;
            var __jamm_prof_start = 0;
            if (__jamm_prof_sample) __jamm_prof_start = get_timer();
            {{(script ? "var __jamm_prof_result;" : "")}}
            {{call}}
            if (__jamm_prof_sample)
            {
                var __jamm_prof_elapsed = (get_timer() - __jamm_prof_start) * {{sampleRate}};
                var __jamm_prof_us_old = ds_map_find_value(global.__jamm_prof_us, __jamm_prof_key);
                var __jamm_prof_n_old = ds_map_find_value(global.__jamm_prof_n, __jamm_prof_key);
                if (is_undefined(__jamm_prof_us_old)) __jamm_prof_us_old = 0;
                if (is_undefined(__jamm_prof_n_old)) __jamm_prof_n_old = 0;
                ds_map_set(global.__jamm_prof_us, __jamm_prof_key, __jamm_prof_us_old + __jamm_prof_elapsed);
                ds_map_set(global.__jamm_prof_n, __jamm_prof_key, __jamm_prof_n_old + {{sampleRate}});
            }
            {{result}}
            """;
    }

    private static string InlineEventGml(string source, string label, int sampleRate, int index)
    {
        string prefix = $"__jamm_pf_{index}_";
        string completion = $$"""
            if ({{prefix}}sample)
            {
                var {{prefix}}elapsed = (get_timer() - {{prefix}}start) * {{sampleRate}};
                var {{prefix}}us_old = ds_map_find_value(global.__jamm_prof_us, "{{label}}");
                var {{prefix}}n_old = ds_map_find_value(global.__jamm_prof_n, "{{label}}");
                if (is_undefined({{prefix}}us_old)) {{prefix}}us_old = 0;
                if (is_undefined({{prefix}}n_old)) {{prefix}}n_old = 0;
                ds_map_set(global.__jamm_prof_us, "{{label}}", {{prefix}}us_old + {{prefix}}elapsed);
                ds_map_set(global.__jamm_prof_n, "{{label}}", {{prefix}}n_old + {{sampleRate}});
            }
            """;
        string withExitTimers = Regex.Replace(source, @"\bexit\s*;", _ => "{\n" + completion + "\nexit;\n}", RegexOptions.CultureInvariant);
        return $$"""
            var {{prefix}}sample = global.__jamm_prof_on;
            var {{prefix}}start = 0;
            if ({{prefix}}sample) {{prefix}}start = get_timer();
            {{withExitTimers}}
            {{completion}}
            """;
    }
}
