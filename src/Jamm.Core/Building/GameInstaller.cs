using System.Text.Json;

namespace Jamm.Core.Building;

/// <summary>Content identities that allow replacing only the base or a recognized previous build.</summary>
public sealed record BuildMarker(int FormatVersion, string BaseSha256, string BuildSha256, string BackupSha256,
    IReadOnlyList<CatalogMapping>? Catalog = null);

/// <summary>Installs and restores ordinary copies in a game directory, retaining the first verified backup.</summary>
public static class GameInstaller
{
    private const string Archive = "data.win";
    private const string Backup = "data.win.cgfmm-original";
    private const string Marker = "data.win.cgfmm-build.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>Checks install eligibility without writing, including protected input paths and any existing backup.</summary>
    public static void Validate(string gameDirectory, string baseHash, IEnumerable<string>? protectedInputs = null, string? basePath = null)
    {
        if (!Directory.Exists(gameDirectory)) throw new DirectoryNotFoundException($"Game directory '{Path.GetFileName(gameDirectory)}' does not exist. Choose a separate legacy game folder containing data.win.");
        string current = Path.Combine(gameDirectory, Archive), backup = Path.Combine(gameDirectory, Backup), marker = Path.Combine(gameDirectory, Marker);
        foreach (string path in new[] { current, backup, marker }) BuildFiles.RefuseLink(path);
        if (!File.Exists(current))
            throw new FileNotFoundException($"Cannot install: 'data.win' is missing in '{Path.GetFileName(gameDirectory)}'. Choose a legacy game folder containing the selected original data.win.");
        if ((protectedInputs ?? []).Any(p => BuildFiles.SamePath(BuildFiles.Resolve(p), BuildFiles.Resolve(current))
            || BuildFiles.SamePath(BuildFiles.Resolve(p), BuildFiles.Resolve(marker))))
            throw new IOException("Refusing to install over a supplied input archive or installation marker. Select the original base game folder or a separate legacy installation directory.");
        string currentHash = BuildFiles.Hash(current);
        if (basePath is not null && BuildFiles.SamePath(BuildFiles.Resolve(basePath), BuildFiles.Resolve(current)) && currentHash != baseHash)
            throw new IOException("The installation archive supplied as --base no longer matches the original base hash. Use the verified data.win.cgfmm-original backup as --base.");
        if (File.Exists(backup) && BuildFiles.Hash(backup) != baseHash)
            throw new IOException($"Existing '{Backup}' differs from the selected base. It will never be overwritten. Choose the matching base or another game directory.");
        if (currentHash == baseHash) return;
        BuildMarker? identity;
        try { identity = File.Exists(marker) ? JsonSerializer.Deserialize<BuildMarker>(File.ReadAllText(marker), JsonOptions) : null; }
        catch (JsonException) { identity = null; }
        if (identity is null || identity.FormatVersion != 1 || identity.BaseSha256 != baseHash || identity.BuildSha256 != currentHash
            || !File.Exists(backup) || identity.BackupSha256 != BuildFiles.Hash(backup))
            throw new IOException($"Refusing to overwrite '{Archive}': it is neither the selected original base nor a recognized cgfmm build with matching hashes. Restore a verified backup or choose another legacy game directory.");
    }

    /// <summary>Backs up once, verifies all copies by SHA-256 and records the installed build's identity.</summary>
    public static void Install(string mergedPath, string gameDirectory, string baseHash, IEnumerable<string>? protectedInputs = null, string? basePath = null,
        IReadOnlyList<CatalogMapping>? catalog = null)
    {
        string[] inputs = [mergedPath, .. protectedInputs ?? []];
        Validate(gameDirectory, baseHash, inputs, basePath);
        gameDirectory = BuildFiles.Resolve(gameDirectory);
        string current = Path.Combine(gameDirectory, Archive), backup = Path.Combine(gameDirectory, Backup), marker = Path.Combine(gameDirectory, Marker);
        string currentHash = BuildFiles.Hash(current), buildHash = BuildFiles.Hash(mergedPath);
        if (!File.Exists(backup))
        {
            // No replacement is permitted: the backup name belongs permanently to this first copy.
            CopyVerified(current, backup, currentHash, overwrite: false);
        }
        string backupHash = BuildFiles.Hash(backup);
        if (backupHash != baseHash) throw new IOException("The verified backup does not match the base. Installation stopped before replacing data.win.");
        Validate(gameDirectory, baseHash, inputs, basePath);
        CopyVerified(mergedPath, current, buildHash, overwrite: true);
        WriteMarker(marker, new(1, baseHash, buildHash, backupHash, catalog));
    }

    /// <summary>Restores the retained original and verifies the result; never deletes or overwrites the backup.</summary>
    public static string Restore(string gameDirectory)
    {
        string current = Path.Combine(gameDirectory, Archive), backup = Path.Combine(gameDirectory, Backup), marker = Path.Combine(gameDirectory, Marker);
        foreach (string path in new[] { current, backup, marker }) BuildFiles.RefuseLink(path);
        gameDirectory = BuildFiles.Resolve(gameDirectory);
        current = Path.Combine(gameDirectory, Archive); backup = Path.Combine(gameDirectory, Backup); marker = Path.Combine(gameDirectory, Marker);
        if (!File.Exists(backup)) throw new FileNotFoundException($"Cannot restore: '{Backup}' is missing in '{Path.GetFileName(gameDirectory)}'. Select the game folder where cgfmm installed the build.");
        string hash = BuildFiles.Hash(backup);
        if (File.Exists(marker))
        {
            BuildMarker? identity;
            try { identity = JsonSerializer.Deserialize<BuildMarker>(File.ReadAllText(marker), JsonOptions); }
            catch (JsonException ex) { throw new InvalidDataException($"Cannot read '{Marker}'. Check the installation marker before restoring.", ex); }
            if (identity is null || identity.FormatVersion != 1 || identity.BackupSha256 != hash)
                throw new IOException($"Backup '{Backup}' does not match the recorded SHA-256. Restore stopped; the backup has been kept.");
        }
        CopyVerified(backup, current, hash, overwrite: true);
        if (File.Exists(marker)) File.Delete(marker);
        return hash;
    }

    private static void CopyVerified(string source, string destination, string expectedHash, bool overwrite)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, Path.GetFileName(destination) == Archive
            ? Archive + ".new" : $".cgfmm-copy-{Guid.NewGuid():N}.tmp");
        try
        {
            BuildFiles.RefuseLink(temporary);
            if (File.Exists(temporary)) File.Delete(temporary);
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            if (BuildFiles.Hash(temporary) != expectedHash) throw new IOException($"Copy verification failed for '{Path.GetFileName(destination)}'. Destination was not replaced; check disk space and retry.");
            BuildFiles.RefuseLink(destination);
            File.Move(temporary, destination, overwrite);
            if (BuildFiles.Hash(destination) != expectedHash) throw new IOException($"Installed copy '{Path.GetFileName(destination)}' failed SHA-256 verification. Restore the retained original.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteMarker(string path, BuildMarker marker)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".cgfmm-marker-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(marker, JsonOptions) + "\n");
            BuildFiles.RefuseLink(path); File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
