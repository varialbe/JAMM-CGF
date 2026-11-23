using Jamm.Core.Building;
using Jamm.Core.Management;

namespace Jamm.Core.Browsing;

/// <summary>Links imported library mods to an unambiguous first-party catalog identity.</summary>
public static class CatalogOriginLinker
{
    /// <summary>Checks archive digest first, then requires matching name, base digest, and version.</summary>
    public static (CatalogMod? Mod, string? Reason, bool Ambiguous) Match(ManagedMod installed, CatalogSnapshot snapshot)
    {
        if (IsBuiltin(installed)) return (null, "built-in mods are generated locally and cannot be linked", false);
        var entries = snapshot.Mods.Where(m => m.Catalog == "first-party").ToArray();
        var hashes = entries.Where(m => ValidHash(m.Sha256) && string.Equals(installed.Sha256, m.Sha256, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (hashes.Length == 1) return (hashes[0], "archive SHA-256", false);
        if (hashes.Length > 1) return (null, "multiple catalog entries share this archive SHA-256", true);

        string? baseHash = installed.BaseSha256;
        var candidates = entries.Where(m => NamesMatch(m.Name, installed.Name)
            && string.Equals(m.Compat?.RequiresBase, baseHash, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length > 1 && !string.IsNullOrWhiteSpace(installed.Version))
        {
            var versionMatches = candidates.Where(m => string.Equals(m.Version, installed.Version, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (versionMatches.Length > 0) candidates = versionMatches;
        }
        return candidates.Length switch
        {
            1 => (candidates[0], string.IsNullOrWhiteSpace(installed.Version)
                ? "normalized name and base SHA-256 (catalog version recorded)"
                : string.Equals(candidates[0].Version, installed.Version, StringComparison.OrdinalIgnoreCase)
                    ? "name, base SHA-256, and version" : "unique name and base SHA-256; catalog version differs" , false),
            > 1 => (null, "name and base SHA-256 match multiple catalog entries; version did not disambiguate", true),
            _ => (null, null, false)
        };
    }

    /// <summary>Persists first-party provenance and catalog package identity for a selected mod.</summary>
    public static ManagedMod Link(ModLibrary library, ManagedMod installed, CatalogMod entry, string? matchReason = null)
    {
        if (IsBuiltin(installed)) throw new InvalidOperationException("Built-in mods are generated locally and cannot be linked to a catalog.");
        string matchKind = matchReason switch
        {
            "archive SHA-256" => "archive-sha",
            "name, base SHA-256, and version" => "name-base-version",
            "normalized name and base SHA-256 (catalog version recorded)" or "unique name and base SHA-256; catalog version differs" => "name-base",
            _ => "explicit"
        };
        bool exactArchive = matchKind == "archive-sha";
        var origin = new ModOrigin("first-party", entry.Id, entry.SourcePage, "catalog", entry.Id, null, entry.Version)
        {
            PackageSha256 = exactArchive ? entry.Sha256 : null,
            MatchKind = matchKind,
            LocalBuildSha256 = exactArchive ? null : installed.Sha256
        };
        var updated = installed with
        {
            Origins = [.. installed.Origins.Where(o => o.Catalog != "first-party"), origin],
            PackageId = entry.Id,
            Version = entry.Version ?? installed.Version,
            Author = installed.Author ?? entry.Author,
            Description = installed.Description ?? entry.Description
        };
        library.SaveMetadata(updated);
        return updated;
    }

    private static bool IsBuiltin(ManagedMod mod) => mod.Id.StartsWith("builtin-", StringComparison.Ordinal);

    private static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static bool NamesMatch(string catalogName, string localName)
    {
        static string Normalize(string value)
        {
            string normalized = new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (normalized.StartsWith("the", StringComparison.Ordinal)) normalized = normalized[3..];
            if (normalized.EndsWith("preview", StringComparison.Ordinal)) normalized = normalized[..^7];
            return normalized;
        }
        return Normalize(catalogName) == Normalize(localName);
    }
}
