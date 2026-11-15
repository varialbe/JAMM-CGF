using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Patching;

/// <summary>Creates portable semantic patches from read-only game models.</summary>
public static class PatchBuilder
{
    /// <summary>Rejects files with unavailable bytecode or incompatible versions before analysis.</summary>
    public static void Validate(GameInspection original, GameInspection mod)
    {
        foreach (var info in new[] { original, mod })
        {
            if (info.IsYyc) throw new NotComparableException($"{info.FileName} is YYC (no bytecode). Use the legacy 2023 VM build described in docs/legacy-version.md.");
            if (!info.BytecodeSupported) throw new InvalidDataException($"{info.FileName} has unsupported bytecode {info.BytecodeVersion}. Use a supported legacy VM build.");
        }
        if (original.GameMakerVersion != mod.GameMakerVersion || original.DetectedGameMakerVersion != mod.DetectedGameMakerVersion
            || original.DetectedBranch != mod.DetectedBranch || original.BytecodeVersion != mod.BytecodeVersion)
            throw new InvalidDataException($"Cannot diff {original.FileName} (GameMaker {original.GameMakerVersion}, detected {original.DetectedGameMakerVersion}, bytecode {original.BytecodeVersion}) against {mod.FileName} (GameMaker {mod.GameMakerVersion}, detected {mod.DetectedGameMakerVersion}, bytecode {mod.BytecodeVersion}). Select a matching base; see docs/legacy-version.md.");
    }

    /// <summary>Builds a patch, reporting decompile progress through the optional callback.</summary>
    public static ModPatch Create(LoadedGameFile original, LoadedGameFile mod, string? name = null, Action<string>? progress = null)
    {
        Validate(original.Inspection, mod.Inspection);
        var patch = Create(original.Data, mod.Data, Identity(original.Inspection), Identity(mod.Inspection) with
        { Name = name ?? Path.GetFileNameWithoutExtension(mod.Inspection.FileName) }, progress);
        return patch with { Warnings = original.Warnings.Concat(mod.Warnings).Concat(patch.Warnings).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
    }

    /// <summary>Reads one asset snapshot for displaying a removal without retaining removed payloads in a patch.</summary>
    public static JsonElement ReadAsset(LoadedGameFile source, AssetKind kind, string name, Action<string>? warning = null)
    {
        var fields = new AssetFields(warning ?? (_ => { }));
        if (kind == AssetKind.Strings) return AssetFields.Json(new { Value = name });
        if (kind is AssetKind.Variables or AssetKind.Functions) return Definitions(source.Data, kind, fields)[name];
        string? hash = kind == AssetKind.TexturePages
            ? new ContentFingerprints(warning).Snapshot(source.Data)[kind].Single(x => x.Name == name).Fingerprint : null;
        return Snapshot(Assets(source.Data, kind)[name], fields, hash);
    }

    /// <summary>Decompiles a single named entry for display, including removed code stored only in the base archive.</summary>
    public static CodeText ReadCode(LoadedGameFile source, string name, Action<string>? warning = null)
    {
        var code = source.Data.Code.SingleOrDefault(x => x.Name.Content == name)
            ?? throw new InvalidDataException($"Code '{name}' is missing from {source.Inspection.FileName}. Check that this is the patch's base archive.");
        return new CodeDecompiler(source.Data, source.Inspection.FileName, warning ?? (_ => { })).Read(code);
    }

    private static PatchFile Identity(GameInspection i) => new(i.FileName, i.Sha256, i.Timestamp, i.GameMakerVersion, i.BytecodeVersion);

    /// <summary>Diffs synthetic or loaded VM models. Caller retains model ownership; no files are written.</summary>
    public static ModPatch Create(UndertaleData original, UndertaleData mod, PatchFile baseFile, PatchFile modFile,
        Action<string>? progress = null)
    {
        if (original.IsYYC() || mod.IsYYC() || original.Code is null || mod.Code is null || original.Code.Count == 0 || mod.Code.Count == 0)
            throw new NotComparableException($"Cannot diff {baseFile.FileName} and {modFile.FileName}: a file is YYC (no bytecode). Use the legacy VM build.");
        if (baseFile.GmVersion != modFile.GmVersion || baseFile.Bytecode != modFile.Bytecode)
            throw new InvalidDataException($"Cannot diff {baseFile.FileName} and {modFile.FileName}: GameMaker/bytecode versions differ. Select a matching base.");
        List<string> warnings = [];
        progress?.Invoke($"Fingerprinting {baseFile.FileName}");
        var leftHashes = new ContentFingerprints(warnings.Add).Snapshot(original);
        progress?.Invoke($"Fingerprinting {modFile.FileName}");
        var rightHashes = new ContentFingerprints(warnings.Add).Snapshot(mod);
        var leftFields = new AssetFields(warnings.Add);
        var rightFields = new AssetFields(warnings.Add);
        var leftSource = new CodeDecompiler(original, baseFile.FileName, warnings.Add);
        var rightSource = new CodeDecompiler(mod, modFile.FileName, warnings.Add);
        SortedDictionary<string, AssetChanges> changes = new(StringComparer.Ordinal);
        foreach (var kind in leftHashes.Keys.Union(rightHashes.Keys).Order())
        {
            List<AssetPatch> added = [], changed = [];
            List<string> removed = [];
            if (kind == AssetKind.Strings)
            {
                var left = original.Strings.Select(x => x.Content).ToHashSet(StringComparer.Ordinal);
                var right = mod.Strings.Select(x => x.Content).ToHashSet(StringComparer.Ordinal);
                foreach (string value in right.Except(left).Order(StringComparer.Ordinal))
                    added.Add(new(value, null, AssetFields.Json(new { Value = value }), []));
                removed.AddRange(left.Except(right).Order(StringComparer.Ordinal));
            }
            else if (kind is AssetKind.Variables or AssetKind.Functions)
            {
                var left = Definitions(original, kind, leftFields);
                var right = Definitions(mod, kind, rightFields);
                foreach (string key in right.Keys.Except(left.Keys).Order(StringComparer.Ordinal))
                    added.Add(new(key, null, right[key], []));
                removed.AddRange(left.Keys.Except(right.Keys).Order(StringComparer.Ordinal));
            }
            else
            {
                var classification = AssetComparer.CompareAssets(kind, leftHashes.GetValueOrDefault(kind) ?? [], rightHashes.GetValueOrDefault(kind) ?? []);
                var left = Assets(original, kind);
                var right = Assets(mod, kind);
                var differences = classification.Assets.Where(x => x.Classification != AssetClassification.Identical
                    || x.OriginalFingerprint?.StartsWith("unsupported:", StringComparison.Ordinal) == true
                    || x.ModFingerprint?.StartsWith("unsupported:", StringComparison.Ordinal) == true).ToArray();
                int completed = 0, total = differences.Count(x => x.Classification != AssetClassification.Removed);
                foreach (var difference in differences)
                {
                    string key = difference.Name;
                    if (difference.Classification == AssetClassification.Removed) { removed.Add(key); continue; }
                    JsonElement? before = difference.Classification == AssetClassification.Added ? null : Snapshot(left[key], leftFields, difference.OriginalFingerprint);
                    JsonElement after = Snapshot(right[key], rightFields, difference.ModFingerprint);
                    CodeText? baseCode = null, modCode = null;
                    if (kind == AssetKind.Code)
                    {
                        progress?.Invoke($"Decompiling {modFile.FileName} {++completed}/{total}: {key}");
                        if (before is not null) baseCode = leftSource.Read((UndertaleCode)left[key]);
                        modCode = rightSource.Read((UndertaleCode)right[key]);
                        // Fingerprints are a cheap candidate filter. GML and metadata decide actual code changes.
                        if (baseCode is not null && JsonElement.DeepEquals(AssetFields.Json(baseCode), AssetFields.Json(modCode))
                            && before is { } originalFields && JsonElement.DeepEquals(originalFields, after)) continue;
                    }
                    if (difference.Classification == AssetClassification.Identical && before is { } identicalFields
                        && JsonElement.DeepEquals(identicalFields, after)) continue;
                    var patch = new AssetPatch(key, before, after, FieldDiff.Compare(before, after), baseCode, modCode);
                    if (before is null) added.Add(patch); else changed.Add(patch);
                }
            }
            changes.Add(kind.ToString()[..1].ToLowerInvariant() + kind.ToString()[1..], new(added, changed, removed));
        }
        string version = typeof(PatchBuilder).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return new(PatchJson.FormatVersion, $"cgfmm {version}", DateTimeOffset.UtcNow, modFile, baseFile,
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), changes);
    }

    private static JsonElement Snapshot(object asset, AssetFields fields, string? hash) => asset switch
    {
        UndertaleCode x => AssetFields.Json(new { x.LocalsCount, x.ArgumentsCount, x.Offset, x.WeirdLocalFlag,
            Parent = x.ParentEntry?.Name?.Content, Children = x.ChildEntries.Select(c => c.Name.Content).Order(StringComparer.Ordinal).ToArray() }),
        UndertaleEmbeddedTexture => AssetFields.Json(new { ContentHash = hash }),
        _ => fields.Snapshot(asset)
    };

    private static SortedDictionary<string, JsonElement> Definitions(UndertaleData data, AssetKind kind, AssetFields fields)
    {
        var result = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in AssetsList(data, kind))
        {
            var value = fields.Snapshot(pair);
            string name = ((UndertaleNamedResource)pair).Name.Content;
            string key = kind == AssetKind.Variables ? AssetFields.Json(new object[] { name, ((UndertaleVariable)pair).InstanceType.ToString() }).GetRawText() : name;
            if (!result.TryAdd(key, value) && !JsonElement.DeepEquals(result[key], value))
                throw new InvalidDataException($"Conflicting duplicate definition '{name}'. Use a mod with unambiguous definitions.");
        }
        return result;
    }

    private static Dictionary<string, object> Assets(UndertaleData data, AssetKind kind)
    {
        if (kind == AssetKind.TexturePages) return data.EmbeddedTextures.Select((x, i) => (x, i))
            .ToDictionary(x => x.i.ToString(System.Globalization.CultureInfo.InvariantCulture), x => (object)x.x, StringComparer.Ordinal);
        // A duplicate name has no unambiguous merge identity. Do not silently choose one.
        Dictionary<string, object> result = new(StringComparer.Ordinal);
        foreach (var asset in AssetsList(data, kind))
        {
            string name = ((UndertaleNamedResource)asset).Name.Content;
            if (!result.TryAdd(name, asset))
                throw new InvalidDataException($"Duplicate {kind.Label()} name '{name}' cannot be matched safely. Use a mod with unique asset names.");
        }
        return result;
    }

    private static IEnumerable<object> AssetsList(UndertaleData data, AssetKind kind) => kind switch
    {
        AssetKind.Code => data.Code, AssetKind.Scripts => data.Scripts, AssetKind.Objects => data.GameObjects,
        AssetKind.Rooms => data.Rooms, AssetKind.Sprites => data.Sprites, AssetKind.Sounds => data.Sounds,
        AssetKind.Fonts => data.Fonts, AssetKind.Backgrounds => data.Backgrounds, AssetKind.Paths => data.Paths,
        AssetKind.Timelines => data.Timelines, AssetKind.Variables => data.Variables, AssetKind.Functions => data.Functions,
        _ => []
    };
}

/// <summary>A file lacks VM bytecode; the CLI reports this as exit code 2.</summary>
public sealed class NotComparableException(string message) : Exception(message);
