using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Jamm.Core.Management;
using Jamm.Core.Browsing;
using Jamm.Gui.ViewModels;

[assembly: AvaloniaTestApplication(typeof(Jamm.Gui.Tests.HeadlessApp))]

namespace Jamm.Gui.Tests;

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class HeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainWindowOpensWithEmptyAndConfiguredStorage(bool configured)
    {
        using var files = new TestFiles(configured);
        var vm = new MainViewModel(files.Store);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        while (vm.IsBusy) await Task.Delay(10);
        Assert.True(window.IsVisible); Assert.IsType<MainViewModel>(window.DataContext);
        Assert.Equal(configured ? "Original game" : "Choose the legacy game folder and base in Settings.", vm.GameStatus);
        Assert.NotNull(window.FindControl<ListBox>("LibraryList")); window.Close();
    }

    [AvaloniaTheory]
    [InlineData("{")]
    [InlineData("{\"wine\":{\"mode\":\"invalid\"}}")]
    public async Task InvalidConfigurationStaysInTheGuiRecoveryPath(string json)
    {
        using var files = new TestFiles(false);
        string path = Path.Combine(files.Store.Root, "config.json"); File.WriteAllText(path, json);
        var vm = new MainViewModel(files.Store);
        var window = new MainWindow { DataContext = vm }; window.Show();
        while (vm.IsBusy) await Task.Delay(10);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible); Assert.Contains("config.json", vm.Log);
        Assert.Contains("Repair", vm.Log); Assert.False(vm.IsBusy);
        Assert.False(vm.CanInstall); Assert.False(vm.CanRestore);
        bool constructed = false;
        await vm.OpenSettingsAsync(() => { constructed = true; return new SettingsWindow(vm).ShowDialog(window); });
        Assert.False(constructed); Assert.Contains("Could not open settings", vm.Status);
        Assert.Equal(json, File.ReadAllText(path));
        File.Delete(path);
        await vm.OpenSettingsAsync(() => throw new InvalidOperationException("Settings construction failed."));
        Assert.Contains("Settings construction failed", vm.Status);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MouseDragChangesAndPersistsLoadOrder()
    {
        using var files = new TestFiles();
        var library = new ModLibrary(files.Store);
        var a = library.Add(files.Mod("Example Mod A", "x = 1;\n")).Mod;
        var b = library.Add(files.Mod("Example Mod B", "x = 2;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store); profiles.Create("drag", [a.Id, b.Id]); profiles.Use("drag");
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync();
        vm.AdvancedMode = true;
        var window = new MainWindow(false) { DataContext = vm }; window.Show(); window.UpdateLayout();
        var list = window.FindControl<ListBox>("OrderList")!;
        var first = list.ContainerFromIndex(0)!; var second = list.ContainerFromIndex(1)!;
        var start = first.TranslatePoint(new Point(150, 22), window)!.Value;
        var finish = second.TranslatePoint(new Point(150, 22), window)!.Value;
        window.MouseDown(start, Avalonia.Input.MouseButton.Left);
        window.MouseMove(finish);
        window.MouseUp(finish, Avalonia.Input.MouseButton.Left);
        while (vm.IsBusy) await Task.Delay(10);
        Assert.Equal(b.Id, vm.Order[0].Id);
        Assert.Equal(b.Id, profiles.Get("drag").Mods[0]); window.Close();
    }

    private sealed class BrowseCatalog : IModCatalogProvider
    {
        public Task<CatalogSnapshot> LoadAsync(CancellationToken token) => Task.FromResult(new CatalogSnapshot([
            new("demo", "Example Ocean Expansion", "Example creator", "A neutral catalog demonstration. Adds imaginary fish and a new area. Author notice: back up your saves before trying a new mod.", "https://example.com/data.win", false, ["New Fish", "Areas"], null, 4),
            new("preview", "Example Boat Preview", "Another creator", "A future project without a released download.", null, true, ["Boats"], null, null)
        ], DateTimeOffset.Parse("2026-11-16T12:00:00Z")));
    }
    private sealed class BrowseResolver : IModDownloadResolver
    {
        public Task<ModDownloadResolution> ResolveAsync(CatalogMod mod, CancellationToken token) => Task.FromResult(new ModDownloadResolution([
            new("https", "demo-file", "demo-release", "1.0", "data.win", "Example package", 54000000, mod.FileUrl!)
        ]));
        public Task<Uri> GetFileUriAsync(ModDownloadFile file, CancellationToken token) => throw new InvalidOperationException("No download in headless rendering.");
    }
    [AvaloniaFact]
    public async Task BrowseTabUsesNativeControlsAndKeepsManagerNavigation()
    {
        using var files = new TestFiles();
        var vm = new MainViewModel(files.Store, catalog: new BrowseCatalog(), resolver: new BrowseResolver());
        await vm.InitializeAsync(); await vm.Browse.RefreshAsync();
        var window = new MainWindow(false) { DataContext = vm }; window.Show();
        var tabs = window.FindControl<TabControl>("MainTabs")!; tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var list = window.FindControl<ListBox>("BrowseList")!; list.SelectedIndex = 1;
        while (vm.Browse.IsLoading) await Task.Delay(10);
        Assert.NotNull(vm.Browse.Selected); Assert.NotEmpty(vm.Browse.Files); Assert.True(vm.Browse.CanDownload);
        vm.Browse.SelectedFile = vm.Browse.Files[0]; Assert.True(vm.Browse.CanDownload);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        if (Environment.GetEnvironmentVariable("JAMM_BROWSE_SCREENSHOT") is { Length: > 0 } output)
        {
            using var bitmap = window.CaptureRenderedFrame()!; bitmap.Save(output);
        }
        tabs.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Assert.NotNull(window.FindControl<ListBox>("LibraryList")); Assert.True(vm.CanEdit);
        window.Close();
    }

    [AvaloniaFact]
    public async Task GenerateNeutralReadmeScreenshotsWhenRequested()
    {
        string? output = Environment.GetEnvironmentVariable("JAMM_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using var files = new TestFiles();
        var library = new ModLibrary(files.Store);
        var mod = library.Add(files.Mod("Example Mod A", "x = 1;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store);
        profiles.Create("weekend-fishing", [mod.Id]); profiles.Use("weekend-fishing");
        var vm = new MainViewModel(files.Store);
        await vm.InitializeAsync();
        vm.Mods.Clear(); vm.Order.Clear();
        vm.Mods.Add(new("demo-a", "Example Mod A", "Compatible legacy base ✓", true, 32100000));
        vm.Mods.Add(new("demo-b", "Example Mod B", "Compatible legacy base ✓", true, 28500000));
        vm.Mods.Add(new("demo-c", "Example Mod C", "Source build differs · review", true, 41000000));
        foreach (var row in vm.Mods) { row.Enabled = true; vm.Order.Add(row); }
        vm.Conflicts.Add(new("CONFLICT", "code/example_catalog#12", "list", "Example Mod A, Example Mod B", "Mods insert different lines here. Choose one, or keep both in load order.", "Neutral demonstration only.", ["Example Mod A", "Example Mod B", "keep-both"], "keep-both"));
        vm.Conflicts.Add(new("AUTO", "objects/example_boat", "field", "Example Mod A, Example Mod C", "Changes to different settings combine automatically.", "Neutral demonstration only.", [], null));
        vm.Conflicts.Add(new("INFO", "sprites/example_marker", "identical", "Example Mod B, Example Mod C", "Both mods make the same change. Keep it once.", "Neutral demonstration only.", [], null));
        var window = new MainWindow(false) { DataContext = vm, Height = 920 };
        window.Show();
        window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        using (var bitmap = window.CaptureRenderedFrame()!) bitmap.Save(Path.Combine(output, "gui-hero.png"));
        window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        using (var bitmap = window.CaptureRenderedFrame()!) bitmap.Save(Path.Combine(output, "gui-conflicts.png"));
        window.Close();
    }
}
