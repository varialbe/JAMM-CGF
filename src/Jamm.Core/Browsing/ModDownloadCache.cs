using Jamm.Core.Building;
using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>Retains validated archive bytes by hash, without trusting expiring URLs or filenames.</summary>
public sealed class ModDownloadCache(ManagerStore store, ModHttp http)
{
    private sealed record CachedFile(string Hash);
    /// <summary>Reuses immutable provider file identities across sessions after verifying the cached bytes.</summary>
    public async Task<string> FetchFileAsync(ModDownloadFile file, IModDownloadResolver resolver, IProgress<ModDownloadProgress>? progress, CancellationToken token)
    {
        string key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file.Provider + "\n" + file.SourceUrl + "\n" + file.ReleaseId + "\n" + file.Id)));
        string index = store.Path("download-cache", key + ".json");
        if (File.Exists(index))
        {
            var entry = store.Read<CachedFile>(index);
            if (entry.Hash.Length == 64 && entry.Hash.All(char.IsAsciiHexDigit))
            {
                string path = store.Path("download-cache", entry.Hash + ".win");
                if (File.Exists(path) && BuildFiles.Hash(path) == entry.Hash) { token.ThrowIfCancellationRequested(); return path; }
            }
        }
        string downloaded = await FetchAsync(await resolver.GetFileUriAsync(file, token), progress, token);
        store.Write(index, new CachedFile(BuildFiles.Hash(downloaded))); return downloaded;
    }
    /// <summary>Downloads with bounds/cancellation, extracts the single archive and publishes a content-addressed copy.</summary>
    public async Task<string> FetchAsync(Uri uri, IProgress<ModDownloadProgress>? progress, CancellationToken token)
    {
        string staging = await new ModDownloadService(store, http).DownloadAsync(uri, progress, token);
        string? extracted = null;
        try
        {
            string archive = staging;
            if (staging.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                extracted = store.Path("downloads", Guid.NewGuid().ToString("N") + ".win");
                await Task.Run(() => ModLibrary.CopyZip(staging, extracted, token), token); archive = extracted;
            }
            string hash = BuildFiles.Hash(archive);
            string destination = store.Path("download-cache", hash + ".win");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            token.ThrowIfCancellationRequested();
            if (!File.Exists(destination)) File.Move(archive, destination);
            else if (BuildFiles.Hash(destination) != hash) throw new InvalidDataException("Cached download changed. Remove the download-cache folder and retry.");
            return destination;
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
            if (extracted is not null && File.Exists(extracted)) File.Delete(extracted);
        }
    }
}

/// <summary>Deterministic default selection, retaining real ambiguity for the advanced picker.</summary>
public static class ModFileSelection
{
    /// <summary>Selects a unique supported file from the newest release ID, otherwise asks the player.</summary>
    public static ModDownloadFile? Choose(IEnumerable<ModDownloadFile> files)
    {
        var all = files.ToArray();
        if (all.Length == 1) return all[0];
        if (all.Length == 0 || all.Any(f => !ulong.TryParse(f.ReleaseId, out _))) return null;
        ulong newest = all.Max(f => ulong.Parse(f.ReleaseId!));
        var candidates = all.Where(f => ulong.Parse(f.ReleaseId!) == newest).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
