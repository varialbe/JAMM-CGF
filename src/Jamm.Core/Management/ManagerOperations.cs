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
    public ConflictReport Check(string profile, CancellationToken cancellationToken = default, Action<string>? progress = null)
    {
        var library = new ModLibrary(store);
        var selection = new ProfileLibrary(store).Get(profile);
        if (selection.EnabledMods.Length == 0) throw new ArgumentException("Enable at least one mod in this profile before checking or installing.");
        string basePath = store.Config().Base ?? throw new ArgumentException("Choose a legacy base data.win in Settings first.");
        string hash = BuildFiles.Hash(basePath);
        var patches = selection.EnabledMods.Select(id =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var patch = library.Patch(library.Find(id), progress, cancellationToken);
            if (patch.Base.Sha256 != hash) throw new InvalidDataException("This profile targets another base. Select its original base in Settings or re-add its mods.");
            return patch;
        }).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var report = ConflictAnalyzer.Analyze(patches, allowSingle: true, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return report;
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
