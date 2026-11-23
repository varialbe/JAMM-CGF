using Jamm.ModMaker;
using System.Collections;
using Jamm.Core.Acceptance;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

/// <summary>A patch paired with the local archive whose entire-file SHA-256 must match it.</summary>
public sealed record BuildInput(ModPatch Patch, string ArchivePath)
{
    /// <summary>Opts this input into shop repair for legacy items with no unlock path.</summary>
    public bool UnlockFix { get; init; }
}

/// <summary>A completed build or an in-memory dry run, including decisions and archive counts.</summary>
public sealed record BuildResult(BuildPlan Plan, GameInspection Base, VerificationResult? Verification,
    IReadOnlyDictionary<AssetKind, int> Counts, long SizeBytes,
    IReadOnlyDictionary<string, int>? RemappedReferences = null, int SemanticEntries = 0,
    PackAcceptanceReport? Acceptance = null);

/// <summary>Builds a new archive from an exact base and verified local mod payloads.</summary>
public static class ArchiveBuilder
{
    /// <summary>Compiles a merged working copy; publishes only after the temporary output passes verification.</summary>
    public static BuildResult Build(string basePath, IReadOnlyList<BuildInput> inputs, string output,
        IReadOnlyDictionary<string, string>? resolutions = null, bool dryRun = false, Action<string>? progress = null,
        CancellationToken cancellationToken = default, AnalysisOptions? options = null, bool noRepack = false,
        PerfSettings? perfSettings = null, bool bigMoney = true, ValueGuardOptions? valueGuard = null) =>
        BuildLayer(basePath, inputs, output, resolutions, dryRun, progress, cancellationToken, options, noRepack, perfSettings: perfSettings, bigMoney: bigMoney, valueGuard: valueGuard);

    internal static BuildResult BuildLayer(string basePath, IReadOnlyList<BuildInput> inputs, string output,
        IReadOnlyDictionary<string, string>? resolutions, bool dryRun, Action<string>? progress,
        CancellationToken cancellationToken, AnalysisOptions? options, bool noRepack,
        bool intermediate = false, IReadOnlyList<string>? additionalProtectedPaths = null, bool hasMergedBase = false,
        PerfSettings? perfSettings = null, bool bigMoney = true, ValueGuardOptions? valueGuard = null,
        string? vanillaPath = null)
    {
        valueGuard ??= new();
        if (valueGuard.FishValueCap < 0) throw new ArgumentOutOfRangeException(nameof(valueGuard), "Fish value cap must be a nonnegative whole number.");
        options ??= AnalysisOptions.Default;
        var reportProgress = progress;
        progress = message => { cancellationToken.ThrowIfCancellationRequested(); reportProgress?.Invoke(message); };
        cancellationToken.ThrowIfCancellationRequested();
        string[] protectedPaths = [basePath, .. inputs.Select(i => i.ArchivePath), .. additionalProtectedPaths ?? [], .. vanillaPath is null ? Array.Empty<string>() : new[] { vanillaPath }];
        BuildFiles.ValidateNewOutput(output, protectedPaths, dryRun);
        using var original = GameFileLoader.Load(basePath, progress, cancellationToken, options);
        using var vanillaArchive = vanillaPath is not null && (valueGuard.FairPlay || valueGuard.FishValueCap > 0)
            ? GameFileLoader.Load(vanillaPath, progress, cancellationToken, options) : null;
        if (inputs.Count == 0)
        {
            if (resolutions is { Count: > 0 })
                throw new ArgumentException("Conflict resolutions require a build with mods.");
            var vanilla = new BuildResult(new([], []), original.Inspection, null, original.Inspection.Counts, 0);
            if (dryRun) return vanilla;
            progress?.Invoke("Copying the original archive byte for byte");
            var verified = Publish(output, protectedPaths, vanilla.Plan, original.Inspection, stream =>
            {
                using var source = new CancellableGameStream(basePath, cancellationToken);
                source.CopyTo(stream);
            }, progress, cancellationToken, options, exactCopy: true);
            return vanilla with { Verification = verified, SizeBytes = verified.Inspection.FileSize };
        }
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
                var source = GameFileLoader.Load(input.ArchivePath, progress, cancellationToken, options);
                sources.Add(source);
                if (source.Inspection.Sha256 != input.Patch.Mod.Sha256) throw new IOException("A mod archive changed during loading. Retry with stable input copies.");
                PatchBuilder.Validate(original.Inspection, source.Inspection);
            }
            var semantics = new SemanticCode(original.Data, sources.Select(s => s.Data).ToArray(), progress, options, original.Inspection.Sha256);
            var patches = semantics.Prepare(inputs.Select(i => i.Patch).ToArray());
            bool perfIdentityBuild = !intermediate && inputs.Count == 1 && IsPerfIdentityPatch(inputs[0].Patch)
                && (resolutions is null || resolutions.Count == 0)
                && perfSettings?.StartEnabled != true && perfSettings?.FishGrid != true
                && perfSettings?.DecorSleep != true && perfSettings?.FishSleep != true
                && perfSettings?.MasksRect != true && perfSettings?.FishLod != true
                && perfSettings?.DecorAnimNative != true && perfSettings?.CacheHot != true
                && perfSettings?.ParticlesFx != true;
            if (perfIdentityBuild && noRepack)
            {
                var emptyPlan = new BuildPlan([], ["JAMM Perf contains no asset changes; preserving the original archive."]);
                var unchanged = new BuildResult(emptyPlan, original.Inspection, null, original.Inspection.Counts, 0);
                if (dryRun) return unchanged;
                progress?.Invoke("JAMM Perf contains no asset changes; copying the original archive byte for byte");
                var verifiedCopy = Publish(output, protectedPaths, emptyPlan, original.Inspection, stream =>
                {
                    using var source = new CancellableGameStream(basePath, cancellationToken);
                    source.CopyTo(stream);
                }, progress, cancellationToken, options, exactCopy: true);
                return unchanged with { Verification = verifiedCopy, SizeBytes = verifiedCopy.Inspection.FileSize };
            }
            var vanillaCode = vanillaArchive is null ? semantics.Base : SemanticCode.Read(vanillaArchive.Data, "vanilla guard baseline", progress,
                AssetSymbols.Ambiguous([SemanticReferences.Names(vanillaArchive.Data), semantics.BaseNames]), options, vanillaArchive.Inspection.Sha256);
            var baseReferences = PatchBuilder.ReferenceSnapshots(original.Data, new AssetFields(_ => { }, original.Data));
            var unlockFixMods = inputs.Select((input, index) => (input, index)).Where(x => x.input.UnlockFix)
                .Select(x => x.index).ToHashSet();
            // Order matters: ItemsRuntime owns jamm_api_init; FishRuntime runs last and only adds a standalone
            // jamm_api_init when no items runtime was injected.
            var plan = BuildPlan.Create(patches, resolutions, baseReferences, semantics.Base, unlockFixMods, hasMergedBase);
            if (!intermediate && !perfIdentityBuild)
                plan = UpgradeLoopBounds.PreparePlan(
                    CatalogRuntime.PreparePlan(
                        FishRuntime.PreparePlan(
                            ItemsRuntime.PreparePlan(
                                HudRuntime.PreparePlan(
                                    MenuRuntime.PreparePlan(plan, semantics.Base, original.Data),
                                    semantics.Base),
                                semantics.Base, semantics.BaseNames),
                            semantics.Base, original.Data),
                        semantics.Base),
                    semantics.Base);
            if (!perfIdentityBuild)
            {
                plan = InstanceCompatibility.Repair(plan, patches, semantics.Base);
                if (!intermediate) plan = NavCompatibility.GuardPatrol(plan, semantics.Base);
            }
            else
                plan = plan with { Decisions = [.. plan.Decisions, "JAMM Perf contains no asset changes; skipped generic runtime injection."] };
            ApplySourceRenames(sources.Select(s => s.Data).ToArray(), plan.AssetRenames);
            // In the addon pass the first source is the already merged base. Start with its
            // asset indices and room IDs while planning edits in the raw ancestor's coordinates.
            var workingAncestor = hasMergedBase ? sources[0] : original;
            string workingPath = hasMergedBase ? inputs[0].ArchivePath : basePath;
            using var working = GameFileLoader.Load(workingPath, cancellationToken: cancellationToken, options: options);
            if (working.Inspection.Sha256 != workingAncestor.Inspection.Sha256)
                throw new IOException("The working base changed during loading. Retry with stable input copies.");
            var data = working.Data;
            IReadOnlyDictionary<string, string> generatedCode;
            using (new AnalysisPhase(options, "build/compile"))
            {
                List<string> extraDecisions = [];
                generatedCode = Apply(data, workingAncestor.Data, sources.Select(s => s.Data).ToArray(), patches, plan, semantics.Base, progress, extraDecisions, intermediate, bigMoney, valueGuard, vanillaArchive?.Data ?? original.Data, vanillaCode, perfIdentityBuild);
                if (extraDecisions.Count > 0)
                    plan = plan with { Decisions = [.. plan.Decisions, .. extraDecisions] };
            }
            plan = InstanceCompatibility.Diagnose(plan, patches, original.Data, data);
            if (!noRepack && TextureRepacker.HasAtlas(data))
            {
                using var phase = new AnalysisPhase(options, "build/texture-repack");
                string decision = TextureRepacker.RepackForBuild(data, output, progress, cancellationToken, options);
                plan = plan with { Decisions = [.. plan.Decisions, decision] };
            }
            var expectedReferences = semantics.Expected(plan, data, generatedCode);
            var mergedCode = ArchiveVerifier.ValidateSemantics(data, expectedReferences, cancellationToken, options, plan.Assets.FirstOrDefault(a => a.Name == CatalogMerge.Create)?.Gml);
            var remapped = semantics.Remapped(patches, data, plan.AssetRenames);
            progress?.Invoke($"Semantic reference verification passed: {expectedReferences.Count} code entries (selected mod hunks and unchanged base)");
            var acceptance = intermediate ? null : AnalyzeAcceptance(original.Data, inputs, sources, patches, plan, data, semantics, mergedCode);
            if (!intermediate && (data.GameObjects.ByName(PerfMode.ControllerName) is not null
                || perfSettings?.StartEnabled == true || perfSettings?.FishGrid == true
                || perfSettings?.DecorSleep == true || perfSettings?.FishSleep == true
                || perfSettings?.MasksRect == true || perfSettings?.FishLod == true
                || perfSettings?.DecorAnimNative == true || perfSettings?.CacheHot == true
                || perfSettings?.ParticlesFx == true))
            {
                // Verify selected source references and acceptance before profiling-independent
                // runtime edits. The read-back verifier then checks the generated bytecode too.
                List<UndertaleCode> perfEdits = [];
                if (data.GameObjects.ByName(PerfMode.ControllerName) is null && perfSettings?.StartEnabled == true)
                {
                    var controllerCompiler = new CompileGroup(data);
                    PerfMode.Queue(data, controllerCompiler, debug: false);
                    ModObjects.Compile(controllerCompiler);
                }
                if (perfSettings is not null && data.GameObjects.ByName(PerfMode.ControllerName) is not null)
                    PerfMode.ApplyDefaults(data, perfSettings, perfEdits);
                if (perfSettings?.StartEnabled == true)
                    PerfMode.ApplyScheduling(data, perfEdits);
                if (perfSettings?.FishGrid == true)
                {
                    string decision = FishGrid.Apply(data, perfEdits, enabledByDefault: true);
                    plan = plan with { Decisions = [.. plan.Decisions, decision] };
                }
                if (perfSettings?.DecorSleep == true)
                {
                    var decorDecisions = DecorSleep.Apply(data, perfEdits, enabledByDefault: true);
                    plan = plan with { Decisions = [.. plan.Decisions, .. decorDecisions] };
                }
                if (perfSettings?.FishSleep == true)
                {
                    var areaFishNames = plan.Assets
                        .Where(a => a.Family == "code" && a.Patch is not null && AreaDeclarations.IsDeclaration(a.Name))
                        .Select(a => AreaDeclarations.Read(a.Name, a.Gml))
                        .SelectMany(d => d.Fish.Select(f => f.Object))
                        .ToHashSet(StringComparer.Ordinal);
                    var sleepDecisions = FishSleep.Apply(data, perfEdits, enabledByDefault: true,
                        margin: perfSettings.FishSleepMargin, areaFishNames: areaFishNames);
                    plan = plan with { Decisions = [.. plan.Decisions, .. sleepDecisions] };
                }
                var cheapWinDecisions = GmlCheapWins.Apply(data, perfEdits,
                    masksRect: perfSettings?.MasksRect == true,
                    fishLod: perfSettings?.FishLod == true,
                    decorAnimNative: perfSettings?.DecorAnimNative == true,
                    cacheHot: perfSettings?.CacheHot == true,
                    particlesFx: perfSettings?.ParticlesFx == true);
                if (cheapWinDecisions.Count > 0)
                    plan = plan with { Decisions = [.. plan.Decisions, .. cheapWinDecisions] };
                if (perfEdits.Count > 0)
                {
                    var names = SemanticReferences.Names(data);
                    var generated = CodeDecompiler.ReadAll(data, "JAMM Perf", _ => { },
                        AssetSymbols.Ambiguous([names]), options, selected: perfEdits.Distinct().ToArray());
                    foreach (var (name, code) in generated)
                        expectedReferences[name] = SemanticReferences.Resolve(code.Text, name, names);
                }
            }
            ArchiveVerifier.ValidateReferences(data);
            var counts = Counts(data);
            if (dryRun) return new(plan, original.Inspection, null, counts, 0, remapped, expectedReferences.Count, acceptance);
            progress?.Invoke("Writing temporary merged archive");
            var verification = Publish(output, protectedPaths, plan, original.Inspection, stream => UndertaleIO.Write(stream, data),
                progress, cancellationToken, options, expectedReferences);
            return new(plan, original.Inspection, verification, counts, verification.Inspection.FileSize, remapped, expectedReferences.Count, acceptance);
        }
        finally { foreach (var source in sources) source.Dispose(); }
    }

    private static bool IsPerfIdentityPatch(ModPatch patch)
    {
        string identity = $"{patch.Mod.Name} {patch.Mod.FileName}";
        return identity.Contains("perf", StringComparison.OrdinalIgnoreCase)
            && !patch.Changes.Values.Any(changes =>
                changes.Added.Count > 0 || changes.Changed.Count > 0 || changes.Removed.Count > 0);
    }

    private static VerificationResult Publish(string output, string[] protectedPaths, BuildPlan plan, GameInspection original,
        Action<Stream> write, Action<string>? progress, CancellationToken cancellationToken, AnalysisOptions options,
        IReadOnlyDictionary<string, IReadOnlyList<ResolvedCodeReference>>? expectedReferences = null, bool exactCopy = false)
    {
        string destination = Path.GetFullPath(output);
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".cgfmm-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new AnalysisPhase(options, "write"))
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                write(stream);
            VerificationResult verification;
            using (new AnalysisPhase(options, "verify"))
                verification = ArchiveVerifier.Verify(temporary, original, plan.Assets, progress, expectedReferences, cancellationToken, options);
            if (exactCopy && verification.Inspection.Sha256 != original.Sha256)
                throw new IOException("The original archive changed during copying. Retry with a stable base file.");
            BuildFiles.ValidateNewOutput(output, protectedPaths);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return verification;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ApplySourceRenames(IReadOnlyList<UndertaleData> sources,
        IReadOnlyDictionary<(int Mod, string Family, string Original), string> renames)
    {
        foreach (var ((mod, family, original), renamed) in renames)
        {
            var data = sources[mod];
            if (ResourceCatalog.Find(ResourceCatalog.Tables(data)[family], original) is UndertaleNamedResource resource)
                resource.Name = data.Strings.MakeString(renamed);
            string? prefix = family switch
            {
                "objects" => $"gml_Object_{original}_",
                "scripts" => $"gml_Script_{original}",
                _ => null
            };
            if (prefix is null) continue;
            foreach (var code in data.Code.Where(c => family == "scripts"
                ? c.Name.Content == prefix : c.Name.Content.StartsWith(prefix, StringComparison.Ordinal)))
                code.Name = data.Strings.MakeString(code.Name.Content.Replace(prefix, prefix.Replace(original, renamed, StringComparison.Ordinal), StringComparison.Ordinal));
        }
    }

    private static IReadOnlyDictionary<string, string> Apply(UndertaleData data, UndertaleData ancestor,
        IReadOnlyList<UndertaleData> sources, IReadOnlyList<ModPatch> patches, BuildPlan plan,
        IReadOnlyDictionary<string, CodeText> baseCode, Action<string>? progress, List<string> extraDecisions, bool intermediate,
        bool bigMoney, ValueGuardOptions valueGuard, UndertaleData vanilla, IReadOnlyDictionary<string, CodeText> vanillaCode,
        bool perfIdentityBuild)
    {
        if (perfIdentityBuild) return new Dictionary<string, string>(StringComparer.Ordinal);
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
            .All(f => tables[f].Cast<object>().All(a => ResourceCatalog.Find(sourceTables[0][f], ResourceCatalog.Name(a)) is not null))
            && !HasSanitizedHudDifference(ancestor, sources[0], plan);
        if (useCompleteAtlas)
        {
            // Semantically unchanged binary assets can use the verified mod's atlas as well.
            // Using the source atlas here preserves its layout for --no-repack and supplies
            // the final repacker with each image from that atlas when repacking is enabled.
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
            else if (target is UndertaleSprite targetSprite && sourceAsset is UndertaleSprite sourceSprite
                && (TryApplyRelocatedUpgradeIcons(targetSprite, sourceSprite, asset, plan.UpgradeIcons, patches, sources, importers, extraDecisions)
                    || TryApplyHybridSpriteFrames(targetSprite, sourceSprite, asset, patches, sources, importers)))
            {
                continue;
            }
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
        if (plan.UpgradeIcons.Count > 0
            && !plan.Assets.Any(a => a.Family == "sprites" && a.Name == "spr_upgradeicons")
            && data.Sprites.FirstOrDefault(s => s.Name.Content == "spr_upgradeicons") is { } baseUpgradeIcons)
        {
            AppendRelocatedUpgradeIconFrames(baseUpgradeIcons, plan.UpgradeIcons, sources, importers, extraDecisions);
        }
        if (plan.RodFrames.Count > 0)
            ApplyRodFrames(data, plan.RodFrames, patches, sources, importers);
        foreach (var entry in data.GeneralInfo.RoomOrder.Where(e => !data.Rooms.Contains(e.Resource)).ToArray()) data.GeneralInfo.RoomOrder.Remove(entry);
        foreach (var room in data.Rooms)
            if (!data.GeneralInfo.RoomOrder.Any(e => e.Resource == room))
                data.GeneralInfo.RoomOrder.Add(new(room, data.Rooms.IndexOf(room)));
        // Intermediate base-mod layers keep area and weather declarations as plain code; the final
        // pass places them once, together with the shared runtimes.
        var generatedCode = intermediate ? new Dictionary<string, string>(StringComparer.Ordinal)
            : AreaPlacement.Apply(data, plan, patches, baseCode, recompile, extraDecisions);
        if (!intermediate)
        {
            CatalogRuntime.Inject(data, recompile);
            HudRuntime.Inject(data, recompile);
            MenuRuntime.Inject(data, recompile);
            ItemsRuntime.Inject(data, recompile);
            FishRuntime.Inject(data, recompile, generatedCode, valueGuard.FishValueCap);
            WeatherRuntime.Apply(data, ancestor, plan, baseCode, recompile, generatedCode, extraDecisions);
            HarborRuntime.Apply(data, baseCode, recompile, generatedCode, extraDecisions);
            EndlessOceanMod.RefreshMergedLayout(data, recompile, generatedCode, extraDecisions);
            NavCompatibility.RepairRoom(data, ancestor, baseCode, recompile, generatedCode, extraDecisions);
            if (bigMoney) MoneyRuntime.Apply(baseCode, recompile, generatedCode, extraDecisions);
            ValueGuard.Apply(data, vanilla, vanillaCode, baseCode, recompile, generatedCode, plan, patches, valueGuard, extraDecisions,
                baseMod: vanilla != ancestor ? patches[0].Mod.Name : null);
            ConsoleUpgradeGuard.Apply(data, baseCode, recompile, generatedCode, extraDecisions);
            MoneyRuntime.ConfigureConsole(bigMoney, recompile, generatedCode);
            // Last, so the guard also covers code that the runtimes above generated or merged.
            InstanceGuard.Apply(data, ancestor, baseCode, recompile, generatedCode, extraDecisions);
        }
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
        return generatedCode;
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

    private static bool HasSanitizedHudDifference(UndertaleData ancestor, UndertaleData source, BuildPlan plan)
    {
        var plannedSprites = plan.Assets.Where(a => a.Family == "sprites" && a.Patch is not null)
            .ToDictionary(a => a.Name, a => a, StringComparer.Ordinal);
        foreach (string hudName in ModSanitizer.HudSprites)
        {
            var baseSprite = ancestor.Sprites.FirstOrDefault(s => s.Name.Content == hudName);
            var modSprite = source.Sprites.FirstOrDefault(s => s.Name.Content == hudName);
            if (baseSprite is null || modSprite is null) continue;
            if (plannedSprites.TryGetValue(hudName, out var planned))
            {
                if (planned.Patch?.Base is { ValueKind: System.Text.Json.JsonValueKind.Object } bSnap
                    && planned.Snapshot is { ValueKind: System.Text.Json.JsonValueKind.Object } mSnap
                    && bSnap.TryGetProperty("frames", out var bf) && mSnap.TryGetProperty("frames", out var mf)
                    && bf.ValueKind == System.Text.Json.JsonValueKind.Array && mf.ValueKind == System.Text.Json.JsonValueKind.Array
                    && mf.GetArrayLength() > bf.GetArrayLength())
                {
                    return true;
                }
                continue;
            }
            if (!SpritesHaveSameGeometryAndFrames(baseSprite, modSprite))
                return true;
        }
        var plannedFonts = plan.Assets.Where(a => a.Family == "fonts" && a.Patch is not null)
            .Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string fontName in ModSanitizer.HudFonts)
        {
            if (plannedFonts.Contains(fontName)) continue;
            var baseFont = ancestor.Fonts.FirstOrDefault(f => f.Name.Content == fontName);
            var modFont = source.Fonts.FirstOrDefault(f => f.Name.Content == fontName);
            if (baseFont is null || modFont is null) continue;
            if (baseFont.EmSize != modFont.EmSize || baseFont.DisplayName?.Content != modFont.DisplayName?.Content
                || baseFont.Glyphs.Count != modFont.Glyphs.Count)
                return true;
        }
        return false;
    }

    private static bool SpritesHaveSameGeometryAndFrames(UndertaleSprite a, UndertaleSprite b)
    {
        if (a.Width != b.Width || a.Height != b.Height || a.Textures.Count != b.Textures.Count) return false;
        var aSnap = new AssetFields(_ => { }).Snapshot(a);
        var bSnap = new AssetFields(_ => { }).Snapshot(b);
        return System.Text.Json.JsonElement.DeepEquals(aSnap, bSnap);
    }

    private static bool TryApplyHybridSpriteFrames(
        UndertaleSprite targetSprite,
        UndertaleSprite sourceSprite,
        PlannedAsset asset,
        IReadOnlyList<ModPatch> patches,
        IReadOnlyList<UndertaleData> sources,
        ResourceImporter[] importers)
    {
        if (asset.Patch?.Base is not { ValueKind: System.Text.Json.JsonValueKind.Object } baseSnap
            || asset.Snapshot is not { ValueKind: System.Text.Json.JsonValueKind.Object } modSnap
            || !baseSnap.TryGetProperty("frames", out var bfElem)
            || !modSnap.TryGetProperty("frames", out var mfElem)
            || bfElem.ValueKind != System.Text.Json.JsonValueKind.Array
            || mfElem.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }
        var baseFrames = bfElem.EnumerateArray().ToArray();
        var modFrames = mfElem.EnumerateArray().ToArray();
        if (modFrames.Length <= baseFrames.Length || targetSprite.Textures.Count < baseFrames.Length)
            return false;

        bool keepsAllBaseFrames = true;
        for (int i = 0; i < baseFrames.Length; i++)
        {
            if (!System.Text.Json.JsonElement.DeepEquals(baseFrames[i], modFrames[i]))
            {
                keepsAllBaseFrames = false;
                break;
            }
        }
        if (keepsAllBaseFrames && sourceSprite.Textures.Count >= modFrames.Length)
        {
            // Keep base frames 0..baseFrames.Length-1 already present on targetSprite, and append mod frames baseFrames.Length..modFrames.Length-1.
            while (targetSprite.Textures.Count > baseFrames.Length)
                targetSprite.Textures.RemoveAt(targetSprite.Textures.Count - 1);
            for (int i = baseFrames.Length; i < modFrames.Length; i++)
            {
                var copiedEntry = (UndertaleSprite.TextureEntry)importers[asset.Source].Copy(sourceSprite.Textures[i]);
                targetSprite.Textures.Add(copiedEntry);
            }
            return true;
        }

        // Check if any participating patch is a sanitized hybrid appender (keeps base frames 0..baseFrames.Length-1 and appends >= baseFrames.Length).
        bool hasHybridAppender = false;
        for (int m = 0; m < patches.Count; m++)
        {
            var patchEntry = patches[m].Changes.GetValueOrDefault("sprites")?.Changed.FirstOrDefault(c => c.Name == asset.Name);
            if (patchEntry is null || !patchEntry.Mod.TryGetProperty("frames", out var pfElem) || pfElem.ValueKind != System.Text.Json.JsonValueKind.Array)
                continue;
            var pFrames = pfElem.EnumerateArray().ToArray();
            if (pFrames.Length > baseFrames.Length && Enumerable.Range(0, baseFrames.Length).All(i => System.Text.Json.JsonElement.DeepEquals(baseFrames[i], pFrames[i])))
            {
                hasHybridAppender = true;
                break;
            }
        }
        if (!hasHybridAppender)
            return false;

        // Multi-mod merge involving a hybrid sprite (e.g., custom base UI frames from one mod + appended icon frame from a sanitized mod).
        var baseEntries = targetSprite.Textures.Take(baseFrames.Length).ToArray();
        List<UndertaleSprite.TextureEntry> resolved = new(modFrames.Length);
        for (int i = 0; i < modFrames.Length; i++)
        {
            if (i < baseFrames.Length && System.Text.Json.JsonElement.DeepEquals(modFrames[i], baseFrames[i]))
            {
                resolved.Add(baseEntries[i]);
                continue;
            }
            UndertaleSprite.TextureEntry? copied = null;
            foreach (int m in Enumerable.Range(0, sources.Count).Reverse().Prepend(asset.Source).Distinct())
            {
                var patchEntry = patches[m].Changes.GetValueOrDefault("sprites")?.Changed.FirstOrDefault(c => c.Name == asset.Name);
                if (patchEntry is null || !patchEntry.Mod.TryGetProperty("frames", out var pfElem) || pfElem.ValueKind != System.Text.Json.JsonValueKind.Array)
                    continue;
                var pFrames = pfElem.EnumerateArray().ToArray();
                if (i < pFrames.Length && System.Text.Json.JsonElement.DeepEquals(modFrames[i], pFrames[i]))
                {
                    var candidateSprite = sources[m].Sprites.FirstOrDefault(s => s.Name.Content == asset.Name);
                    if (candidateSprite is not null && i < candidateSprite.Textures.Count)
                    {
                        copied = (UndertaleSprite.TextureEntry)importers[m].Copy(candidateSprite.Textures[i]);
                        break;
                    }
                }
            }
            if (copied is null)
                return false;
            resolved.Add(copied);
        }

        targetSprite.Textures.Clear();
        foreach (var entry in resolved)
            targetSprite.Textures.Add(entry);

        if (modSnap.TryGetProperty("width", out var w)) targetSprite.Width = w.GetUInt32();
        if (modSnap.TryGetProperty("height", out var h)) targetSprite.Height = h.GetUInt32();
        if (modSnap.TryGetProperty("originX", out var ox)) targetSprite.OriginX = ox.GetInt32();
        if (modSnap.TryGetProperty("originY", out var oy)) targetSprite.OriginY = oy.GetInt32();
        if (modSnap.TryGetProperty("marginLeft", out var ml)) targetSprite.MarginLeft = ml.GetInt32();
        if (modSnap.TryGetProperty("marginRight", out var mr)) targetSprite.MarginRight = mr.GetInt32();
        if (modSnap.TryGetProperty("marginTop", out var mt)) targetSprite.MarginTop = mt.GetInt32();
        if (modSnap.TryGetProperty("marginBottom", out var mb)) targetSprite.MarginBottom = mb.GetInt32();
        return true;
    }

    private static bool TryApplyRelocatedUpgradeIcons(
        UndertaleSprite targetSprite,
        UndertaleSprite sourceSprite,
        PlannedAsset asset,
        IReadOnlyList<UpgradeIconRelocation> upgradeIcons,
        IReadOnlyList<ModPatch> patches,
        IReadOnlyList<UndertaleData> sources,
        ResourceImporter[] importers,
        List<string> extraDecisions)
    {
        if (targetSprite.Name.Content != "spr_upgradeicons" || upgradeIcons.Count == 0)
            return false;

        bool sourceIsRelocated = upgradeIcons.Any(u => u.SourceMod == asset.Source);
        if (!sourceIsRelocated)
        {
            if (!TryApplyHybridSpriteFrames(targetSprite, sourceSprite, asset, patches, sources, importers))
                importers[asset.Source].Populate(sourceSprite, targetSprite);
        }

        AppendRelocatedUpgradeIconFrames(targetSprite, upgradeIcons, sources, importers, extraDecisions);
        return true;
    }

    private static void AppendRelocatedUpgradeIconFrames(
        UndertaleSprite targetSprite,
        IReadOnlyList<UpgradeIconRelocation> upgradeIcons,
        IReadOnlyList<UndertaleData> sources,
        ResourceImporter[] importers,
        List<string> extraDecisions)
    {
        var fallbackTexture = targetSprite.Textures.FirstOrDefault(t => t?.Texture is not null)?.Texture;
        foreach (var reloc in upgradeIcons.OrderBy(r => r.TargetFrame))
        {
            var relocSprite = sources[reloc.SourceMod].Sprites.FirstOrDefault(s => s.Name.Content == "spr_upgradeicons");
            var padTexture = fallbackTexture ?? relocSprite?.Textures.FirstOrDefault(t => t?.Texture is not null)?.Texture;
            while (targetSprite.Textures.Count <= reloc.TargetFrame)
                targetSprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = padTexture! });

            bool canCopy = !reloc.Fallback
                && relocSprite is not null
                && reloc.SourceFrame >= 0
                && reloc.SourceFrame < relocSprite.Textures.Count
                && relocSprite.Width == targetSprite.Width
                && relocSprite.Height == targetSprite.Height
                && relocSprite.Textures[reloc.SourceFrame]?.Texture is not null;

            if (canCopy)
            {
                targetSprite.Textures[reloc.TargetFrame] =
                    (UndertaleSprite.TextureEntry)importers[reloc.SourceMod].Copy(relocSprite!.Textures[reloc.SourceFrame]);
            }
            else if (!reloc.Fallback)
            {
                extraDecisions.Add($"WARNING: could not append spr_upgradeicons frame {reloc.SourceFrame} at {reloc.TargetFrame}; kept duplicated/blank icon frame.");
            }
        }
    }

    private static void ApplyRodFrames(
        UndertaleData data,
        IReadOnlyList<RodFrame> frames,
        IReadOnlyList<ModPatch> patches,
        IReadOnlyList<UndertaleData> sources,
        ResourceImporter[] importers)
    {
        var target = data.Sprites.First(s => s.Name.Content == LegacyRodRelocation.Sprite);
        foreach (var frame in frames.OrderBy(f => f.TargetFrame))
        {
            var source = sources[frame.Mod].Sprites.FirstOrDefault(s => s.Name.Content == LegacyRodRelocation.Sprite);
            if (source is null || source.Width != target.Width || source.Height != target.Height
                || source.Textures.Count <= frame.SourceFrame || source.Textures[frame.SourceFrame]?.Texture is null)
                throw new InvalidDataException($"Cannot copy spr_rod frame {frame.SourceFrame} from '{patches[frame.Mod].Mod.FileName}'. Use compatible rod sprites and rebuild.");

            var texture = (UndertaleSprite.TextureEntry)importers[frame.Mod].Copy(source.Textures[frame.SourceFrame]);
            while (target.Textures.Count <= frame.TargetFrame)
                target.Textures.Add(new UndertaleSprite.TextureEntry { Texture = texture.Texture });
            target.Textures[frame.TargetFrame] = texture;
        }
    }

    private static PackAcceptanceReport AnalyzeAcceptance(
        UndertaleData baseData,
        IReadOnlyList<BuildInput> inputs,
        IReadOnlyList<LoadedGameFile> sources,
        IReadOnlyList<ModPatch> patches,
        BuildPlan plan,
        UndertaleData mergedData,
        SemanticCode semantics,
        IReadOnlyDictionary<string, string> mergedCode)
    {
        var modInputs = inputs.Select((input, i) => new AcceptanceModInput(
            patches[i].Mod.Name ?? Path.GetFileNameWithoutExtension(patches[i].Mod.FileName),
            patches[i].Mod.FileName,
            sources[i].Data,
            patches[i])).ToArray();
        var baseCode = semantics.Base.ToDictionary(p => p.Key, p => p.Value.Text, StringComparer.Ordinal);
        var modCodes = semantics.Mods
            .Select(m => (IReadOnlyDictionary<string, string>)m.ToDictionary(p => p.Key, p => p.Value.Text, StringComparer.Ordinal))
            .ToArray();
        return PackAcceptance.Analyze(baseData, modInputs, plan, mergedData, baseCode, modCodes, mergedCode);
    }

    private static IReadOnlyDictionary<AssetKind, int> Counts(UndertaleData data)
    {
        var result = ResourceCatalog.Tables(data).ToDictionary(p => Enum.Parse<AssetKind>(p.Key, true), p => p.Value.Count);
        result[AssetKind.TexturePages] = data.EmbeddedTextures.Count; result[AssetKind.TexturePageItems] = data.TexturePageItems.Count;
        result[AssetKind.Strings] = data.Strings.Count; result[AssetKind.Variables] = data.Variables.Count; result[AssetKind.Functions] = data.Functions.Count;
        return result;
    }
}
