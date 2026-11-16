using System.Diagnostics;

namespace Jamm.Core.Management;

/// <summary>Bounded, read-only discovery of moved legacy game folders.</summary>
public static class GameFolderSearch
{
    /// <summary>Common user and mounted drive roots, without requiring Steam library registration.</summary>
    public static string[] Roots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
            return DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).Select(d => d.RootDirectory.FullName)
                .Concat(new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), home }).Where(Directory.Exists).ToArray();
        var roots = new List<string> { home };
        foreach (string mount in new[] { "/run/media/" + Environment.UserName, "/media/" + Environment.UserName, "/mnt", "/Volumes" })
            try { if (Directory.Exists(mount)) roots.AddRange(Directory.GetDirectories(mount)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return roots.ToArray();
    }

    /// <summary>Searches breadth first, skipping links and system/cache trees; limits include classification time.</summary>
    public static DetectedGame[] Scan(IEnumerable<string> roots, CancellationToken token = default,
        IProgress<string>? progress = null, int maxDepth = 8, TimeSpan? limit = null, int maxDirectories = 30000)
    {
        token.ThrowIfCancellationRequested();
        var budget = limit ?? TimeSpan.FromSeconds(25);
        if (budget <= TimeSpan.Zero) return [];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(budget);
        var clock = Stopwatch.StartNew();
        var queue = new Queue<(string Path, int Depth)>(roots.Where(Directory.Exists).Select(p => (Path.GetFullPath(p), 0)));
        HashSet<string> seen = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        List<DetectedGame> found = [];
        int count = 0;
        while (queue.TryDequeue(out var entry))
        {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed >= budget || count++ >= maxDirectories) break;
            try
            {
                if (!seen.Add(entry.Path) || (File.GetAttributes(entry.Path) & FileAttributes.ReparsePoint) != 0) continue;
                if (count % 100 == 1) progress?.Report("Searching: " + entry.Path);
                string archive = Path.Combine(entry.Path, "data.win");
                if (File.Exists(archive) && File.Exists(Path.Combine(entry.Path, "Cat Goes Fishing.exe")))
                {
                    try { found.Add(SteamGameDetection.Inspect(archive, deadline.Token, progress: progress)); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { break; }
                    continue; // Do not search an existing modded copy nested in a game.
                }
                if (entry.Depth >= maxDepth) continue;
                foreach (string child in Directory.EnumerateDirectories(entry.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (clock.Elapsed >= budget || count + queue.Count >= maxDirectories) break;
                    string name = Path.GetFileName(child);
                    if (name.StartsWith('.') || new[] { "Windows", "System Volume Information", "$RECYCLE.BIN", "proc", "sys", "dev", "node_modules", "bin", "obj", "Cache", "cache" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    queue.Enqueue((child, entry.Depth + 1));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        progress?.Report("Search finished (bounded to 25 seconds and 30,000 folders). Choose folder if your game is not listed.");
        return SteamGameDetection.OrderCandidates(found.DistinctBy(g => g.Archive)).ToArray();
    }

    /// <summary>Chooses an installed Proton and Steam runner, preferring Flatpak on Linux.</summary>
    public static Dictionary<string, string> RunnerSettings(string home, bool windows, IEnumerable<string> steamRoots)
    {
        if (windows) return new() { ["wine.mode"] = "direct" };
        var roots = steamRoots.Where(Directory.Exists).OrderByDescending(p => p.Contains("com.valvesoftware.Steam", StringComparison.Ordinal)).ToArray();
        foreach (string root in roots)
        {
            var proton = new List<string>();
            foreach (string parent in SteamGameDetection.Libraries([root]).Select(p => Path.Combine(p, "steamapps", "common")).Concat(new[] { Path.Combine(root, "compatibilitytools.d"), Path.Combine(home, ".steam", "compatibilitytools.d") }))
                try { if (Directory.Exists(parent)) proton.AddRange(Directory.GetDirectories(parent).Where(p => File.Exists(Path.Combine(p, "proton")))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            string? chosen = proton.OrderByDescending(p => p.Contains("Experimental", StringComparison.OrdinalIgnoreCase)).ThenByDescending(p => p, StringComparer.Ordinal).FirstOrDefault();
            var settings = new Dictionary<string, string> { ["wine.mode"] = root.Contains("com.valvesoftware.Steam", StringComparison.Ordinal) ? "proton-flatpak" : "proton", ["proton.clientInstallPath"] = root };
            if (chosen is not null) settings["proton.path"] = chosen;
            return settings;
        }
        return new() { ["wine.mode"] = "proton", ["proton.clientInstallPath"] = roots.FirstOrDefault() ?? Path.Combine(home, ".local", "share", "Steam") };
    }
}
