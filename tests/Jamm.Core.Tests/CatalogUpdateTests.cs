using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Core.Tests;

public sealed class CatalogUpdateTests
{
    private const string OldHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string NewHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void CurrentVersionAndPackageHashAreUpToDate()
    {
        Assert.Empty(CatalogUpdates.Compare(Profile(), [Installed("1.2.0", OldHash)], Snapshot("1.2.0", OldHash)));
    }

    [Fact]
    public void NewerVersionIsAvailable()
    {
        var update = Assert.Single(CatalogUpdates.Compare(Profile(), [Installed("1.0.0", OldHash)], Snapshot("1.1.0", OldHash)));
        Assert.Equal("1.0.0", update.InstalledVersion);
        Assert.Equal("1.1.0", update.AvailableVersion);
        Assert.Equal(123L, update.SizeBytes);
        Assert.Equal("fix", update.Changelog);
    }

    [Fact]
    public void ChangedCatalogPackageHashIsAnUpdateEvenWhenVersionIsUnchanged()
    {
        Assert.Single(CatalogUpdates.Compare(Profile(), [Installed("1.0.0", OldHash)], Snapshot("1.0.0", NewHash)));
    }

    private static ModProfile Profile() => new("test", ["mod-1"], []);
    private static ManagedMod Installed(string? version, string hash) => new("mod-1", "Example", "example.win", hash,
        DateTimeOffset.UnixEpoch, OldHash, 0, "1.4", 16)
    {
        Origins = [new ModOrigin("first-party", "example", "https://example.test", "https", "package", null, version)
            { PackageSha256 = hash }]
    };
    private static CatalogSnapshot Snapshot(string version, string hash) => new(
        [new CatalogMod("example", "Example", "Author", "", "https://example.test/package.zip", false, [], null, null,
            Catalog: "first-party", Version: version, Sha256: hash, SizeBytes: 123, Changelog: "fix")], DateTimeOffset.UnixEpoch);
}
