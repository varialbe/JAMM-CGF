using System.Security.Cryptography;

namespace Jamm.Core.Building;

/// <summary>Read-only hashing and destination safeguards shared by build and installation.</summary>
public static class BuildFiles
{
    /// <summary>Hashes the entire archive without opening it for writing.</summary>
    public static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Resolves symbolic links in the file and all existing parent directories.</summary>
    public static string Resolve(string path) => Resolve(path, new HashSet<string>(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));

    private static string Resolve(string path, HashSet<string> visiting)
    {
        path = Path.GetFullPath(path);
        if (!visiting.Add(path) || visiting.Count > 256)
            throw new IOException($"Cannot resolve linked path '{Path.GetFileName(path)}': a symbolic-link loop or excessive nesting was found. Use an ordinary file path.");
        string original = path;
        try
        {
            string? parent = Path.GetDirectoryName(path);
            if (parent is not null && parent != path) path = Path.Combine(Resolve(parent, visiting), Path.GetFileName(path));
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.LinkTarget is null ? path : Resolve(info.ResolveLinkTarget(false)!.FullName, visiting);
        }
        finally { visiting.Remove(original); }
    }

    /// <summary>Allows replacing an earlier output, but refuses aliases of protected inputs and installation files.</summary>
    public static void ValidateNewOutput(string path, IEnumerable<string> inputs, bool dryRun = false)
    {
        string resolved = Resolve(path);
        if (inputs.Any(p => SamePath(Resolve(p), resolved))
            || resolved.EndsWith(".cgfmm-original", StringComparison.OrdinalIgnoreCase)
            || resolved.EndsWith(".cgfmm-build.json", StringComparison.OrdinalIgnoreCase)
            || File.Exists(resolved + ".cgfmm-original") || File.Exists(resolved + ".cgfmm-build.json"))
            throw new IOException($"Refusing output '{Path.GetFileName(path)}': choose a path separate from every base/mod/patch/resolution input, installed archive and original backup.");
        if (!dryRun && Directory.Exists(path))
            throw new IOException($"Refusing output '{Path.GetFileName(path)}': this is a directory. Choose an output filename.");
    }

    internal static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static void RefuseLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null)
            throw new IOException($"Refusing linked installation path '{Path.GetFileName(path)}'. Use a separate real game directory with ordinary files.");
    }
    internal static void RefuseStorageLink(string path)
    {
        RefuseLink(path);
        if (!SamePath(Resolve(path), path))
            throw new IOException($"Refusing linked managed-storage path '{Path.GetFileName(path)}'. Use an ordinary data directory.");
    }

}
