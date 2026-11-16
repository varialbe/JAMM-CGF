using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Patching;

namespace Jamm.Core;

// Disposable JSON only. No archive models, decoded images or input streams are retained.
internal sealed class AnalysisCache(AnalysisOptions options)
{
    private static readonly object StorageGate = new();
    private static readonly object[] EntryGates = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    private sealed record Envelope<T>(string Digest, T Value);
    private const long EntryLimit = 64 * 1024 * 1024;
    private const long TotalLimit = 256 * 1024 * 1024;
    private string Root => Path.Combine(options.DataDirectory ?? Environment.GetEnvironmentVariable("CGFMM_DATA_DIR")
        ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "cgfmm"), "base-analysis");
    private string PathFor(string hash, string category) => Path.Combine(Root, ContentFingerprints.Hash(System.Text.Encoding.UTF8.GetBytes(
        hash + category + typeof(AnalysisCache).Assembly.ManifestModule.ModuleVersionId
        + typeof(UndertaleModLib.UndertaleData).Assembly.ManifestModule.ModuleVersionId
        + typeof(Underanalyzer.Decompiler.DecompileContext).Assembly.ManifestModule.ModuleVersionId + PatchJson.AssetTypesVersion)) + ".json");
    internal T Get<T>(string? hash, string category, Func<T> create)
    {
        if (!options.UseCache || hash is null) return create();
        string path = PathFor(hash, category);
        // Fixed stripes bound lock memory and coalesce simultaneous checks of the same base.
        int stripe = (StringComparer.Ordinal.GetHashCode(path) & int.MaxValue) % EntryGates.Length;
        lock (EntryGates[stripe]) return GetCore(path, create);
    }

    private T GetCore<T>(string path, Func<T> create)
    {
        try
        {
            BuildFiles.RefuseStorageLink(path);
            if (File.Exists(path) && new FileInfo(path).Length <= EntryLimit)
            {
                var value = JsonSerializer.Deserialize<Envelope<T>>(File.ReadAllText(path));
                if (value is not null && value.Value is not null
                    && value.Digest == ContentFingerprints.Hash(JsonSerializer.SerializeToUtf8Bytes(value.Value))) return value.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        var result = create();
        lock (StorageGate)
        {
            try
            {
                BuildFiles.RefuseStorageLink(path);
                Directory.CreateDirectory(Root);
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(result);
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope<T>(ContentFingerprints.Hash(payload), result));
                if (bytes.LongLength > EntryLimit) return result;
                // Evict oldest complete entries before publishing. Cache failures never block analysis.
                var files = new DirectoryInfo(Root).GetFiles("*.json").OrderBy(f => f.LastWriteTimeUtc).ToArray();
                long size = files.Sum(f => f.Length);
                foreach (var file in files)
                {
                    if (size + bytes.LongLength <= TotalLimit) break;
                    BuildFiles.RefuseStorageLink(file.FullName);
                    long length = file.Length; file.Delete(); size -= length;
                }
                string temp = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllBytes(temp, bytes);
                    BuildFiles.RefuseStorageLink(path);
                    File.Move(temp, path, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }
}
