using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Jamm.Core.Management;
using Jamm.Gui;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class Phase9bLiveTests
{
    [AvaloniaFact]
    public async Task ColourExpansionSelectionAnalysisAndDownloadInRealWindow()
    {
        if (Environment.GetEnvironmentVariable("JAMM_LIVE_BROWSE") != "1") return;
        var store = new ManagerStore();
        Assert.StartsWith(Path.GetTempPath(), store.Root);
        store.Set("base", Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!);
        var vm = new MainViewModel(store); await vm.InitializeAsync(); await vm.Browse.RefreshAsync();
        var window = new MainWindow(false) { DataContext = vm }; window.Show();
        window.FindControl<TabControl>("MainTabs")!.SelectedIndex = 1;
        var row = vm.Browse.Mods.Single(r => r.Mod.Id == "40");
        window.FindControl<ListBox>("BrowseList")!.SelectedItem = row;
        while (vm.Browse.IsLoading) await Task.Delay(100);
        Console.WriteLine(row.Adds + "\n" + row.CompatibilityReason + "\n" + vm.Browse.DetailStatus);
        Assert.NotNull(vm.Browse.SelectedFile); Assert.True(vm.Browse.CanDownload);
        var automaticallyChosen = vm.Browse.SelectedFile;
        vm.Browse.SelectedFile = null; Assert.False(vm.Browse.CanDownload); // Original Phase 9 disabled-button precondition.
        vm.Browse.SelectedFile = automaticallyChosen;
        while (vm.Browse.IsLoading) await Task.Delay(100);
        Assert.True(vm.Browse.CanDownload);
        Assert.Contains("fish species", row.Adds);
        Assert.NotEmpty(vm.Browse.Images);
        await vm.Browse.DownloadAsync(false, _ => throw new Exception("Download must not install"));
        Assert.Single(vm.Mods); Assert.Single(new ProfileLibrary(store).Get(vm.SelectedProfile!).EnabledMods);
        Console.WriteLine(vm.Log);
        if (Environment.GetEnvironmentVariable("JAMM_BROWSE_SCREENSHOT") is { } output)
        {
            window.FindControl<ListBox>("BrowseList")!.ScrollIntoView(row);
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var frame = window.CaptureRenderedFrame(); frame!.Save(output);
        }
        window.Close();
    }
}
