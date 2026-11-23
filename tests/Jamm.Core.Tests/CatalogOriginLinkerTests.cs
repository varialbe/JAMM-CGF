using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Core.Tests;

public sealed class CatalogOriginLinkerTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BaseHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void ExactArchiveHashMatchesRegardlessOfDisplayName()
    {
        var entry = Catalog("catalog-name", "1.0.0", Hash);
        var result = CatalogOriginLinker.Match(Installed("different local name", Hash), Snapshot(entry));
        Assert.Same(entry, result.Mod);
        Assert.Equal("archive SHA-256", result.Reason);
        Assert.False(result.Ambiguous);
    }

    [Fact]
    public void HeuristicRequiresNameBaseAndVersionTogether()
    {
        var entry = Catalog("Weather Pack", "1.2.0", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
        Assert.Equal(entry, CatalogOriginLinker.Match(Installed("weather pack", "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"), Snapshot(entry)).Mod);
        var versionless = Installed("weather-pack", "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd") with { Version = null };
        var inferred = CatalogOriginLinker.Match(versionless, Snapshot(entry));
        Assert.Equal(entry, inferred.Mod);
        Assert.Contains("catalog version recorded", inferred.Reason);
        Assert.Null(CatalogOriginLinker.Match(Installed("Weather Pack", "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd") with { BaseSha256 = Hash }, Snapshot(entry)).Mod);
    }

    [Fact]
    public void VersionDisambiguatesSameNameAndBase()
    {
        var old = Catalog("Silo", "1.0.0", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
        var current = old with { Id = "silo-current", Version = "1.2.0" };
        var installed = Installed("silo", Hash) with { Version = "1.2.0" };
        Assert.Equal(current, CatalogOriginLinker.Match(installed, Snapshot(old, current)).Mod);
    }

    [Fact]
    public void LinkedLocalArchiveRemainsEligibleForPublishedPackageUpdate()
    {
        var entry = Catalog("Weather Pack", "1.2.0", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
        var installed = Installed("Weather Pack", "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd") with
        {
            Version = entry.Version,
            Origins = [new ModOrigin("first-party", entry.Id, entry.SourcePage, "catalog", entry.Id, null, entry.Version)]
        };
        var profile = new ModProfile("default", [installed.Id], []);
        Assert.Single(CatalogUpdates.Compare(profile, [installed], Snapshot(entry)));
    }

    [Fact]
    public void AmbiguousMatchesAreNotGuessed()
    {
        var first = Catalog("Silo", "1.0.0", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
        var second = first with { Id = "silo-alt" };
        var result = CatalogOriginLinker.Match(Installed("Silo", Hash), Snapshot(first, second));
        Assert.Null(result.Mod);
        Assert.True(result.Ambiguous);
    }

    private static ManagedMod Installed(string name, string hash) => new("mod-id", name, "mod.win", hash,
        DateTimeOffset.UnixEpoch, BaseHash, 1, "v1", 1) { Version = "1.2.0" };

    private static CatalogMod Catalog(string name, string version, string hash) => new("silo", name, "Author", "", "https://example.test/mod.zip",
        false, [], null, null, Catalog: "first-party", Version: version, Sha256: hash,
        Compat: new FirstPartyCompatibility(BaseHash, null));

    private static CatalogSnapshot Snapshot(params CatalogMod[] entries) => new(entries, DateTimeOffset.UnixEpoch);
}
