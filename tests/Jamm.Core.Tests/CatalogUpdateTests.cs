using Jamm.Core.Browsing;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using System.Text.Json;

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
    public void SameVersionChangedPackageHashIsNotAnUpdate()
    {
        Assert.Empty(CatalogUpdates.Compare(Profile(), [Installed("1.0.0", OldHash)], Snapshot("1.0.0", NewHash)));
    }

    [Fact]
    public void SameVersionLocalBuildIsReportedSeparatelyAndRequiresForceToReplace()
    {
        var mod = Installed("1.0.0", OldHash) with
        {
            Origins = [new ModOrigin("first-party", "example", "https://example.test", "catalog", "example", null, "1.0.0")
            { MatchKind = "name-base-version", LocalBuildSha256 = OldHash }]
        };
        var snapshot = Snapshot("1.0.0", NewHash);
        Assert.Empty(CatalogUpdates.Compare(Profile(), [mod], snapshot));
        Assert.Single(CatalogUpdates.LocalBuilds(Profile(), [mod], snapshot));
        var replacement = Assert.Single(CatalogUpdates.Compare(Profile(), [mod], snapshot, includeLocalReplacements: true));
        Assert.True(replacement.IsLocalReplacement);
    }

    [Fact]
    public void MajorVersionChangeIsAConflict()
    {
        var update = new CatalogUpdate("example", "Example", "1.2.0", "2.0.0", null, null,
            Snapshot("2.0.0", NewHash).Mods[0], Installed("1.2.0", OldHash));
        Assert.Contains("major version changed from 1 to 2", CatalogUpdates.ConflictReasons(update, EmptyPatch(), EmptyPatch()));
    }

    [Fact]
    public void DroppedAddedObjectIsAConflict()
    {
        var update = new CatalogUpdate("example", "Example", "1.0.0", "1.1.0", null, null,
            Snapshot("1.1.0", NewHash).Mods[0], Installed("1.0.0", OldHash));
        Assert.Contains(CatalogUpdates.ConflictReasons(update, PatchWithObject("objects:Boat9"), EmptyPatch()),
            reason => reason.Contains("objects:Boat9", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageHashMismatchIsRejected()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "untrusted package");
            Assert.False(ModUpdateWorkflow.PackageHashMatches(path, NewHash));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FirstPartyCatalogReadsSaveBreakingFlag()
    {
        const string json = "[{\"id\":\"example\",\"name\":\"Example\",\"author\":\"Author\",\"description\":\"\",\"fileUrl\":\"https://example.test/mod.win\",\"isPreview\":false,\"categories\":[],\"version\":\"2.0.0\",\"sha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"saveBreaking\":true}]";
        Assert.True(Assert.Single(FirstPartyCatalogProvider.Parse(System.Text.Encoding.UTF8.GetBytes(json))).SaveBreaking);
    }

    [Fact]
    public void BuiltinsAreNeverCatalogUpdates()
    {
        var builtin = Installed("1.0.0", OldHash) with { Id = "builtin-console", Origins = [Installed("1.0.0", OldHash).Origins[0]] };
        Assert.Empty(CatalogUpdates.Compare(new ModProfile("test", [builtin.Id], []), [builtin], Snapshot("2.0.0", NewHash)));
        Assert.Empty(CatalogUpdates.LocalBuilds(new ModProfile("test", [builtin.Id], []), [builtin], Snapshot("1.0.0", NewHash)));
    }

    [Fact]
    public void RollbackRestoresPreviousGameArchiveAndModPackage()
    {
        string root = Path.Combine(Path.GetTempPath(), "jamm-update-rollback-" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "game");
        var store = new ManagerStore(Path.Combine(root, "store"));
        string mods = store.Path("mods");
        Directory.CreateDirectory(game); Directory.CreateDirectory(Path.Combine(mods, "mod-1.prev")); Directory.CreateDirectory(Path.Combine(mods, "mod-1"));
        try
        {
            File.WriteAllText(Path.Combine(game, "data.win"), "new game");
            string backup = Path.Combine(game, "data.win.bak-test"); File.WriteAllText(backup, "old game");
            File.WriteAllText(Path.Combine(mods, "mod-1", "data.win"), "new package");
            File.WriteAllText(Path.Combine(mods, "mod-1.prev", "data.win"), "old package");
            Directory.CreateDirectory(store.Path("updates"));
            File.WriteAllText(store.Path("updates", "rollback.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                profile = "test", gameDirectory = game, gameBackup = backup, modIds = new[] { "mod-1" }, saveBackup = ""
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            using var http = new ModHttp(new HttpClientHandler());
            var workflow = new ModUpdateWorkflow(store, new EmptyCatalog(), new EmptyResolver(), http);
            workflow.Rollback();
            Assert.Equal("old game", File.ReadAllText(Path.Combine(game, "data.win")));
            Assert.Equal("old package", File.ReadAllText(Path.Combine(mods, "mod-1", "data.win")));
        }
        finally { Directory.Delete(root, true); }
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

    private static ModPatch EmptyPatch() => new(PatchJson.FormatVersion, "test", DateTimeOffset.UnixEpoch,
        new("x.win", NewHash, 0, "1", 1), new("base.win", OldHash, 0, "1", 1), [], new(StringComparer.Ordinal));
    private static ModPatch PatchWithObject(string name)
    {
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal)
        { ["objects"] = new([new(name, JsonDocument.Parse("{}").RootElement.Clone(), JsonDocument.Parse("{}").RootElement.Clone(), [])], [], []) };
        return EmptyPatch() with { Changes = changes };
    }
    private sealed class EmptyCatalog : IModCatalogProvider
    { public Task<CatalogSnapshot> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new CatalogSnapshot([], DateTimeOffset.UnixEpoch)); }
    private sealed class EmptyResolver : IModDownloadResolver
    {
        public Task<ModDownloadResolution> ResolveAsync(CatalogMod mod, CancellationToken cancellationToken) => Task.FromResult(new ModDownloadResolution([]));
        public Task<Uri> GetFileUriAsync(ModDownloadFile file, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
