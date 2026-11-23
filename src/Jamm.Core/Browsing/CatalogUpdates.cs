using Jamm.Core.Management;
using Jamm.Core.Patching;
using System.Globalization;

namespace Jamm.Core.Browsing;

/// <summary>A catalog update available for a mod enabled in a profile.</summary>
public sealed record CatalogUpdate(string Id, string Name, string? InstalledVersion, string AvailableVersion,
    long? SizeBytes, string? Changelog, CatalogMod CatalogEntry, ManagedMod InstalledMod)
{
    /// <summary>True when applying this same-version update replaces a locally built archive.</summary>
    public bool IsLocalReplacement { get; init; }
}

/// <summary>A same-version local build whose archive differs from the published catalog package.</summary>
public sealed record CatalogLocalBuild(string Id, string Name, string? Version, ManagedMod InstalledMod);

/// <summary>Compares catalog provenance with the selected profile's installed library packages.</summary>
public sealed class CatalogUpdates(ManagerStore store, IModCatalogProvider catalog)
{
    /// <summary>Lists updates for catalog sourced mods in the profile without changing local files.</summary>
    public async Task<IReadOnlyList<CatalogUpdate>> CheckAsync(string? profileName = null, CancellationToken cancellationToken = default, bool includeLocalReplacements = false)
    {
        var snapshot = await catalog.LoadAsync(cancellationToken);
        var storeLibrary = new ModLibrary(store);
        var library = storeLibrary.List().Select(x => x.Mod).ToDictionary(x => x.Id, StringComparer.Ordinal);
        var config = store.Config();
        var profile = new ProfileLibrary(store).Get(profileName ?? config.ActiveProfile
            ?? throw new ArgumentException("No active profile. Use 'profile use <name>' or pass --profile <name>."));
        return Compare(profile, library.Values, snapshot, includeLocalReplacements);
    }

    /// <summary>Compares catalog metadata with selected library mods; exposed for offline tests.</summary>
    public static IReadOnlyList<CatalogUpdate> Compare(ModProfile profile, IEnumerable<ManagedMod> installedMods, CatalogSnapshot snapshot, bool includeLocalReplacements = false)
    {
        var library = installedMods.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var entries = snapshot.Mods.Where(m => !string.IsNullOrWhiteSpace(m.Version) && ValidHash(m.Sha256))
            .GroupBy(m => m.Catalog + ":" + m.Id, StringComparer.Ordinal).Select(g => g.First()).ToDictionary(m => m.Catalog + ":" + m.Id, StringComparer.Ordinal);
        var updates = new List<CatalogUpdate>();
        foreach (string modId in profile.EnabledMods)
        {
            if (!library.TryGetValue(modId, out var mod)) continue;
            if (mod.Id.StartsWith("builtin-", StringComparison.Ordinal)) continue;
            foreach (var origin in mod.Origins)
            {
                if (!entries.TryGetValue(origin.Catalog + ":" + origin.ModId, out var entry)) continue;
                int versionComparison = CompareVersions(entry.Version!, origin.Version ?? mod.Version);
                bool localBuild = IsLocalBuild(origin, mod, entry);
                if (versionComparison <= 0 && !(includeLocalReplacements && versionComparison == 0 && localBuild)) continue;
                updates.Add(new CatalogUpdate(entry.Id, entry.Name, origin.Version ?? mod.Version, entry.Version!, entry.SizeBytes, entry.Changelog, entry, mod)
                { IsLocalReplacement = versionComparison == 0 && localBuild });
                break;
            }
        }
        return updates;
    }

    /// <summary>Lists same-version local builds separately from available catalog updates.</summary>
    public static IReadOnlyList<CatalogLocalBuild> LocalBuilds(ModProfile profile, IEnumerable<ManagedMod> installedMods, CatalogSnapshot snapshot)
    {
        var library = installedMods.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var entries = snapshot.Mods.Where(m => !string.IsNullOrWhiteSpace(m.Version) && ValidHash(m.Sha256))
            .GroupBy(m => m.Catalog + ":" + m.Id, StringComparer.Ordinal).Select(g => g.First()).ToDictionary(m => m.Catalog + ":" + m.Id, StringComparer.Ordinal);
        var result = new List<CatalogLocalBuild>();
        foreach (string id in profile.EnabledMods)
        {
            if (!library.TryGetValue(id, out var mod)) continue;
            if (mod.Id.StartsWith("builtin-", StringComparison.Ordinal)) continue;
            foreach (var origin in mod.Origins)
            {
                if (!entries.TryGetValue(origin.Catalog + ":" + origin.ModId, out var entry)) continue;
                if (CompareVersions(entry.Version!, origin.Version ?? mod.Version) == 0 && IsLocalBuild(origin, mod, entry))
                    result.Add(new(entry.Id, entry.Name, origin.Version ?? mod.Version, mod));
                break;
            }
        }
        return result;
    }

    private static bool IsLocalBuild(ModOrigin origin, ManagedMod mod, CatalogMod entry)
    {
        if (origin.MatchKind is not ("name-base-version" or "name-base" or "explicit")) return false;
        string? localHash = origin.LocalBuildSha256;
        return ValidHash(localHash) && !string.Equals(localHash, entry.Sha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(localHash, mod.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Finds enabled catalog-backed mods whose catalog identity has disappeared.</summary>
    public static IReadOnlyList<(ManagedMod Mod, ModOrigin Origin)> MissingCatalogEntries(ModProfile profile, IEnumerable<ManagedMod> installedMods, CatalogSnapshot snapshot)
    {
        var entries = snapshot.Mods.Select(m => m.Catalog + ":" + m.Id).ToHashSet(StringComparer.Ordinal);
        var library = installedMods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        return profile.EnabledMods.Where(library.ContainsKey).Where(id => !library[id].Id.StartsWith("builtin-", StringComparison.Ordinal)).SelectMany(id => library[id].Origins
            .Where(o => !entries.Contains(o.Catalog + ":" + o.ModId)).Select(o => (library[id], o))).ToArray();
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal static int CompareVersions(string available, string? installed)
    {
        static (string[] Core, string[]? Prerelease)? Parse(string value)
        {
            string clean = value.Trim();
            if (clean.StartsWith('v') || clean.StartsWith('V')) clean = clean[1..];
            clean = clean.Split('+', 2)[0];
            string[] parts = clean.Split('-', 2);
            string[] numbers = parts[0].Split('.');
            if (numbers.Length != 3 || numbers.Any(n => n.Length == 0 || (n.Length > 1 && n[0] == '0') || !n.All(char.IsAsciiDigit))) return null;
            string[]? prerelease = parts.Length == 1 ? null : parts[1].Split('.');
            if (prerelease is { Length: 0 } || prerelease?.Any(p => string.IsNullOrEmpty(p)
                || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
                || (p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0')) == true) return null;
            return (numbers, prerelease);
        }
        var a = Parse(available);
        if (a is null) return 0;
        if (installed is null) return 1;
        var b = Parse(installed);
        if (b is null) return 0;
        for (int i = 0; i < 3; i++)
        {
            int comparison = a.Value.Core[i].Length != b.Value.Core[i].Length
                ? a.Value.Core[i].Length.CompareTo(b.Value.Core[i].Length)
                : string.CompareOrdinal(a.Value.Core[i], b.Value.Core[i]);
            if (comparison != 0) return comparison;
        }
        if (a.Value.Prerelease is null) return b.Value.Prerelease is null ? 0 : 1;
        if (b.Value.Prerelease is null) return -1;
        for (int i = 0; i < Math.Min(a.Value.Prerelease.Length, b.Value.Prerelease.Length); i++)
        {
            string left = a.Value.Prerelease[i], right = b.Value.Prerelease[i];
            bool leftNumeric = left.All(char.IsAsciiDigit), rightNumeric = right.All(char.IsAsciiDigit);
            int comparison = leftNumeric && rightNumeric
                ? left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right)
                : leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1) : string.CompareOrdinal(left, right);
            if (comparison != 0) return comparison;
        }
        return a.Value.Prerelease.Length.CompareTo(b.Value.Prerelease.Length);
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
