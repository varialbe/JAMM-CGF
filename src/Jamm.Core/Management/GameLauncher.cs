using System.Diagnostics;

namespace Jamm.Core.Management;

/// <summary>A process command with argument boundaries and explicit environment overrides.</summary>
public sealed record GameLaunchCommand(string FileName, string[] Arguments, string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>Compatibility directory to create for a Flatpak launch.</summary>
    public string? FlatpakCompatDataPath { get; init; }

    /// <summary>Creates a shell-free, nonblocking process start request.</summary>
    public ProcessStartInfo ToStartInfo()
    {
        var info = new ProcessStartInfo(FileName) { WorkingDirectory = WorkingDirectory, UseShellExecute = false };
        foreach (string argument in Arguments) info.ArgumentList.Add(argument);
        foreach (var (key, value) in Environment) info.Environment[key] = value;
        return info;
    }

    /// <summary>Formats a readable command for display only; execution never uses a shell.</summary>
    public string Display() => FlatpakCompatDataPath is not null ? $"flatpak enter Steam: {WorkingDirectory}" : string.Join(" ", Environment.Select(p => p.Key + "=" + Quote(p.Value))
        .Concat(new[] { FileName }.Concat(Arguments).Select(Quote)));

    private static string Quote(string text) => "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}

/// <summary>Constructs and validates legacy game launches without waiting for the runner to exit.</summary>
public static class GameLauncher
{
    /// <summary>Constructs runner arguments and environment; Flatpak mode queries Steam and sandbox visibility without writing files.</summary>
    public static GameLaunchCommand Create(ManagerConfig config, string dataDirectory, FlatpakLaunchHost? flatpakHost = null)
    {
        string game = Path.GetFullPath(config.GameDir ?? throw new ArgumentException("Set gameDir before playing."));
        string executable = Path.Combine(game, "Cat Goes Fishing.exe");
        Dictionary<string, string> environment = new(StringComparer.Ordinal) { ["SteamAppId"] = config.Steam.AppId, ["SteamGameId"] = config.Steam.AppId };
        if (config.Wine.Mode == "direct") return new(executable, [], game, environment);
        if (config.Wine.Mode == "wine")
        {
            if (config.Wine.Prefix is not null) environment["WINEPREFIX"] = config.Wine.Prefix;
            return new(config.Wine.Binary, [executable], game, environment);
        }
        if (config.Wine.Mode == "proton-flatpak")
            return FlatpakSteamLauncher.Create(config, game, flatpakHost ?? new FlatpakLaunchHost());
        if (config.Wine.Mode != "proton") throw new ArgumentException("wine.mode must be 'wine', 'proton' or 'proton-flatpak'.");
        if (config.Proton.Path is null)
            throw new ArgumentException("Proton was not found. Install Proton - Experimental through Steam's Library (Tools), then choose its folder in Settings before Play.");
        environment["STEAM_COMPAT_DATA_PATH"] = config.Proton.CompatDataPath ?? Path.Combine(Path.GetFullPath(dataDirectory), "proton", config.Steam.AppId);
        string? steam = config.Proton.ClientInstallPath;
        if (string.IsNullOrWhiteSpace(steam)) steam = System.Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH");
        if (string.IsNullOrWhiteSpace(steam))
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            steam = new[] { Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".local", "share", "Steam") }
                .FirstOrDefault(Directory.Exists);
        }
        if (string.IsNullOrWhiteSpace(steam))
            throw new ArgumentException("Cannot find the Steam installation. Set proton.clientInstallPath with config set or export STEAM_COMPAT_CLIENT_INSTALL_PATH before playing.");
        environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = Path.GetFullPath(steam);
        return new(Path.Combine(config.Proton.Path, "proton"), ["run", executable], game, environment);
    }

    /// <summary>Checks that the game and runner exist and the runner is executable, before any profile installation.</summary>
    public static void Validate(GameLaunchCommand command)
    {
        if (!File.Exists(Path.Combine(command.WorkingDirectory, "Cat Goes Fishing.exe"))) throw new FileNotFoundException($"Cannot launch: 'Cat Goes Fishing.exe' is missing in '{command.WorkingDirectory}'. Copy the whole legacy game folder and set gameDir to that folder.");
        string? runner = FindRunner(command.FileName);
        if (runner is null || !IsExecutable(runner))
            throw new FileNotFoundException($"Runner '{command.FileName}' is missing or not executable. Install Wine or Proton and set wine.binary or proton.path to its executable location.");
        if (command.Environment.TryGetValue("STEAM_COMPAT_CLIENT_INSTALL_PATH", out string? steam) && !Directory.Exists(steam))
            throw new DirectoryNotFoundException($"Steam directory '{steam}' is missing. Correct proton.clientInstallPath before playing.");
    }

    private static bool IsExecutable(string path) => File.Exists(path) && (OperatingSystem.IsWindows()
        || (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0);

    private static string? FindRunner(string fileName)
    {
        bool windows = OperatingSystem.IsWindows();
        IEnumerable<string> names = [fileName];
        if (windows && !Path.HasExtension(fileName))
            names = names.Concat((System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries).Select(extension => fileName + extension));
        if (Path.IsPathRooted(fileName) || fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            return names.Select(Path.GetFullPath).FirstOrDefault(IsExecutable);
        IEnumerable<string> directories = (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        if (windows) directories = directories.Prepend(System.Environment.CurrentDirectory);
        return directories.SelectMany(directory => names.Select(name => Path.GetFullPath(Path.Combine(directory, name))))
            .FirstOrDefault(IsExecutable);
    }

    /// <summary>Starts the runner and immediately releases its process handle; does not wait for the game.</summary>
    public static void Launch(GameLaunchCommand command)
    {
        Validate(command);
        if (command.FlatpakCompatDataPath is not null) Directory.CreateDirectory(command.FlatpakCompatDataPath);
        var resolved = command with { FileName = FindRunner(command.FileName)! };
        using var process = Process.Start(resolved.ToStartInfo()) ?? throw new IOException("The game runner could not be started. Check its executable and prefix settings.");
    }
}
