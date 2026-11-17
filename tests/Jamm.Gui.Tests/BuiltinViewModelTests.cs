using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;
using Jamm.ModMaker;
using UndertaleModLib.Models;
using UndertaleModLib;

namespace Jamm.Gui.Tests;

public sealed class BuiltinViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuiltinGroupShowsDescriptionsAndClickGeneratesMod(bool advanced)
    {
        using var files = new TestFiles(customize: data =>
        {
            foreach (string name in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
                ModObjects.Create(data, name, false);
            foreach (string name in new[] { "NewWorld", "room11" }) data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });
        });
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync(); vm.AdvancedMode = advanced;
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Built-in (JAMM)");
            var toggle = window.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.IsEffectivelyVisible && t.DataContext is BuiltinRow { Id: "console" });
            toggle.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("F12 or backtick") == true);
            var point = toggle.TranslatePoint(new Point(16, toggle.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            for (int i = 0; vm.IsBusy && i < 1000; i++) await Task.Delay(10);
            Assert.False(vm.IsBusy);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.Single(vm.Mods).Enabled, vm.Log);
            Assert.True(toggle.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ToggleGeneratesImportsAndEnablesThenDisablesInActiveProfile()
    {
        using var files = new TestFiles(customize: data =>
        {
            foreach (string name in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
                ModObjects.Create(data, name, false);
            foreach (string name in new[] { "NewWorld", "room11" }) data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });
        });
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        Assert.True(vm.HasBuiltins);
        Assert.Equal(4, vm.Builtins.Count);
        Assert.Empty(vm.Mods);
        await vm.ToggleBuiltinAsync("console");
        var row = Assert.Single(vm.Mods);
        Assert.True(row.Enabled, vm.Log);
        Assert.True(vm.Builtins.Single(b => b.Id == "console").Enabled);
        Assert.Equal("builtin-console", Assert.Single(new ProfileLibrary(files.Store).Get(vm.SelectedProfile!).Mods));
        Assert.Equal(0, vm.PendingCount);
        await vm.ToggleBuiltinAsync("console");
        Assert.False(vm.Builtins.Single(b => b.Id == "console").Enabled);
        Assert.Contains(row.Id, new ProfileLibrary(files.Store).Get(vm.SelectedProfile!).DisabledMods);
        await vm.ToggleBuiltinAsync("console");
        Assert.True(vm.Mods.Single().Enabled);
        Assert.Contains("Using cached", vm.Log);
    }

    [AvaloniaFact]
    public async Task MissingBaseHidesGroupAndGenerationFailureIsReported()
    {
        using var noBase = new TestFiles(configure: false);
        var empty = new MainViewModel(noBase.Store, gameSearch: (_, _) => []);
        Assert.False(empty.HasBuiltins);
        using var files = new TestFiles();
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        await vm.ToggleBuiltinAsync("console");
        Assert.Contains("Could not generate JAMM Console", vm.Log);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Mods);
        Assert.All(vm.Builtins, b => Assert.False(b.Enabled));
    }
}
