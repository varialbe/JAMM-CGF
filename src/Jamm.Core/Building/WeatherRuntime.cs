using System.Globalization;
using System.Text;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

internal static class WeatherRuntime
{
    internal const string Controller = "obj_jamm_weather";
    internal static readonly string[] PublicScripts = ["jamm_weather_current", "jamm_weather_name", "jamm_weather_command", "jamm_weather_intensity"];
    private static readonly string[] InternalScripts =
    [
        "schedule", "slot", "find", "active", "change", "population", "gate", "restore_fish", "audio", "draw", "particles", "color", "waves", "music_gain"
    ];

    internal static void Apply(UndertaleData data, UndertaleData ancestor, BuildPlan plan,
        IReadOnlyDictionary<string, CodeText> baseline, Dictionary<string, string> recompile,
        Dictionary<string, string> generated, List<string> decisions)
    {
        var declarations = plan.Assets.Where(a => a.Family == "code" && a.Patch is not null && WeatherDeclaration.IsDeclaration(a.Name))
            .Select(a => (Definition: WeatherDeclaration.Read(a.Name, a.Gml), a.Source))
            .OrderBy(d => d.Definition.Package + ":" + d.Definition.Weather.Id, StringComparer.Ordinal).ToArray();
        if (declarations.Length == 0) return;

        var controller = ModObjects.Create(data, Controller, persistent: true);
        ModObjects.PlaceInstance(data, "NewWorld", controller, first: true);
        AddEvent(EventType.Create, 0, Gml("create") + "\n" + Registry(declarations, data, ancestor, plan));
        AddEvent(EventType.Step, (uint)EventSubtypeStep.BeginStep, Gml("step"));
        AddEvent(EventType.Draw, 0, """
            if (room != NewWorld || view_current != 0) exit;
            jamm_weather_draw(1);
            jamm_weather_particles();
            """);
        AddEvent(EventType.Destroy, 0, """
            if (!variable_instance_exists(id, "weather_vanilla_fish")) exit;
            jamm_weather_change(-1, true);
            ds_map_destroy(weather_vanilla_fish);
            ds_list_destroy(weather_parked);
            """);
        AddEvent(EventType.Other, (uint)EventSubtypeOther.RoomEnd, """
            jamm_weather_change(-1, true);
            weather_slot_owner = -1;
            """);

        foreach (string script in PublicScripts.Concat(InternalScripts.Select(s => "jamm_weather_" + s)))
        {
            string code = "gml_Script_" + script;
            if (data.Scripts.ByName(script) is null)
                data.Scripts.Add(new() { Name = data.Strings.MakeString(script), Code = UndertaleCode.CreateEmptyEntry(data, code) });
            else if (!PublicScripts.Contains(script, StringComparer.Ordinal))
                throw new InvalidDataException($"Weather script '{script}' already exists. Remove the conflicting runtime and rebuild.");
            Put(code, Gml(script["jamm_weather_".Length..]));
        }

        // These anchors are shared by vanilla 2023 and the known legacy packs. Patch the selected
        // merged text, so adjacent changes by other mods stay in place.
        Patch("gml_Object_Fish_Step_2", "speed = spd;",
            "if (jamm_weather_gate()) exit;\nspeed = spd;");
        Patch("gml_Object_ParallaxBackground_Draw_0", "sky_color, sky_color, sky_color, sky_color,",
            "jamm_weather_sky, jamm_weather_sky, jamm_weather_sky, jamm_weather_sky,",
            "var jamm_weather_sky = jamm_weather_color(sky_color, 0);\n");
        Patch("gml_Object_ParallaxBackground_Draw_0", "alpha = 1 - power(",
            "if (view_current == 0) with (obj_jamm_weather) jamm_weather_draw(0);\nalpha = 1 - power(");
        Patch("gml_Object_Girl_Step_0", "audio_sound_gain(background_music, gain * power(background_music_volume, 2), 0);",
            "audio_sound_gain(background_music, jamm_weather_music_gain(gain * power(background_music_volume, 2)), 0);");
        Patch("gml_Object_Sea_Step_0", "x = origin + (sin(phase) * SCALE);",
            "x = origin + (sin(phase) * SCALE * jamm_weather_waves());\n" + Gml("sea"));
        Patch("gml_Script_x_pos_to_y_off", "return ty;", "return ty * jamm_weather_waves();");
        Patch("gml_Script_color_from_depth", "return make_color_rgb(val * 255, val * 255, val * val * 240);",
            "return jamm_weather_color(make_color_rgb(val * 255, val * 255, val * val * 240), 2);");

        if (data.GameObjects.ByName(ConsoleMod.ObjectName) is null)
            foreach (var (entry, body) in ConsoleMod.Install(data))
                Put(entry.Name.Content, body);
        const string console = "gml_Object_obj_jamm_console_Step_1";
        Patch(console, "else if (jamm_cmd == \"clear\")", """
            else if (jamm_cmd == "weather" || string_pos("weather ", jamm_cmd) == 1)
            {
                ds_list_add(jamm_console_log, jamm_weather_command(string_delete(jamm_cmd, 1, 8)));
            }
            else if (jamm_cmd == "clear")
            """);
        const string help = "ds_list_add(jamm_console_log, \"  help        : list commands\");";
        Patch(console, help, help + "\n    ds_list_add(jamm_console_log, \"  weather <id|clear|auto> : set dock weather\");");
        decisions.Add($"Weather API: installed one controller for {declarations.Length} weather(s) and verified all vanilla hook anchors.");

        void AddEvent(EventType type, uint subtype, string body) =>
            Put(controller.EventHandlerFor(type, subtype, data).Name.Content, body);

        void Put(string name, string body)
        {
            recompile[name] = body;
            generated[name] = body;
        }

        void Patch(string name, string anchor, string replacement, string prefix = "")
        {
            if (!recompile.TryGetValue(name, out string? body))
                body = baseline.GetValueOrDefault(name)?.Text;
            Put(name, prefix + ReplaceAnchor(name, body, anchor, replacement));
        }
    }

    internal static string ReplaceAnchor(string name, string? body, string anchor, string replacement)
    {
        int index = body?.IndexOf(anchor, StringComparison.Ordinal) ?? -1;
        if (index < 0 || body!.IndexOf(anchor, index + anchor.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException($"Weather API could not locate a unique anchor in '{name}'. Use the supported legacy base and disable the mod that replaces this code.");
        return string.Concat(body.AsSpan(0, index), replacement, body.AsSpan(index + anchor.Length));
    }

    internal static string Gml(string name)
    {
        using var stream = typeof(WeatherRuntime).Assembly.GetManifestResourceStream($"Jamm.Core.Building.Weather.{name}.gml")
            ?? throw new InvalidOperationException($"Missing Weather API script '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static int PhaseMask(IEnumerable<string> phases) =>
        phases.Aggregate(0, (mask, phase) => mask | (phase switch
        {
            "dawn" => 1, "day" => 2, "dusk" => 4, "night" => 8,
            _ => throw new InvalidDataException($"Unsupported weather phase '{phase}'. Check weathers[].schedule.phases.")
        }));

    private static string Registry((WeatherDeclaration Definition, int Source)[] definitions, UndertaleData data, UndertaleData ancestor, BuildPlan plan)
    {
        var gml = new StringBuilder();
        gml.AppendLine($"weather_count = {definitions.Length};");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var tables = ResourceCatalog.Tables(data);
        var fishParent = ancestor.GameObjects.ByName("Fish");
        foreach (var obj in ancestor.GameObjects.Where(o => IsFish(o, fishParent)))
            if (data.GameObjects.ByName(obj.Name.Content) is not null)
                gml.AppendLine($"ds_map_add(weather_vanilla_fish, asset_get_index({Quote(obj.Name.Content)}), 1);");

        for (int i = 0; i < definitions.Length; i++)
        {
            var (declaration, source) = definitions[i];
            var w = declaration.Weather;
            string id = declaration.Package + ":" + w.Id;
            if (!ids.Add(id))
                throw new InvalidDataException($"Duplicate weather id '{id}'. Keep one version of its ModKit package.");

            Row("weather_key", Quote(id));
            Row("weather_short", Quote(w.Id));
            Row("weather_display", Quote(w.Name));
            Fields("weather_rule", N(w.Weight), N(PhaseMask(w.Phases)), N(w.DurationMin), N(w.DurationMax),
                N(w.Transition), w.ReplaceDayFish ? "1" : "0");
            Fields("weather_paint", Color(w.Sky), Color(w.Water.Tint), Color(w.Light), N(w.Water.Wave));
            Fields("weather_hook", Lookup(w.Hooks.Start, "scripts"), Lookup(w.Hooks.Step, "scripts"), Lookup(w.Hooks.End, "scripts"));
            Audio("weather_music", w.Music);
            Audio("weather_ambience", w.Ambience);
            var p = w.Particles;
            Fields("weather_emitter", N(p?.Kind switch { "rain" => 0, "wind" => 1, "glints" => 2, "mist" => 3, _ => -1 }),
                N(p?.Count ?? 0), N(p?.Speed ?? 1), Color(p?.Color), N(p?.Opacity ?? 0));
            Row("weather_layers", N(w.Background.Count));
            for (int j = 0; j < w.Background.Count; j++)
            {
                var l = w.Background[j];
                FieldsAt("weather_layer", j * 12, Lookup(l.Sprite, "sprites"), l.Plane == "sky" ? "0" : "1",
                    N(l.X), N(l.Y), N(l.Parallax), N(l.Scroll), N(l.Fps), N(l.Scale), N(l.Opacity),
                    l.Repeat ? "1" : "0", Color(l.TintStart), Color(l.TintEnd));
            }

            Row("weather_species", N(declaration.Population.Count));
            for (int j = 0; j < declaration.Population.Count; j++)
            {
                var fish = declaration.Population[j];
                var name = Asset(fish.Object, "objects");
                if (!IsFish(data.GameObjects.ByName(name), data.GameObjects.ByName("Fish")))
                    throw new InvalidDataException($"Weather '{id}' requires '{name}' to inherit from Fish. Rebuild its source mod.");
                var s = fish.Spawn;
                FieldsAt("weather_fish", j * 6, $"asset_get_index({Quote(name)})", N(s.Count), N(s.XMin), N(s.XMax), N(s.YMin), N(s.YMax));
            }

            void Row(string name, string value) => gml.AppendLine($"{name}[{i}] = {value};");
            void Fields(string name, params string[] fields) => FieldsAt(name, 0, fields);
            void FieldsAt(string name, int offset, params string[] fields)
            {
                for (int k = 0; k < fields.Length; k++)
                    gml.AppendLine($"{name}[{i}, {offset + k}] = {fields[k]};");
            }

            void Audio(string array, WeatherAudio? clip) => Fields(array,
                Lookup(clip?.Sound, "sounds"), clip?.Loop == true ? "1" : "0", N(clip?.Crossfade ?? 0), N(clip?.Volume ?? 0));

            string Lookup(string? name, string family) =>
                name is null ? "-1" : $"asset_get_index({Quote(Asset(name, family))})";

            string Asset(string name, string family)
            {
                name = plan.AssetRenames.GetValueOrDefault((source, family, name)) ?? name;
                var table = tables[family];
                if (ResourceCatalog.Find(table, name) is null)
                    throw new InvalidDataException($"Weather '{id}' needs {family}/{name}. Keep that asset in the profile and rebuild.");
                return name;
            }
        }
        return gml.ToString();
    }

    private static bool IsFish(UndertaleGameObject? obj, UndertaleGameObject? fish)
    {
        if (fish is null) return false;
        for (var current = obj; current is not null; current = current.ParentId)
            if (current == fish) return true;
        return false;
    }

    private static string Quote(string text) => ModKitBuilder.GmlString(text.Replace('\r', ' ').Replace('\n', ' '));
    private static string N(double value) => value.ToString("0.################", CultureInfo.InvariantCulture);
    private static string Color(string? color) => color is null ? "-1" : "$" + color[5..7] + color[3..5] + color[1..3];
}
