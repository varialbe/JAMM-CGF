using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>A catalog update available for a mod enabled in a profile.</summary>
public sealed record CatalogUpdate(string Id, string Name, string? InstalledVersion, string AvailableVersion,
    long? SizeBytes, string? Changelog, CatalogMod CatalogEntry, ManagedMod InstalledMod);

/// <summary>Compares catalog provenance with the selected profile's installed library packages.</summary>
public sealed class CatalogUpdates(ManagerStore store, IModCatalogProvider catalog)
{
    /// <summary>Lists updates for catalog sourced mods in the profile without changing local files.</summary>
    public async Task<IReadOnlyList<CatalogUpdate>> CheckAsync(string? profileName = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await catalog.LoadAsync(cancellationToken);
        var storeLibrary = new ModLibrary(store);
        var library = storeLibrary.List().Select(x => x.Mod).ToDictionary(x => x.Id, StringComparer.Ordinal);
        var config = store.Config();
        var profile = new ProfileLibrary(store).Get(profileName ?? config.ActiveProfile
            ?? throw new ArgumentException("No active profile. Use 'profile use <name>' or pass --profile <name>."));
        return Compare(profile, library.Values, snapshot);
    }

    /// <summary>Compares catalog metadata with selected library mods; exposed for offline tests.</summary>
    public static IReadOnlyList<CatalogUpdate> Compare(ModProfile profile, IEnumerable<ManagedMod> installedMods, CatalogSnapshot snapshot)
    {
        var library = installedMods.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var entries = snapshot.Mods.Where(m => !string.IsNullOrWhiteSpace(m.Version) && ValidHash(m.Sha256))
            .GroupBy(m => m.Catalog + ":" + m.Id, StringComparer.Ordinal).Select(g => g.First()).ToDictionary(m => m.Catalog + ":" + m.Id, StringComparer.Ordinal);
        var updates = new List<CatalogUpdate>();
        foreach (string modId in profile.EnabledMods)
        {
            if (!library.TryGetValue(modId, out var mod)) continue;
            foreach (var origin in mod.Origins)
            {
                if (!entries.TryGetValue(origin.Catalog + ":" + origin.ModId, out var entry)) continue;
                if (origin.Version == entry.Version && string.Equals(origin.PackageSha256, entry.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                if (CompareVersions(entry.Version!, origin.Version) <= 0 && string.Equals(origin.PackageSha256, entry.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                updates.Add(new(entry.Id, entry.Name, origin.Version ?? mod.Version, entry.Version!, entry.SizeBytes, entry.Changelog, entry, mod));
                break;
            }
        }
        return updates;
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static int CompareVersions(string available, string? installed)
    {
        if (Version.TryParse(available, out var a) && Version.TryParse(installed, out var b)) return a.CompareTo(b);
        return string.Compare(available, installed, StringComparison.OrdinalIgnoreCase);
    }
}
