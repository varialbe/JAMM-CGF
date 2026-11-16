using Jamm.Core.Browsing;
using Jamm.Core.Management;
using UndertaleModLib.Models;
using UndertaleModLib;

namespace Jamm.Gui.Tests;

public sealed class Phase9bTests
{
    [Fact]
    public void DefaultsAreSimpleAndExpertPreferencePersists()
    {
        using var files = new TestFiles();
        Assert.False(files.Store.Config().AdvancedMode);
        files.Store.Set("advancedMode", "true");
        Assert.True(new ManagerStore(files.Store.Root).Config().AdvancedMode);
    }
    [Fact]
    public void NewestUniqueReleaseIsChosenAndAmbiguityRequiresChoice()
    {
        var old = new ModDownloadFile("gamejolt", "1", "11", "1", "data.win", "Main", null, "https://example.com");
        var newest = old with { Id = "2", ReleaseId = "12" };
        Assert.Equal(newest, ModFileSelection.Choose([newest, old]));
        Assert.Null(ModFileSelection.Choose([newest, newest with { Id = "3" }, old]));
        Assert.Null(ModFileSelection.Choose([]));
    }
    [Fact]
    public void FactsProveFishThroughParentChainAndCacheByBothHashes()
    {
        using var files = new TestFiles(customize: data => data.GameObjects.Add(new() { Name = data.Strings.MakeString("Fish") }));
        string mod = files.Mod("Content", "x = 1;\n", data =>
        {
            var fish = new UndertaleGameObject { Name = data.Strings.MakeString("Fish") };
            var intermediate = new UndertaleGameObject { Name = data.Strings.MakeString("Intermediate"), ParentId = fish };
            data.GameObjects.Add(fish); data.GameObjects.Add(intermediate);
            data.GameObjects.Add(new() { Name = data.Strings.MakeString("NewSpecies"), ParentId = intermediate });
            data.GameObjects.Add(new() { Name = data.Strings.MakeString("FishPretender") });
        });
        var analyzer = new ModFactAnalyzer(files.Store);
        var result = analyzer.Analyze(mod, default);
        Assert.Contains("2 fish species", result.Adds);
        Assert.Contains("3 game objects", result.Adds);
        Assert.Equal("Compatible", result.Compatibility);
        Assert.Equal(result.FileHash, analyzer.Analyze(mod, default).FileHash);
        Assert.Single(Directory.GetFiles(Path.Combine(files.Store.Root, "analysis"), "*.json"));
        string other = files.Mod("OtherBase", "x = 4;\n"); files.Store.Set("base", other);
        Assert.NotEqual(result.BaseHash, analyzer.Analyze(mod, default).BaseHash);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(files.Store.Root, "analysis"), "*.json").Length);
    }
    [Fact]
    public void LegacyTimestampDifferenceIsCompatibleUnknownBuildIsUnsureAndYycIsIncompatible()
    {
        using var files = new TestFiles();
        string older = files.Mod("Older", "x = 1;\n", d => d.GeneralInfo.Timestamp--);
        var result = new ModFactAnalyzer(files.Store).Analyze(older, default);
        Assert.Equal("Compatible", result.Compatibility);
        Assert.Contains("2023-08-16", result.Reason);
        string unknown = files.Mod("Unknown", "x = 1;\n", d => d.GeneralInfo.Timestamp = 1730000000);
        Assert.Equal("Unsure", new ModFactAnalyzer(files.Store).Analyze(unknown, default).Compatibility);
        string yyc = files.Mod("Yyc", "x = 1;\n", d => d.Code.Clear());
        Assert.Equal("Incompatible", new ModFactAnalyzer(files.Store).Analyze(yyc, default).Compatibility);
    }
    [Fact]
    public async Task TogglesCreateDefaultProfileAndChangedSelectionRequiresApply()
    {
        using var files = new TestFiles();
        var vm = new Jamm.Gui.ViewModels.MainViewModel(files.Store);
        await vm.InitializeAsync(); await vm.AddAsync([files.Mod("Toggle", "x = 1;\n")]);
        string id = Assert.Single(vm.Mods).Id;
        await vm.ToggleModAsync(id);
        Assert.Null(vm.SelectedProfile);
        Assert.True(Assert.Single(vm.Order).IsPending);
        Assert.Empty(new ProfileLibrary(files.Store).List());
        var installer = new ProfileInstaller(files.Store);
        Assert.False(new ManagerOperations(files.Store).Status().CanRestore);
        vm.UndoChanges(); Assert.False(Assert.Single(vm.Order).Enabled);
        await vm.ToggleModAsync(id); Assert.True(Assert.Single(vm.Order).Enabled);
        await vm.ToggleModAsync(id); Assert.False(Assert.Single(vm.Order).IsPending);
        vm.Browse.Dispose();
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void SteamLayoutsAndVdfLibrariesFindCommonAndDepotArchives(int layout)
    {
        using var files = new TestFiles();
        var roots = SteamGameDetection.Roots(files.Root, Path.Combine(files.Root, "Programs"), Path.Combine(files.Root, "Programs86"));
        Assert.Equal(9, roots.Length);
        string root = roots[layout];
        string common = Path.Combine(root, "steamapps", "common", "Cat Goes Fishing", "data.win");
        Directory.CreateDirectory(Path.GetDirectoryName(common)!); File.Copy(files.Original, common);
        string library = Path.Combine(files.Root, "Second Library");
        string depot = Path.Combine(library, "steamapps", "content", "app_343780", "depot_343782", "data.win");
        Directory.CreateDirectory(Path.GetDirectoryName(depot)!); File.Copy(files.Original, depot);
        File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }");
        var found = SteamGameDetection.Candidates([root]); Assert.Contains(common, found); Assert.Contains(depot, found); Assert.Equal(2, found.Length);
        Assert.True(SteamGameDetection.Inspect(common).Usable);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(depot)!, "Cat Goes Fishing.exe"), "fake");
        SteamGameDetection.Use(files.Store, SteamGameDetection.Inspect(depot), default);
        Assert.NotEqual(depot, files.Store.Config().Base); Assert.True(File.Exists(depot));
        Assert.True(File.Exists(Path.Combine(files.Store.Config().GameDir!, "data.win")));
    }
    [Fact]
    public void SteamCandidatesFindsSteamrt64AndUbuntu12_32DepotLayoutsAcrossAllRoots()
    {
        using var files = new TestFiles();
        var roots = SteamGameDetection.Roots(files.Root, Path.Combine(files.Root, "Programs"), Path.Combine(files.Root, "Programs86"));
        Assert.Contains(Path.Combine(files.Root, ".steam", "debian-installation"), roots);
        Assert.Contains(Path.Combine(files.Root, ".local", "share", "Steam"), roots);
        Assert.Contains(Path.Combine(files.Root, ".steam", "steam"), roots);
        Assert.Contains(Path.Combine(files.Root, ".steam", "root"), roots);
        Assert.Contains(Path.Combine(files.Root, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"), roots);
        Assert.Contains(Path.Combine(files.Root, "snap", "steam", "common", ".local", "share", "Steam"), roots);
        foreach (string root in roots)
        {
            string steamrt64 = Path.Combine(root, "steamrt64", "steamapps", "content", "app_343780", "depot_343782", "data.win");
            string ubuntu12 = Path.Combine(root, "ubuntu12_32", "steamapps", "content", "app_343780", "depot_343782", "data.win");
            string direct = Path.Combine(root, "steamapps", "content", "app_343780", "depot_343782", "data.win");
            foreach (string path in new[] { steamrt64, ubuntu12, direct })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(files.Original, path, overwrite: true);
            }
            var found = SteamGameDetection.Candidates([root, root]);
            Assert.Equal(new[] { direct, steamrt64, ubuntu12 }.Order(StringComparer.Ordinal), found);
        }
        string flatpakRoot = Path.Combine(files.Root, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        string snapRoot = Path.Combine(files.Root, "snap", "steam", "common", ".local", "share", "Steam");
        string aptRoot = Path.Combine(files.Root, ".steam", "debian-installation");
        Assert.Equal("flatpak", SteamGameDetection.LinuxConsoleCommand(files.Root, [flatpakRoot, aptRoot]).FileName);
        Assert.Equal("snap", SteamGameDetection.LinuxConsoleCommand(files.Root, [snapRoot, aptRoot]).FileName);
        Assert.Equal("steam", SteamGameDetection.LinuxConsoleCommand(files.Root, [aptRoot]).FileName);
        Assert.Contains("install.sh --get-game", Jamm.Gui.ViewModels.MainViewModel.MissingLegacyMessage(true));
        Assert.Contains("-console", Jamm.Gui.ViewModels.MainViewModel.MissingLegacyMessage(true));
        Assert.Contains("Win+R", Jamm.Gui.ViewModels.MainViewModel.MissingLegacyMessage(false));
    }
}
