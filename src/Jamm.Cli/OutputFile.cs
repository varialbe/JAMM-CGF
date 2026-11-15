namespace Jamm.Cli;

internal static class OutputFile
{
    internal static void Validate(string path, IEnumerable<string> inputs)
    {
        string destination = Path.GetFullPath(path);
        string resolved = Resolve(destination);
        if (string.Equals(Path.GetExtension(destination), ".win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(resolved), ".win", StringComparison.OrdinalIgnoreCase)
            || inputs.Any(x => string.Equals(Resolve(Path.GetFullPath(x)), resolved,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new ArgumentException($"Refusing to write '{path}': reports must use a separate path from game inputs and cannot be .win files.");
    }

    // Resolve parent-directory links too, so an aliased input path cannot become a report target.
    private static string Resolve(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is not null && parent != path) path = Path.Combine(Resolve(parent), Path.GetFileName(path));
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget is null ? path : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
    }

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
