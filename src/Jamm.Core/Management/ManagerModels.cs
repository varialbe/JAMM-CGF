namespace Jamm.Core.Management;

/// <summary>Persistent paths, selected profile and Wine/Proton settings.</summary>
public sealed record ManagerConfig
{
    /// <summary>Allows anonymous read-only shared compatibility lookups.</summary>
    public bool UseSharedCompatibilityResults { get; init; } = true;
    /// <summary>Checks official GitHub releases on startup and asks before downloading an installer.</summary>
    public bool CheckForUpdates { get; init; }
    /// <summary>Shows expert controls when explicitly enabled.</summary>
    public bool AdvancedMode { get; init; }
    /// <summary>Includes previews and unsupported download sources in Browse.</summary>
    public bool ShowModsWithoutDownload { get; init; }
    /// <summary>Includes Litterbox entries in Browse.</summary>
    public bool ShowLitterboxCatalog { get; init; } = true;
    /// <summary>Includes Varialbe first-party entries in Browse.</summary>
    public bool ShowFirstPartyCatalog { get; init; } = true;
    /// <summary>Includes official Game Jolt entries in Browse.</summary>
    public bool ShowGameJoltCatalog { get; init; } = true;
    /// <summary>Game copy whose concrete replacement and backup paths were approved.</summary>
    public string? ConfirmedInstallDirectory { get; init; }
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
    /// <summary>Either direct (Windows), wine, proton or proton-flatpak.</summary>
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
    /// <summary>STEAM_COMPAT_DATA_PATH; defaults inside manager storage, or Flatpak Steam storage in proton-flatpak mode.</summary>
    public string? CompatDataPath { get; init; }
    /// <summary>Steam installation; defaults to the Flatpak data directory in proton-flatpak mode, otherwise the environment or standard Steam directories.</summary>
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
    DateTimeOffset AddedUtc, string BaseSha256, ulong Gen8Timestamp, string GmVersion, byte Bytecode)
{
    /// <summary>Player-facing description and controls, when supplied by a bundled generator.</summary>
    public string? Description { get; init; }
    /// <summary>Exact-byte online provenance; absent in older/manual imports. Hashes identify bytes, not trusted authorship.</summary>
    public ModOrigin[] Origins { get; init; } = [];
    /// <summary>Expert per-mod opt-out that uses the original unsanitized performance changes instead of JAMM's sanitized patch.</summary>
    public bool KeepAuthorPerformanceChanges { get; init; }
    /// <summary>Expert per-mod opt-out that keeps the author's HUD/UI asset replacements instead of reverting to base HUD art.</summary>
    public bool KeepAuthorHudDesign { get; init; }
    /// <summary>Expert per-mod opt-out that keeps the author's gameplay/sonar changes instead of enforcing vanilla progression.</summary>
    public bool KeepAuthorGameplayChanges { get; init; }
    /// <summary>Source folder path on disk for ModKit folder mods.</summary>
    public string? SourceFolder { get; init; }
    /// <summary>Mod author when supplied by a ModKit package manifest.</summary>
    public string? Author { get; init; }
    /// <summary>Mod package version when supplied by a ModKit package manifest.</summary>
    public string? Version { get; init; }
    /// <summary>Whether this mod was built or imported from a ModKit package.</summary>
    public bool IsModKit { get; init; }
    /// <summary>Stable identity declared by the package, independent of the library archive ID.</summary>
    public string? PackageId { get; init; }
    /// <summary>Author-declared profile role, if supplied.</summary>
    public ModRole? Role { get; init; }
    /// <summary>Whether the vanilla code diff suggests this mod could be an overhaul.</summary>
    public bool SuggestBase { get; init; }
}

/// <summary>Public source identity recorded only after successful local import/hash deduplication.</summary>
public sealed record ModOrigin(string Catalog, string ModId, string SourcePage, string Provider, string FileId, string? ReleaseId, string? Version);

/// <summary>Ordered library IDs and reusable Phase 3 resolution choices.</summary>
public sealed record ModProfile(string Name, string[] Mods, Dictionary<string, string> Resolutions)
{
    /// <summary>Unrecognized profile fields retained across saves for forward compatibility.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; init; }
    /// <summary>Disabled library IDs retained in the profile order; absent in older profiles.</summary>
    public string[] DisabledMods { get; init; } = [];
    /// <summary>Skips atlas repacking when building this profile's enabled mods.</summary>
    public bool NoRepack { get; init; }
    /// <summary>Per-profile roles override package defaults, including an explicit vanilla role.</summary>
    public Dictionary<string, ModRole> Roles { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Active library IDs in load order.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string[] EnabledMods => Mods.Except(DisabledMods, StringComparer.Ordinal).ToArray();
}

/// <summary>Library entry with compatibility and cache validity for the currently configured base.</summary>
public sealed record ModListEntry(ManagedMod Mod, bool BaseHashMatches, bool TimestampMatches,
    bool VersionMatches, bool PatchCached, string Compatibility);

/// <summary>Completed import and notices about source compatibility.</summary>
public sealed record AddModResult(ManagedMod Mod, IReadOnlyList<string> Warnings, bool AlreadyAdded);

/// <summary>A library mod as configured in a profile, with its resolved addon ancestor where available.</summary>
public sealed record ProfileMod(ManagedMod Mod, ModRole Role, bool Enabled, ManagedMod? Ancestor = null)
{
    /// <summary>Role label using the base mod's display name when available.</summary>
    public string RoleLabel => Role.Role == "addon" && Ancestor is not null ? $"Addon of {Ancestor.Name}" : Role.Label;
}
