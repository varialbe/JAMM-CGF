using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jamm.Core.ModKit;

/// <summary>Fish spawn rectangle and target population count in the ocean.</summary>
public sealed record ModKitSpawnBox(double XMin, double XMax, double YMin, double YMax, int Count)
{
    /// <summary>Optional id of an area defined in the same package.</summary>
    public string? Area { get; init; }

    /// <summary>Optional room subrectangle relative to the area's origin.</summary>
    public ModKitAreaBounds? Within { get; init; }
}

/// <summary>Optional GML hook files attached to a ModKit fish.</summary>
public sealed record ModKitFishHooks(string? Create, string? Step, string? Caught, string? Draw);

/// <summary>A nonpersistent room object with optional event hooks.</summary>
public sealed record ModKitObject(string Key, string? Sprite, int Depth, string? Create, string? Step, string? Draw);

/// <summary>A fish definition in a ModKit package.</summary>
public sealed record ModKitFish(
    string Key,
    string Name,
    string Size,
    int SizeTab,
    string ParentObject,
    string SpritePath,
    int Frames,
    double Value,
    double Xp,
    double Stamina,
    double LungeSpeed,
    double LungeRange,
    double LungeRangeStop,
    double Speed,
    ModKitSpawnBox Spawn,
    bool Night,
    string Description,
    string CaughtDescription,
    string SeenDescription,
    string HiddenDescription,
    bool Exotic,
    int[]? Origin,
    ModKitFishHooks Hooks)
{
    /// <summary>Optional key of a composed sprite defined in the same package.</summary>
    public string? SpriteKey { get; init; }
}

/// <summary>Optional boat movement and capacity stats.</summary>
public sealed record ModKitBoatStats(double MaxSpeed, int Capacity, double MaxDistance, double MoveBonus);

/// <summary>A boat definition in a ModKit package.</summary>
public sealed record ModKitBoat(
    string Key,
    string Name,
    double Cost,
    string CardPath,
    string SpritePath,
    string Description,
    bool Unlocked,
    ModKitBoatStats Stats,
    int[]? Origin);

/// <summary>Optional GML hook files attached to a ModKit hat.</summary>
public sealed record ModKitHatHooks(string? Effect);

/// <summary>A hat (gear) definition in a ModKit package.</summary>
public sealed record ModKitHat(
    string Key,
    string Name,
    string SpritePath,
    double Value,
    double Weight,
    double RangeMin,
    double RangeMax,
    double OffsetX,
    double OffsetY,
    double HatalogOffsetX,
    double HatalogOffsetY,
    bool Mythic,
    string Description,
    int[]? Origin,
    ModKitHatHooks Hooks);

/// <summary>Optional GML hook files attached to a ModKit rod upgrade.</summary>
public sealed record ModKitUpgradeHooks(string? Apply, string? Step);

/// <summary>A rod upgrade definition in a ModKit package.</summary>
public sealed record ModKitUpgrade(
    string Key,
    string Name,
    string Description,
    string Description2,
    double Cost,
    string IconPath,
    int MaxStacks,
    ModKitUpgradeHooks Hooks);

/// <summary>Validated ModKit package manifest loaded from a package directory.</summary>
public sealed record ModKitManifest(
    string RootDirectory,
    string ManifestPath,
    string Id,
    string Name,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<ModKitFish> Fish,
    IReadOnlyList<ModKitBoat> Boats,
    IReadOnlyList<ModKitHat> Hats,
    IReadOnlyList<ModKitUpgrade> Upgrades)
{
    /// <summary>Room areas included in the package.</summary>
    public IReadOnlyList<ModKitArea> Areas { get; init; } = [];

    /// <summary>Weather declarations to install when JAMM builds a profile.</summary>
    public IReadOnlyList<ModKitWeather> Weathers { get; init; } = [];

    /// <summary>Build-time sprite recipes included in the package.</summary>
    public IReadOnlyList<ModKitSprite> Sprites { get; init; } = [];

    /// <summary>Custom room objects available to area placements.</summary>
    public IReadOnlyList<ModKitObject> Objects { get; init; } = [];
}

/// <summary>Parses, validates, and scaffolds ModKit folder packages (<c>jamm-mod.json</c> + art + GML hooks).</summary>
public static partial class ModKitPackage
{
    /// <summary>Manifest filename expected inside every ModKit package directory.</summary>
    public const string ManifestFileName = "jamm-mod.json";

    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();

    /// <summary>Loads and validates a ModKit package directory.</summary>
    public static ModKitManifest Load(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("ModKit package directory cannot be empty.");

        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"ModKit directory '{directory}' was not found. Check the folder path or create one with 'cgfmm modkit new <dir>'.");

        string manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Missing '{ManifestFileName}' in '{directory}'. Add a jamm-mod.json manifest or run 'cgfmm modkit new <dir>'.", manifestPath);

        string json = File.ReadAllText(manifestPath, Encoding.UTF8);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Invalid JSON in '{ManifestFileName}' at {ex.Path ?? "$"} (line {ex.LineNumber + 1}): {ex.Message} Fix the JSON syntax and run 'cgfmm modkit check'.",
                ex);
        }

        using (document)
        {
            var rootObj = ExpectObject(document.RootElement, "$");
            string id = RequireKey(rootObj, "id", "$.id");
            string name = RequireNonEmptyString(rootObj, "name", "$.name");
            string version = OptionalString(rootObj, "version", "$.version", "1.0.0");
            string author = OptionalString(rootObj, "author", "$.author", "");
            string description = OptionalString(rootObj, "description", "$.description", "");

            var areas = ParseArray(rootObj, "areas", "$.areas", ParseArea);
            var sprites = ParseArray(rootObj, "sprites", "$.sprites", (el, path) => ParseSprite(root, el, path));
            EnsureUniqueKeys(areas.Select(a => ModKitBuilder.SanitizeIdent(a.Id)), "$.areas");
            EnsureUniqueKeys(sprites.Select(s => ModKitBuilder.SanitizeIdent(s.Key)), "$.sprites");
            var spritesByKey = sprites.ToDictionary(s => s.Key, StringComparer.Ordinal);
            var objects = ParseArray(rootObj, "objects", "$.objects", (el, path) => ParseObject(root, el, path, spritesByKey));
            var fish = ParseArray(rootObj, "fish", "$.fish", (el, path) => ParseFish(root, el, path, spritesByKey));
            foreach (var entry in fish.Where(f => f.Spawn.Area is not null))
            {
                var area = areas.FirstOrDefault(a => a.Id == entry.Spawn.Area)
                    ?? throw Error("$.fish", $"Fish '{entry.Key}' references unknown area '{entry.Spawn.Area}'. Use an id from areas[].");
                if (entry.Spawn.Within is { } region && (region.Right > area.Bounds.W || region.Bottom > area.Bounds.H))
                    throw Error("$.fish", $"Fish '{entry.Key}' has a 'within' rectangle outside area '{area.Id}'.");
            }
            var boats = ParseArray(rootObj, "boats", "$.boats", (el, path) => ParseBoat(root, el, path));
            var hats = ParseArray(rootObj, "hats", "$.hats", (el, path) => ParseHat(root, el, path));
            var upgrades = ParseArray(rootObj, "upgrades", "$.upgrades", (el, path) => ParseUpgrade(root, el, path));

            EnsureUniqueKeys(fish.Select(f => f.Key), "$.fish");
            EnsureUniqueKeys(boats.Select(b => b.Key), "$.boats");
            EnsureUniqueKeys(hats.Select(h => h.Key), "$.hats");
            EnsureUniqueKeys(upgrades.Select(u => u.Key), "$.upgrades");
            EnsureUniqueKeys(objects.Select(o => ModKitBuilder.SanitizeIdent(o.Key)), "$.objects");

            var weathers = ParseArray(rootObj, "weathers", "$.weathers", (el, path) => ParseWeather(root, el, path, spritesByKey, fish));
            EnsureUniqueKeys(weathers.Select(w => ModKitBuilder.SanitizeIdent(w.Id)), "$.weathers");

            return new ModKitManifest(root, manifestPath, id, name, version, author, description, fish, boats, hats, upgrades)
                { Areas = areas, Sprites = sprites, Objects = objects, Weathers = weathers };
        }
    }

    private static ModKitObject ParseObject(string root, JsonElement element, string path, IReadOnlyDictionary<string, ModKitSprite> sprites)
    {
        var obj = ExpectObject(element, path);
        string key = RequireKey(obj, "key", $"{path}.key");
        string? sprite = TryGetNonNull(obj, "sprite", out _) ? RequireKey(obj, "sprite", $"{path}.sprite") : null;
        if (sprite is not null && !sprites.ContainsKey(sprite))
            throw Error($"{path}.sprite", $"Unknown sprite recipe '{sprite}'. Use a key from sprites[].");
        int depth = OptionalInt(obj, "depth", $"{path}.depth", 0);
        JsonElement hooks = TryGetNonNull(obj, "hooks", out var value) ? ExpectObject(value, $"{path}.hooks") : default;
        string? Hook(string name) => hooks.ValueKind == JsonValueKind.Object
            ? OptionalAssetFile(root, hooks, name, $"{path}.hooks.{name}", [".gml"]) : null;
        return new(key, sprite, depth, Hook("create"), Hook("step"), Hook("draw"));
    }

    /// <summary>Creates a starter ModKit package folder for the requested template (<c>fish</c>, <c>upgrade</c>, <c>boat</c>, or <c>hat</c>).</summary>
    public static string Scaffold(string directory, string template = "fish")
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Target directory cannot be empty.");

        string normalized = template.Trim().ToLowerInvariant();
        if (normalized is not ("fish" or "upgrade" or "boat" or "hat"))
            throw new ArgumentException($"Unknown template '{template}'. Choose fish, upgrade, boat, or hat.");

        string root = Path.GetFullPath(directory);
        string manifestPath = Path.Combine(root, ManifestFileName);
        if (File.Exists(manifestPath))
            throw new InvalidOperationException($"'{ManifestFileName}' already exists in '{directory}'. Choose a new folder so existing mod files are not overwritten.");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "art"));
        Directory.CreateDirectory(Path.Combine(root, "hooks"));

        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        string id = Regex.Replace(folderName.ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-', '_');
        if (string.IsNullOrEmpty(id)) id = "my-mod";
        string displayName = string.Join(' ', folderName.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        if (string.IsNullOrEmpty(displayName)) displayName = "My Mod";

        object manifestObj;
        switch (normalized)
        {
            case "fish":
                File.WriteAllBytes(Path.Combine(root, "art", "sample_fish.png"), CreateSampleFishPng(48, 32));
                File.WriteAllText(Path.Combine(root, "hooks", "sample_step.gml"),
                    "// Optional step hook for your fish (runs each frame inside the fish instance).\n" +
                    "// Example: image_blend = c_white;\n");
                manifestObj = new
                {
                    id,
                    name = displayName,
                    version = "1.0.0",
                    author = "Mod Author",
                    description = "Adds a custom fish created with JAMM ModKit.",
                    fish = new[]
                    {
                        new
                        {
                            key = "sample-fish",
                            name = "Sample Fish",
                            size = "small",
                            sprite = "art/sample_fish.png",
                            frames = 1,
                            value = 250,
                            xp = 50,
                            stamina = 40,
                            lungeSpeed = 8,
                            lungeRange = 60,
                            speed = 1.5,
                            spawn = new { x = new[] { 1100, 2200 }, y = new[] { 1400, 1900 }, count = 4 },
                            night = false,
                            description = "A friendly starter fish swimming near the dock.",
                            caughtDescription = "Caught the Sample Fish!",
                            hooks = new { create = (string?)null, step = "hooks/sample_step.gml", caught = (string?)null }
                        }
                    }
                };
                break;

            case "upgrade":
                File.WriteAllBytes(Path.Combine(root, "art", "sample_upgrade.png"), CreateSampleIconPng(50, 50, 255, 205, 65));
                File.WriteAllText(Path.Combine(root, "hooks", "sample_apply.gml"),
                    "// Called by Rod Other_10 whenever upgrades are recomputed.\n" +
                    "// argument0 = number of stacks of this upgrade installed on the current rod.\n" +
                    "Rod.upgrades[1] += 1.5 * argument0;\n");
                manifestObj = new
                {
                    id,
                    name = displayName,
                    version = "1.0.0",
                    author = "Mod Author",
                    description = "Adds a custom rod upgrade created with JAMM ModKit.",
                    upgrades = new[]
                    {
                        new
                        {
                            key = "sample-upgrade",
                            name = "Swift Crank",
                            description = "Increases reel speed by +1.5 per stack.",
                            description2 = "Faster reeling.",
                            cost = 0.25,
                            icon = "art/sample_upgrade.png",
                            maxStacks = 3,
                            hooks = new { apply = "hooks/sample_apply.gml", step = (string?)null }
                        }
                    }
                };
                break;

            case "boat":
                File.WriteAllBytes(Path.Combine(root, "art", "sample_boat.png"), CreateSampleBoatPng(200, 72));
                File.WriteAllBytes(Path.Combine(root, "art", "sample_boat_card.png"), CreateSampleBoatPng(100, 36));
                manifestObj = new
                {
                    id,
                    name = displayName,
                    version = "1.0.0",
                    author = "Mod Author",
                    description = "Adds a custom boat created with JAMM ModKit.",
                    boats = new[]
                    {
                        new
                        {
                            key = "sample-boat",
                            name = "Wooden Skiff",
                            cost = 15000,
                            card = "art/sample_boat_card.png",
                            sprite = "art/sample_boat.png",
                            description = "A nimble wooden skiff.#Easy to steer.",
                            stats = new { maxSpeed = 5, capacity = 6, maxDistance = 5000 }
                        }
                    }
                };
                break;

            default: // hat
                File.WriteAllBytes(Path.Combine(root, "art", "sample_hat.png"), CreateSampleIconPng(96, 72, 220, 70, 90));
                File.WriteAllText(Path.Combine(root, "hooks", "sample_effect.gml"),
                    "// Runs in Gear Other_15 when this hat is equipped.\n" +
                    "value_scalar[0] = 1.25;\n");
                manifestObj = new
                {
                    id,
                    name = displayName,
                    version = "1.0.0",
                    author = "Mod Author",
                    description = "Adds a custom hat created with JAMM ModKit.",
                    hats = new[]
                    {
                        new
                        {
                            key = "sample-hat",
                            name = "Lucky Cap",
                            sprite = "art/sample_hat.png",
                            value = 2500,
                            weight = 5,
                            rangeMin = 0.0,
                            rangeMax = 0.75,
                            description = "Small fish sell for 25% more.",
                            hooks = new { effect = "hooks/sample_effect.gml" }
                        }
                    }
                };
                break;
        }

        string formatted = JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, formatted + Environment.NewLine, Encoding.UTF8);
        return manifestPath;
    }

    private static ModKitFish ParseFish(string root, JsonElement element, string path, IReadOnlyDictionary<string, ModKitSprite> sprites)
    {
        var obj = ExpectObject(element, path);
        string key = RequireKey(obj, "key", $"{path}.key");
        string name = RequireNonEmptyString(obj, "name", $"{path}.name");
        string sizeRaw = RequireNonEmptyString(obj, "size", $"{path}.size");
        (string size, int sizeTab, string parent, double defaultStamina) = sizeRaw.ToLowerInvariant() switch
        {
            "small" => ("small", 0, "FishSmall", 50.0),
            "medium" => ("medium", 1, "FishMedium", 70.0),
            "large" => ("large", 2, "FishLarge", 50.0),
            "huge" => ("huge", 3, "FishHuge", 100.0),
            _ => throw Error($"{path}.size", $"Unknown fish size '{sizeRaw}'. Use one of: small, medium, large, huge.")
        };

        int frames = OptionalInt(obj, "frames", $"{path}.frames", 1, min: 1);
        string sprite = RequireNonEmptyString(obj, "sprite", $"{path}.sprite");
        string? spriteKey = null;
        string spritePath = "";
        if (sprites.TryGetValue(sprite, out var composition))
        {
            if (TryGetNonNull(obj, "frames", out _) && frames != composition.Frames.Count)
                throw Error($"{path}.frames", $"Sprite '{sprite}' has {composition.Frames.Count} recipe frame(s). Match that count or omit 'frames'.");
            spriteKey = sprite;
            frames = composition.Frames.Count;
        }
        else if (Path.GetExtension(sprite).Equals(".png", StringComparison.OrdinalIgnoreCase))
            spritePath = ValidateAssetPath(root, sprite, $"{path}.sprite", [".png"], frames);
        else
            throw Error($"{path}.sprite", $"Sprite '{sprite}' was not found. Use a key from sprites[] or a relative PNG path.");
        double value = OptionalDouble(obj, "value", $"{path}.value", 100.0, min: 0.0);
        double xp = OptionalDouble(obj, "xp", $"{path}.xp", 25.0, min: 0.0);
        double stamina = OptionalDouble(obj, "stamina", $"{path}.stamina", defaultStamina, min: 1.0);
        double lungeSpeed = OptionalDouble(obj, "lungeSpeed", $"{path}.lungeSpeed", 8.0, min: 0.0);
        double lungeRange = OptionalDouble(obj, "lungeRange", $"{path}.lungeRange", 60.0, min: 0.0);
        double lungeRangeStop = OptionalDouble(obj, "lungeRangeStop", $"{path}.lungeRangeStop", lungeRange + 5.0, min: lungeRange);
        double speed = OptionalDouble(obj, "speed", $"{path}.speed", 1.5, min: 0.0);
        var spawn = ParseSpawnBox(obj, $"{path}.spawn");
        bool night = OptionalBool(obj, "night", $"{path}.night", false);
        string description = OptionalString(obj, "description", $"{path}.description", $"{name}.");
        string caughtDesc = OptionalString(obj, "caughtDescription", $"{path}.caughtDescription", description);
        string seenDesc = OptionalString(obj, "seenDescription", $"{path}.seenDescription", description);
        string hiddenDesc = OptionalString(obj, "hiddenDescription", $"{path}.hiddenDescription", "Little is known about this fish.");
        bool exotic = OptionalBool(obj, "exotic", $"{path}.exotic", false);
        int[]? origin = OptionalOrigin(obj, "origin", $"{path}.origin");
        if (spriteKey is not null && origin is not null)
            throw Error($"{path}.origin", $"Set the origin on composed sprite '{spriteKey}' in sprites[].");

        ModKitFishHooks hooks = new(null, null, null, null);
        if (TryGetNonNull(obj, "hooks", out var hooksEl))
        {
            var hooksObj = ExpectObject(hooksEl, $"{path}.hooks");
            hooks = new(
                OptionalAssetFile(root, hooksObj, "create", $"{path}.hooks.create", [".gml"]),
                OptionalAssetFile(root, hooksObj, "step", $"{path}.hooks.step", [".gml"]),
                OptionalAssetFile(root, hooksObj, "caught", $"{path}.hooks.caught", [".gml"]),
                OptionalAssetFile(root, hooksObj, "draw", $"{path}.hooks.draw", [".gml"]));
        }

        return new ModKitFish(
            key, name, size, sizeTab, parent, spritePath, frames,
            value, xp, stamina, lungeSpeed, lungeRange, lungeRangeStop, speed,
            spawn, night, description, caughtDesc, seenDesc, hiddenDesc, exotic, origin, hooks) { SpriteKey = spriteKey };
    }

    private static ModKitSpawnBox ParseSpawnBox(JsonElement parent, string path)
    {
        if (!TryGetNonNull(parent, "spawn", out var spawnEl))
            throw Error(path, "Missing required 'spawn' object with { \"x\": [x1, x2], \"y\": [y1, y2], \"count\": N }.");

        var obj = ExpectObject(spawnEl, path);
        if (TryGetNonNull(obj, "area", out _))
        {
            string area = RequireKey(obj, "area", $"{path}.area");
            if (TryGetNonNull(obj, "x", out _) || TryGetNonNull(obj, "y", out _))
                throw Error(path, "Use either an area id or x and y ranges for a fish spawn.");
            int population = OptionalInt(obj, "count", $"{path}.count", 3, min: 1);
            ModKitAreaBounds? within = null;
            if (TryGetNonNull(obj, "within", out var region))
            {
                var rect = ExpectObject(region, $"{path}.within");
                within = new(RequireInt(rect, "x", $"{path}.within.x", 0), RequireInt(rect, "y", $"{path}.within.y", 0),
                    RequireInt(rect, "w", $"{path}.within.w", 1), RequireInt(rect, "h", $"{path}.within.h", 1));
            }
            return new(0, 0, 0, 0, population) { Area = area, Within = within };
        }
        var (x1, x2) = RequireRange(obj, "x", $"{path}.x");
        var (y1, y2) = RequireRange(obj, "y", $"{path}.y");
        int count = OptionalInt(obj, "count", $"{path}.count", 3, min: 1);
        return new ModKitSpawnBox(Math.Min(x1, x2), Math.Max(x1, x2), Math.Min(y1, y2), Math.Max(y1, y2), count);
    }

    private static ModKitBoat ParseBoat(string root, JsonElement element, string path)
    {
        var obj = ExpectObject(element, path);
        string key = RequireKey(obj, "key", $"{path}.key");
        string name = RequireNonEmptyString(obj, "name", $"{path}.name");
        double cost = OptionalDouble(obj, "cost", $"{path}.cost", 15000.0, min: 0.0);
        string cardPath = RequireAssetFile(root, obj, "card", $"{path}.card", [".png"], expectedFrames: 1);
        string spritePath = RequireAssetFile(root, obj, "sprite", $"{path}.sprite", [".png"], expectedFrames: 1);
        string description = OptionalString(obj, "description", $"{path}.description", $"{name}.");
        bool unlocked = OptionalBool(obj, "unlocked", $"{path}.unlocked", false);
        int[]? origin = OptionalOrigin(obj, "origin", $"{path}.origin");

        ModKitBoatStats stats = new(4.0, 4, 4500.0, 0.0);
        if (TryGetNonNull(obj, "stats", out var statsEl))
        {
            var statsObj = ExpectObject(statsEl, $"{path}.stats");
            stats = new(
                OptionalDouble(statsObj, "maxSpeed", $"{path}.stats.maxSpeed", 4.0, min: 0.1),
                OptionalInt(statsObj, "capacity", $"{path}.stats.capacity", 4, min: 1),
                OptionalDouble(statsObj, "maxDistance", $"{path}.stats.maxDistance", 4500.0, min: 500.0),
                OptionalDouble(statsObj, "moveBonus", $"{path}.stats.moveBonus", 0.0, min: 0.0));
        }

        return new ModKitBoat(key, name, cost, cardPath, spritePath, description, unlocked, stats, origin);
    }

    private static ModKitHat ParseHat(string root, JsonElement element, string path)
    {
        var obj = ExpectObject(element, path);
        string key = RequireKey(obj, "key", $"{path}.key");
        string name = RequireNonEmptyString(obj, "name", $"{path}.name");
        string spritePath = RequireAssetFile(root, obj, "sprite", $"{path}.sprite", [".png"], expectedFrames: 1);
        double value = OptionalDouble(obj, "value", $"{path}.value", 1500.0, min: 0.0);
        double weight = OptionalDouble(obj, "weight", $"{path}.weight", 5.0, min: 0.0);
        double rangeMin = OptionalDouble(obj, "rangeMin", $"{path}.rangeMin", 0.0, min: 0.0, max: 1.0);
        double rangeMax = OptionalDouble(obj, "rangeMax", $"{path}.rangeMax", 0.75, min: rangeMin, max: 1.0);
        double offsetX = OptionalDouble(obj, "offsetX", $"{path}.offsetX", 14.0);
        double offsetY = OptionalDouble(obj, "offsetY", $"{path}.offsetY", -42.0);
        double hatalogOffsetX = OptionalDouble(obj, "hatalogOffsetX", $"{path}.hatalogOffsetX", 0.0);
        double hatalogOffsetY = OptionalDouble(obj, "hatalogOffsetY", $"{path}.hatalogOffsetY", 0.0);
        bool mythic = OptionalBool(obj, "mythic", $"{path}.mythic", false);
        string description = OptionalString(obj, "description", $"{path}.description", $"{name}.");
        int[]? origin = OptionalOrigin(obj, "origin", $"{path}.origin");

        ModKitHatHooks hooks = new((string?)null);
        if (TryGetNonNull(obj, "hooks", out var hooksEl))
        {
            var hooksObj = ExpectObject(hooksEl, $"{path}.hooks");
            hooks = new(OptionalAssetFile(root, hooksObj, "effect", $"{path}.hooks.effect", [".gml"]));
        }

        return new ModKitHat(key, name, spritePath, value, weight, rangeMin, rangeMax, offsetX, offsetY, hatalogOffsetX, hatalogOffsetY, mythic, description, origin, hooks);
    }

    private static ModKitUpgrade ParseUpgrade(string root, JsonElement element, string path)
    {
        var obj = ExpectObject(element, path);
        string key = RequireKey(obj, "key", $"{path}.key");
        string name = RequireNonEmptyString(obj, "name", $"{path}.name");
        string description = OptionalString(obj, "description", $"{path}.description", $"{name}.");
        string description2 = OptionalString(obj, "description2", $"{path}.description2", description);
        double cost = OptionalDouble(obj, "cost", $"{path}.cost", 0.25, min: 0.0);
        string iconPath = RequireAssetFile(root, obj, "icon", $"{path}.icon", [".png"], expectedFrames: 1);
        int maxStacks = OptionalInt(obj, "maxStacks", $"{path}.maxStacks", 1, min: 1);

        ModKitUpgradeHooks hooks = new(null, null);
        if (TryGetNonNull(obj, "hooks", out var hooksEl))
        {
            var hooksObj = ExpectObject(hooksEl, $"{path}.hooks");
            hooks = new(
                OptionalAssetFile(root, hooksObj, "apply", $"{path}.hooks.apply", [".gml"]),
                OptionalAssetFile(root, hooksObj, "step", $"{path}.hooks.step", [".gml"]));
        }

        return new ModKitUpgrade(key, name, description, description2, cost, iconPath, maxStacks, hooks);
    }

    private static void EnsureUniqueKeys(IEnumerable<string> keys, string arrayPath)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (!seen.Add(key))
                throw Error(arrayPath, $"Duplicate key '{key}'. Each item in {arrayPath} must have a unique 'key'.");
        }
    }

    private static List<T> ParseArray<T>(JsonElement obj, string property, string path, Func<JsonElement, string, T> parseItem)
    {
        if (!TryGetNonNull(obj, property, out var arr))
            return [];
        if (arr.ValueKind != JsonValueKind.Array)
            throw Error(path, $"Expected an array for '{property}'.");

        var list = new List<T>();
        int idx = 0;
        foreach (var item in arr.EnumerateArray())
        {
            list.Add(parseItem(item, $"{path}[{idx}]"));
            idx++;
        }
        return list;
    }

    private static JsonElement ExpectObject(JsonElement el, string path) =>
        el.ValueKind == JsonValueKind.Object ? el : throw Error(path, "Expected a JSON object { ... }.");

    private static string RequireKey(JsonElement obj, string property, string path)
    {
        string value = RequireNonEmptyString(obj, property, path);
        if (!KeyRegex().IsMatch(value))
            throw Error(path, $"Invalid identifier '{value}'. Use only lowercase letters, digits, underscores, or hyphens ([a-z0-9_-]+).");
        return value;
    }

    private static string RequireNonEmptyString(JsonElement obj, string property, string path)
    {
        if (!TryGetNonNull(obj, property, out var el) || el.ValueKind != JsonValueKind.String)
            throw Error(path, $"Missing required string field '{property}'.");
        string value = el.GetString()!;
        if (string.IsNullOrWhiteSpace(value))
            throw Error(path, $"Field '{property}' cannot be empty.");
        return value;
    }

    private static string OptionalString(JsonElement obj, string property, string path, string fallback)
    {
        if (!TryGetNonNull(obj, property, out var el)) return fallback;
        if (el.ValueKind != JsonValueKind.String)
            throw Error(path, $"Expected a string for '{property}'.");
        return el.GetString() ?? fallback;
    }

    private static bool OptionalBool(JsonElement obj, string property, string path, bool fallback)
    {
        if (!TryGetNonNull(obj, property, out var el)) return fallback;
        if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Error(path, $"Expected true or false for '{property}'.");
        return el.GetBoolean();
    }

    private static int OptionalInt(JsonElement obj, string property, string path, int fallback, int min = int.MinValue)
    {
        if (!TryGetNonNull(obj, property, out var el)) return fallback;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out int value))
            throw Error(path, $"Expected an integer for '{property}'.");
        if (value < min)
            throw Error(path, $"'{property}' must be at least {min} (found {value}).");
        return value;
    }

    private static double OptionalDouble(JsonElement obj, string property, string path, double fallback, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        if (!TryGetNonNull(obj, property, out var el)) return fallback;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out double value) || double.IsNaN(value) || double.IsInfinity(value))
            throw Error(path, $"Expected a finite number for '{property}'.");
        if (value < min || value > max)
            throw Error(path, $"'{property}' must be between {min} and {max} (found {value}).");
        return value;
    }

    private static (double Min, double Max) RequireRange(JsonElement obj, string property, string path)
    {
        if (!TryGetNonNull(obj, property, out var el) || el.ValueKind != JsonValueKind.Array || el.GetArrayLength() != 2)
            throw Error(path, $"Expected a two-number array [min, max] for '{property}'.");
        double a = ReadNumber(el[0], $"{path}[0]");
        double b = ReadNumber(el[1], $"{path}[1]");
        return (a, b);
    }

    private static int[]? OptionalOrigin(JsonElement obj, string property, string path)
    {
        if (!TryGetNonNull(obj, property, out var el)) return null;
        if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() != 2)
            throw Error(path, $"Expected a two-integer array [x, y] for '{property}'.");
        if (!el[0].TryGetInt32(out int x) || !el[1].TryGetInt32(out int y))
            throw Error(path, $"Origin coordinates in '{property}' must be integers.");
        return [x, y];
    }

    private static double ReadNumber(JsonElement el, string path)
    {
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out double value) || double.IsNaN(value) || double.IsInfinity(value))
            throw Error(path, "Expected a finite number.");
        return value;
    }

    private static string RequireAssetFile(string root, JsonElement obj, string property, string path, string[] extensions, int expectedFrames = 0)
    {
        string relative = RequireNonEmptyString(obj, property, path);
        return ValidateAssetPath(root, relative, path, extensions, expectedFrames);
    }

    private static string? OptionalAssetFile(string root, JsonElement obj, string property, string path, string[] extensions)
    {
        if (!TryGetNonNull(obj, property, out var el)) return null;
        if (el.ValueKind != JsonValueKind.String)
            throw Error(path, $"Expected a string path or null for '{property}'.");
        string relative = el.GetString()!;
        if (string.IsNullOrWhiteSpace(relative)) return null;
        return ValidateAssetPath(root, relative, path, extensions, expectedFrames: 0);
    }

    private static string ValidateAssetPath(string root, string relative, string path, string[] extensions, int expectedFrames)
    {
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
            throw Error(path, $"Asset path '{relative}' must be a relative path inside the package directory (no '..' or absolute paths).");

        string full = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw Error(path, $"Asset path '{relative}' resolves outside the package directory.");

        string ext = Path.GetExtension(full);
        if (!extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw Error(path, $"Asset '{relative}' must have extension {string.Join(" or ", extensions)}.");

        if (!File.Exists(full))
            throw Error(path, $"Referenced file '{relative}' was not found inside the package directory. Create or fix the path in jamm-mod.json.");

        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            ValidatePng(full, relative, path, expectedFrames);

        return full;
    }

    private static void ValidatePng(string fullPath, string relative, string jsonPath, int expectedFrames)
    {
        byte[] header = new byte[24];
        using (var fs = File.OpenRead(fullPath))
        {
            if (fs.Read(header, 0, 24) < 24 || !header.AsSpan(0, 8).SequenceEqual(PngSignature)
                || header[12] != (byte)'I' || header[13] != (byte)'H' || header[14] != (byte)'D' || header[15] != (byte)'R')
            {
                throw Error(jsonPath, $"File '{relative}' is not a valid PNG image. Export it as a standard PNG file.");
            }
        }
        int width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        if (width <= 0 || height <= 0)
            throw Error(jsonPath, $"PNG '{relative}' has invalid dimensions {width}x{height}.");
        if (expectedFrames > 1 && width % expectedFrames != 0)
            throw Error(jsonPath, $"PNG '{relative}' is {width}x{height}, which is not evenly divisible by frames={expectedFrames}. Make the horizontal sprite strip width a multiple of {expectedFrames}.");
    }

    private static bool TryGetNonNull(JsonElement obj, string property, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Null)
                {
                    value = default;
                    return false;
                }
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static InvalidDataException Error(string jsonPath, string message) =>
        new($"{ManifestFileName} ({jsonPath}): {message}");

    private static byte[] CreateSampleFishPng(int w, int h)
    {
        byte[] rgba = new byte[w * h * 4];
        double cx = w * 0.45, cy = h * 0.5, rx = w * 0.32, ry = h * 0.32;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = (x - cx) / rx, dy = (y - cy) / ry;
                bool body = (dx * dx) + (dy * dy) <= 1.0;
                bool tail = x > w * 0.72 && Math.Abs(y - cy) <= (x - w * 0.72) * 0.7;
                if (!body && !tail) continue;
                int i = (y * w + x) * 4;
                rgba[i] = 245; rgba[i + 1] = 175; rgba[i + 2] = 55; rgba[i + 3] = 255;
                double edx = x - w * 0.25, edy = y - h * 0.42;
                if ((edx * edx) + (edy * edy) < 2.2 * 2.2)
                { rgba[i] = 25; rgba[i + 1] = 25; rgba[i + 2] = 35; }
            }
        return EncodePng(w, h, rgba);
    }

    private static byte[] CreateSampleBoatPng(int w, int h)
    {
        byte[] rgba = new byte[w * h * 4];
        for (int y = h / 2; y < h - 4; y++)
            for (int x = 6; x < w - 6; x++)
            {
                int inset = (y - h / 2) / 2;
                if (x < 6 + inset || x >= w - 6 - inset) continue;
                int i = (y * w + x) * 4;
                rgba[i] = 155; rgba[i + 1] = 100; rgba[i + 2] = 55; rgba[i + 3] = 255;
            }
        return EncodePng(w, h, rgba);
    }

    private static byte[] CreateSampleIconPng(int w, int h, byte r, byte g, byte b)
    {
        byte[] rgba = new byte[w * h * 4];
        for (int y = 6; y < h - 6; y++)
            for (int x = 6; x < w - 6; x++)
            {
                int i = (y * w + x) * 4;
                bool border = x < 9 || x >= w - 9 || y < 9 || y >= h - 9;
                rgba[i] = border ? (byte)255 : r;
                rgba[i + 1] = border ? (byte)255 : g;
                rgba[i + 2] = border ? (byte)255 : b;
                rgba[i + 3] = 255;
            }
        return EncodePng(w, h, rgba);
    }

    private static byte[] EncodePng(int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(PngSignature);
        WriteChunk(ms, "IHDR"u8, w =>
        {
            Span<byte> buf = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(buf[0..4], width);
            BinaryPrimitives.WriteInt32BigEndian(buf[4..8], height);
            buf[8] = 8; // bit depth
            buf[9] = 6; // RGBA
            w.Write(buf);
        });
        byte[] raw = new byte[height * (1 + width * 4)];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (1 + width * 4) + 1, width * 4);
        byte[] compressed;
        using (var zms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(zms, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            compressed = zms.ToArray();
        }
        WriteChunk(ms, "IDAT"u8, w => w.Write(compressed));
        WriteChunk(ms, "IEND"u8, _ => { });
        return ms.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, Action<Stream> writeData)
    {
        using var body = new MemoryStream();
        body.Write(type);
        writeData(body);
        byte[] bytes = body.ToArray();
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, bytes.Length - 4);
        output.Write(len);
        output.Write(bytes);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(bytes));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
