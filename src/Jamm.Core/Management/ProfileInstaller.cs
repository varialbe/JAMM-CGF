using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jamm.Core.Building;

namespace Jamm.Core.Management;

/// <summary>A verified profile build awaiting an installation decision.</summary>
public sealed record PreparedProfile(ModProfile Profile, ManagerConfig Config, string Signature,
    string OutputPath, string BaseSha256, BuildResult Build);

/// <summary>Recognizes the installed profile and delegates every game write to Phase 3's guarded installer.</summary>
public sealed class ProfileInstaller(ManagerStore store)
{
    private sealed record InstalledProfile(string Signature, string GameDirectory, string BuildSha256, string? Profile = null);

    private (ModProfile Profile, ManagerConfig Config, IReadOnlyList<BuildInput> Inputs, string Signature) Selection(string? name)
    {
        var config = store.Config();
        if (config.Base is null || config.GameDir is null)
            throw new ArgumentException("Set 'base' and 'gameDir' with cgfmm config set before applying a profile.");
        var profile = new ProfileLibrary(store).Get(name ?? config.ActiveProfile
            ?? throw new ArgumentException("No active profile. Use 'profile use <name>' or pass --profile <name>."));
        var library = new ModLibrary(store);
        var inputs = profile.EnabledMods.Select(id => library.Find(id)).Select(m => new BuildInput(library.Patch(m), library.Archive(m))).ToArray();
        string hash = BuildFiles.Hash(config.Base);
        if (inputs.Any(i => i.Patch.Base.Sha256 != hash))
            throw new InvalidDataException("This profile's patches target a different base. Restore its base configuration or remove and re-add its mods against the selected base.");
        string signature = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            // Invalidate installation records made before numeric-reference verification.
            Builder = "phase16", profile.Name, profile.Mods, profile.DisabledMods, Resolutions = profile.Resolutions.OrderBy(p => p.Key, StringComparer.Ordinal),
            Base = hash, Game = Path.GetFullPath(config.GameDir), Patches = inputs.Select(i => i.Patch)
        }, ManagerStore.JsonOptions))));
        return (profile, config, inputs, signature);
    }

    /// <summary>Checks installation identities without writing. Stale installation state returns false;
    /// invalid configuration, profiles or mod sources still report errors.</summary>
    public bool IsInstalled(string? name = null)
    {
        var selection = Selection(name);
        string path = store.Path("installed.json");
        if (!File.Exists(path)) return false;
        InstalledProfile installed;
        try { installed = store.Read<InstalledProfile>(path); }
        catch (InvalidDataException) { return false; }
        if (installed.Signature != selection.Signature || installed.GameDirectory != selection.Config.GameDir) return false;
        try
        {
            string current = Path.Combine(selection.Config.GameDir!, "data.win");
            if (!File.Exists(current) || BuildFiles.Hash(current) != installed.BuildSha256) return false;
            GameInstaller.Validate(selection.Config.GameDir!, BuildFiles.Hash(selection.Config.Base!),
                selection.Inputs.Select(i => i.ArchivePath), selection.Config.Base);
            return File.Exists(Path.Combine(selection.Config.GameDir!, "data.win.cgfmm-build.json"));
        }
        catch (IOException) { return false; }
    }

    /// <summary>Returns the recorded installed profile name when the game and retained backup are still recognized.</summary>
    public string? InstalledProfileName()
    {
        string path = store.Path("installed.json");
        if (!File.Exists(path)) return null;
        try
        {
            var installed = store.Read<InstalledProfile>(path);
            var config = store.Config();
            if (config.GameDir != installed.GameDirectory || config.Base is null) return null;
            GameInstaller.Validate(config.GameDir!, BuildFiles.Hash(config.Base), basePath: config.Base);
            return BuildFiles.Hash(Path.Combine(config.GameDir!, "data.win")) == installed.BuildSha256 ? installed.Profile : null;
        }
        catch (IOException) { return null; }
    }

    /// <summary>Builds a temporary verified output without altering the game. Call Install or Discard when finished.</summary>
    public PreparedProfile Prepare(string? name = null, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = Selection(name);
        string hash = BuildFiles.Hash(selection.Config.Base!);
        string output = store.Path("builds", $"{Guid.NewGuid():N}.win");
        GameInstaller.Validate(selection.Config.GameDir!, hash, selection.Inputs.Select(i => i.ArchivePath), selection.Config.Base);
        var result = ArchiveBuilder.Build(selection.Config.Base!, selection.Inputs, output, selection.Profile.Resolutions, progress: progress, cancellationToken: cancellationToken, options: AnalysisOptions.Default with { DataDirectory = store.Root });
        return new(selection.Profile, selection.Config, selection.Signature, output, hash, result);
    }

    /// <summary>Retains an additional permanent original in the data directory, then performs a Phase 3 install.</summary>
    public void Install(PreparedProfile prepared, bool retainIndependentBase = false)
    {
        var selection = Selection(prepared.Profile.Name);
        if (selection.Signature != prepared.Signature || selection.Config != prepared.Config)
            throw new IOException("The profile or configuration changed after building. Run apply again before installing.");
        if (prepared.Build.Verification?.Inspection.Sha256 != BuildFiles.Hash(prepared.OutputPath))
            throw new IOException("The verified profile build changed. Run apply again to rebuild it before installing.");
        string backup = store.Path("backups", prepared.BaseSha256 + ".win");
        if (!File.Exists(backup))
        {
            string temporary = store.Path("backups", $".cgfmm-backup-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var source = new FileStream(prepared.Config.Base!, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) source.CopyTo(target);
                if (BuildFiles.Hash(temporary) != prepared.BaseSha256) throw new IOException("Original backup verification failed. Retry with a stable base archive.");
                File.Move(temporary, backup, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (BuildFiles.Hash(backup) != prepared.BaseSha256) throw new IOException("The retained manager backup differs from the base. Choose another data directory; backups are never overwritten.");
        bool rebase = retainIndependentBase && BuildFiles.SamePath(BuildFiles.Resolve(prepared.Config.Base!),
            BuildFiles.Resolve(Path.Combine(prepared.Config.GameDir!, "data.win")));
        GameInstaller.Install(prepared.OutputPath, prepared.Config.GameDir!, prepared.BaseSha256,
            selection.Inputs.Select(i => i.ArchivePath), prepared.Config.Base, prepared.Build.Plan.Catalog);
        // First-run desktop setup may choose the game's own archive. Once installed,
        // keep subsequent checks bound to the verified ancestor rather than the replacement.
        if (rebase)
            store.Set("base", backup);
        store.Write(store.Path("installed.json"), new InstalledProfile(prepared.Signature, prepared.Config.GameDir!, BuildFiles.Hash(prepared.OutputPath), prepared.Profile.Name));
        new ProfileLibrary(store).Use(prepared.Profile.Name);
        Discard(prepared);
    }

    /// <summary>Deletes only the selected temporary build in this store; original backups are retained.</summary>
    public void Discard(PreparedProfile prepared)
    {
        string file = Path.GetFileName(prepared.OutputPath);
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)
            || Path.GetExtension(file) != ".win" || !BuildFiles.SamePath(prepared.OutputPath, store.Path("builds", file)))
            throw new IOException("Refusing to remove a build outside this manager's temporary builds directory.");
        File.Delete(store.Path("builds", file));
    }

    /// <summary>Restores the configured game's retained original and invalidates the selected installation record.</summary>
    public string Restore()
    {
        string game = store.Config().GameDir ?? throw new ArgumentException("Set gameDir first: cgfmm config set gameDir <legacy game folder>.");
        string hash = GameInstaller.Restore(game);
        File.Delete(store.Path("installed.json"));
        return hash;
    }
}
