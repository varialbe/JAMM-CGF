using System.Text.RegularExpressions;

namespace Jamm.Core.Management;

/// <summary>Evidence-based suitability for a merge base.</summary>
public enum GameSuitability
{
    /// <summary>Matches a known original hash and size.</summary>
    SupportedOriginal,
    /// <summary>Compatible legacy header, with unverified contents.</summary>
    UnverifiedLegacy,
    /// <summary>Cannot serve as a merge base.</summary>
    NotUsable
}

/// <summary>A read-only inspected game candidate.</summary>
public sealed record DetectedGame(string Archive, bool Usable, string Reason)
{
    /// <summary>Classification; unverified legacy games require explicit selection.</summary>
    public GameSuitability Suitability { get; init; } = Usable ? GameSuitability.UnverifiedLegacy : GameSuitability.NotUsable;
    /// <summary>Accessible classification label.</summary>
    public string Display => (Suitability == GameSuitability.SupportedOriginal ? "✓ " : Usable ? "? " : "✗ ") + Archive;
    /// <summary>Classification color for the setup results.</summary>
    public string Color => Suitability == GameSuitability.SupportedOriginal ? "#16803C" : Usable ? "#A66B00" : "#DC2626";
}

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
    public static string[] Candidates(IEnumerable<string> roots) => Libraries(roots).SelectMany(root => new[] {
        Path.Combine(root, "steamapps", "common", "Cat Goes Fishing", "data.win"),
        Path.Combine(root, "steamapps", "content", "app_343780", "depot_343782", "data.win")
    }).Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Steam roots plus VDF registered library roots.</summary>
    public static string[] Libraries(IEnumerable<string> roots)
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
        return libraries.ToArray();
    }

    /// <summary>Classifies all found originals via the existing read-only inspector.</summary>
    public static DetectedGame[] Detect(CancellationToken token = default, IProgress<string>? progress = null)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Candidates(Roots(home, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)))
            .Select(path => Inspect(path, token, progress: progress)).OrderBy(g => g.Suitability).ToArray();
    }

    /// <summary>Classifies one candidate using bounded header reads before hashing plausible originals.
    /// An injected hash/size list allows synthetic fixtures without game data.</summary>
    public static DetectedGame Inspect(string path, CancellationToken token = default,
        IReadOnlyDictionary<string, long>? supportedOriginals = null, IProgress<string>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        supportedOriginals ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["dbb2e5c4b788358d0c6465d3005b2e9d46c4c540e423b064d7a5a3629286d2c1"] = 44572166
        };
        try
        {
            bool marked = Path.GetFileName(path).Equals("data.win", StringComparison.OrdinalIgnoreCase)
                && (File.Exists(path + ".cgfmm-original") || File.Exists(path + ".cgfmm-build.json"));
            if (marked)
            {
                string backup = path + ".cgfmm-original";
                if (File.Exists(backup))
                {
                    var original = Inspect(backup, token, supportedOriginals, progress);
                    if (original.Suitability == GameSuitability.SupportedOriginal)
                        return original with { Reason = "Supported original backup from a modded copy made by JAMM (read-only)." };
                }
                return new(path, false, "Modded copy made by JAMM. Its backup is missing or is not a supported original; choose the legacy depot folder.");
            }
            progress?.Report("Checking game header: " + path);
            using var stream = new CancellableGameStream(path, token);
            using var reader = new BinaryReader(stream);
            if (new string(reader.ReadChars(4)) != "FORM") throw new InvalidDataException("Not a GameMaker FORM archive.");
            long end = 8L + reader.ReadUInt32();
            if (end > stream.Length) throw new InvalidDataException("The archive is truncated.");
            byte bytecode = 0; uint build = 0; ulong timestamp = 0; bool gen8 = false, code = false;
            while (stream.Position + 8 <= end)
            {
                string chunk = new(reader.ReadChars(4)); uint size = reader.ReadUInt32(); long start = stream.Position;
                if (start + size > end) throw new InvalidDataException("An archive chunk is truncated.");
                if (chunk == "GEN8")
                {
                    if (size < 100) throw new InvalidDataException("The GEN8 header is truncated.");
                    stream.Position = start + 1; bytecode = reader.ReadByte();
                    stream.Position = start + 56; build = reader.ReadUInt32();
                    stream.Position = start + 92; timestamp = reader.ReadUInt64(); gen8 = true;
                }
                if (chunk == "CODE" && size >= 4) code = reader.ReadUInt32() > 0;
                stream.Position = start + size;
            }
            if (!gen8) throw new InvalidDataException("The GEN8 header is missing.");
            if (!code) return new(path, false, "Current Steam build (YYC); download the legacy depot to use mods.");
            if (bytecode != 16 || build < 1773 || timestamp < 1692057600)
                return new(path, false, $"Unsupported version (GEN8 build {build}, timestamp {timestamp}). Download the 2023 legacy depot.");
            if (supportedOriginals.Values.Contains(stream.Length))
            {
                progress?.Report("Verifying original SHA-256: " + path);
                stream.Position = 0;
                string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
                if (supportedOriginals.TryGetValue(hash, out long size) && size == stream.Length)
                    return new(path, true, "Supported original — verified SHA-256.") { Suitability = GameSuitability.SupportedOriginal };
            }
            return new(path, true, timestamp < 1692207312 && stream.Length <= 44572166
                ? "Older legacy build (usable with differences); not the verified supported original."
                : "Modified or other build; usable with differences, but not the verified supported original.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { token.ThrowIfCancellationRequested(); return new(path, false, $"Could not inspect '{path}': {ex.Message} Choose a complete legacy game folder."); }
    }

    /// <summary>Copies a legacy game into guarded manager storage, retaining an independent base.</summary>
    public static void Use(ManagerStore store, DetectedGame candidate, CancellationToken token, string? destinationPath = null)
    {
        var verified = Inspect(candidate.Archive, token);
        if (verified.Archive != candidate.Archive || !verified.Usable) throw new InvalidDataException("This game cannot be used as a base. Download the legacy depot first.");
        string sourceDirectory = Building.BuildFiles.Resolve(Path.GetDirectoryName(candidate.Archive)!);
        if (!File.Exists(Path.Combine(sourceDirectory, "Cat Goes Fishing.exe")))
            throw new IOException($"Cannot copy '{sourceDirectory}': Cat Goes Fishing.exe is missing. Choose the complete legacy game folder.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Building.BuildFiles.Resolve(store.Root).Equals(sourceDirectory, comparison) || Building.BuildFiles.Resolve(store.Root).StartsWith(sourceDirectory + Path.DirectorySeparatorChar, comparison))
            throw new IOException("Choose a manager data folder outside the original game folder before creating a modded copy.");
        string destination = Building.BuildFiles.Resolve(destinationPath ?? store.Path("game"));
        if (destination.Equals(sourceDirectory, comparison) || destination.StartsWith(sourceDirectory + Path.DirectorySeparatorChar, comparison))
            throw new IOException("Choose a modded folder outside the original game folder.");
        string staging = destination + ".creating-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(destination)) throw new IOException("A separate game copy already exists at '" + destination + "'. Choose another destination in Advanced mode, or select the existing copy in Settings.");
        Directory.CreateDirectory(staging);
        try
        {
            CopyDirectory(sourceDirectory, staging, token);
            if (Path.GetFileName(candidate.Archive) != "data.win") File.Copy(candidate.Archive, Path.Combine(staging, "data.win"), true);
            string basePath = store.Path("base", Building.BuildFiles.Hash(candidate.Archive) + ".win"); Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            if (!File.Exists(basePath)) File.Copy(candidate.Archive, basePath);
            if (Building.BuildFiles.Hash(basePath) != Building.BuildFiles.Hash(candidate.Archive)) throw new IOException("The saved base differs. Choose the correct base in Settings.");
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var settings = GameFolderSearch.RunnerSettings(home, OperatingSystem.IsWindows(), Roots(home, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)));
            settings["base"] = basePath; settings["gameDir"] = destination;
            store.SetMany(settings);
            var profiles = new ProfileLibrary(store);
            if (!profiles.List().Any(p => p.Name == "default")) profiles.CreateEmpty("default");
            profiles.Use("default");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    private static void CopyDirectory(string source, string destination, CancellationToken token)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(source))
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetFileName(path) is "data.win.cgfmm-original" or "data.win.cgfmm-build.json") continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            string target = Path.Combine(destination, Path.GetFileName(path));
            if (Directory.Exists(path)) { Directory.CreateDirectory(target); CopyDirectory(path, target, token); }
            else File.Copy(path, target);
        }
    }
    [GeneratedRegex("\"(?:path|[0-9]+)\"\\s*\"([^\"]+)\"")]
    private static partial Regex LibraryPath();
}
