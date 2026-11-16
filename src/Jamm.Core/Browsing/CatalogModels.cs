namespace Jamm.Core.Browsing;

/// <summary>Public catalog metadata; compatibility remains unknown until local archive validation.</summary>
public sealed record CatalogMod(string Id, string Name, string Author, string Description, string? FileUrl,
    bool IsPreview, string[] Categories, DateTimeOffset? CreatedUtc, double? Rating,
    string? IconUrl = null, string? BannerUrl = null, string[]? Screenshots = null, bool VerifiedCreator = false, string Credits = "")
{
    /// <summary>Stable catalog detail page, distinct from the file host.</summary>
    public string SourcePage => "https://litterbox.fyi/mod.html?id=" + Uri.EscapeDataString(Id);
    /// <summary>Readable list summary without implying compatibility.</summary>
    public string Summary => $"{Author} · {string.Join(", ", Categories)} · {(IsPreview ? "Preview · no released download" : HasDownload ? "Released · source available" : "Released · no download")}";
    /// <summary>Whether the catalog advertises a released source.</summary>
    public bool HasDownload => !IsPreview && !string.IsNullOrWhiteSpace(FileUrl);
}

/// <summary>A timestamped successful catalog snapshot, including offline fallback information.</summary>
public sealed record CatalogSnapshot(CatalogMod[] Mods, DateTimeOffset FetchedUtc, string? OfflineReason = null);

/// <summary>A selectable file with stable provider identities; signed URLs are resolved only when downloading.</summary>
public sealed record ModDownloadFile(string Provider, string Id, string? ReleaseId, string? Version,
    string Name, string Package, long? Size, string SourceUrl)
{
    /// <summary>Selection label includes unknown metadata explicitly.</summary>
    public string Label => $"{Package} · {Version ?? "Version unknown"} · {Name} · {(Size is { } n ? $"{n / 1048576d:0.0} MB" : "Size unknown")}";
}

/// <summary>Resolution outcome with a browser fallback explanation when no supported files exist.</summary>
public sealed record ModDownloadResolution(ModDownloadFile[] Files, string? Notice = null);

/// <summary>Transfer progress; a missing total means indeterminate progress.</summary>
public sealed record ModDownloadProgress(long Received, long? Total);

/// <summary>Reads public catalog data without login or remote mutations.</summary>
public interface IModCatalogProvider
{
    /// <summary>Refreshes metadata, falling back to a timestamped local snapshot on network failure.</summary>
    Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>Separates host project pages from concrete downloadable files.</summary>
public interface IModDownloadResolver
{
    /// <summary>Lists every supported file so the user can choose explicitly.</summary>
    Task<ModDownloadResolution> ResolveAsync(CatalogMod mod, CancellationToken cancellationToken);
    /// <summary>Obtains a fresh concrete HTTPS URL for the selected file.</summary>
    Task<Uri> GetFileUriAsync(ModDownloadFile file, CancellationToken cancellationToken);
}
