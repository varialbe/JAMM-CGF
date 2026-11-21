using System.Text;

namespace Jamm.Core.Management;

internal static class PerfIni
{
    internal static void Seed(ManagerConfig config, string storeRoot)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Desktop installations on Windows can be launched directly even when the
        // manager still has the default Wine settings for a new store.
        string? localAppData = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : config.Wine.Mode switch
        {
            "wine" => LocalAppData(config.Wine.Prefix ?? Environment.GetEnvironmentVariable("WINEPREFIX")
                ?? Path.Combine(home, ".wine"), Environment.UserName),
            "proton" => ProtonLocalAppData(config.Proton.CompatDataPath
                ?? Path.Combine(storeRoot, "proton", config.Steam.AppId)),
            "proton-flatpak" => ProtonLocalAppData(FlatpakSteamLauncher.CompatDataPath(config, home)),
            _ => null
        };
        if (!string.IsNullOrEmpty(localAppData))
            SeedDirectory(Path.Combine(localAppData, "Cat_Goes_Fishing"));
    }

    private static string? ProtonLocalAppData(string compatPath) => LocalAppData(Path.Combine(compatPath, "pfx"), "steamuser");

    private static string? LocalAppData(string prefix, string username)
    {
        string path = Path.Combine(prefix, "drive_c", "users", username, "AppData", "Local");
        return Directory.Exists(path) ? path : null;
    }

    internal static void SeedDirectory(string directory)
    {
        string path = Path.Combine(directory, "jamm_perf.ini");
        string pending = Path.Combine(directory, $".jamm-perf-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            if (!File.Exists(path))
            {
                File.WriteAllText(pending, "[Perf]\r\nMode=1\r\n");
                File.Move(pending, path, overwrite: false);
                return;
            }
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return;
            using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (existing.Length > 65536) return;
            byte[] contents = new byte[existing.Length];
            existing.ReadExactly(contents);
            byte[]? seeded = WithDefaultMode(contents);
            if (seeded is null) return;
            File.WriteAllBytes(pending, seeded);
            if (contents.AsSpan().SequenceEqual(File.ReadAllBytes(path)))
                File.Move(pending, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A runner may initialize its save directory or create the INI concurrently.
        }
        finally
        {
            try { File.Delete(pending); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static byte[]? WithDefaultMode(byte[] bytes)
    {
        ReadOnlySpan<byte> input = bytes;
        Encoding encoding = input.StartsWith(Encoding.Unicode.Preamble) ? Encoding.Unicode
            : input.StartsWith(Encoding.BigEndianUnicode.Preamble) ? Encoding.BigEndianUnicode
            : input.StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8 : Encoding.Latin1;
        string text = encoding.GetString(bytes);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) || !text.Contains('\n') ? "\r\n" : "\n";
        var lines = text.Split('\n').ToList();
        int firstPerf = -1;
        bool inPerf = false;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim().TrimStart('\uFEFF');
            if (line.StartsWith('[') && line.IndexOf(']') is int end && end > 0)
            {
                inPerf = line[1..end].Trim().Equals("Perf", StringComparison.OrdinalIgnoreCase);
                if (inPerf && firstPerf < 0) firstPerf = i;
            }
            else if (inPerf && line.IndexOf('=') is int separator && separator >= 0
                && line[..separator].Trim().Equals("Mode", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        if (firstPerf < 0)
        {
            string separator = text.Length == 0 || text.EndsWith('\n') ? "" : newline;
            return encoding.GetBytes(text + separator + "[Perf]" + newline + "Mode=1" + newline);
        }

        if (firstPerf == lines.Count - 1 && !lines[firstPerf].EndsWith('\r') && newline == "\r\n")
            lines[firstPerf] += '\r';
        lines.Insert(firstPerf + 1, newline == "\r\n" ? "Mode=1\r" : "Mode=1");
        if (firstPerf + 2 == lines.Count) lines.Add("");
        return encoding.GetBytes(string.Join('\n', lines));
    }
}
