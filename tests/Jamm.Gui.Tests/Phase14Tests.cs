using System.Net;
using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class Phase14Tests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }
    private sealed class Catalog : IModCatalogProvider
    {
        public Task<CatalogSnapshot> LoadAsync(CancellationToken token) => Task.FromResult(new CatalogSnapshot(
            [new("42", "Catalog mod", "Author", "", "https://example.com/mod.win", false, [], null, null)], DateTimeOffset.UtcNow));
    }
    private sealed class Resolver : IModDownloadResolver
    {
        public Task<ModDownloadResolution> ResolveAsync(CatalogMod mod, CancellationToken token) => Task.FromResult(new ModDownloadResolution(
            [new("https", "file-42", null, null, "mod.win", "Main", null, "https://example.com/mod.win")]));
        public Task<Uri> GetFileUriAsync(ModDownloadFile file, CancellationToken token) => throw new Exception("Automatic status must never download archives");
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AutomaticFactsMatchHashOrProvenanceAndRespectOptOut(bool provenance, bool shared)
    {
        using var files = new TestFiles();
        files.Store.Set("useSharedCompatibilityResults", shared.ToString());
        var mod = new ModLibrary(files.Store).Add(files.Mod("Colour Expansion", "x = 2;\n"), origin: provenance
            ? new("litterbox", "42", "https://example.com", "https", "file-42", null, null) : null).Mod;
        string hash = BuildFiles.Hash(files.Original); int requests = 0;
        var index = new SharedIndex(1, hash, ModFactAnalyzer.Version, [new("https", "file-42", "Server mod", null, null, null, null,
            DateTimeOffset.UtcNow, new(provenance ? new string('a', 64) : mod.Sha256, hash, ModFactAnalyzer.Version, "Compatible", "Automatic marker", ["42 sprites"]))]);
        var vm = new MainViewModel(files.Store, new ModHttp(new Handler(_ => { requests++; return new(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(index)) }; })), new Catalog(), new Resolver());
        await vm.InitializeAsync();
        Assert.Equal(shared, Assert.Single(vm.Mods).Summary.Contains("Automatic marker"));
        await vm.Browse.RefreshAsync();
        Assert.Equal(shared, Assert.Single(vm.Browse.Mods).Adds.Contains("42 sprites"));
        Assert.Equal(shared, requests > 0);
        Assert.False(Directory.Exists(Path.Combine(files.Store.Root, "analysis")));
        vm.Browse.Dispose();
    }
    [Fact]
    public async Task PendingUndoAndPlayApplyThroughGuardedInstallation()
    {
        using var files = new TestFiles(); files.Store.Set("useSharedCompatibilityResults", "false");
        var mod = new ModLibrary(files.Store).Add(files.Mod("Toggle", "x = 1;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.CreateEmpty("test"); profiles.Use("test");
        string game = Path.Combine(files.Store.Config().GameDir!, "data.win"), before = BuildFiles.Hash(game);
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync();
        Assert.Equal("Ready to play", vm.StatusTitle);
        await vm.ToggleModAsync(mod.Id);
        Assert.Equal("1 change waiting", vm.StatusTitle); Assert.True(vm.CanUndo);
        Assert.Empty(profiles.Get("test").EnabledMods); Assert.Equal(before, BuildFiles.Hash(game));
        vm.UndoChanges(); Assert.False(Assert.Single(vm.Mods).Enabled); Assert.False(vm.CanUndo);
        await vm.ToggleModAsync(mod.Id); await vm.ToggleModAsync(mod.Id); Assert.Equal(0, vm.PendingCount);
        await vm.ToggleModAsync(mod.Id);
        await vm.PlayAsync(_ => throw new Exception("Invalid runner must stop before confirmation"));
        Assert.Equal(1, vm.PendingCount); Assert.Empty(profiles.Get("test").EnabledMods);
        Assert.Equal(before, BuildFiles.Hash(game));
        // Use a harmless runner; all archive build/install checks still execute normally.
        File.WriteAllText(Path.Combine(files.Store.Config().GameDir!, "Cat Goes Fishing.exe"), "test runner placeholder");
        files.Store.Set("wine.mode", "wine"); files.Store.Set("wine.binary", "/bin/true");
        await vm.PlayAsync(_ => Task.FromResult(true));
        Assert.Contains(mod.Id, profiles.Get("test").EnabledMods);
        Assert.True(new ProfileInstaller(files.Store).IsInstalled("test")); Assert.Equal(0, vm.PendingCount);
        Assert.Contains("Started", vm.Status);
        await vm.ToggleModAsync(mod.Id); Assert.True(vm.CanPlay);
        await vm.PlayAsync(_ => Task.FromResult(true));
        Assert.Empty(profiles.Get("test").EnabledMods); Assert.Equal(before, BuildFiles.Hash(game));
        Assert.Equal(0, vm.PendingCount);
        vm.Browse.Dispose();
    }
    [Fact]
    public async Task CachedFactsRemainAvailableWithSharedLookupsDisabled()
    {
        using var files = new TestFiles(); files.Store.Set("useSharedCompatibilityResults", "false");
        string source = files.Mod("Cached", "x = 1;\n");
        new ModLibrary(files.Store).Add(source);
        var facts = new ModFactAnalyzer(files.Store).Analyze(source, default);
        var vm = new MainViewModel(files.Store, new ModHttp(new Handler(_ => throw new Exception("Opt-out must prevent requests"))));
        await vm.InitializeAsync();
        Assert.Contains(facts.Reason, Assert.Single(vm.Mods).Summary);
        await vm.RunAsync("Installing", (_, _) =>
        {
            Assert.Equal("Installing…", vm.StatusTitle);
            Assert.Contains("Please wait", vm.StatusExplanation);
            return Task.CompletedTask;
        });
        Assert.Equal("Ready to play", vm.StatusTitle); vm.Browse.Dispose();
    }
    [Fact]
    public void IconsResolveCatalogKnownLocalAndFallbackWithoutPathTraversal()
    {
        using var files = new TestFiles(false);
        Assert.Null(ModIconResolver.Resolve("42", "Unknown", files.Root));
        foreach (string name in new[] { "default", "litterbox-42", "local-abyss", "local-hdlite" }) File.WriteAllText(Path.Combine(files.Root, name + ".svg"), "test");
        Assert.EndsWith("litterbox-42.svg", ModIconResolver.Resolve("42", "Abyss", files.Root));
        Assert.EndsWith("local-abyss.svg", ModIconResolver.Resolve(null, "The Abyss", files.Root));
        Assert.EndsWith("local-hdlite.svg", ModIconResolver.Resolve(null, "HD Lite", files.Root));
        Assert.EndsWith("default.svg", ModIconResolver.Resolve("../../bad", "Unknown", files.Root));
        Assert.EndsWith("default.svg", ModIconResolver.Resolve("99", "Unknown", files.Root));
    }
}
