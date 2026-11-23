using Jamm.Core.Management;
using Jamm.Core.Patching;
using System.Globalization;

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

    /// <summary>Finds enabled catalog-backed mods whose catalog identity has disappeared.</summary>
    public static IReadOnlyList<(ManagedMod Mod, ModOrigin Origin)> MissingCatalogEntries(ModProfile profile, IEnumerable<ManagedMod> installedMods, CatalogSnapshot snapshot)
    {
        var entries = snapshot.Mods.Select(m => m.Catalog + ":" + m.Id).ToHashSet(StringComparer.Ordinal);
        var library = installedMods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        return profile.EnabledMods.Where(library.ContainsKey).SelectMany(id => library[id].Origins
            .Where(o => !entries.Contains(o.Catalog + ":" + o.ModId)).Select(o => (library[id], o))).ToArray();
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal static int CompareVersions(string available, string? installed)
    {
        static (Version? Core, bool Prerelease) Parse(string value)
        {
            string[] parts = value.TrimStart('v', 'V').Split('-', 2);
            return (Version.TryParse(parts[0], out var version) ? version : null, parts.Length > 1);
        }
        if (installed is not null)
        {
            var a = Parse(available); var b = Parse(installed);
            if (a.Core is not null && b.Core is not null)
            {
                int comparison = a.Core.CompareTo(b.Core);
                return comparison != 0 ? comparison : a.Prerelease.CompareTo(b.Prerelease) * -1;
            }
        }
        return string.Compare(available, installed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns concrete save compatibility reasons using version metadata and added asset names.</summary>
    public static IReadOnlyList<string> ConflictReasons(CatalogUpdate update, ModPatch? before, ModPatch? after)
    {
        List<string> reasons = [];
        if (update.CatalogEntry.SaveBreaking) reasons.Add("catalog marks this release as save-breaking");
        if (Major(update.InstalledVersion) is int oldMajor && Major(update.AvailableVersion) is int newMajor && oldMajor != newMajor)
            reasons.Add($"major version changed from {oldMajor} to {newMajor}");
        if (before is not null && after is not null)
        {
            var oldNames = AddedNames(before);
            var newNames = AddedNames(after);
            string[] removed = oldNames.Except(newNames, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (removed.Length > 0) reasons.Add("removed or renamed added assets: " + string.Join(", ", removed.Take(8)) + (removed.Length > 8 ? $" (+{removed.Length - 8} more)" : ""));
        }
        return reasons;
    }

    private static int? Major(string? value) => int.TryParse(value?.TrimStart('v', 'V').Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
    private static HashSet<string> AddedNames(ModPatch patch) => patch.Changes
        .Where(p => p.Key is "objects" or "code" or "scripts")
        .SelectMany(p => p.Value.Added.Select(a => p.Key + ":" + a.Name)).ToHashSet(StringComparer.Ordinal);
}
