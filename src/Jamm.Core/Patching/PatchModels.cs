using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jamm.Core.Patching;

/// <summary>A portable diff. These records have no UndertaleModLib dependencies.</summary>
public sealed record ModPatch(int FormatVersion, string Tool, DateTimeOffset CreatedUtc,
    PatchFile Mod, PatchFile Base, IReadOnlyList<string> Warnings, SortedDictionary<string, AssetChanges> Changes,
    SortedDictionary<string, string[]>? BaseAssets = null, IReadOnlyList<AssetReference>? References = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int AssetTypesVersion = 0);

/// <summary>Content-addressed source identity; no personal source paths are saved.</summary>
public sealed record PatchFile(string FileName, string Sha256, ulong Gen8Timestamp,
    string GmVersion, byte Bytecode, string? Name = null);

/// <summary>A named resource reference retained for checks without opening game archives.</summary>
public sealed record AssetReference(string Family, string Asset, string TargetFamily, string Target);

/// <summary>Ordinally ordered changes in one asset family; removed entries are matching keys.</summary>
public sealed record AssetChanges(IReadOnlyList<AssetPatch> Added, IReadOnlyList<AssetPatch> Changed,
    IReadOnlyList<string> Removed);

/// <summary>Full semantic snapshots plus leaf-level differences. Binary data is represented only by hashes.</summary>
public sealed record AssetPatch(string Name,
    [property: JsonConverter(typeof(OptionalJsonConverter))] JsonElement? Base, JsonElement Mod,
    IReadOnlyList<FieldChange> Fields, CodeText? BaseCode = null, CodeText? ModCode = null, int? OriginalIndex = null);

/// <summary>A JSON Pointer and its old/new value; missing sides differ from JSON null.</summary>
public sealed record FieldChange(string Path,
    [property: JsonConverter(typeof(OptionalJsonConverter))] JsonElement? Base,
    [property: JsonConverter(typeof(OptionalJsonConverter))] JsonElement? Mod);

/// <summary>Normalized source text and format, with counts and named parent/children for child code entries.</summary>
public sealed record CodeText(string Text, string Format, uint LocalsCount, ushort ArgumentsCount,
    string? Parent, IReadOnlyList<string> Children);

/// <summary>Source-generated, portable JSON encoding and version checking.</summary>
public static class PatchJson
{
    /// <summary>The current on-disk schema version.</summary>
    public const int FormatVersion = 2;

    /// <summary>Decompiler container-definition revision used to invalidate older analysis caches.</summary>
    public const int AssetTypesVersion = 3;

    /// <summary>Writes deterministic JSON for an already ordered patch.</summary>
    public static string Serialize(ModPatch patch) => JsonSerializer.Serialize(patch, PatchJsonContext.Default.ModPatch) + "\n";

    private static readonly string[] Families = ["code", "scripts", "objects", "rooms", "sprites", "texturePages",
        "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions"];

    /// <summary>Reads a patch without loading a game file. Unsupported versions must be recomputed.</summary>
    public static ModPatch Deserialize(string json)
    {
        var patch = JsonSerializer.Deserialize(json, PatchJsonContext.Default.ModPatch)
            ?? throw new InvalidDataException("The patch is empty. Recompute it with --force.");
        if (patch.FormatVersion != FormatVersion)
            throw new InvalidDataException($"Patch format {patch.FormatVersion} is unsupported. Recompute it with --force.");
        if (patch.Base is null || patch.Mod is null || patch.Changes is null || patch.Warnings is null || patch.BaseAssets is null || patch.References is null
            || string.IsNullOrWhiteSpace(patch.Base.Sha256) || string.IsNullOrWhiteSpace(patch.Mod.Sha256)
            || Families.Any(family => !patch.Changes.ContainsKey(family))
            || patch.Changes.Values.Any(x => x is null || x.Added is null || x.Changed is null || x.Removed is null
                || x.Added.Concat(x.Changed).Any(a => a is null || a.Name is null || a.Fields is null
                    || a.Mod.ValueKind == JsonValueKind.Undefined || a.Fields.Any(f => f is null || f.Path is null))))
            throw new InvalidDataException("The patch is incomplete. Recompute it with --force.");
        return patch;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ModPatch))]
internal partial class PatchJsonContext : JsonSerializerContext;

// Nullable<JsonElement> normally collapses explicit JSON null into an absent value on read.
// Preserve that distinction for field additions/removals across serialization round trips.
internal sealed class OptionalJsonConverter : JsonConverter<JsonElement?>
{
    public override bool HandleNull => true;
    public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }
    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    {
        if (value is { } element) element.WriteTo(writer); else writer.WriteNullValue();
    }
}
