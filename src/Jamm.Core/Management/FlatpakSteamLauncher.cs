using System.ComponentModel;
using System.Diagnostics;

namespace Jamm.Core.Management;

/// <summary>Operating-system access for Flatpak discovery; overridable for tests without Steam.</summary>
public class FlatpakLaunchHost
{
    /// <summary>The current user's home directory.</summary>
    public virtual string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Checks a host directory when finding the visible parent of a new compatibility directory.</summary>
    public virtual bool DirectoryExists(string path) => Directory.Exists(path);

    /// <summary>Reads Linux process metadata, including NUL-separated environment entries.</summary>
    public virtual string ReadProc(string path) => File.ReadAllText(path);

    /// <summary>Runs a shell-free Flatpak query and returns its exit code and output.</summary>
    public virtual (int ExitCode, string Output) Run(params string[] arguments)
    {
        var info = new ProcessStartInfo("flatpak") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info) ?? throw new IOException("Cannot start flatpak. Install Flatpak and start Steam before playing.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                process.Kill(entireProcessTree: true);
                throw new IOException("Flatpak did not respond. Restart Steam, then press Play.");
            }
            Task.WhenAll(output, error).GetAwaiter().GetResult();
            return (process.ExitCode, output.Result);
        }
        catch (Win32Exception ex)
        {
            throw new IOException("Cannot run flatpak. Install Flatpak and ensure it is on PATH, then start Steam before playing.", ex);
        }
    }
}

internal static class FlatpakSteamLauncher
{
    internal static GameLaunchCommand Create(ManagerConfig config, string game, FlatpakLaunchHost host)
    {
        var listing = host.Run("ps", "--columns=pid,application");
        int pid = listing.Output.Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1] == "com.valvesoftware.Steam")
            .Select(parts => int.TryParse(parts[0], out int value) ? value : 0).Where(value => value > 0)
            .Order().FirstOrDefault();
        if (listing.ExitCode != 0 || pid == 0)
            throw new IOException("Flatpak Steam is not running. Start Steam first, then press Play.");

        Dictionary<string, string> environment;
        try
        {
            int bwrap = Children(pid).First(child => Path.GetFileName(host.ReadProc($"/proc/{child}/comm").Trim()) == "bwrap");
            int steam = Children(bwrap).First();
            environment = new(StringComparer.Ordinal);
            foreach (string entry in host.ReadProc($"/proc/{steam}/environ").Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.IndexOf('=');
                if (separator > 0) environment[entry[..separator]] = entry[(separator + 1)..];
            }
            if (!environment.TryGetValue("HOME", out string? home) || string.IsNullOrWhiteSpace(home)) throw new IOException("Steam environment has no HOME.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            throw new IOException($"Cannot read the running Steam environment from /proc/{pid}. Restart Flatpak Steam, then press Play.", ex);
        }

        string steamPath = config.Proton.ClientInstallPath ?? Path.Combine(host.Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        string proton = Path.Combine(config.Proton.Path ?? Path.Combine(steamPath, "steamapps", "common", "Proton - Experimental"), "proton");
        string compat = ManagerStore.NormalizeCompatDataPath(config.Proton.CompatDataPath)
            ?? Path.Combine(steamPath, "steamapps", "compatdata", config.Steam.AppId);
        string instance = pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!Visible("-d", game))
            throw new IOException($"Game folder '{game}' is not visible inside Flatpak Steam. Move the whole legacy game folder under '{steamPath}' or grant Steam access to that folder, then set gameDir.");
        if (!Visible("-f", proton))
            throw new IOException($"Proton script '{proton}' is missing or not visible inside Flatpak Steam. Install Proton - Experimental in Steam or set proton.path to a visible Proton folder.");
        if (!Visible("-d", steamPath))
            throw new IOException($"Steam directory '{steamPath}' is not visible inside Flatpak Steam. Correct proton.clientInstallPath.");
        string? parent = compat;
        while (parent is not null && !host.DirectoryExists(parent)) parent = Path.GetDirectoryName(parent);
        if (parent is null || !Visible("-d", parent))
            throw new IOException($"Compatibility directory '{compat}' is not visible inside Flatpak Steam. Clear proton.compatDataPath to use the default inside '{steamPath}'.");

        environment["STEAM_COMPAT_DATA_PATH"] = compat;
        environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamPath;
        environment["SteamAppId"] = config.Steam.AppId;
        environment["SteamGameId"] = config.Steam.AppId;
        string[] arguments = ["enter", instance, "env", "-i", .. environment.Select(pair => pair.Key + "=" + pair.Value),
            "python3", proton, "run", Path.Combine(game, "Cat Goes Fishing.exe")];
        return new("flatpak", arguments, game, new Dictionary<string, string>()) { FlatpakCompatDataPath = compat };

        IEnumerable<int> Children(int process) => host.ReadProc($"/proc/{process}/task/{process}/children")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);
        bool Visible(string kind, string path) => host.Run("enter", instance, "test", kind, path).ExitCode == 0;
    }
}
