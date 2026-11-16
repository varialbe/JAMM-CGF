using System.Net;
using Jamm.Core.Browsing;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class Phase9cTests
{
    private static string Game(TestFiles files, string relative, bool yyc = false)
    {
        string folder = Path.Combine(files.Root, relative);
        Directory.CreateDirectory(folder);
        File.Copy(files.Mod(Guid.NewGuid().ToString("N"), "x = 0;\n", yyc ? d => d.Code.Clear() : null), Path.Combine(folder, "data.win"));
        File.WriteAllText(Path.Combine(folder, "Cat Goes Fishing.exe"), "fake");
        return folder;
    }

    [Fact]
    public void ScanFindsMovedDepotAndClassifiesCurrentBuildWithoutWriting()
    {
        using var files = new TestFiles(false);
        string legacy = Game(files, "run/media/player/Festplatte 2/Games/CatGoesFishing-legacy/2023-08-16_manifest-9039707068725406600");
        string current = Game(files, "run/media/player/Festplatte 2/Steam/steamapps/common/Cat Goes Fishing", true);
        string hash = BuildFiles.Hash(Path.Combine(legacy, "data.win"));
        string hidden = Game(files, "run/media/player/Festplatte 2/.cache/hidden");
        if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(Path.Combine(files.Root, "loop"), files.Root);
        var found = GameFolderSearch.Scan([files.Root]);
        Assert.Equal(2, found.Length);
        Assert.True(found.Single(g => g.Archive.StartsWith(legacy, StringComparison.Ordinal)).Usable);
        Assert.False(found.Single(g => g.Archive.StartsWith(current, StringComparison.Ordinal)).Usable);
        Assert.DoesNotContain(found, g => g.Archive.StartsWith(hidden, StringComparison.Ordinal));
        Assert.Equal(hash, BuildFiles.Hash(Path.Combine(legacy, "data.win")));
        Assert.Empty(GameFolderSearch.Scan([files.Root], maxDepth: 1));
        Assert.Empty(GameFolderSearch.Scan([files.Root], limit: TimeSpan.Zero));
        Assert.Empty(GameFolderSearch.Scan([files.Root], maxDirectories: 0));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => GameFolderSearch.Scan([files.Root], cancel.Token));
    }

    [Fact]
    public async Task AddWaitsForSetupThenResumesAndCancellationDiscardsAction()
    {
        using var files = new TestFiles(false);
        string folder = Game(files, "Games/legacy");
        string original = Path.Combine(folder, "data.win"), hash = BuildFiles.Hash(original);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => [SteamGameDetection.Inspect(original)]);
        await vm.InitializeAsync();
        string mod = files.Mod("Local", "x = 1;\n");
        await vm.AddAsync([mod]); Assert.True(vm.SetupOpen); Assert.Empty(vm.Mods);
        vm.CancelSetup(); vm.DetectedLegacy = vm.DetectedGames.Single(); await vm.UseDetectedGameAsync(); Assert.Empty(vm.Mods);
        Assert.Equal(folder + "-modded", files.Store.Config().GameDir);
        Assert.Equal("default", vm.SelectedProfile);
        Assert.Equal(hash, BuildFiles.Hash(original));
        await vm.AddAsync([mod]); Assert.Single(vm.Mods);
        vm.Browse.Dispose();
    }

    [Fact]
    public async Task DeferredAddResumesAfterManualFolderSelectionAndRejectsYyc()
    {
        using var files = new TestFiles(false);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        List<string> errors = []; vm.Error += errors.Add;
        await vm.AddAsync([files.Mod("Local", "x = 1;\n")]);
        await vm.ChooseGameFolderAsync(Game(files, "current", true));
        Assert.False(vm.HasDetectedLegacy); Assert.Contains("legacy", Assert.Single(errors));
        await vm.UseDetectedGameAsync(); Assert.Empty(vm.Mods);
        await vm.ChooseGameFolderAsync(Game(files, "legacy"));
        await vm.UseDetectedGameAsync(); Assert.Single(vm.Mods); Assert.False(vm.NeedsSetup);
        vm.Browse.Dispose();
    }

    [Fact]
    public async Task InvalidConfiguredBaseOpensSetupAndPreferredManifestIsSelected()
    {
        using var files = new TestFiles();
        files.Store.Set("base", files.Mod("CurrentBase", "x = 0;\n", d => d.Code.Clear()));
        string other = Game(files, "other-legacy");
        string preferred = Game(files, "2023-08-16_manifest-9039707068725406600");
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => [SteamGameDetection.Inspect(Path.Combine(other, "data.win")), SteamGameDetection.Inspect(Path.Combine(preferred, "data.win"))]);
        await vm.InitializeAsync(); Assert.True(vm.NeedsSetup); Assert.Null(vm.DetectedLegacy);
        vm.DetectedLegacy = vm.DetectedGames.Single(g => g.Archive.StartsWith(preferred, StringComparison.Ordinal));
        await vm.AddAsync([files.Mod("Local", "x = 1;\n")]); Assert.Empty(vm.Mods);
        await vm.UseDetectedGameAsync(); Assert.Single(vm.Mods); Assert.False(vm.NeedsSetup);
        vm.Browse.Dispose();
    }

    [Fact]
    public void SetupRefusesOriginalAndLinkedNestedDestinationsAndExistingCopy()
    {
        using var files = new TestFiles(false);
        string folder = Game(files, "legacy"); var candidate = SteamGameDetection.Inspect(Path.Combine(folder, "data.win"));
        string hash = BuildFiles.Hash(candidate.Archive);
        Assert.Throws<IOException>(() => SteamGameDetection.Use(files.Store, candidate, default, folder));
        Assert.Throws<IOException>(() => SteamGameDetection.Use(files.Store, candidate, default, Path.Combine(folder, "nested")));
        if (!OperatingSystem.IsWindows())
        {
            string alias = Path.Combine(files.Root, "alias"); Directory.CreateSymbolicLink(alias, folder);
            Assert.Throws<IOException>(() => SteamGameDetection.Use(files.Store, candidate, default, Path.Combine(alias, "nested")));
        }
        string existing = Path.Combine(files.Root, "existing"); Directory.CreateDirectory(existing);
        Assert.Throws<IOException>(() => SteamGameDetection.Use(files.Store, candidate, default, existing));
        Assert.Equal(hash, BuildFiles.Hash(candidate.Archive)); Assert.Null(files.Store.Config().Base);
    }

    private sealed class Catalog : IModCatalogProvider
    {
        public Task<CatalogSnapshot> LoadAsync(CancellationToken token) => Task.FromResult(new CatalogSnapshot([new("40", "Example", "Author", "Description", "https://example.com/data.win", false, [], DateTimeOffset.UtcNow, 1)], DateTimeOffset.UtcNow));
    }
    private sealed class Resolver : IModDownloadResolver
    {
        public Task<ModDownloadResolution> ResolveAsync(CatalogMod mod, CancellationToken token) => Task.FromResult(new ModDownloadResolution([new("https", "file", "release", "1", "data.win", "Main", null, mod.FileUrl!)]));
        public Task<Uri> GetFileUriAsync(ModDownloadFile file, CancellationToken token) => Task.FromResult(new Uri(file.SourceUrl));
    }
    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    [Fact]
    public async Task DownloadWaitsForSetupThenImportsAndEnablesRequestedFile()
    {
        using var files = new TestFiles(false);
        string folder = Game(files, "legacy");
        using var http = new ModHttp(new Handler(File.ReadAllBytes(files.Mod("Online", "x = 1;\n"))));
        var vm = new MainViewModel(files.Store, http, new Catalog(), new Resolver(), (_, _) => [SteamGameDetection.Inspect(Path.Combine(folder, "data.win"))]);
        await vm.InitializeAsync(); await vm.Browse.RefreshAsync(); vm.Browse.Selected = vm.Browse.Mods[0]; await vm.Browse.SelectAsync();
        Assert.True(vm.Browse.CanDownload);
        await vm.Browse.DownloadAsync(false, _ => throw new Exception("No install"));
        Assert.Empty(vm.Mods); Assert.True(vm.SetupOpen);
        vm.DetectedLegacy = vm.DetectedGames.Single();
        await vm.UseDetectedGameAsync();
        Assert.Single(vm.Mods); Assert.Single(new ProfileLibrary(files.Store).Get("default").EnabledMods);
        vm.Browse.Dispose();
    }

    [Fact]
    public void RunnerSelectionPrefersFlatpakAndSupportsNativeAndWindows()
    {
        using var files = new TestFiles(false);
        string flatpak = Path.Combine(files.Root, ".var/app/com.valvesoftware.Steam/.local/share/Steam");
        string native = Path.Combine(files.Root, "Steam");
        foreach (string root in new[] { flatpak, native })
        {
            string proton = Path.Combine(root, "steamapps/common/Proton - Experimental"); Directory.CreateDirectory(proton); File.WriteAllText(Path.Combine(proton, "proton"), "fake");
        }
        var selected = GameFolderSearch.RunnerSettings(files.Root, false, [native, flatpak]);
        Assert.Equal("proton-flatpak", selected["wine.mode"]); Assert.Contains(flatpak, selected["proton.path"]);
        Assert.Equal("proton", GameFolderSearch.RunnerSettings(files.Root, false, [native])["wine.mode"]);
        string secondary = Path.Combine(files.Root, "secondary");
        string secondaryProton = Path.Combine(secondary, "steamapps", "common", "Proton 10.0");
        Directory.CreateDirectory(secondaryProton); File.WriteAllText(Path.Combine(secondaryProton, "proton"), "fake");
        Directory.Delete(Path.Combine(native, "steamapps", "common"), true);
        File.WriteAllText(Path.Combine(native, "steamapps", "libraryfolders.vdf"), "\"path\" \"" + secondary.Replace("\\", "\\\\") + "\"");
        Assert.Equal(secondaryProton, GameFolderSearch.RunnerSettings(files.Root, false, [native])["proton.path"]);
        Assert.Equal("direct", GameFolderSearch.RunnerSettings(files.Root, true, [native])["wine.mode"]);
        files.Store.Set("wine.mode", "direct"); files.Store.Set("gameDir", Game(files, "windows-game"));
        var command = GameLauncher.Create(files.Store.Config(), files.Store.Root);
        Assert.EndsWith("Cat Goes Fishing.exe", command.FileName); Assert.Empty(command.Arguments);
    }
}
