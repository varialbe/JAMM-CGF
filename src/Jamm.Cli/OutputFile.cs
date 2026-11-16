namespace Jamm.Cli;

internal static class OutputFile
{
    internal static void Validate(string path, IEnumerable<string> inputs, bool allowNewWin = false)
    {
        string destination = Path.GetFullPath(path);
        string resolved = Resolve(destination);
        if ((!allowNewWin && (string.Equals(Path.GetExtension(destination), ".win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(resolved), ".win", StringComparison.OrdinalIgnoreCase)))
            || (allowNewWin && (File.Exists(destination) || Directory.Exists(destination) || new FileInfo(destination).LinkTarget is not null))
            || new[] { destination, resolved }.Any(p => p.EndsWith(".cgfmm-original", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".cgfmm-build.json", StringComparison.OrdinalIgnoreCase)
                || File.Exists(p + ".cgfmm-original") || File.Exists(p + ".cgfmm-build.json"))
            || inputs.Any(x => string.Equals(Resolve(Path.GetFullPath(x)), resolved,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new ArgumentException($"Refusing to write '{Path.GetFileName(path)}': use a separate path from game inputs, installed archives, original backups and installation markers; reports cannot be .win files and build outputs must be new files.");
    }

    private static string Resolve(string path) => Jamm.Core.Building.BuildFiles.Resolve(path);

    internal static void Write(string path, string contents, IEnumerable<string> inputs)
    {
        Validate(path, inputs);
        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".jamm-{Guid.NewGuid():N}.tmp");
        try
        {
            // Replacement never opens an existing destination or follows its hard/symbolic links.
            File.WriteAllText(temporary, contents);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
