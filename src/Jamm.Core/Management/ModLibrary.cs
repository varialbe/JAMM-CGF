using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jamm.Core.Building;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;

namespace Jamm.Core.Management;

/// <summary>Imports immutable local mod copies, computes patches and reports exact-base compatibility.</summary>
public sealed class ModLibrary(ManagerStore store)
{
    /// <summary>Returns the path of the read-only library copy of the mod archive.</summary>
    public string Archive(ManagedMod mod) => store.Path("mods", ManagerStore.Name(mod.Id), "data.win");
    /// <summary>Returns the path to the raw unsanitized patch file for a library mod.</summary>
    public string PatchPath(ManagedMod mod) => store.Path("patches", ManagerStore.Name(mod.Id) + ".cgfmm-patch.json");
    /// <summary>Returns the path to the sanitized patch file stored alongside the raw patch.</summary>
    public string SanitizedPatchPath(ManagedMod mod) => store.Path("patches", ManagerStore.Name(mod.Id) + ".sanitized.cgfmm-patch.json");
    /// <summary>Returns the path to the sanitizer report file stored alongside the raw patch.</summary>
    public string SanitizerReportPath(ManagedMod mod) => store.Path("patches", ManagerStore.Name(mod.Id) + ".sanitizer.json");

    /// <summary>Verifies archive identities, refreshes outdated caches, and returns the patch selected by the mod's settings.</summary>
    public ModPatch Patch(ManagedMod mod, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (mod.Role is { Role: "addon" } role)
        {
            var ancestor = DeclaredAncestor(mod, role);
            return PatchAgainst(mod, Archive(ancestor), ancestor.Sha256, progress, cancellationToken);
        }
        var (raw, sanitized, _) = EnsurePatches(mod, progress, cancellationToken);
        return SelectPatch(mod, raw, sanitized, ResolveSonarCondition);
    }

    private ModPatch SelectPatch(ManagedMod mod, ModPatch raw, ModPatch sanitized, Func<string, VanillaSonarCondition> sonar)
    {
        bool keepPerf = mod.EffectiveKeepAuthorPerformanceChanges;
        bool keepHud = mod.KeepAuthorHudDesign;
        bool keepGameplay = mod.KeepAuthorGameplayChanges;
        string metaPath = store.Path("mods", ManagerStore.Name(mod.Id), "mod.json");
        if (File.Exists(metaPath))
        {
            try
            {
                var stored = ReadMod(mod.Id);
                keepPerf = stored.EffectiveKeepAuthorPerformanceChanges;
                keepHud = stored.KeepAuthorHudDesign;
                keepGameplay = stored.KeepAuthorGameplayChanges;
            }
            catch (InvalidDataException) { }
        }
        if (!keepPerf && !keepHud && !keepGameplay) return sanitized;
        if (keepPerf && keepHud && keepGameplay) return raw;
        return ModSanitizer.Sanitize(raw,
            keepPerformanceChanges: keepPerf,
            keepHudDesign: keepHud,
            keepGameplayChanges: keepGameplay,
            sonarCondition: sonar(raw.Base.Sha256)).Patch;
    }

    internal ModPatch PatchAgainst(ManagedMod mod, string ancestorPath, string ancestorHash,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        var (raw, sanitized, _) = EnsureAncestorPatches(mod, ancestorPath, ancestorHash, progress, cancellationToken);
        return SelectPatch(mod, raw, sanitized, _ => LoadSonarCondition(ancestorPath, cancellationToken));
    }

    /// <summary>Returns the verified unsanitized patch for a library mod.</summary>
    public ModPatch RawPatch(ManagedMod mod, Action<string>? progress = null, CancellationToken cancellationToken = default) =>
        EnsurePatches(mod, progress, cancellationToken).Raw;

    /// <summary>Returns the cached or freshly computed sanitizer report for a library mod.</summary>
    public ModSanitizerReport SanitizerReport(ManagedMod mod, Action<string>? progress = null, CancellationToken cancellationToken = default) =>
        EnsurePatches(mod, progress, cancellationToken).Report;

    /// <summary>Toggles whether a mod keeps all of its author's performance changes, including harmful ones stripped by default.</summary>
    public ManagedMod SetKeepAuthorPerformanceChanges(string query, bool keep)
    {
        var mod = Find(query);
        var updated = mod with { KeepAuthorPerformanceChanges = keep, KeepAuthorPerformanceChangesConfigured = true };
        store.Write(store.Path("mods", mod.Id, "mod.json"), updated);
        return updated;
    }

    /// <summary>Toggles whether a mod keeps its author's HUD design instead of reverting HUD assets to the base game.</summary>
    public ManagedMod SetKeepAuthorHudDesign(string query, bool keep)
    {
        var mod = Find(query);
        var updated = mod with { KeepAuthorHudDesign = keep };
        store.Write(store.Path("mods", mod.Id, "mod.json"), updated);
        return updated;
    }

    /// <summary>Toggles whether a mod keeps its author's gameplay/sonar changes instead of enforcing vanilla progression.</summary>
    public ManagedMod SetKeepAuthorGameplayChanges(string query, bool keep)
    {
        var mod = Find(query);
        var updated = mod with { KeepAuthorGameplayChanges = keep };
        store.Write(store.Path("mods", mod.Id, "mod.json"), updated);
        return updated;
    }

    private VanillaSonarCondition ResolveSonarCondition(string baseSha256)
    {
        string? basePath = store.Config().Base;
        if (string.IsNullOrWhiteSpace(basePath) || !File.Exists(basePath) || BuildFiles.Hash(basePath) != baseSha256)
            return VanillaSonarCondition.Default;
        return LoadSonarCondition(basePath);
    }

    private static VanillaSonarCondition LoadSonarCondition(string path, CancellationToken cancellationToken = default)
    {
        using var file = GameFileLoader.Load(path, cancellationToken: cancellationToken);
        return VanillaSonarCondition.FromData(file.Data);
    }

    private (ModPatch Raw, ModPatch Sanitized, ModSanitizerReport Report) EnsureAncestorPatches(ManagedMod mod,
        string ancestorPath, string ancestorHash, Action<string>? progress, CancellationToken cancellationToken)
    {
        string archive = Archive(mod);
        if (!File.Exists(archive) || BuildFiles.Hash(archive) != mod.Sha256)
            throw new InvalidDataException($"Stored archive '{archive}' for '{mod.Name}' is missing or changed. Re-add the original mod.");
        if (!File.Exists(ancestorPath) || BuildFiles.Hash(ancestorPath) != ancestorHash)
            throw new InvalidDataException($"Ancestor archive '{ancestorPath}' is missing or changed. Re-add the original base mod and retry.");
        cancellationToken.ThrowIfCancellationRequested();
        string cache = AncestorCachePath(mod, ancestorHash);
        if (File.Exists(cache))
        {
            try
            {
                var cached = store.Read<AncestorPatchCache>(cache);
                bool Matches(ModPatch patch) => patch.Base.Sha256 == ancestorHash && patch.Mod.Sha256 == mod.Sha256
                    && patch.Mod.Name == mod.Name && patch.AssetTypesVersion == PatchJson.AssetTypesVersion
                    && patch.FormatVersion == PatchJson.FormatVersion;
                if (cached.Version == AncestorCacheVersion && cached.Raw is not null && cached.Sanitized is not null && cached.Report is not null
                    && cached.Report.SanitizerVersion == ModSanitizer.Version && Matches(cached.Raw) && Matches(cached.Sanitized))
                    return (cached.Raw, cached.Sanitized, cached.Report);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { }
        }
        progress?.Invoke($"Diffing {mod.Name} against its selected ancestor…");
        void Report(string message) { cancellationToken.ThrowIfCancellationRequested(); progress?.Invoke(message); }
        using var original = GameFileLoader.Load(ancestorPath, Report, cancellationToken);
        using var source = GameFileLoader.Load(archive, Report, cancellationToken);
        var raw = PatchBuilder.Create(original, source, mod.Name, Report, AnalysisOptions.Default with { DataDirectory = store.Root });
        if (raw.Base.Sha256 != ancestorHash || raw.Mod.Sha256 != mod.Sha256)
            throw new IOException($"Inputs changed while diffing '{mod.Name}'. Retry with stable archive copies.");
        raw = raw with { Mod = raw.Mod with { FileName = mod.SourceFileName } };
        var sanitized = ModSanitizer.Sanitize(raw, sonarCondition: VanillaSonarCondition.FromData(original.Data));
        cancellationToken.ThrowIfCancellationRequested();
        store.WriteText(cache, JsonSerializer.Serialize(new AncestorPatchCache(AncestorCacheVersion, raw, sanitized.Patch, sanitized.Report),
            AncestorCacheJson) + "\n");
        return (raw, sanitized.Patch, sanitized.Report);
    }

    private const int AncestorCacheVersion = 1;
    private static readonly JsonSerializerOptions AncestorCacheJson = new(ManagerStore.JsonOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private sealed record AncestorPatchCache(int Version, ModPatch Raw, ModPatch Sanitized, ModSanitizerReport Report);

    private string AncestorCachePath(ManagedMod mod, string ancestorHash) => store.Path("patches", ManagerStore.Name(mod.Id),
        ManagerStore.Name(ancestorHash) + "-" + ManagerStore.Name(mod.Sha256) + ".json");

    internal static ManagedMod? FindAncestor(IEnumerable<ManagedMod> candidates, ModRole role)
    {
        var mods = candidates.ToArray();
        return mods.FirstOrDefault(m => m.Id == role.Base) ?? (mods.Where(role.Targets).Take(2).ToArray() switch
        {
            [var only] => only,
            _ => null
        });
    }

    private ManagedMod DeclaredAncestor(ManagedMod mod, ModRole role) =>
        FindAncestor(All().Where(m => m.Id != mod.Id), role)
        ?? throw new InvalidDataException($"Addon '{mod.Name}' requires base '{role.Base}'. Add the matching base mod to the library.");

    /// <summary>Resolves the source archive for a ModKit package using its declaration or configured vanilla.</summary>
    public string ModKitAncestor(ModKitManifest manifest)
    {
        if (manifest.Role is not { Role: "addon" } role)
            return store.Config().Base ?? throw new ArgumentException("Configure a legacy base with 'cgfmm config set base <data.win>' or pass --base.");
        var ancestor = FindAncestor(All(), role)
            ?? throw new InvalidDataException($"ModKit addon '{manifest.Name}' requires base '{role.Base}'. Import the matching base mod before building the addon.");
        string path = Archive(ancestor);
        if (!File.Exists(path) || BuildFiles.Hash(path) != ancestor.Sha256)
            throw new InvalidDataException($"Stored base archive '{path}' for '{ancestor.Name}' is missing or changed. Re-add the original base mod.");
        return path;
    }

    /// <summary>Stores or updates the ModKit source folder path in the mod's metadata.</summary>
    public ManagedMod SetSourceFolder(string query, string sourceFolder)
    {
        var mod = Find(query);
        var updated = mod with { SourceFolder = Path.GetFullPath(sourceFolder), IsModKit = true };
        store.Write(store.Path("mods", mod.Id, "mod.json"), updated);
        return updated;
    }

    /// <summary>Rebuilds a ModKit folder mod from its source folder, updating its library copy and cached patch.</summary>
    public AddModResult Rebuild(string query, string? sourceFolder = null, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var mod = Find(query);
        string? folder = sourceFolder ?? mod.SourceFolder;
        if (string.IsNullOrWhiteSpace(folder))
            throw new InvalidOperationException($"Mod '{mod.Name}' does not have a stored ModKit source folder.");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"The ModKit source folder for '{mod.Name}' was not found: '{folder}'.");
        if (!File.Exists(Path.Combine(folder, ModKitPackage.ManifestFileName)))
            throw new FileNotFoundException($"Missing '{ModKitPackage.ManifestFileName}' in '{folder}'. Add a jamm-mod.json manifest or fix the folder path.");
        return Add(folder, progress: progress, cancellationToken: cancellationToken, stableId: mod.Id);
    }

    private (ModPatch Raw, ModPatch Sanitized, ModSanitizerReport Report) EnsurePatches(ManagedMod mod, Action<string>? progress, CancellationToken cancellationToken)
    {
        if (mod.Role is { Role: "addon" } role)
        {
            var ancestor = DeclaredAncestor(mod, role);
            return EnsureAncestorPatches(mod, Archive(ancestor), ancestor.Sha256, progress, cancellationToken);
        }
        var patch = PatchJson.Deserialize(File.ReadAllText(PatchPath(mod)));
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(Archive(mod)))
            throw new InvalidDataException($"Stored library copy for '{mod.Name}' is missing: '{Archive(mod)}'. Add the original mod again to regenerate its cached patch.");
        if (patch.Base.Sha256 != mod.BaseSha256 || patch.Mod.Sha256 != mod.Sha256 || patch.Mod.Name != mod.Name
            || BuildFiles.Hash(Archive(mod)) != mod.Sha256)
            throw new InvalidDataException($"Mod '{mod.Name}' or its patch changed. Remove and add the original mod again.");
        if (patch.AssetTypesVersion != PatchJson.AssetTypesVersion)
        {
            string basePath = store.Config().Base ?? throw new ArgumentException("Choose the legacy base data.win in Settings before refreshing cached patches.");
            if (BuildFiles.Hash(basePath) != mod.BaseSha256)
                throw new InvalidDataException($"Cannot refresh cached patch for '{mod.Name}': '{basePath}' is not its original base. Select the original legacy base in Settings.");
            progress?.Invoke($"Refreshing outdated cached patch for {mod.Name}…");
            void Report(string message) { cancellationToken.ThrowIfCancellationRequested(); progress?.Invoke(message); }
            using var original = GameFileLoader.Load(basePath, Report, cancellationToken);
            using var archive = GameFileLoader.Load(Archive(mod), Report, cancellationToken);
            var refreshed = PatchBuilder.Create(original, archive, mod.Name, Report, AnalysisOptions.Default with { DataDirectory = store.Root });
            refreshed = refreshed with { Mod = refreshed.Mod with { FileName = patch.Mod.FileName } };
            if (BuildFiles.Hash(basePath) != mod.BaseSha256 || BuildFiles.Hash(Archive(mod)) != mod.Sha256
                || refreshed.Base.Sha256 != mod.BaseSha256 || refreshed.Mod.Sha256 != mod.Sha256)
                throw new IOException($"Inputs changed while refreshing '{mod.Name}'. Retry with stable archive copies.");
            cancellationToken.ThrowIfCancellationRequested();
            store.WriteText(PatchPath(mod), PatchJson.Serialize(refreshed));
            patch = refreshed;
            progress?.Invoke($"Refreshed cached patch for {mod.Name}.");
        }
        string sanitizedPath = SanitizedPatchPath(mod);
        string reportPath = SanitizerReportPath(mod);
        if (File.Exists(sanitizedPath) && File.Exists(reportPath))
        {
            try
            {
                var cachedReport = store.Read<ModSanitizerReport>(reportPath);
                var cachedSanitized = PatchJson.Deserialize(File.ReadAllText(sanitizedPath));
                if (cachedReport.SanitizerVersion == ModSanitizer.Version
                    && cachedReport.ModSha256 == mod.Sha256
                    && cachedReport.BaseSha256 == mod.BaseSha256
                    && cachedSanitized.Base.Sha256 == mod.BaseSha256
                    && cachedSanitized.Mod.Sha256 == mod.Sha256
                    && cachedSanitized.Mod.Name == mod.Name
                    && cachedSanitized.AssetTypesVersion == PatchJson.AssetTypesVersion)
                {
                    return (patch, cachedSanitized, cachedReport);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var sanitizedResult = ModSanitizer.Sanitize(patch, sonarCondition: ResolveSonarCondition(mod.BaseSha256));
        store.WriteText(sanitizedPath, PatchJson.Serialize(sanitizedResult.Patch));
        store.Write(reportPath, sanitizedResult.Report);
        return (patch, sanitizedResult.Patch, sanitizedResult.Report);
    }

    internal IReadOnlyList<ManagedMod> All() => Directory.EnumerateDirectories(store.Path("mods"))
        .Where(p => !Path.GetFileName(p).StartsWith('.')).Order(StringComparer.Ordinal)
        .Select(p => ReadMod(Path.GetFileName(p))).ToArray();

    /// <summary>Reads library identities and provenance without loading game archives.</summary>
    public IReadOnlyList<ManagedMod> Metadata() => All();

    private ManagedMod ReadMod(string id)
    {
        ManagerStore.Name(id);
        var mod = store.Read<ManagedMod>(store.Path("mods", id, "mod.json"));
        if (mod.Id != id || string.IsNullOrWhiteSpace(mod.Name) || string.IsNullOrWhiteSpace(mod.Sha256) || mod.Role?.IsValid() == false)
            throw new InvalidDataException($"Library metadata for '{id}' is invalid. Remove the damaged entry and add the source mod again.");
        return mod;
    }

    /// <summary>Finds a mod by exact ID, unique display name or source filename stem.</summary>
    public ManagedMod Find(string query)
    {
        var all = All();
        var exact = all.FirstOrDefault(m => m.Id == query);
        if (exact is not null) return exact;
        var matches = all.Where(m => m.Name == query || Path.GetFileNameWithoutExtension(m.SourceFileName) == query).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ArgumentException(matches.Length == 0
            ? $"Mod '{query}' was not found. Use 'cgfmm list' to see library IDs."
            : $"Mod name '{query}' is ambiguous. Use its full ID from 'cgfmm list'.");
    }

    /// <summary>Copies a .win, a .zip (containing data.win, ModKit package, or recipe), or builds a ModKit package folder, then patches against the configured base.</summary>
    public AddModResult Add(string source, string? name = null, Action<string>? progress = null, CancellationToken cancellationToken = default, ModOrigin? origin = null, string? stableId = null, string? description = null)
    {
        var reportProgress = progress;
        progress = message => { cancellationToken.ThrowIfCancellationRequested(); reportProgress?.Invoke(message); };
        cancellationToken.ThrowIfCancellationRequested();
        string basePath = store.Config().Base ?? throw new ArgumentException("Set the legacy base first: cgfmm config set base <original data.win>.");
        string trimmedSource = Path.TrimEndingDirectorySeparator(source);
        bool isModKitFolder = Directory.Exists(trimmedSource);
        bool isZip = !isModKitFolder && trimmedSource.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (name is not null) ValidateDisplayName(name);
        else if (!isModKitFolder && !isZip) ValidateDisplayName(Path.GetFileNameWithoutExtension(trimmedSource));
        if (stableId is not null) ManagerStore.Name(stableId);
        string? extractedPackageDir = null;
        string stage = store.Path("mods", ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string copy = Path.Combine(stage, "data.win");
        string? publishedPatch = null, publishedSanitized = null, publishedReport = null;
        string? previousDirectory = null;
        Dictionary<string, byte[]> previousFiles = [];
        bool published = false;
        try
        {
            ModKitManifest? modKitManifest = isModKitFolder ? ModKitPackage.Load(trimmedSource) : null;
            RecipeManifest? recipeManifest = null;
            (string? Id, string? Version, ModRole? Role) archiveManifest = default;
            string? sourceFolder = isModKitFolder ? Path.GetFullPath(trimmedSource) : null;
            if (modKitManifest is not null) ValidateDisplayName(name ?? modKitManifest.Name);

            if (isZip)
            {
                var zipKind = InspectZipKind(trimmedSource, cancellationToken);
                if (zipKind == ZipPackageKind.DataWin)
                {
                    ValidateDisplayName(name ?? Path.GetFileNameWithoutExtension(trimmedSource));
                    archiveManifest = ReadArchiveManifest(trimmedSource, zip: true);
                    CopyZip(trimmedSource, copy, cancellationToken);
                }
                else
                {
                    extractedPackageDir = store.Path("mods", ".pkg-" + Guid.NewGuid().ToString("N"));
                    string packageRoot = ExtractSourceZip(trimmedSource, extractedPackageDir, zipKind, cancellationToken);
                    if (zipKind == ZipPackageKind.ModKit)
                    {
                        modKitManifest = ModKitPackage.Load(packageRoot);
                        ValidateDisplayName(name ?? modKitManifest.Name);
                        ModKitBuilder.Build(packageRoot, ModKitAncestor(modKitManifest), copy, progress, cancellationToken, noRepack: true);
                    }
                    else
                    {
                        recipeManifest = RecipePackage.Load(packageRoot);
                        ValidateDisplayName(name ?? recipeManifest.Name);
                        stableId ??= recipeManifest.StableId;
                        ManagerStore.Name(stableId);
                        RecipePackage.Build(packageRoot, basePath, copy, progress, cancellationToken);
                    }
                }
            }
            else if (modKitManifest is not null)
            {
                ModKitBuilder.Build(trimmedSource, ModKitAncestor(modKitManifest), copy, progress, cancellationToken, noRepack: true);
            }
            else if (trimmedSource.EndsWith(".win", StringComparison.OrdinalIgnoreCase))
            {
                archiveManifest = ReadArchiveManifest(trimmedSource, zip: false);
                using var input = new FileStream(trimmedSource, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write);
                input.CopyTo(output);
            }
            else
            {
                throw new ArgumentException($"Cannot add '{Path.GetFileName(trimmedSource)}'. Choose a .win archive, a .zip package, or a ModKit package folder.");
            }

            string sourceName = modKitManifest is not null ? $"{modKitManifest.Id}.win"
                : recipeManifest is not null ? $"{recipeManifest.Id}.win"
                : Path.GetFileName(trimmedSource);
            string displayName = name ?? modKitManifest?.Name ?? recipeManifest?.Name ?? Path.GetFileNameWithoutExtension(trimmedSource);
            description ??= modKitManifest?.Description ?? recipeManifest?.Description;
            string? author = modKitManifest?.Author ?? recipeManifest?.Author;
            string? version = modKitManifest?.Version ?? recipeManifest?.Version ?? archiveManifest.Version;
            string? packageId = modKitManifest?.Id ?? archiveManifest.Id;
            ModRole? role = modKitManifest?.Role ?? archiveManifest.Role;
            bool isModKit = modKitManifest is not null;
            ValidateDisplayName(displayName);
            if (stableId is not null) ManagerStore.Name(stableId);
            string hash = BuildFiles.Hash(copy);
            var existing = All().FirstOrDefault(m => m.Sha256 == hash && (stableId is null || m.Id == stableId));
            if (existing is not null)
            {
                if (existing.Role is not { Role: "addon" } && role is not { Role: "addon" })
                {
                    Patch(existing, progress, cancellationToken);
                    if (existing.BaseSha256 != BuildFiles.Hash(basePath))
                        throw new InvalidDataException($"'{existing.Name}' was patched against another base. Restore that base configuration, or remove this mod from its profiles and re-add it.");
                }
                bool updatedMeta = false;
                if (origin is not null && !existing.Origins.Contains(origin))
                {
                    existing = existing with { Origins = [.. existing.Origins, origin] };
                    updatedMeta = true;
                }
                if (isModKitFolder && (existing.SourceFolder != sourceFolder || !existing.IsModKit || existing.Version != version || existing.Author != author || existing.Description != description || existing.Name != displayName))
                {
                    existing = existing with
                    {
                        Name = displayName,
                        SourceFolder = sourceFolder,
                        IsModKit = true,
                        Author = author ?? existing.Author,
                        Version = version ?? existing.Version,
                        Description = description ?? existing.Description
                    };
                    updatedMeta = true;
                }
                if ((isModKit || archiveManifest != default)
                    && (existing.PackageId != packageId || existing.Role != role || existing.Version != version))
                {
                    existing = existing with { PackageId = packageId, Role = role, Version = version };
                    updatedMeta = true;
                }
                if (updatedMeta)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    store.Write(store.Path("mods", existing.Id, "mod.json"), existing);
                }
                return new(existing, ["This exact archive is already in the library; its existing name is retained."], true);
            }
            if (All().Any(m => m.Name == displayName && m.Id != stableId)) throw new ArgumentException($"Mod name '{displayName}' already exists. Use --name with a unique name.");
            using var original = GameFileLoader.Load(basePath, progress, cancellationToken);
            using var mod = GameFileLoader.Load(copy, progress, cancellationToken);
            List<string> notices = CompatibilityWarnings(original.Inspection, mod.Inspection).ToList();
            ModPatch? patch = null;
            try
            {
                if (role is { Role: "addon" }) PatchBuilder.Validate(original.Inspection, mod.Inspection);
                else patch = PatchBuilder.Create(original, mod, displayName, progress, AnalysisOptions.Default with { DataDirectory = store.Root });
            }
            catch (Exception ex) when (ex is ArgumentException or NotComparableException or InvalidDataException)
            { throw new InvalidDataException($"Cannot add '{sourceName}': {string.Join(" ", notices)} {ex.Message} Choose a compatible legacy VM mod and base.", ex); }
            // Patch identity describes the .win bytes; metadata separately retains the supplied zip filename.
            if (patch is not null)
                patch = patch with { Mod = patch.Mod with { FileName = isZip && recipeManifest is null && modKitManifest is null ? "data.win" : sourceName } };
            if (BuildFiles.Hash(basePath) != original.Inspection.Sha256 || hash != mod.Inspection.Sha256)
                throw new IOException("An input changed during import. Retry with stable copies.");
            var entry = new ManagedMod(stableId ?? hash, displayName, sourceName, hash, DateTimeOffset.UtcNow, original.Inspection.Sha256,
                mod.Inspection.Timestamp, mod.Inspection.GameMakerVersion, mod.Inspection.BytecodeVersion)
            {
                Origins = origin is null ? [] : [origin],
                Description = description,
                SourceFolder = sourceFolder,
                Author = author,
                Version = version,
                IsModKit = isModKit,
                PackageId = packageId,
                Role = role,
                SuggestBase = patch is not null && SuggestsBase(patch, original.Inspection)
            };
            store.Write(store.Path("mods", Path.GetFileName(stage), "mod.json"), entry);
            string patchPath = PatchPath(entry);
            if (stableId is null && File.Exists(patchPath)) throw new IOException("An orphaned patch already exists for this mod. Remove it from the data directory and retry.");
            if (stableId is not null && Directory.Exists(store.Path("mods", stableId)))
            {
                foreach (string path in new[] { patchPath, SanitizedPatchPath(entry), SanitizerReportPath(entry) })
                    if (File.Exists(path)) previousFiles[path] = File.ReadAllBytes(path);
                string backupDirectory = store.Path("mods", ".previous-" + Guid.NewGuid().ToString("N"));
                Directory.Move(store.Path("mods", stableId), backupDirectory);
                previousDirectory = backupDirectory;
            }
            if (patch is not null)
            {
                var sanitized = ModSanitizer.Sanitize(patch, sonarCondition: VanillaSonarCondition.FromData(original.Data));
                publishedPatch = patchPath;
                publishedSanitized = SanitizedPatchPath(entry);
                publishedReport = SanitizerReportPath(entry);
                store.WriteText(publishedPatch, PatchJson.Serialize(patch));
                store.WriteText(publishedSanitized, PatchJson.Serialize(sanitized.Patch));
                store.Write(publishedReport, sanitized.Report);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, store.Path("mods", entry.Id));
            published = true;
            if (role is { Role: "addon" })
                notices.Add($"'{displayName}' declares addon base '{role.Base}'. Enable that base in the profile to generate its addon patch.");
            else if (entry.SuggestBase && role is null)
                notices.Add($"'{displayName}' changes over 25% of vanilla code and may be a base mod. Assign its role in the profile if needed.");
            return new(entry, notices.Concat(patch?.Warnings ?? []).Distinct(StringComparer.Ordinal).ToArray(), false);
        }
        catch
        {
            if (Directory.Exists(stage))
            {
                if (publishedPatch is not null && File.Exists(publishedPatch)) File.Delete(publishedPatch);
                if (publishedSanitized is not null && File.Exists(publishedSanitized)) File.Delete(publishedSanitized);
                if (publishedReport is not null && File.Exists(publishedReport)) File.Delete(publishedReport);
            }
            foreach (var saved in previousFiles) File.WriteAllBytes(saved.Key, saved.Value);
            if (previousDirectory is not null) Directory.Move(previousDirectory, store.Path("mods", stableId!));
            throw;
        }
        finally
        {
            if (extractedPackageDir is not null && Directory.Exists(extractedPackageDir)) Directory.Delete(extractedPackageDir, true);
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            if (published && previousDirectory is not null) Directory.Delete(previousDirectory, true);
        }
    }

    /// <summary>Lists timestamp/version compatibility separately from exact patch-base hash compatibility.</summary>
    public IReadOnlyList<ModListEntry> List(Action<string>? progress = null)
    {
        var all = All();
        if (all.Count == 0) return [];
        string? basePath = store.Config().Base;
        using var original = basePath is null ? null : GameFileLoader.Load(basePath);
        return all.Select(m =>
        {
            if (m.Role is { Role: "addon" } role)
            {
                var ancestor = FindAncestor(all.Where(other => other.Id != m.Id), role);
                if (ancestor is null)
                    return new ModListEntry(m, false, false, false, false, $"requires base '{role.Base}'; add it to the library");
                bool matches = role.Mismatch(ancestor) is null;
                bool sameVersion = m.GmVersion == ancestor.GmVersion && m.Bytecode == ancestor.Bytecode;
                return new ModListEntry(m, matches, m.Gen8Timestamp == ancestor.Gen8Timestamp, sameVersion,
                    File.Exists(AncestorCachePath(m, ancestor.Sha256)),
                    matches ? $"targets {ancestor.Name}" : $"targets {ancestor.Name}; declared base version or hash differs");
            }
            bool hash = m.BaseSha256 == original?.Inspection.Sha256;
            bool timestamp = m.Gen8Timestamp == original?.Inspection.Timestamp;
            bool version = m.GmVersion == original?.Inspection.GameMakerVersion && m.Bytecode == original?.Inspection.BytecodeVersion;
            bool cached;
            try { cached = hash && Patch(m, progress).Base.Sha256 == m.BaseSha256; }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or NotSupportedException) { cached = false; }
            return new ModListEntry(m, hash, timestamp, version, cached, original is null ? "base not configured"
                : !hash ? "patch targets another base; re-add against this base"
                : !version ? "incompatible GameMaker/bytecode version"
                : !timestamp ? "patched against configured base; source build timestamp differs" : "matching base hash and source timestamp/version");
        }).ToArray();
    }

    /// <summary>Removes an unused mod and its patch; profiles must be deleted first if they use it.</summary>
    public void Remove(string query)
    {
        var mod = Find(query);
        string[] used = new ProfileLibrary(store).List().Where(p => p.Mods.Contains(mod.Id, StringComparer.Ordinal)).Select(p => p.Name).ToArray();
        if (used.Length > 0) throw new IOException($"Mod '{mod.Name}' is used by profiles: {string.Join(", ", used)}. Delete those profiles before removing it.");
        string directory = store.Path("mods", mod.Id);
        // Refuse linked contents before recursive deletion, too.
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            BuildFiles.RefuseStorageLink(path);
            if (Directory.Exists(path)) throw new IOException($"Mod '{mod.Name}' contains an unexpected directory. Move it out of the library entry before removing the mod.");
        }
        File.Delete(PatchPath(mod));
        if (File.Exists(SanitizedPatchPath(mod))) File.Delete(SanitizedPatchPath(mod));
        if (File.Exists(SanitizerReportPath(mod))) File.Delete(SanitizerReportPath(mod));
        string ancestorCache = store.Path("patches", mod.Id);
        if (Directory.Exists(ancestorCache))
        {
            foreach (string path in Directory.EnumerateFiles(ancestorCache, "*.json"))
            {
                BuildFiles.RefuseStorageLink(path);
                File.Delete(path);
            }
            Directory.Delete(ancestorCache);
        }
        Directory.Delete(directory, true);
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("The mod needs a nonempty name.");
        if (displayName.Contains(',') || displayName.Any(char.IsControl))
            throw new ArgumentException("Mod names cannot contain commas or control characters. Use --name with a readable name so it can be selected with --mods.");
    }

    private static bool SuggestsBase(ModPatch patch, GameInspection original)
    {
        var code = patch.Changes["code"];
        return (code.Changed.Count + code.Removed.Count) * 4L > original.Counts.GetValueOrDefault(AssetKind.Code);
    }

    private static (string? Id, string? Version, ModRole? Role) ReadArchiveManifest(string source, bool zip)
    {
        string sidecar = Path.ChangeExtension(source, ".jamm.json");
        using var archive = zip ? ZipFile.OpenRead(source) : null;
        var entries = archive?.Entries.Where(e => e.Name.Equals("jamm.json", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (entries?.Length > 1)
            throw new InvalidDataException($"'{source}' contains multiple jamm.json manifests. Keep only one.");
        var entry = entries?.SingleOrDefault();
        if (entry?.Length > 64 * 1024 || File.Exists(sidecar) && new FileInfo(sidecar).Length > 64 * 1024)
            throw new InvalidDataException($"The manifest for '{source}' exceeds 64 KiB. Use a small JSON metadata file.");
        using var stream = entry?.Open() ?? (File.Exists(sidecar) ? File.OpenRead(sidecar) : null);
        if (stream is null) return default;
        string label = entry is null ? sidecar : $"{source}/{entry.FullName}";
        try
        {
            using var document = JsonDocument.Parse(stream);
            return ModRole.ReadManifest(document.RootElement, label);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Cannot read manifest '{label}'. Fix its JSON syntax and retry.", ex);
        }
    }

    private enum ZipPackageKind { DataWin, ModKit, Recipe }

    private static ZipPackageKind InspectZipKind(string source, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(source);
        var entries = ValidateZipEntries(zip, Path.GetFileName(source), 512L * 1048576, cancellationToken);
        int dataWins = 0, modKits = 0, recipes = 0;
        foreach (var (entry, normalized) in entries)
        {
            string fileName = normalized.Split('/').Last();
            if (fileName.Equals("data.win", StringComparison.OrdinalIgnoreCase)) dataWins++;
            else if (normalized.Count(c => c == '/') <= 1 && fileName.Equals(ModKitPackage.ManifestFileName, StringComparison.OrdinalIgnoreCase)) modKits++;
            else if (normalized.Count(c => c == '/') <= 1 && fileName.Equals(RecipePackage.ManifestFileName, StringComparison.OrdinalIgnoreCase)) recipes++;
        }
        if (dataWins == 1 && modKits == 0 && recipes == 0) return ZipPackageKind.DataWin;
        if (dataWins == 0 && modKits == 1 && recipes == 0) return ZipPackageKind.ModKit;
        if (dataWins == 0 && modKits == 0 && recipes == 1) return ZipPackageKind.Recipe;
        throw new InvalidDataException($"'{Path.GetFileName(source)}' must contain exactly one data.win, one jamm-mod.json, or one jamm-recipe.json.");
    }

    private static string ExtractSourceZip(string source, string destinationRoot, ZipPackageKind kind, CancellationToken cancellationToken)
    {
        const long limit = 64L * 1048576;
        using var zip = ZipFile.OpenRead(source);
        var entries = ValidateZipEntries(zip, Path.GetFileName(source), limit, cancellationToken);
        Directory.CreateDirectory(destinationRoot);
        string fullRoot = Path.GetFullPath(destinationRoot);
        string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        string manifestName = kind == ZipPackageKind.ModKit ? ModKitPackage.ManifestFileName : RecipePackage.ManifestFileName;
        string? manifestDir = null;
        long total = 0;
        byte[] buffer = new byte[65536];

        foreach (var (entry, normalized) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(normalized) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            string ext = Path.GetExtension(entry.Name);
            if (ext.Equals(".win", StringComparison.OrdinalIgnoreCase) || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Source package '{Path.GetFileName(source)}' cannot contain binary '{entry.FullName}'.");
            string target = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(rootPrefix, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsafe zip entry '{entry.FullName}' in '{Path.GetFileName(source)}'.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            long copied = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = input.Read(buffer);
                if (count == 0) break;
                copied += count;
                total += count;
                if (total > limit) throw new InvalidDataException("Expanded source package exceeds the 64 MB safety limit.");
                output.Write(buffer, 0, count);
            }
            if (Path.GetFileName(target).Equals(manifestName, StringComparison.OrdinalIgnoreCase))
                manifestDir = Path.GetDirectoryName(target);
        }

        return manifestDir ?? throw new InvalidDataException($"Missing '{manifestName}' in '{Path.GetFileName(source)}'.");
    }

    private static List<(ZipArchiveEntry Entry, string Normalized)> ValidateZipEntries(ZipArchive zip, string label, long limit, CancellationToken cancellationToken)
    {
        long expanded = 0;
        if (zip.Entries.Count > 4096) throw new InvalidDataException("ZIP has too many entries. Choose a smaller mod archive.");
        var result = new List<(ZipArchiveEntry, string)>(zip.Entries.Count);
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Length > limit - expanded) throw new InvalidDataException($"ZIP expanded data exceeds the {limit / 1048576} MB safety limit.");
            expanded += entry.Length;
            string normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(p => p == "..")
                || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException($"Unsafe zip entry '{entry.FullName}' in '{label}'. Choose a zip without absolute paths, parent traversal or symbolic links.");
            result.Add((entry, normalized));
        }
        return result;
    }

    internal static void CopyZip(string source, string destination, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(source);
        const long limit = 512L * 1048576;
        var entries = ValidateZipEntries(zip, Path.GetFileName(source), limit, cancellationToken);
        var candidates = entries
            .Where(e => e.Normalized.Split('/').Last().Equals("data.win", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Entry)
            .ToList();
        if (candidates.Count != 1) throw new InvalidDataException($"'{Path.GetFileName(source)}' must contain exactly one data.win (nested folders are supported); found {candidates.Count}.");
        using var input = candidates[0].Open();
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        byte[] buffer = new byte[65536]; long copied = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = input.Read(buffer); if (count == 0) break; copied += count;
            if (copied > limit) throw new InvalidDataException("Expanded data.win exceeds the 512 MB safety limit.");
            output.Write(buffer, 0, count);
        }
    }

    private static IEnumerable<string> CompatibilityWarnings(GameInspection original, GameInspection mod)
    {
        if (original.Timestamp == mod.Timestamp && original.GameMakerVersion == mod.GameMakerVersion && original.BytecodeVersion == mod.BytecodeVersion) yield break;
        string manifest = mod.Timestamp switch
        {
            1692207312 => "Expected Steam depot 343782 manifest 9039707068725406600 (16 Aug 2023).",
            1692057761 => "Expected Steam depot 343782 manifest 1512054085756769140 (15 Aug 2023 fast); public 2811289735487427888 is near-identical.",
            _ => "See docs/legacy-version.md to locate the matching Steam manifest."
        };
        yield return $"Mod source GEN8 timestamp {mod.Timestamp}, GameMaker {mod.GameMakerVersion}, bytecode {mod.BytecodeVersion} differs from configured base {original.Timestamp}, {original.GameMakerVersion}, bytecode {original.BytecodeVersion}. {manifest} Timestamp differences are included in the patch against the configured base.";
    }
}
