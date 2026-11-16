using System.Net;
using System.Text.Json;
using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>A server analysis with a stable public source identity.</summary>
public sealed record SharedCheck(string Provider, string FileId, string ModName, string? ExternalId,
    string? Version, long? SizeBytes, string? SourceBuildUtc, DateTimeOffset CheckedUtc, ModFacts Facts);
/// <summary>Versioned static compatibility document.</summary>
public sealed record SharedIndex(int SchemaVersion, string BaseHash, int AnalyzerVersion, SharedCheck[] Checks);

/// <summary>Bounded anonymous static lookups; unavailable or invalid results always fall back locally.</summary>
public sealed class SharedCompatibility(ManagerStore store, ModHttp http)
{
    private sealed record Cache(string? ETag, DateTimeOffset? Modified, SharedIndex Document);
    /// <summary>Checks canonical SHA-256 paths without accepting arbitrary URL components.</summary>
    public static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    /// <summary>Looks up by provider identity before download or by verified archive hash afterwards.</summary>
    public async Task<SharedCheck?> FindAsync(string baseHash, ModDownloadFile file, string? verifiedHash, CancellationToken token)
    {
        if (!store.Config().UseSharedCompatibilityResults || !IsHash(baseHash) || verifiedHash is not null && !IsHash(verifiedHash)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            string name = verifiedHash ?? "index";
            string path = store.Path("shared-compat", baseHash + "-" + name + ".json");
            Cache? cached = null;
            if (File.Exists(path)) { try { cached = store.Read<Cache>(path); } catch (InvalidDataException) { } }
            using var response = await http.SendRangeAsync(new Uri($"https://varialbe.xyz/jamm/compat/v1/{baseHash}/{name}.json"), timeout.Token,
                etag: cached?.ETag, modified: cached?.Modified);
            SharedIndex document;
            if (response.StatusCode == HttpStatusCode.NotModified) document = cached?.Document ?? throw new InvalidDataException();
            else
            {
                const int limit = 2 * 1048576;
                if (response.Content.Headers.ContentLength > limit) return null;
                using var bytes = new MemoryStream();
                await ModHttp.CopyBoundedAsync(await response.Content.ReadAsStreamAsync(timeout.Token), bytes, limit, response.Content.Headers.ContentLength, null, timeout.Token);
                document = JsonSerializer.Deserialize<SharedIndex>(bytes.ToArray()) ?? throw new InvalidDataException();
            }
            if (!Valid(document, baseHash)) return null;
            if (response.StatusCode != HttpStatusCode.NotModified)
                store.Write(path, new Cache(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, document));
            return document.Checks.FirstOrDefault(c => c.Provider == file.Provider && c.FileId == file.Id
                && (verifiedHash is null || c.Facts.FileHash == verifiedHash));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidDataException or OperationCanceledException or ArgumentException)
        { token.ThrowIfCancellationRequested(); return null; }
    }
    private static bool Valid(SharedIndex d, string hash) => d.SchemaVersion == 1 && d.BaseHash == hash && d.AnalyzerVersion == ModFactAnalyzer.Version
        && d.Checks is { Length: <= 10000 } && d.Checks.All(c => c is not null && c.Facts is { } f && IsHash(f.FileHash)
            && f.BaseHash == hash && f.AnalyzerVersion == ModFactAnalyzer.Version && f.Compatibility is "Compatible" or "Incompatible" or "Unsure" or "Tool limitation"
            && f.Reason is { Length: <= 32000 } && f.Adds is { Length: <= 100 } && f.Adds.All(a => a is { Length: <= 1000 })
            && !string.IsNullOrWhiteSpace(c.Provider) && !string.IsNullOrWhiteSpace(c.FileId) && c.CheckedUtc != default);
}
