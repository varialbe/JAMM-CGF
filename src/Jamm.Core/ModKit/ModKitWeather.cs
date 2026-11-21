using System.Globalization;
using System.Text.Json;

namespace Jamm.Core.ModKit;

/// <summary>A weighted weather and the presentation and population it activates.</summary>
public sealed record ModKitWeather(string Id, string Name, int Weight, IReadOnlyList<string> Phases,
    int DurationMin, int DurationMax, double Transition, string? Sky, IReadOnlyList<WeatherLayer> Background,
    WeatherParticles? Particles, WeatherWater Water, string? Light, WeatherAudio? Music, WeatherAudio? Ambience,
    IReadOnlyList<string> Fish, bool ReplaceDayFish, WeatherHooks Hooks);

/// <summary>A sky or foreground sprite layer in world coordinates relative to the sea.</summary>
public sealed record WeatherLayer(string Sprite, string Plane, double X, double Y, double Parallax, double Scroll,
    double Fps, double Scale, double Opacity, bool Repeat, string TintStart, string TintEnd);

/// <summary>The built-in weather particle emitter parameters.</summary>
public sealed record WeatherParticles(string Kind, int Count, double Speed, string Color, double Opacity);

/// <summary>Surface tint and visual and buoyancy wave multiplier.</summary>
public sealed record WeatherWater(string Tint, double Wave);

/// <summary>A package-relative Ogg Vorbis asset and its playback settings.</summary>
public sealed record WeatherAudio(string Sound, bool Loop, double Crossfade, double Volume);

/// <summary>Optional GML files executed in the weather controller's scope.</summary>
public sealed record WeatherHooks(string? Start, string? Step, string? End);

public static partial class ModKitPackage
{
    private static ModKitWeather ParseWeather(string root, JsonElement element, string path,
        IReadOnlyDictionary<string, ModKitSprite> sprites, IReadOnlyList<ModKitFish> fish)
    {
        var obj = ExpectObject(element, path);
        var schedule = Section("schedule");
        var phases = ParseArray(schedule, "phases", path + ".schedule.phases", (value, key) =>
            Choice(value, key, "dawn", "day", "dusk", "night"));
        if (phases.Count == 0 && TryGetNonNull(schedule, "phases", out _))
            throw Error(path + ".schedule.phases", "Select at least one phase.");
        EnsureUniqueKeys(phases, path + ".schedule.phases");
        var (min, max) = TryGetNonNull(obj, "duration", out _) ? RequireRange(obj, "duration", path + ".duration") : (600, 1800);
        if (min < 1 || max > 3600 || min > max || min != Math.Truncate(min) || max != Math.Truncate(max))
            throw Error(path + ".duration", "Use ordered whole seconds from 1 to 3600.");

        var layers = ParseArray(obj, "background", path + ".background", (value, key) =>
        {
            var layer = ExpectObject(value, key);
            string sprite = RequireKey(layer, "sprite", key + ".sprite");
            if (!sprites.ContainsKey(sprite))
                throw Error(key + ".sprite", $"Unknown sprite '{sprite}'. Use a key from sprites[].");
            string tint = ReadWeatherColor(layer, "tintStart", key, "#ffffff")!;
            return new WeatherLayer(sprite, TryGetNonNull(layer, "plane", out var plane)
                    ? Choice(plane, key + ".plane", "sky", "foreground") : "sky",
                OptionalDouble(layer, "x", key + ".x", 0, -10000, 10000),
                OptionalDouble(layer, "y", key + ".y", -390, -10000, 10000),
                OptionalDouble(layer, "parallax", key + ".parallax", 0.15, 0, 1),
                OptionalDouble(layer, "scroll", key + ".scroll", 0, -600, 600),
                OptionalDouble(layer, "fps", key + ".fps", 0, 0, 60),
                OptionalDouble(layer, "scale", key + ".scale", 1, 0.05, 10),
                OptionalDouble(layer, "opacity", key + ".opacity", 1, 0, 1),
                OptionalBool(layer, "repeat", key + ".repeat", true), tint,
                ReadWeatherColor(layer, "tintEnd", key, tint)!);
        });
        var species = ParseArray(obj, "fish", path + ".fish", (value, key) =>
        {
            string? id = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var entry = fish.FirstOrDefault(f => f.Key == id);
            if (entry is null)
                throw Error(key, "Use a fish key declared in this package.");
            if (entry.Spawn.Area is not null || entry.Spawn.YMin < 1310 || entry.Spawn.YMax > 1950
                || entry.Spawn.XMin < -4000 || entry.Spawn.XMax > 4000 || entry.Spawn.Count > 24)
                throw Error(key, $"Weather fish '{id}' needs x offsets within -4000 to 4000, y within 1310 to 1950, and 1 to 24 fish.");
            return id!;
        });
        EnsureUniqueKeys(species, path + ".fish");
        var particles = Section("particles");
        var water = Section("water");
        var hooks = Section("hooks");
        int weight = OptionalInt(schedule, "weight", path + ".schedule.weight",
            OptionalInt(obj, "weight", path + ".weight", 1, min: 0), min: 0);
        if (weight > 1000)
            throw Error(path + ".schedule.weight", "Use a weight from 0 to 1000.");
        return new(RequireKey(obj, "id", path + ".id"), RequireNonEmptyString(obj, "name", path + ".name"), weight,
            phases.Count == 0 ? ["dawn", "day", "dusk"] : phases, (int)min, (int)max,
            OptionalDouble(obj, "transition", path + ".transition", 3, 0, 30),
            ReadWeatherColor(obj, "sky", path, null), layers,
            TryGetNonNull(obj, "particles", out _) ? new(
                Choice(particles.TryGetProperty("kind", out var kind) ? kind : default, path + ".particles.kind",
                    "rain", "wind", "glints", "mist"),
                ParticleCount(particles, path + ".particles.count"),
                OptionalDouble(particles, "speed", path + ".particles.speed", 1, 0.1, 4),
                ReadWeatherColor(particles, "color", path + ".particles", "#ffffff")!,
                OptionalDouble(particles, "opacity", path + ".particles.opacity", 0.45, 0, 1)) : null,
            new(ReadWeatherColor(water, "tint", path + ".water", "#ffffff")!,
                OptionalDouble(water, "wave", path + ".water.wave", 1, 0, 3)),
            ReadWeatherColor(obj, "light", path, null), Audio("music", 0.65), Audio("ambience", 0.3),
            species, OptionalBool(obj, "replaceDayFish", path + ".replaceDayFish", true),
            new(Hook("on_start"), Hook("on_step"), Hook("on_end")));

        // An empty object lets optional fields share the standard ModKit validators.
        JsonElement Section(string key) => TryGetNonNull(obj, key, out var section)
            ? ExpectObject(section, path + "." + key) : EmptyWeatherSection;

        string? Hook(string key) => OptionalAssetFile(root, hooks, key, path + ".hooks." + key, [".gml"]);

        WeatherAudio? Audio(string key, double volume)
        {
            if (!TryGetNonNull(obj, key, out var element)) return null;
            var audio = ExpectObject(element, path + "." + key);
            string file = RequireAssetFile(root, audio, "sound", path + "." + key + ".sound", [".ogg"]);
            using var stream = File.OpenRead(file);
            Span<byte> header = stackalloc byte[27];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length
                || !header[..4].SequenceEqual("OggS"u8) || header[4] != 0 || header[26] == 0)
                throw Error(path + "." + key + ".sound", "Use an Ogg Vorbis file.");
            Span<byte> segments = stackalloc byte[header[26]];
            Span<byte> packet = stackalloc byte[7];
            if (stream.ReadAtLeast(segments, segments.Length, throwOnEndOfStream: false) != segments.Length
                || segments[0] < packet.Length
                || stream.ReadAtLeast(packet, packet.Length, throwOnEndOfStream: false) != packet.Length
                || packet[0] != 1 || !packet[1..].SequenceEqual("vorbis"u8))
                throw Error(path + "." + key + ".sound", "Use an Ogg Vorbis file.");
            return new(file, OptionalBool(audio, "loop", path + "." + key + ".loop", true),
                OptionalDouble(audio, "crossfade", path + "." + key + ".crossfade", 2.5, 0, 30),
                OptionalDouble(audio, "volume", path + "." + key + ".volume", volume, 0, 1));
        }
    }

    private static int ParticleCount(JsonElement obj, string path)
    {
        int count = OptionalInt(obj, "count", path, 48, min: 0);
        if (count > 180) throw Error(path, "Use at most 180 particles.");
        return count;
    }

    private static readonly JsonElement EmptyWeatherSection = JsonSerializer.SerializeToElement(new Dictionary<string, int>());

    private static string Choice(JsonElement value, string path, params string[] options)
    {
        if (value.ValueKind == JsonValueKind.String && options.Contains(value.GetString(), StringComparer.Ordinal))
            return value.GetString()!;
        throw Error(path, $"Choose one of {string.Join(", ", options)}.");
    }

    private static string? ReadWeatherColor(JsonElement obj, string key, string path, string? fallback)
    {
        if (!TryGetNonNull(obj, key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 7 } color && color[0] == '#'
            && uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
            return color;
        throw Error(path + "." + key, "Use an RGB colour in #rrggbb format.");
    }
}
