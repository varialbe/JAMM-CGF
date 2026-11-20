using Jamm.Core.Building;
using Jamm.Core.Merging;

namespace Jamm.Core.Management;

/// <summary>Read-only game status suitable for CLI and desktop presentation.</summary>
public sealed record ManagedGameStatus(bool Known, bool CanRestore, string Message);

/// <summary>Shared read-only checks used before presenting manager actions.</summary>
public sealed class ManagerOperations(ManagerStore store)
{
    /// <summary>Validates the supported legacy base without changing configuration or the archive.</summary>
    public static string ValidateBase(string path)
    {
        using var file = GameFileLoader.Load(path);
        var info = file.Inspection;
        if (info.IsYyc || !info.BytecodeSupported || info.BytecodeVersion != 16 || !info.DetectedGameMakerVersion.StartsWith("1.", StringComparison.Ordinal))
            throw new InvalidDataException($"'{Path.GetFileName(path)}' is not the supported legacy GameMaker 1.4 bytecode 16 build. Download the August 2023 legacy game and select its data.win. The current YYC Steam build cannot be merged.");
        return "Legacy bytecode build ✓";
    }

    /// <summary>Runs the portable conflict analyzer on the active profile's verified cached patches.</summary>
    public ConflictReport Check(string profile, CancellationToken cancellationToken = default, Action<string>? progress = null, bool strict = false)
    {
        var selection = new ProfileLibrary(store).Get(profile);
        if (selection.EnabledMods.Length == 0) throw new ArgumentException("Enable at least one mod in this profile before checking or installing.");
        var loaded = ProfileSelection.Load(store, selection, strict, progress, cancellationToken);
        var report = loaded.Analyze(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return report;
    }

    /// <summary>Builds an enabled profile to a new archive without installing it.</summary>
    public BuildResult Build(string profile, string output, bool dryRun = false, bool noRepack = false, bool strict = false,
        Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var saved = new ProfileLibrary(store).Get(profile);
        var selection = ProfileSelection.Load(store, saved, strict, progress, cancellationToken);
        return ProfileArchiveBuilder.Build(store, saved, selection, output, dryRun, noRepack || saved.NoRepack, progress, cancellationToken);
    }

    /// <summary>Recognizes the original or a guarded installed build; unknown game files cannot be replaced.</summary>
    public ManagedGameStatus Status()
    {
        var config = store.Config();
        if (config.Base is null || config.GameDir is null) return new(false, false, "Choose the legacy game folder and base in Settings.");
        try
        {
            string hash = BuildFiles.Hash(config.Base);
            GameInstaller.Validate(config.GameDir, hash, basePath: config.Base);
            bool original = BuildFiles.Hash(Path.Combine(config.GameDir, "data.win")) == hash;
            string label = "cgfmm build";
            if (new ProfileInstaller(store).InstalledProfileName() is { } installed) label += $" of profile {installed}";
            return new(true, File.Exists(Path.Combine(config.GameDir, "data.win.cgfmm-original")), original ? "Original game" : label);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException)
        {
            return new(false, false, "Unknown game data: " + ex.Message);
        }
    }
}
