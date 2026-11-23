namespace Jamm.Core.Management;

/// <summary>Resolves Cat Goes Fishing's Local AppData folder from the configured game runner.</summary>
public static class SaveGamePaths
{
    /// <summary>Returns the runner's local AppData path when it can be determined safely.</summary>
    public static string? LocalAppData(ManagerConfig config, string storeRoot)
    {
        if (OperatingSystem.IsWindows()) return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return config.Wine.Mode switch
        {
            "wine" => Path.Combine(config.Wine.Prefix ?? Environment.GetEnvironmentVariable("WINEPREFIX") ?? Path.Combine(home, ".wine"), "drive_c", "users", Environment.UserName, "AppData", "Local"),
            "proton" => Path.Combine(config.Proton.CompatDataPath ?? Path.Combine(storeRoot, "proton", config.Steam.AppId), "pfx", "drive_c", "users", "steamuser", "AppData", "Local"),
            "proton-flatpak" => Path.Combine(FlatpakSteamLauncher.CompatDataPath(config, home), "pfx", "drive_c", "users", "steamuser", "AppData", "Local"),
            _ => null
        };
    }
}
