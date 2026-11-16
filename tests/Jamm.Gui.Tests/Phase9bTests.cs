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
        Assert.Equal("default", vm.SelectedProfile);
        Assert.Contains(id, new ProfileLibrary(files.Store).Get("default").EnabledMods);
        var installer = new ProfileInstaller(files.Store);
        Assert.False(installer.IsInstalled());
        await vm.InstallAsync(_ => Task.FromResult(true)); Assert.True(installer.IsInstalled());
        await vm.InstallAsync(_ => throw new Exception("Unchanged selection must not prepare/confirm again")); Assert.True(installer.IsInstalled());
        await vm.ToggleModAsync(id); Assert.Empty(new ProfileLibrary(files.Store).Get("default").EnabledMods);
        Assert.False(installer.IsInstalled()); Assert.True(vm.CanPlay);
        Assert.Equal("Enable", Assert.Single(vm.Order).ToggleLabel);
        await vm.ToggleModAsync(id); Assert.True(Assert.Single(vm.Order).Enabled);
        Assert.Equal("Disable", Assert.Single(vm.Order).ToggleLabel);
        vm.Browse.Dispose();
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void SteamLayoutsAndVdfLibrariesFindCommonAndDepotArchives(int layout)
    {
        using var files = new TestFiles();
        string root = SteamGameDetection.Roots(files.Root, Path.Combine(files.Root, "Programs"), Path.Combine(files.Root, "Programs86"))[layout];
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
}
