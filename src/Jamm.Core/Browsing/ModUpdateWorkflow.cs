using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;

namespace Jamm.Core.Browsing;

/// <summary>Downloads verified catalog packages, warns about save compatibility and supports updater rollback.</summary>
public sealed class ModUpdateWorkflow(ManagerStore store, IModCatalogProvider catalog, IModDownloadResolver resolver, ModHttp http)
{
    private sealed record RollbackState(string Profile, string GameDirectory, string GameBackup, string[] ModIds, string SaveBackup);

    /// <summary>Checks enabled catalog packages without making local changes.</summary>
    public Task<IReadOnlyList<CatalogUpdate>> CheckAsync(string? profile = null, CancellationToken token = default)
        => new CatalogUpdates(store, catalog).CheckAsync(profile, token);

    /// <summary>Checks exact downloaded bytes against a published catalog digest.</summary>
    public static bool PackageHashMatches(string path, string expectedSha256) =>
        expectedSha256 is { Length: 64 } && expectedSha256.All(Uri.IsHexDigit)
        && string.Equals(BuildFiles.Hash(path), expectedSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>Downloads and stages updates, returning save-risk reasons before any game installation.</summary>
    public async Task<(PreparedProfile Build, IReadOnlyList<(CatalogUpdate Update, IReadOnlyList<string> Reasons)> Conflicts, string SaveBackup)> PrepareAsync(
        string? profileName = null, Action<string>? progress = null, CancellationToken token = default)
    {
        var updates = await CheckAsync(profileName, token);
        if (updates.Count == 0) throw new InvalidOperationException("All catalog mods in this profile are up to date.");
        var config = store.Config();
        string profile = profileName ?? config.ActiveProfile ?? throw new ArgumentException("No active profile is selected.");
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var library = new ModLibrary(store);
        List<(CatalogUpdate, IReadOnlyList<string>)> conflicts = [];
        var profileModel = new ProfileLibrary(store).Get(profile);
        var installedMods = library.Metadata();
        var currentCatalog = await catalog.LoadAsync(token);
        foreach (var missing in CatalogUpdates.MissingCatalogEntries(profileModel, installedMods, currentCatalog))
        {
            var removed = new CatalogMod(missing.Origin.ModId, missing.Mod.Name, missing.Mod.Author ?? "Unknown author", "This mod is no longer listed in its source catalog.",
                null, false, [], null, null, Catalog: missing.Origin.Catalog, CatalogPage: missing.Origin.SourcePage);
            conflicts.Add((new(missing.Origin.ModId, missing.Mod.Name, missing.Origin.Version ?? missing.Mod.Version, "removed from catalog", null,
                "The installed package was retained.", removed, missing.Mod), ["mod was removed from the catalog; installed package remains enabled"]));
        }
        List<string> changed = [];
        try
        {
            foreach (var update in updates)
            {
                token.ThrowIfCancellationRequested();
                progress?.Invoke($"Downloading {update.Name}…");
                string oldPatchPath = library.PatchPath(update.InstalledMod);
                ModPatch? before = File.Exists(oldPatchPath) ? PatchJson.Deserialize(File.ReadAllText(oldPatchPath)) : null;
                var resolution = await resolver.ResolveAsync(update.CatalogEntry, token);
                var file = resolution.Files.FirstOrDefault(f => f.Version == update.AvailableVersion) ?? (resolution.Files.Length == 1 ? resolution.Files[0] : null)
                    ?? throw new IOException($"Could not identify one download for {update.Name}. Open its catalog page and select a release manually.");
                var origin = new ModOrigin(update.CatalogEntry.Catalog, update.CatalogEntry.Id, update.CatalogEntry.SourcePage,
                    file.Provider, file.Id, file.ReleaseId, update.AvailableVersion) { PackageSha256 = update.CatalogEntry.Sha256 };
                string part = await new ModDownloadService(store, http).DownloadResolvedAsync(ct => resolver.GetFileUriAsync(file, ct), null, token);
                if (!PackageHashMatches(part, update.CatalogEntry.Sha256!))
                    throw new InvalidDataException($"SHA-256 verification failed for {update.Name}. The downloaded package was rejected; retry or contact the catalog maintainer.");
                string ext = Path.GetExtension(file.Name).Equals(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".win";
                string staged = store.Path("downloads", Guid.NewGuid().ToString("N") + ext);
                File.Move(part, staged);
                string previous = store.Path("mods", update.InstalledMod.Id + ".prev");
                if (Directory.Exists(previous)) Directory.Delete(previous, true);
                CopyDirectory(store.Path("mods", update.InstalledMod.Id), previous);
                string previousPatches = Path.Combine(previous, "patches"); Directory.CreateDirectory(previousPatches);
                foreach (string patchPath in new[] { library.PatchPath(update.InstalledMod), library.SanitizedPatchPath(update.InstalledMod), library.SanitizerReportPath(update.InstalledMod) })
                    if (File.Exists(patchPath)) File.Copy(patchPath, Path.Combine(previousPatches, Path.GetFileName(patchPath)), true);
                changed.Add(update.InstalledMod.Id);
                try
                {
                    var added = library.Add(staged, update.Name, progress, token, origin, update.InstalledMod.Id, update.CatalogEntry.Description);
                    // Archives may not carry version metadata, so retain the authoritative catalog version.
                    var refreshed = added.Mod with { Version = update.AvailableVersion, Origins = [.. added.Mod.Origins.Where(o => o.Catalog != origin.Catalog || o.ModId != origin.ModId), origin] };
                    store.Write(store.Path("mods", refreshed.Id, "mod.json"), refreshed);
                    var after = library.RawPatch(refreshed, progress, token);
                    conflicts.Add((update, CatalogUpdates.ConflictReasons(update, before, after)));
                }
                finally { if (File.Exists(staged)) File.Delete(staged); }
            }
            var saveBackup = BackupSaves(profile, timestamp, config);
            string game = config.GameDir ?? throw new ArgumentException("Set gameDir before applying updates.");
            string gameBackup = Path.Combine(game, "data.win.bak-" + timestamp);
            var prepared = new ProfileInstaller(store).Prepare(profile, progress, token);
            store.Write(store.Path("updates", "rollback.json"), new RollbackState(profile, Path.GetFullPath(game), gameBackup, changed.ToArray(), saveBackup));
            return (prepared, conflicts, saveBackup);
        }
        catch
        {
            RestorePackages(changed);
            throw;
        }
    }

    /// <summary>Installs a previously verified updater build with the manager's guarded atomic installer.</summary>
    public void Install(PreparedProfile build)
    {
        var state = store.Read<RollbackState>(store.Path("updates", "rollback.json"));
        if (build.Profile.Name != state.Profile || !BuildFiles.SamePath(BuildFiles.Resolve(build.Config.GameDir!), state.GameDirectory))
            throw new IOException("The prepared update does not match the profile and game directory with updater backups. Re-run the update check.");
        string current = Path.Combine(state.GameDirectory, "data.win");
        BuildFiles.RefuseLink(current); BuildFiles.RefuseLink(state.GameBackup);
        File.Copy(current, state.GameBackup, false);
        string installed = store.Path("installed.json");
        if (File.Exists(installed)) File.Copy(installed, store.Path("updates", "installed.prev.json"), true);
        new ProfileInstaller(store).Install(build);
    }

    /// <summary>Restores the last updater's game archive and package directories.</summary>
    public string Rollback(string? profileName = null)
    {
        var state = store.Read<RollbackState>(store.Path("updates", "rollback.json"));
        if (profileName is not null && !string.Equals(profileName, state.Profile, StringComparison.Ordinal))
            throw new ArgumentException($"The last updater backup belongs to profile '{state.Profile}', not '{profileName}'. Select that profile or omit --profile.");
        if (!Directory.Exists(state.GameDirectory)) throw new DirectoryNotFoundException("The updater's selected game directory is missing; rollback stopped.");
        bool gameRestored = false;
        if (File.Exists(state.GameBackup))
        {
            string current = Path.Combine(state.GameDirectory, "data.win");
            string pending = current + ".new";
            BuildFiles.RefuseLink(current); BuildFiles.RefuseLink(state.GameBackup); BuildFiles.RefuseLink(pending);
            File.Copy(state.GameBackup, pending, true);
            File.Move(pending, current, true);
            gameRestored = true;
        }
        RestorePackages(state.ModIds);
        if (gameRestored)
        {
            // The previous archive is back in place: restore the matching install record (or drop the now-stale one).
            string installed = store.Path("installed.json"), previousInstalled = store.Path("updates", "installed.prev.json");
            if (File.Exists(previousInstalled)) File.Move(previousInstalled, installed, true);
            else File.Delete(installed);
        }
        File.Delete(store.Path("updates", "rollback.json"));
        return state.SaveBackup;
    }

    private void RestorePackages(IEnumerable<string> ids)
    {
        foreach (string id in ids)
        {
            string previous = store.Path("mods", id + ".prev"), current = store.Path("mods", id);
            if (!Directory.Exists(previous)) continue;
            string staging = current + ".rollback";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            CopyDirectory(previous, staging);
            if (Directory.Exists(current)) Directory.Delete(current, true);
            Directory.Move(staging, current);
            string patchBackup = Path.Combine(current, "patches");
            if (Directory.Exists(patchBackup))
                foreach (string file in Directory.EnumerateFiles(patchBackup)) File.Copy(file, store.Path("patches", Path.GetFileName(file)), true);
            Directory.Delete(previous, true);
        }
    }

    private string BackupSaves(string profile, string timestamp, ManagerConfig config)
    {
        string? local = SaveGamePaths.LocalAppData(config, store.Root);
        string source = local is null ? "" : Path.Combine(local, "Cat_Goes_Fishing");
        string destination = store.Path("save-backups", ManagerStore.Name(profile), timestamp);
        Directory.CreateDirectory(destination);
        if (source.Length == 0 || !Directory.Exists(source)) return "";
        CopyDirectory(source, Path.Combine(destination, "Cat_Goes_Fishing"));
        return destination;
    }

    private static void CopyDirectory(string source, string destination)
    {
        BuildFiles.RefuseLink(source);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source)) { BuildFiles.RefuseLink(file); File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true); }
        foreach (string dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
