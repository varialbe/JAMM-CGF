using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;
using UndertaleModLib;

namespace Jamm.Gui.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public async Task AddRemoveReorderSwitchAndDisabledModsPersist()
    {
        using var files = new TestFiles(); var vm = new MainViewModel(files.Store);
        await vm.InitializeAsync(); Assert.False(vm.CanInstall);
        await vm.AddAsync([files.Mod("Example Mod A", "x = 1;\n"), Path.Combine(files.Root, "missing.win"), files.Mod("Example Mod B", "x = 2;\n")]);
        Assert.Equal(2, vm.Mods.Count);
        await vm.CreateProfileAsync("first");
        // Persist enablement explicitly; the property handler exercises the first checkbox.
        vm.Order[0].Enabled = true;
        while (vm.IsBusy) await Task.Delay(10);
        vm.Order[1].Enabled = true;
        while (vm.IsBusy) await Task.Delay(10);
        Assert.True(vm.CanInstall);
        string firstId = vm.Order[0].Id;
        await vm.MoveAsync(0, 1);
        Assert.Equal(firstId, new ProfileLibrary(files.Store).Get("first").Mods[1]);
        await vm.CreateProfileAsync("second"); Assert.All(vm.Order, row => Assert.False(row.Enabled));
        await vm.SelectProfileAsync("first"); Assert.All(vm.Order, row => Assert.True(row.Enabled));
        await vm.RenameProfileAsync("renamed"); Assert.Equal("renamed", vm.SelectedProfile);
        await vm.RemoveAsync(firstId); Assert.Contains("profile", vm.Log, StringComparison.OrdinalIgnoreCase);
        await vm.DeleteProfileAsync(); await vm.SelectProfileAsync("second"); await vm.DeleteProfileAsync();
        await vm.RemoveAsync(firstId); Assert.Single(vm.Mods);
    }

    [Fact]
    public async Task CheckAndResolvePersistAcrossNewViewModels()
    {
        using var files = new TestFiles(); var library = new ModLibrary(files.Store);
        var a = library.Add(files.Mod("Example Mod A", "x = 1;\n")).Mod;
        var b = library.Add(files.Mod("Example Mod B", "x = 2;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("test", [a.Id, b.Id]); profiles.Use("test");
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync(); await vm.CheckAsync();
        var row = Assert.Single(vm.Conflicts, r => r.CanChoose && r.Key.Contains('#'));
        await vm.ResolveAsync(row.Key, a.Name);
        Assert.Equal(a.Name, new ProfileLibrary(new ManagerStore(files.Store.Root)).Get("test").Resolutions[row.Key]);
        var fresh = new MainViewModel(files.Store); await fresh.InitializeAsync(); await fresh.CheckAsync();
        Assert.Equal(a.Name, fresh.Conflicts.Single(r => r.Key == row.Key).SelectedChoice);
    }

    [Theory]
    [InlineData("bare")]
    [InlineData("qualified")]
    [InlineData("hunk-over-qualified")]
    public async Task HunkControlsDisplayEffectiveCoreResolutionPrecedence(string level)
    {
        using var files = new TestFiles(); var library = new ModLibrary(files.Store);
        var a = library.Add(files.Mod("Example Mod A", "x = 1;\n")).Mod;
        var b = library.Add(files.Mod("Example Mod B", "x = 2;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("test", [a.Id, b.Id]); profiles.Use("test");
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync(); await vm.CheckAsync();
        string key = Assert.Single(vm.Conflicts, r => r.CanChoose && r.Key.Contains('#')).Key;
        string entry = key.Split('#')[0], bare = entry.Split('/')[1];
        profiles.Resolve("test", level == "bare" ? bare : entry, level == "hunk-over-qualified" ? b.Name : a.Name);
        if (level == "hunk-over-qualified") profiles.Resolve("test", key, a.Name);
        string profilePath = Path.Combine(files.Store.Root, "profiles", "test.json");
        string saved = File.ReadAllText(profilePath);
        await vm.CheckAsync();
        Assert.Equal(a.Name, vm.Conflicts.Single(r => r.Key == key).SelectedChoice);
        Assert.Equal(saved, File.ReadAllText(profilePath));
    }

    [Fact]
    public async Task LiteralSlotFindingsOfferGuidanceWithoutUnsupportedWinnerControls()
    {
        using var files = new TestFiles(); var library = new ModLibrary(files.Store);
        var a = library.Add(files.Mod("Example Mod A", "catalog[42] = 1;\n")).Mod;
        var b = library.Add(files.Mod("Example Mod B", "catalog[42] = 2;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("test", [a.Id, b.Id]); profiles.Use("test");
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync(); await vm.CheckAsync();
        var row = Assert.Single(vm.Conflicts, r => r.Kind == "index");
        Assert.Equal("code/catalog[42]", row.Key);
        Assert.False(row.CanChoose); Assert.Null(row.SelectedChoice);
        Assert.Contains("disable a conflicting mod", row.Explanation); Assert.Contains("example_code", row.Details);
        Assert.Contains(vm.Conflicts, r => r.CanChoose && r.Key.StartsWith("code/example_code#", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedChoicePersistenceRestoresTheSavedSelection(bool hasSavedChoice)
    {
        using var files = new TestFiles(); var library = new ModLibrary(files.Store);
        var a = library.Add(files.Mod("Example Mod A", "x = 1;\n")).Mod;
        var b = library.Add(files.Mod("Example Mod B", "x = 2;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("test", [a.Id, b.Id]); profiles.Use("test");
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync(); await vm.CheckAsync();
        var row = Assert.Single(vm.Conflicts, r => r.CanChoose && r.Key.Contains('#'));
        if (hasSavedChoice) await vm.ResolveAsync(row.Key, a.Name);
        string? expected = hasSavedChoice ? a.Name : null;
        Assert.Equal(expected, row.SelectedChoice);
        string path = Path.Combine(files.Store.Root, "profiles", "test.json"), saved = File.ReadAllText(path);
        // Simulate an external profile deletion between Check and the dropdown change.
        File.Delete(path);
        row.SelectedChoice = b.Name;
        while (vm.IsBusy) await Task.Delay(10);
        Assert.Equal(expected, row.SelectedChoice);
        Assert.Contains("Could not complete saving conflict choice", vm.Status);
        Assert.False(File.Exists(path));
        File.WriteAllText(path, saved);
        Assert.Equal(expected, profiles.Get("test").Resolutions.GetValueOrDefault(row.Key));
    }

    [Fact]
    public async Task AssetWideChoicesStaySynchronizedAcrossPairwiseFieldConflicts()
    {
        static void Object(UndertaleModLib.UndertaleData data, int depth) => data.GameObjects.Add(new()
        { Name = data.Strings.MakeString("example_object"), Depth = depth });
        using var files = new TestFiles(customize: d => Object(d, 0)); var library = new ModLibrary(files.Store);
        var a = library.Add(files.Mod("Example Mod A", "x = 0;\n", d => Object(d, 1))).Mod;
        var b = library.Add(files.Mod("Example Mod B", "x = 0;\n", d => Object(d, 2))).Mod;
        var c = library.Add(files.Mod("Example Mod C", "x = 0;\n", d => Object(d, 3))).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("test", [a.Id, b.Id, c.Id]); profiles.Use("test");
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync(); await vm.CheckAsync();
        var rows = vm.Conflicts.Where(r => r.CanChoose && r.Key == "objects/example_object").ToArray();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, r => { Assert.Equal(3, r.Choices.Length); Assert.Contains("all conflicts for this asset", r.Explanation); });
        rows[0].SelectedChoice = a.Name;
        while (vm.IsBusy) await Task.Delay(10);
        Assert.All(rows, r => Assert.Equal(a.Name, r.SelectedChoice));
        rows[1].SelectedChoice = c.Name;
        while (vm.IsBusy) await Task.Delay(10);
        Assert.All(rows, r => Assert.Equal(c.Name, r.SelectedChoice));
        Assert.Equal(c.Name, profiles.Get("test").Resolutions["objects/example_object"]);
        await vm.CheckAsync();
        Assert.All(vm.Conflicts.Where(r => r.CanChoose), r => Assert.Equal(c.Name, r.SelectedChoice));
    }

    [Fact]
    public async Task BusyDisablesWritesAndCancellationSurfacesWithoutStackTrace()
    {
        using var files = new TestFiles(false); var vm = new MainViewModel(files.Store);
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        Task operation = vm.RunAsync("Long test", async (token, _) => { started.SetResult(); await release.Task; token.ThrowIfCancellationRequested(); });
        await started.Task;
        Assert.True(vm.IsBusy); Assert.False(vm.CanEdit); Assert.False(vm.CanInstall); Assert.False(vm.CanRestore); Assert.True(vm.CanCancel);
        vm.Cancel(); release.SetResult(); await operation;
        Assert.False(vm.IsBusy); Assert.True(vm.CanEdit); Assert.Contains("Cancelled", vm.Log);
        await vm.AddAsync([Path.Combine(files.Root, "missing.win")]);
        Assert.Contains("Could not complete", vm.Status); Assert.DoesNotContain(" at Jamm.", vm.Log);
    }

    [Fact]
    public async Task EmptyCodeArchiveFailsLegacyValidationWithoutChangingSettings()
    {
        using var files = new TestFiles();
        string unsupported = Path.Combine(files.Root, "current-build.win");
        using (var data = UndertaleModLib.UndertaleData.CreateNew())
        using (var stream = File.Create(unsupported)) UndertaleModLib.UndertaleIO.Write(stream, data);
        var vm = new MainViewModel(files.Store);
        await vm.ValidateBaseAsync(unsupported);
        Assert.Contains("not the supported legacy", vm.Validation);
        Assert.Contains("YYC", vm.Validation);
        await vm.SaveSettingsAsync(new Dictionary<string, string> { ["base"] = unsupported });
        Assert.Equal(files.Original, files.Store.Config().Base);
        Assert.Contains("Could not complete", vm.Status);
    }

    [Fact]
    public async Task UnknownArchiveRefusesInstallAndRestoreEvenIfCalledDirectly()
    {
        using var files = new TestFiles(); var vm = new MainViewModel(files.Store);
        string current = Path.Combine(files.Store.Config().GameDir!, "data.win");
        File.AppendAllText(current, "unknown"); string before = BuildFiles.Hash(current);
        await vm.InitializeAsync(); Assert.False(vm.CanInstall); Assert.False(vm.CanRestore);
        bool confirmed = false;
        await vm.InstallAsync(_ => { confirmed = true; return Task.FromResult(true); });
        await vm.RestoreAsync();
        Assert.False(confirmed); Assert.Equal(before, BuildFiles.Hash(current)); Assert.Contains("Unknown game data", vm.Log);
    }

    [Fact]
    public async Task CancellationDuringConcreteConfirmationDiscardsVerifiedBuildWithoutReplacingGame()
    {
        using var files = new TestFiles();
        var mod = new ModLibrary(files.Store).Add(files.Mod("Example Mod A", "x = 1;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("cancel", [mod.Id]); profiles.Use("cancel");
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync();
        string current = Path.Combine(files.Store.Config().GameDir!, "data.win"), hash = BuildFiles.Hash(files.Original);
        await vm.InstallAsync(_ => { Assert.True(vm.CanCancel); vm.Cancel(); return Task.FromResult(true); });
        Assert.Equal(hash, BuildFiles.Hash(current)); Assert.Contains("Cancelled", vm.Log);
        Assert.Empty(Directory.GetFiles(Path.Combine(files.Store.Root, "builds")));
        Assert.False(File.Exists(current + ".cgfmm-original"));
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("file-link")]
    [InlineData("base-directory-link")]
    [InlineData("game-directory-link")]
    public async Task DefaultBaseInsideGameRetainsVerifiedAncestorForFurtherInstallsAndRestore(string alias)
    {
        using var files = new TestFiles();
        string current = Path.Combine(files.Store.Config().GameDir!, "data.win");
        string basePath = current;
        if (alias == "file-link")
        {
            basePath = Path.Combine(files.Root, "base-link.win"); File.CreateSymbolicLink(basePath, current);
        }
        else if (alias is "base-directory-link" or "game-directory-link")
        {
            string directoryLink = Path.Combine(files.Root, "game-link");
            Directory.CreateSymbolicLink(directoryLink, files.Store.Config().GameDir!);
            if (alias == "base-directory-link") basePath = Path.Combine(directoryLink, "data.win");
            else files.Store.Set("gameDir", directoryLink);
        }
        files.Store.Set("base", basePath);
        var mod = new ModLibrary(files.Store).Add(files.Mod("Example Mod A", "x = 1;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("default-base", [mod.Id]); profiles.Use("default-base");
        string hash = BuildFiles.Hash(current);
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync();
        await vm.InstallAsync(_ => Task.FromResult(true));
        Assert.Contains("Installed and verified", vm.Log);
        Assert.NotEqual(current, files.Store.Config().Base);
        Assert.Equal(hash, BuildFiles.Hash(files.Store.Config().Base!));
        Assert.True(vm.CanRestore); Assert.True(vm.CanInstall);
        Assert.True(new ProfileInstaller(files.Store).IsInstalled());
        await vm.InstallAsync(_ => throw new Exception("Already installed: should not prompt."));
        Assert.Contains("already installed", vm.Status);
        await vm.RestoreAsync(); Assert.Equal(hash, BuildFiles.Hash(current));
    }

    [Fact]
    public void SettingsFormsSaveAtomicallyAndClearOptionalPaths()
    {
        using var files = new TestFiles();
        files.Store.Set("wine.prefix", Path.Combine(files.Root, "prefix"));
        Assert.Throws<ArgumentException>(() => files.Store.SetMany(new Dictionary<string, string> { ["wine.prefix"] = "", ["wine.mode"] = "invalid" }));
        Assert.NotNull(files.Store.Config().Wine.Prefix);
        files.Store.SetMany(new Dictionary<string, string> { ["wine.prefix"] = "" });
        Assert.Null(files.Store.Config().Wine.Prefix);
    }

    [Fact]
    public async Task DeclineInstallDiscardsBuildThenInstallAndRestoreUseVerifiedBackups()
    {
        using var files = new TestFiles(); var library = new ModLibrary(files.Store);
        var a = library.Add(files.Mod("Example Mod A", "x = 1;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("test", [a.Id]); profiles.Use("test");
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync();
        string current = Path.Combine(files.Store.Config().GameDir!, "data.win"); string hash = BuildFiles.Hash(current);
        await vm.InstallAsync(message => { Assert.Contains(current, message); Assert.Contains(".cgfmm-original", message); return Task.FromResult(false); });
        Assert.Equal(hash, BuildFiles.Hash(current)); Assert.Empty(Directory.GetFiles(Path.Combine(files.Store.Root, "builds")));
        await vm.InstallAsync(_ => Task.FromResult(true));
        Assert.NotEqual(hash, BuildFiles.Hash(current)); Assert.True(vm.CanRestore); Assert.Contains("Installed and verified", vm.Log);
        await vm.CreateProfileAsync("another"); Assert.Contains("profile test", vm.GameStatus);
        await vm.RestoreAsync(); Assert.Equal(hash, BuildFiles.Hash(current)); Assert.Equal(hash, BuildFiles.Hash(current + ".cgfmm-original"));
    }
}
