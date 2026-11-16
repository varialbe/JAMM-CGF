using System.Text.RegularExpressions;

namespace Jamm.Core.Management;

/// <summary>A read-only inspected Steam game candidate.</summary>
public sealed record DetectedGame(string Archive, bool Usable, string Reason);

/// <summary>Finds Steam libraries and downloaded legacy depots on supported desktop layouts.</summary>
public static partial class SteamGameDetection
{
    /// <summary>Console command for Steam's legacy Windows depot.</summary>
    public const string LegacyCommand = "download_depot 343780 343782 9039707068725406600";

    /// <summary>Enumerates standard roots; explicit home/program paths make OS layouts testable.</summary>
    public static string[] Roots(string home, string? programFiles = null, string? programFilesX86 = null) => new[]
    {
        Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".steam", "root"),
        Path.Combine(home, ".local", "share", "Steam"),
        Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
        Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"),
        Path.Combine(home, "Library", "Application Support", "Steam"),
        Path.Combine(programFiles ?? "C:\\Program Files", "Steam"),
        Path.Combine(programFilesX86 ?? "C:\\Program Files (x86)", "Steam")
    }.Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Finds archives through libraries, common folders and depot downloads without recursive disk scanning.</summary>
    public static string[] Candidates(IEnumerable<string> roots)
    {
        HashSet<string> libraries = new(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            libraries.Add(root);
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            try
            {
                if (!File.Exists(vdf)) continue;
                foreach (Match match in LibraryPath().Matches(File.ReadAllText(vdf)))
                    libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return libraries.SelectMany(root => new[] {
            Path.Combine(root, "steamapps", "common", "Cat Goes Fishing", "data.win"),
            Path.Combine(root, "steamapps", "content", "app_343780", "depot_343782", "data.win")
        }).Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Classifies all found originals via the existing read-only inspector.</summary>
    public static DetectedGame[] Detect(CancellationToken token = default)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Candidates(Roots(home, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)))
            .Select(path => Inspect(path, token)).ToArray();
    }

    /// <summary>Classifies one candidate without writing to the game.</summary>
    public static DetectedGame Inspect(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var file = GameFileLoader.Load(path);
            bool usable = !file.Inspection.IsYyc && file.Inspection.BytecodeSupported;
            return new(path, usable, usable ? "Legacy game found" : "Current game found; download the legacy depot to use mods.");
        }
        catch (Exception ex) { return new(path, false, "Could not inspect " + Path.GetFileName(path) + ": " + ex.Message); }
    }

    /// <summary>Copies a legacy game into guarded manager storage, retaining an independent base.</summary>
    public static void Use(ManagerStore store, DetectedGame candidate, CancellationToken token)
    {
        if (!Inspect(candidate.Archive, token).Usable) throw new InvalidDataException("This game cannot be used as a base. Download the legacy depot first.");
        string sourceDirectory = Path.GetFullPath(Path.GetDirectoryName(candidate.Archive)!);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (store.Root.Equals(sourceDirectory, comparison) || store.Root.StartsWith(sourceDirectory + Path.DirectorySeparatorChar, comparison))
            throw new IOException("Choose a manager data folder outside the original game folder before creating a modded copy.");
        string destination = store.Path("game"), staging = store.Path(".game-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(destination)) throw new IOException("A separate game copy already exists. Choose it in Settings.");
        Directory.CreateDirectory(staging);
        try
        {
            CopyDirectory(sourceDirectory, staging, token);
            string basePath = store.Path("base", "data.win"); Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            if (!File.Exists(basePath)) File.Copy(candidate.Archive, basePath);
            if (Building.BuildFiles.Hash(basePath) != Building.BuildFiles.Hash(candidate.Archive)) throw new IOException("The saved base differs. Choose the correct base in Settings.");
            Directory.Move(staging, destination);
            store.SetMany(new Dictionary<string, string> { ["base"] = basePath, ["gameDir"] = destination });
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    private static void CopyDirectory(string source, string destination, CancellationToken token)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(source))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            string target = Path.Combine(destination, Path.GetFileName(path));
            if (Directory.Exists(path)) { Directory.CreateDirectory(target); CopyDirectory(path, target, token); }
            else File.Copy(path, target);
        }
    }
    [GeneratedRegex("\"(?:path|[0-9]+)\"\\s*\"([^\"]+)\"")]
    private static partial Regex LibraryPath();
}
