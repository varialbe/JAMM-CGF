using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>Fetches each public source independently and retains successful/offline snapshots when another source fails.</summary>
public sealed class CombinedCatalogProvider(params IModCatalogProvider[] providers) : IModCatalogProvider
{
    /// <inheritdoc />
    public async Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(providers.Select(async provider =>
        {
            try { return (Snapshot: await provider.LoadAsync(cancellationToken), Error: (string?)null); }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or IOException or InvalidDataException or TimeoutException or System.Text.Json.JsonException)
            { return (Snapshot: (CatalogSnapshot?)null, Error: ex.Message); }
        }));
        cancellationToken.ThrowIfCancellationRequested();
        CatalogSnapshot[] good = results.Select(r => r.Snapshot).OfType<CatalogSnapshot>().ToArray();
        if (good.Length == 0) throw new IOException("Could not load any Browse catalog. Check your connection and Retry. " + string.Join("; ", results.Select(r => r.Error).OfType<string>()));
        var mods = good.SelectMany(s => s.Mods).GroupBy(m => m.Catalog + ":" + m.Id, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        string[] notices = results.Select(r => r.Error).OfType<string>().Concat(good.Select(s => s.OfflineReason).OfType<string>()).ToArray();
        DateTimeOffset fetched = good.Min(s => s.FetchedUtc);
        return new(mods, fetched, notices.Length == 0 ? null : string.Join(" · ", notices));
    }
}

/// <summary>First-party listing for the official Game Jolt build of The Abyss.</summary>
public sealed class OfficialAbyssCatalogProvider : IModCatalogProvider
{
    /// <inheritdoc />
    public Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CatalogMod mod = new("gamejolt-842483", "The Abyss", "varialbe", "The Abyss mod for Cat Goes Fishing.",
            "https://gamejolt.com/games/ringdingdingringring/842483", false, ["Content"], null, null,
            Catalog: "gamejolt", CatalogPage: "https://gamejolt.com/games/ringdingdingringring/842483");
        return Task.FromResult(new CatalogSnapshot([mod], DateTimeOffset.UtcNow));
    }
}

/// <summary>Constructs all supported Browse sources.</summary>
public static class BrowseCatalogs
{
    /// <summary>Returns Litterbox, Varialbe's first-party catalog, and The Abyss Game Jolt listing.</summary>
    public static IModCatalogProvider Create(ManagerStore store, ModHttp http) => new CombinedCatalogProvider(
        new LitterboxCatalogProvider(store, http), new FirstPartyCatalogProvider(store, http), new OfficialAbyssCatalogProvider());
}
