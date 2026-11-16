using System.Net;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class BrowseViewModelTests
{
    private static CatalogMod Example(bool preview = false) => new("40", "Online Example", "Author", "Fish and boats", "https://example.com/data.win", preview, ["Fish"], DateTimeOffset.UtcNow, 4);
    private sealed class Catalog(params CatalogMod[] mods) : IModCatalogProvider
    { public Task<CatalogSnapshot> LoadAsync(CancellationToken token) => Task.FromResult(new CatalogSnapshot(mods, DateTimeOffset.UtcNow)); }
    private sealed class Resolver : IModDownloadResolver
    {
        public Task<ModDownloadResolution> ResolveAsync(CatalogMod mod, CancellationToken token) => Task.FromResult(new ModDownloadResolution(mod.HasDownload ? [new("https", "file-id", "release-id", "1.0", "data.win", "Main", null, mod.FileUrl!)] : []));
        public Task<Uri> GetFileUriAsync(ModDownloadFile file, CancellationToken token) => Task.FromResult(new Uri(file.SourceUrl));
    }
    private sealed class Handler(Func<CancellationToken, Task<byte[]>> bytes) : HttpMessageHandler
    { protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(await bytes(token)) }; }
    private static MainViewModel Create(TestFiles files, Func<CancellationToken, Task<byte[]>> bytes, params CatalogMod[] mods)
    {
        // These exercise the local pipeline; shared lookups have dedicated SharedBrowseTests.
        files.Store.Set("useSharedCompatibilityResults", "false");
        return new(files.Store, new ModHttp(new Handler(bytes)), new Catalog(mods.Length == 0 ? [Example()] : mods), new Resolver());
    }
    private static async Task Select(MainViewModel vm)
    {
        await vm.InitializeAsync(); await vm.Browse.RefreshAsync(); vm.Browse.Selected = vm.Browse.Mods[0]; await vm.Browse.SelectAsync();
        Assert.Equal(vm.Browse.Files.FirstOrDefault(), vm.Browse.SelectedFile);
    }
    [Fact]
    public async Task SearchFiltersAndAutomaticSingleFileSelectionDriveButtons()
    {
        using var files = new TestFiles(); var vm = Create(files, _ => throw new Exception(), Example(), Example(true) with { Id = "41", Name = "Preview", Categories = ["Boats"] });
        await vm.InitializeAsync(); await vm.Browse.RefreshAsync();
        vm.Browse.Search = "Author"; Assert.Equal(2, vm.Browse.Mods.Count);
        vm.Browse.Category = "Boats"; Assert.Single(vm.Browse.Mods);
        vm.Browse.Selected = vm.Browse.Mods[0]; await vm.Browse.SelectAsync(); Assert.Empty(vm.Browse.Files); Assert.False(vm.Browse.CanDownload);
        vm.Browse.Category = "All categories"; vm.Browse.Release = "Released"; Assert.Single(vm.Browse.Mods);
        vm.Browse.Selected = vm.Browse.Mods[0]; await vm.Browse.SelectAsync(); Assert.True(vm.Browse.CanDownload);
        vm.Browse.SelectedFile = Assert.Single(vm.Browse.Files); Assert.True(vm.Browse.CanDownload); Assert.False(vm.Browse.CanDownloadInstall);
        vm.Browse.Search = "does not exist"; Assert.Empty(vm.Browse.Mods); Assert.Contains("No mods", vm.Browse.EmptyMessage);
        vm.Browse.Dispose();
    }
    [Fact]
    public async Task DownloadImportDeduplicatesManualBytesAndAttachesOnlyProvenOrigins()
    {
        using var files = new TestFiles(); string source = files.Mod("Manual", "x = 1;\n");
        var manual = new ModLibrary(files.Store).Add(source).Mod;
        var vm = Create(files, _ => Task.FromResult(File.ReadAllBytes(source))); await Select(vm);
        Assert.Contains("no proven local", vm.Browse.Membership);
        await vm.Browse.DownloadAsync(false, _ => throw new Exception("Add must not install"));
        Assert.Single(vm.Mods); Assert.Equal(manual.Id, vm.Mods[0].Id); Assert.Equal("Manual", vm.Mods[0].Name);
        Assert.Contains("Enabled in selected profile", vm.Browse.Membership); Assert.DoesNotContain("currently installed", vm.Browse.Membership);
        var saved = new ModLibrary(files.Store).Find(manual.Id); var origin = Assert.Single(saved.Origins);
        Assert.Equal("litterbox", origin.Catalog); Assert.Equal("40", origin.ModId); Assert.Equal("file-id", origin.FileId); Assert.Equal("release-id", origin.ReleaseId); Assert.Equal("1.0", origin.Version);
        await vm.Browse.DownloadAsync(false, _ => Task.FromResult(false)); Assert.Single(new ModLibrary(files.Store).Find(manual.Id).Origins);
        Assert.Empty(Directory.GetFiles(Path.Combine(files.Store.Root, "downloads"))); vm.Browse.Dispose();
    }
    [Fact]
    public async Task ImportEnableInstallUsesConcreteConfirmationVerifiedBuildAndBackups()
    {
        using var files = new TestFiles(); string source = files.Mod("Source", "x = 1;\n");
        var profiles = new ProfileLibrary(files.Store); profiles.CreateEmpty("online"); profiles.Use("online");
        var vm = Create(files, _ => Task.FromResult(File.ReadAllBytes(source))); await Select(vm);
        string game = Path.Combine(files.Store.Config().GameDir!, "data.win"); string before = BuildFiles.Hash(game); bool confirmed = false;
        Assert.True(vm.Browse.CanDownloadInstall);
        await vm.Browse.DownloadAsync(true, message =>
        {
            confirmed = true; Assert.Contains(game, message); Assert.Contains(".cgfmm-original", message); Assert.Contains("backups", message);
            Assert.False(vm.CanEdit); Assert.False(vm.Browse.CanDownload); return Task.FromResult(true);
        });
        Assert.True(confirmed); Assert.NotEqual(before, BuildFiles.Hash(game)); Assert.True(new ProfileInstaller(files.Store).IsInstalled("online"));
        Assert.Single(profiles.Get("online").EnabledMods); Assert.Contains("currently installed", vm.Browse.Membership);
        Assert.Empty(Directory.GetFiles(Path.Combine(files.Store.Root, "downloads")));
        await vm.RestoreAsync(); Assert.Equal(before, BuildFiles.Hash(game)); vm.Browse.Dispose();
    }
    [Fact]
    public async Task NewOverlapsRequireExistingConflictUiBeforeInstallation()
    {
        using var files = new TestFiles(); string source = files.Mod("Online", "x = 2;\n");
        var old = new ModLibrary(files.Store).Add(files.Mod("Local", "x = 1;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("combined", [old.Id]); profiles.Use("combined");
        var vm = Create(files, _ => Task.FromResult(File.ReadAllBytes(source))); await Select(vm);
        string game = Path.Combine(files.Store.Config().GameDir!, "data.win"), before = BuildFiles.Hash(game);
        await vm.Browse.DownloadAsync(true, _ => throw new Exception("Conflicts must be reviewed first"));
        Assert.Equal(before, BuildFiles.Hash(game)); Assert.Equal(2, profiles.Get("combined").EnabledMods.Length);
        Assert.Contains(vm.Conflicts, c => c.CanChoose); Assert.Contains("Review conflicts", vm.Status);
        foreach (var row in vm.Conflicts.Where(c => c.CanChoose)) await vm.ResolveAsync(row.Key, row.Choices[0]);
        await vm.InstallAsync(_ => Task.FromResult(true)); Assert.True(new ProfileInstaller(files.Store).IsInstalled()); vm.Browse.Dispose();
    }
    [Fact]
    public async Task BusyDownloadExcludesLocalImportRestoreInstallAndDuplicateActions()
    {
        using var files = new TestFiles(); string source = files.Mod("Source", "x = 1;\n");
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource(); int downloads = 0;
        bool first = true;
        var vm = Create(files, async token => { if (first) { first = false; throw new HttpRequestException("Selection check failed; retry Download.", null, HttpStatusCode.NotFound); } downloads++; started.SetResult(); await release.Task.WaitAsync(token); return File.ReadAllBytes(source); });
        await Select(vm); Task download = vm.Browse.DownloadAsync(false, _ => Task.FromResult(false)); await started.Task;
        Assert.True(vm.IsBusy); Assert.False(vm.Browse.CanDownload); Assert.False(vm.CanEdit); Assert.False(vm.CanRestore); Assert.True(vm.CanCancel);
        await vm.AddAsync([source]); await vm.RestoreAsync(); await vm.InstallAsync(_ => throw new Exception("Busy")); await vm.Browse.DownloadAsync(false, _ => Task.FromResult(false));
        Assert.Empty(vm.Mods); Assert.Equal(1, downloads);
        vm.Cancel(); release.SetResult(); await download;
        Assert.Contains("Cancelled", vm.Browse.Membership); Assert.Empty(vm.Mods); Assert.True(vm.CanEdit);
        Assert.Empty(Directory.GetFiles(Path.Combine(files.Store.Root, "downloads"))); vm.Browse.Dispose();
    }
    [Fact]
    public async Task DownloadEnablesWithoutInstallingAndRetainsSelectedReleaseBytes()
    {
        using var files = new TestFiles(); string source = files.Mod("Source", "x = 1;\n");
        var profiles = new ProfileLibrary(files.Store); profiles.CreateEmpty("online"); profiles.Use("online");
        var vm = Create(files, _ => Task.FromResult(File.ReadAllBytes(source))); await Select(vm);
        string game = Path.Combine(files.Store.Config().GameDir!, "data.win"), before = BuildFiles.Hash(game);
        await vm.Browse.DownloadAsync(false, _ => throw new Exception()); Assert.Single(profiles.Get("online").EnabledMods); Assert.Equal(before, BuildFiles.Hash(game));
        string old = profiles.Get("online").Mods[0];
        source = files.Mod("NewRelease", "x = 2;\n");
        await vm.Browse.DownloadAsync(false, _ => throw new Exception());
        Assert.Single(vm.Mods); Assert.Equal(old, Assert.Single(profiles.Get("online").Mods)); Assert.Equal(before, BuildFiles.Hash(game)); vm.Browse.Dispose();
    }
    [Fact]
    public async Task OversizedOptionalThumbnailDoesNotClaimCatalogFailed()
    {
        using var files = new TestFiles();
        var vm = Create(files, _ => Task.FromResult(new byte[300000]), Example() with { IconUrl = "https://example.com/huge.png" });
        await vm.InitializeAsync(); await vm.Browse.RefreshAsync();
        Assert.Single(vm.Browse.Mods); Assert.Contains("Last successful fetch", vm.Browse.Status);
        Assert.DoesNotContain("Could not load", vm.Browse.Status);
        Assert.Null(vm.Browse.Mods[0].Icon); vm.Browse.Dispose();
    }
    private sealed class OfflineCatalog : IModCatalogProvider
    { public Task<CatalogSnapshot> LoadAsync(CancellationToken token) => throw new HttpRequestException("Offline; check your connection and Retry."); }
    [Fact]
    public async Task CatalogFailureDoesNotBlockLocalManagementOrClaimAnInstallation()
    {
        using var files = new TestFiles();
        var vm = new MainViewModel(files.Store, catalog: new OfflineCatalog(), resolver: new Resolver());
        await vm.InitializeAsync(); await vm.Browse.RefreshAsync();
        Assert.Contains("Offline", vm.Browse.Status); Assert.Contains("Retry", vm.Browse.Status);
        Assert.False(vm.IsBusy); Assert.True(vm.CanEdit); Assert.False(vm.Browse.CanDownload);
        await vm.CreateProfileAsync("local-only"); Assert.Contains("local-only", vm.Profiles);
        vm.Browse.Dispose();
    }
    [Fact]
    public async Task FailedDownloadSurfacesErrorAndLeavesNoLibraryEntry()
    {
        using var files = new TestFiles(); var vm = Create(files, _ => Task.FromResult("<html>server error</html>"u8.ToArray())); await Select(vm);
        List<string> errors = []; vm.Error += errors.Add;
        await vm.Browse.DownloadAsync(false, _ => throw new Exception()); Assert.Empty(vm.Mods);
        Assert.Equal(vm.Status, Assert.Single(errors)); Assert.Contains("https://example.com/data.win", vm.Status); Assert.Contains("retry Download", vm.Status);
        Assert.Contains("Failed", vm.Browse.Membership); Assert.Contains("HTML", vm.Status); Assert.True(vm.Browse.CanDownload);
        Assert.Empty(Directory.GetFiles(Path.Combine(files.Store.Root, "downloads"))); vm.Browse.Dispose();
    }
}
