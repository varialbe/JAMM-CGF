using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jamm.Core.Building;
using Jamm.ModMaker;
using UndertaleModLib;

namespace Jamm.Core.ModKit;

/// <summary>Validated first-party recipe manifest loaded from a <c>jamm-recipe.json</c> package directory.</summary>
public sealed record RecipeManifest(
    string RootDirectory,
    string ManifestPath,
    int SchemaVersion,
    string Id,
    string Generator,
    string GeneratorVersion,
    string StableId,
    string Name,
    string Version,
    string Author,
    string Description,
    bool Default);

/// <summary>Loads, validates, and compiles pure-source first-party recipe packages (<c>jamm-recipe.json</c>).</summary>
public static partial class RecipePackage
{
    /// <summary>Manifest filename expected inside every first-party recipe package.</summary>
    public const string ManifestFileName = "jamm-recipe.json";

    /// <summary>Supported local archive generators referenced by <c>generator</c>.</summary>
    public static readonly IReadOnlySet<string> SupportedGenerators = new HashSet<string>(StringComparer.Ordinal)
    {
        "perf", "smooth", "hd-lite", "console", "silo"
    };

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();

    /// <summary>Loads and validates a <c>jamm-recipe.json</c> package directory.</summary>
    public static RecipeManifest Load(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Recipe package directory cannot be empty.");

        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Recipe directory '{directory}' was not found.");

        string manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Missing '{ManifestFileName}' in '{directory}'.", manifestPath);

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
            throw new InvalidDataException($"Invalid JSON in '{ManifestFileName}' at {ex.Path ?? "$"} (line {ex.LineNumber + 1}): {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{ManifestFileName} ($): Expected a JSON object {{ ... }}.");
            var obj = document.RootElement;

            int schemaVersion = OptionalInt(obj, "schemaVersion", 1);
            if (schemaVersion != 1)
                throw new InvalidDataException($"{ManifestFileName} ($.schemaVersion): Unsupported recipe schemaVersion {schemaVersion}; expected 1.");

            string id = RequireIdentifier(obj, "id", "$.id");
            string generator = RequireNonEmpty(obj, "generator", "$.generator").ToLowerInvariant();
            if (!SupportedGenerators.Contains(generator))
                throw new InvalidDataException($"{ManifestFileName} ($.generator): Unknown generator '{generator}'. Expected one of: {string.Join(", ", SupportedGenerators)}.");

            string generatorVersion = OptionalString(obj, "generatorVersion", "1");
            string stableId = OptionalString(obj, "stableId", id);
            if (!KeyRegex().IsMatch(stableId))
                throw new InvalidDataException($"{ManifestFileName} ($.stableId): Invalid identifier '{stableId}'.");

            string name = RequireNonEmpty(obj, "name", "$.name");
            string version = OptionalString(obj, "version", "1.0.0");
            string author = OptionalString(obj, "author", "JAMM");
            string description = OptionalString(obj, "description", "");
            bool isDefault = OptionalBool(obj, "default", false);

            return new RecipeManifest(root, manifestPath, schemaVersion, id, generator, generatorVersion, stableId, name, version, author, description, isDefault);
        }
    }

    /// <summary>Applies a validated recipe package onto a read-only legacy base archive and verifies the resulting <c>.win</c>.</summary>
    public static RecipeManifest Build(
        string packageDirectory,
        string basePath,
        string outputPath,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recipe = Load(packageDirectory);
        if (string.IsNullOrWhiteSpace(basePath) || !File.Exists(basePath))
            throw new FileNotFoundException($"Base archive '{basePath}' was not found. Configure a legacy 2023 base with 'cgfmm config set base' first.", basePath);

        string destination = Path.GetFullPath(outputPath);
        BuildFiles.ValidateNewOutput(destination, [basePath]);

        progress?.Invoke($"Loading legacy base '{Path.GetFileName(basePath)}' read-only…");
        using (var inspectionFile = GameFileLoader.Load(basePath, progress, cancellationToken))
        {
            var info = inspectionFile.Inspection;
            if (info.IsYyc || !info.BytecodeSupported || info.BytecodeVersion != 16)
                throw new InvalidDataException($"'{Path.GetFileName(basePath)}' is not a supported legacy VM bytecode 16 archive. Select the August 2023 legacy data.win.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var input = new CancellableGameStream(basePath, cancellationToken);
        using var data = UndertaleIO.Read(input, (_, _) => cancellationToken.ThrowIfCancellationRequested(), _ => cancellationToken.ThrowIfCancellationRequested());

        progress?.Invoke($"Applying recipe '{recipe.Name}' ({recipe.Generator})…");
        switch (recipe.Generator)
        {
            case "perf": PerfMod.Apply(data, applyFishGrid: false); break;
            case "smooth": SmoothMod.Apply(data); break;
            case "hd-lite": HdMod.Apply(data, null); break;
            case "console": ConsoleMod.Apply(data); break;
            case "silo": SiloMod.Apply(data, recipe.RootDirectory); break;
            default: throw new InvalidDataException($"Unsupported recipe generator '{recipe.Generator}'.");
        }

        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".jmr-{Guid.NewGuid():N}.tmp");
        try
        {
            progress?.Invoke($"Writing recipe archive '{Path.GetFileName(destination)}'…");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                UndertaleIO.Write(stream, data);

            ArchiveVerifier.Verify(temporary, progress: progress, cancellationToken: cancellationToken);
            BuildFiles.ValidateNewOutput(destination, [basePath]);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        return recipe;
    }

    private static string RequireIdentifier(JsonElement obj, string property, string path)
    {
        string value = RequireNonEmpty(obj, property, path);
        if (!KeyRegex().IsMatch(value))
            throw new InvalidDataException($"{ManifestFileName} ({path}): Invalid identifier '{value}'. Use only [a-z0-9_-]+.");
        return value;
    }

    private static string RequireNonEmpty(JsonElement obj, string property, string path)
    {
        if (!TryGet(obj, property, out var el) || el.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{ManifestFileName} ({path}): Missing required string field '{property}'.");
        string value = el.GetString()!;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{ManifestFileName} ({path}): Field '{property}' cannot be empty.");
        return value;
    }

    private static string OptionalString(JsonElement obj, string property, string fallback) =>
        TryGet(obj, property, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? fallback : fallback;

    private static int OptionalInt(JsonElement obj, string property, int fallback) =>
        TryGet(obj, property, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int value) ? value : fallback;

    private static bool OptionalBool(JsonElement obj, string property, bool fallback) =>
        TryGet(obj, property, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : fallback;

    private static bool TryGet(JsonElement obj, string property, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Null) { value = default; return false; }
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
