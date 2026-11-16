namespace Jamm.Core.Management;

/// <summary>Persistent paths, selected profile and Wine/Proton settings.</summary>
public sealed record ManagerConfig
{
    /// <summary>Read-only merge ancestor, preferably outside the installed game.</summary>
    public string? Base { get; init; }
    /// <summary>Legacy game directory containing the executable and installed archive.</summary>
    public string? GameDir { get; init; }
    /// <summary>Selected profile name.</summary>
    public string? ActiveProfile { get; init; }
    /// <summary>Runner selection and Wine settings.</summary>
    public WineSettings Wine { get; init; } = new();
    /// <summary>Proton installation and environment paths.</summary>
    public ProtonSettings Proton { get; init; } = new();
    /// <summary>Steam application identity.</summary>
    public SteamSettings Steam { get; init; } = new();
}

/// <summary>Wine runner settings.</summary>
public sealed record WineSettings
{
    /// <summary>Either wine or proton.</summary>
    public string Mode { get; init; } = "wine";
    /// <summary>Optional absolute Wine prefix.</summary>
    public string? Prefix { get; init; }
    /// <summary>Executable name on PATH or an absolute executable path.</summary>
    public string Binary { get; init; } = "wine";
}

/// <summary>Proton executable directory and required compatibility environment.</summary>
public sealed record ProtonSettings
{
    /// <summary>Directory containing the proton script.</summary>
    public string? Path { get; init; }
    /// <summary>STEAM_COMPAT_DATA_PATH; defaults inside the manager data directory.</summary>
    public string? CompatDataPath { get; init; }
    /// <summary>Steam installation; defaults to STEAM_COMPAT_CLIENT_INSTALL_PATH or a standard Steam directory.</summary>
    public string? ClientInstallPath { get; init; }
}

/// <summary>Steam launch identity.</summary>
public sealed record SteamSettings
{
    /// <summary>Cat Goes Fishing's Steam application ID.</summary>
    public string AppId { get; init; } = "343780";
}

/// <summary>Installed library metadata, including the exact patch ancestor and source build.</summary>
public sealed record ManagedMod(string Id, string Name, string SourceFileName, string Sha256,
    DateTimeOffset AddedUtc, string BaseSha256, ulong Gen8Timestamp, string GmVersion, byte Bytecode);

/// <summary>Ordered library IDs and reusable Phase 3 resolution choices.</summary>
public sealed record ModProfile(string Name, string[] Mods, Dictionary<string, string> Resolutions);

/// <summary>Library entry with compatibility and cache validity for the currently configured base.</summary>
public sealed record ModListEntry(ManagedMod Mod, bool BaseHashMatches, bool TimestampMatches,
    bool VersionMatches, bool PatchCached, string Compatibility);

/// <summary>Completed import and notices about source compatibility.</summary>
public sealed record AddModResult(ManagedMod Mod, IReadOnlyList<string> Warnings, bool AlreadyAdded);
