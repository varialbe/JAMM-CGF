using System.Collections;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

/// <summary>A patch paired with the local archive whose entire-file SHA-256 must match it.</summary>
public sealed record BuildInput(ModPatch Patch, string ArchivePath);

/// <summary>A completed build or an in-memory dry run, including decisions and archive counts.</summary>
public sealed record BuildResult(BuildPlan Plan, GameInspection Base, VerificationResult? Verification,
    IReadOnlyDictionary<AssetKind, int> Counts, long SizeBytes,
    IReadOnlyDictionary<string, int>? RemappedReferences = null, int SemanticEntries = 0);

/// <summary>Builds a new archive from an exact base and verified local mod payloads.</summary>
public static class ArchiveBuilder
{
    /// <summary>Compiles a merged working copy; publishes only after the temporary output passes verification.</summary>
    public static BuildResult Build(string basePath, IReadOnlyList<BuildInput> inputs, string output,
        IReadOnlyDictionary<string, string>? resolutions = null, bool dryRun = false, Action<string>? progress = null)
    {
        string[] protectedPaths = [basePath, .. inputs.Select(i => i.ArchivePath)];
        BuildFiles.ValidateNewOutput(output, protectedPaths, dryRun);
        using var original = GameFileLoader.Load(basePath, progress);
        foreach (var input in inputs)
        {
            if (input.Patch.Base.Sha256 != original.Inspection.Sha256)
                throw new InvalidDataException($"Patch '{input.Patch.Mod.Name}' requires base SHA-256 {input.Patch.Base.Sha256}; '{Path.GetFileName(basePath)}' differs. Regenerate the patch against this exact base.");
            if (!File.Exists(input.ArchivePath) || BuildFiles.Hash(input.ArchivePath) != input.Patch.Mod.Sha256)
                throw new InvalidDataException($"Missing or different mod archive '{input.Patch.Mod.FileName}' (required SHA-256 {input.Patch.Mod.Sha256}). Put the exact .win beside the patch or in JAMM_MODS_DIR.");
        }
        List<LoadedGameFile> sources = [];
        try
        {
            foreach (var input in inputs)
            {
                var source = GameFileLoader.Load(input.ArchivePath, progress);
                sources.Add(source);
                if (source.Inspection.Sha256 != input.Patch.Mod.Sha256) throw new IOException("A mod archive changed during loading. Retry with stable input copies.");
                PatchBuilder.Validate(original.Inspection, source.Inspection);
            }
            var semantics = new SemanticCode(original.Data, sources.Select(s => s.Data).ToArray(), progress);
            var patches = semantics.Prepare(inputs.Select(i => i.Patch).ToArray());
            var baseReferences = PatchBuilder.ReferenceSnapshots(original.Data, new AssetFields(_ => { }, original.Data));
            var plan = BuildPlan.Create(patches, resolutions, baseReferences);
            // This fresh base model is the disposable working copy; no input stream is ever writable.
            using var working = GameFileLoader.Load(basePath);
            if (working.Inspection.Sha256 != original.Inspection.Sha256) throw new IOException("The base changed during loading. Retry with stable input copies.");
            var data = working.Data;
            Apply(data, original.Data, sources.Select(s => s.Data).ToArray(), patches, plan, progress);
            var expectedReferences = semantics.Expected(plan, data);
            ArchiveVerifier.ValidateSemantics(data, expectedReferences);
            var remapped = semantics.Remapped(patches, data);
            progress?.Invoke($"Semantic reference verification passed: {expectedReferences.Count} code entries (selected mod hunks and unchanged base)");
            ArchiveVerifier.ValidateReferences(data);
            var counts = Counts(data);
            if (dryRun) return new(plan, original.Inspection, null, counts, 0, remapped, expectedReferences.Count);
            string destination = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, $".cgfmm-{Guid.NewGuid():N}.tmp");
            try
            {
                progress?.Invoke("Writing temporary merged archive");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    UndertaleIO.Write(stream, data);
                var verification = ArchiveVerifier.Verify(temporary, original.Inspection, plan.Assets, progress, expectedReferences);
                BuildFiles.ValidateNewOutput(output, protectedPaths, dryRun);
                File.Move(temporary, destination, overwrite: true);
                return new(plan, original.Inspection, verification, counts, new FileInfo(destination).Length, remapped, expectedReferences.Count);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { foreach (var source in sources) source.Dispose(); }
    }

    private static void Apply(UndertaleData data, UndertaleData ancestor, IReadOnlyList<UndertaleData> sources, IReadOnlyList<ModPatch> patches, BuildPlan plan, Action<string>? progress)
    {
        var tables = ResourceCatalog.Tables(data);
        var sourceTables = sources.Select(ResourceCatalog.Tables).ToArray();
        Dictionary<string, string> recompile = new(StringComparer.Ordinal);
        foreach (var asset in plan.Assets.Where(a => a.Patch is null && tables.ContainsKey(a.Family)))
        {
            if (CgfAssetTypes.IndexedFamilies.Contains(asset.Family, StringComparer.Ordinal))
                throw new InvalidDataException($"Cannot remove indexed base asset {asset.Family}/{asset.Name}. Regenerate the build plan with stable base indices.");
            var table = tables[asset.Family];
            if (ResourceCatalog.Find(table, asset.Name) is { } removed) table.Remove(removed);
            if (asset.Family == "code")
            {
                recompile.Remove(asset.Name);
                var locals = data.CodeLocals.FirstOrDefault(l => l.Name.Content == asset.Name);
                if (locals is not null) data.CodeLocals.Remove(locals);
            }
        }
        foreach (var asset in plan.Assets.Where(a => a.Patch is not null && tables.ContainsKey(a.Family))
            .OrderBy(a => Introduction(a).Mod).ThenBy(a => Introduction(a).Index))
        {
            var table = tables[asset.Family];
            if (ResourceCatalog.Find(table, asset.Name) is not null) continue;
            if (asset.Family == "code") UndertaleCode.CreateEmptyEntry(data, asset.Name);
            else
            {
                object source = ResourceCatalog.Find(sourceTables[asset.Source][asset.Family], asset.Name)
                    ?? throw new InvalidDataException($"Missing {asset.Family}/{asset.Name} in verified mod archive.");
                var empty = (UndertaleNamedResource)Activator.CreateInstance(source.GetType())!;
                empty.Name = data.Strings.MakeString(asset.Name); table.Add(empty);
            }
        }
        (int Mod, int Index) Introduction(PlannedAsset asset)
        {
            for (int i = 0; i < patches.Count; i++)
                if (patches[i].Changes.GetValueOrDefault(asset.Family)?.Added.FirstOrDefault(a => a.Name == asset.Name) is { } added)
                    return (i, added.OriginalIndex ?? int.MaxValue);
            return (asset.Source, asset.Patch!.OriginalIndex ?? int.MaxValue);
        }
        // Base collision targets retain their names and indices; imported events resolve by name.
        foreach (var obj in data.GameObjects)
        {
            var old = ancestor.GameObjects.FirstOrDefault(o => o.Name.Content == obj.Name.Content);
            if (old is null) continue;
            foreach (var collision in obj.Events[4])
            {
                string other = ancestor.GameObjects[(int)collision.EventSubtype].Name.Content;
                int index = data.GameObjects.ToList().FindIndex(o => o.Name.Content == other);
                if (index < 0) throw new InvalidDataException($"Object '{obj.Name.Content}' still collides with removed object '{other}'. Keep that object in the build.");
                collision.EventSubtype = (uint)index;
            }
        }
        bool useCompleteAtlas = sources.Count == 1 && new[] { "sprites", "fonts", "backgrounds", "sounds" }
            .All(f => tables[f].Cast<object>().All(a => ResourceCatalog.Find(sourceTables[0][f], ResourceCatalog.Name(a)) is not null));
        if (useCompleteAtlas)
        {
            // Semantically unchanged binary assets can use the verified mod's atlas as well.
            // This keeps a single-mod build's page/item/audio counts equal to its source.
            data.EmbeddedTextures.Clear(); data.TexturePageItems.Clear(); data.EmbeddedAudio.Clear();
        }
        var importers = sources.Select(s => new ResourceImporter(s, data)).ToArray();
        if (useCompleteAtlas)
        {
            var importer = importers[0];
            foreach (var page in sources[0].EmbeddedTextures) importer.Copy(page);
            foreach (var item in sources[0].TexturePageItems) importer.Copy(item);
            foreach (var audio in sources[0].EmbeddedAudio) importer.Copy(audio);
            foreach (string family in new[] { "sprites", "fonts", "backgrounds", "sounds" })
                foreach (object source in sourceTables[0][family])
                    if (ResourceCatalog.Find(tables[family], ResourceCatalog.Name(source)) is { } target)
                        importer.Populate(source, target);
        }
        var structured = new StructuredAssets(data, sources);
        foreach (var asset in plan.Assets.Where(a => a.Patch is not null))
        {
            if (asset.Family == "strings") { data.Strings.MakeString(asset.Name); continue; }
            if (asset.Family == "variables")
            {
                var key = System.Text.Json.JsonSerializer.Deserialize<string[]>(asset.Name)!;
                var type = (UndertaleInstruction.InstanceType)asset.Snapshot!.Value.GetProperty("instanceType").GetInt32();
                if (!data.Variables.Any(v => v.Name.Content == key[0] && v.InstanceType == type))
                {
                    var text = data.Strings.MakeString(key[0], out int id);
                    var source = sources[asset.Source].Variables.FirstOrDefault(v => v.Name.Content == key[0] && v.InstanceType == type)
                        ?? throw new InvalidDataException($"Missing variable '{key[0]}' ({type}) in mod '{patches[asset.Source].Mod.FileName}'. Regenerate its patch from the exact archive.");
                    if (type == UndertaleInstruction.InstanceType.Local) data.Variables.DefineLocal(data, source.VarID, text, id);
                    else data.Variables.Define(text, id, type, source.VarID == (int)UndertaleInstruction.InstanceType.Builtin, data);
                }
                continue;
            }
            if (asset.Family == "functions")
            {
                var function = data.Functions.EnsureDefined(asset.Name, data.Strings);
                function.Classification = (UndertaleGeneralInfo.FunctionClassification)asset.Snapshot!.Value.GetProperty("classification").GetUInt64();
                continue;
            }
            if (asset.Family == "texturePages")
            {
                if (sources.Count > 1) continue; // Import only pages reached by selected resource payloads.
                int index = asset.Patch!.OriginalIndex ?? asset.Snapshot!.Value.GetProperty("sourceIndex").GetInt32();
                importers[asset.Source].Copy(sources[asset.Source].EmbeddedTextures[index]); continue;
            }
            if (asset.Family == "code")
            {
                if (asset.Patch!.ModCode?.Parent is not null)
                    throw new InvalidDataException($"Child code '{asset.Name}' requires a GMS2 scope. Use the legacy GM1.4 archives.");
                recompile[asset.Name] = asset.Gml!; continue;
            }
            object sourceAsset = ResourceCatalog.Find(sourceTables[asset.Source][asset.Family], asset.Name)
                ?? throw new InvalidDataException($"Missing {asset.Family}/{asset.Name} in verified source archive.");
            object target = ResourceCatalog.Find(tables[asset.Family], asset.Name)!;
            if (asset.Family is "objects" or "rooms" or "scripts" or "paths" or "timelines") structured.Apply(target, asset.Snapshot!.Value, asset.Source);
            else
            {
                importers[asset.Source].Populate(sourceAsset, target, skipFontTexture: target is UndertaleFont);
                if (target is UndertaleFont font)
                {
                    string? hash = asset.Snapshot!.Value.GetProperty("region").GetProperty("contentHash").GetString();
                    for (int i = sources.Count - 1; i >= 0; i--)
                    {
                        var candidate = sources[i].Fonts.FirstOrDefault(f => f.Name.Content == asset.Name);
                        if (candidate is null) continue;
                        var snapshot = new AssetFields(_ => { }, sources[i]).Snapshot(candidate);
                        if (snapshot.GetProperty("region").GetProperty("contentHash").GetString() != hash) continue;
                        font.Texture = candidate.Texture is null ? null : (UndertaleTexturePageItem)importers[i].Copy(candidate.Texture);
                        break;
                    }
                    structured.Apply(font, asset.Snapshot.Value);
                }
            }
        }
        foreach (var entry in data.GeneralInfo.RoomOrder.Where(e => !data.Rooms.Contains(e.Resource)).ToArray()) data.GeneralInfo.RoomOrder.Remove(entry);
        foreach (var room in data.Rooms)
            if (!data.GeneralInfo.RoomOrder.Any(e => e.Resource == room))
                data.GeneralInfo.RoomOrder.Add(new(room, data.Rooms.IndexOf(room)));
        ReassignRoomIds(data, ancestor);
        progress?.Invoke($"Compiling {recompile.Count} changed/merged/relinked code entries");
        var compiler = new CompileGroup(data);
        var finalNames = SemanticReferences.Names(data);
        foreach (var (name, gml) in recompile)
        {
            var code = data.Code.SingleOrDefault(c => c.Name.Content == name)
                ?? throw new InvalidDataException($"Cannot compile code '{name}' in the merged archive: its code entry is missing. Keep the entry in --resolve or regenerate the patches.");
            compiler.QueueCodeReplace(code, AssetSymbols.Link(gml, name, finalNames));
        }
        ArchiveVerifier.Compile(compiler, "merged archive");
    }

    private static void ReassignRoomIds(UndertaleData data, UndertaleData ancestor)
    {
        uint nextInstance = Math.Max(ancestor.GeneralInfo.LastObj, ancestor.Rooms.SelectMany(r => r.GameObjects).Select(i => i.InstanceID).DefaultIfEmpty().Max());
        uint nextTile = Math.Max(ancestor.GeneralInfo.LastTile, ancestor.Rooms.SelectMany(r => r.Tiles).Select(i => i.InstanceID).DefaultIfEmpty().Max());
        foreach (var room in data.Rooms)
        {
            var old = ancestor.Rooms.FirstOrDefault(r => r.Name.Content == room.Name.Content);
            var instances = old?.GameObjects.GroupBy(i => InstanceKey(i)).ToDictionary(g => g.Key, g => new Queue<uint>(g.Select(i => i.InstanceID))) ?? [];
            foreach (var instance in room.GameObjects)
                instance.InstanceID = instances.TryGetValue(InstanceKey(instance), out var ids) && ids.Count > 0 ? ids.Dequeue() : ++nextInstance;
            var tiles = old?.Tiles.GroupBy(TileKey).ToDictionary(g => g.Key, g => new Queue<uint>(g.Select(i => i.InstanceID))) ?? [];
            foreach (var tile in room.Tiles)
                tile.InstanceID = tiles.TryGetValue(TileKey(tile), out var ids) && ids.Count > 0 ? ids.Dequeue() : ++nextTile;
        }
        data.GeneralInfo.LastObj = nextInstance; data.GeneralInfo.LastTile = nextTile;
        static string InstanceKey(UndertaleRoom.GameObject i) => AssetFields.Json(new object?[] { i.ObjectDefinition?.Name.Content, i.X, i.Y }).GetRawText();
        static string TileKey(UndertaleRoom.Tile i) => AssetFields.Tiles([i]).Keys.Single();
    }

    private static IReadOnlyDictionary<AssetKind, int> Counts(UndertaleData data)
    {
        var result = ResourceCatalog.Tables(data).ToDictionary(p => Enum.Parse<AssetKind>(p.Key, true), p => p.Value.Count);
        result[AssetKind.TexturePages] = data.EmbeddedTextures.Count; result[AssetKind.TexturePageItems] = data.TexturePageItems.Count;
        result[AssetKind.Strings] = data.Strings.Count; result[AssetKind.Variables] = data.Variables.Count; result[AssetKind.Functions] = data.Functions.Count;
        return result;
    }
}
