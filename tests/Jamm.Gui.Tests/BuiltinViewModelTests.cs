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

    [Fact]
    public void SingleSourceOfTruthHotkeysListCoversAllRequiredKeysAndCommands()
    {
        Assert.Contains(BuiltinMods.Hotkeys, h => h.Key == "F8" && h.Description.Contains("Perf") && !h.IsCommand);
        Assert.Contains(BuiltinMods.Hotkeys, h => h.Key == "F7" && h.Description.Contains("Smooth") && !h.IsCommand);
        Assert.Contains(BuiltinMods.Hotkeys, h => h.Key == "F11" && h.Description.Contains("HD Lite") && !h.IsCommand);
        Assert.Contains(BuiltinMods.Hotkeys, h => h.Key.Contains("F12") && h.Key.Contains("backtick") && h.Description.Contains("Console") && !h.IsCommand);

        string[] requiredCommands = ["help", "unlockall", "unlockgear", "maxlevel", "level <n>", "catalog all", "money <n>"];
        foreach (string cmd in requiredCommands)
        {
            Assert.Contains(BuiltinMods.Hotkeys, h => h.Key == cmd && h.IsCommand);
        }

        Assert.Equal(4, BuiltinMods.HotkeyEntries.Count());
        Assert.True(BuiltinMods.ConsoleCommands.Count() >= 7);

        Assert.Contains("F8", BuiltinMods.HotkeysHelpText);
        Assert.Contains("F7", BuiltinMods.HotkeysHelpText);
        Assert.Contains("F11", BuiltinMods.HotkeysHelpText);
        Assert.Contains("F12 or backtick", BuiltinMods.HotkeysHelpText);
        foreach (string cmd in requiredCommands)
            Assert.Contains(cmd, BuiltinMods.HotkeysHelpText);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainWindowDisplaysHotkeysHelpButtonNearBuiltinGroup(bool advanced)
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
            var hotkeysButton = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b =>
                b.IsEffectivelyVisible && Equals(b.Content, "Hotkeys"));
            Assert.NotNull(hotkeysButton);
            Assert.NotNull(hotkeysButton.Flyout);
            Assert.Equal("Show in-game hotkeys and console commands", ToolTip.GetTip(hotkeysButton));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FirstRunShowsOptionalExtrasPanelAfterGameUseWithTogglesHotkeysAndIsSkippable()
    {
        using var files = new TestFiles(false, customize: data =>
        {
            foreach (string name in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
                ModObjects.Create(data, name, false);
            foreach (string name in new[] { "NewWorld", "room11" }) data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });
        });
        string folder = Path.Combine(files.Root, "legacy-game");
        Directory.CreateDirectory(folder);
        File.Copy(files.Mod("Base", "x = 0;\n", customize: data =>
        {
            foreach (string name in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
                ModObjects.Create(data, name, false);
            foreach (string name in new[] { "NewWorld", "room11" }) data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });
        }), Path.Combine(folder, "data.win"));
        File.WriteAllText(Path.Combine(folder, "Cat Goes Fishing.exe"), "fake");

        var detected = SteamGameDetection.Inspect(Path.Combine(folder, "data.win"));
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => [detected]);
        await vm.InitializeAsync();

        Assert.True(vm.NeedsSetup);
        Assert.True(vm.SetupOpen);
        Assert.True(vm.IsSetupDetecting);
        Assert.False(vm.IsSetupExtras);

        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            // Click Use this
            vm.DetectedLegacy = vm.DetectedGames.Single();
            await vm.UseDetectedGameAsync();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            // After Use this, setup advances to Extras panel (discoverable built-in mods)
            Assert.True(vm.SetupOpen);
            Assert.True(vm.IsSetupExtras);
            Assert.False(vm.IsSetupDetecting);
            Assert.False(vm.NeedsSetup);

            // The panel displays the title "Optional JAMM extras"
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Optional JAMM extras");

            // All 4 built-in mods are present and default off
            Assert.Equal(4, vm.Builtins.Count);
            Assert.All(vm.Builtins, b => Assert.False(b.Enabled));

            // Hotkeys info text is visible
            Assert.Contains(window.GetVisualDescendants().OfType<SelectableTextBlock>(), t =>
                t.IsEffectivelyVisible && t.Text?.Contains("F8") == true && t.Text?.Contains("unlockall") == true);

            // Toggle a built-in from the extras panel (e.g. JAMM Console)
            var setupBorder = window.GetVisualDescendants().OfType<Border>().First(b => b.ZIndex == 10);
            var consoleToggle = setupBorder.GetVisualDescendants().OfType<ToggleSwitch>().Single(t =>
                t.IsEffectivelyVisible && t.DataContext is BuiltinRow { Id: "console" });
            var point = consoleToggle.TranslatePoint(new Point(16, consoleToggle.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            for (int i = 0; vm.IsBusy && i < 1000; i++) await Task.Delay(10);
            Assert.False(vm.IsBusy);
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.Builtins.Single(b => b.Id == "console").Enabled);
            Assert.Equal("builtin-console", Assert.Single(new ProfileLibrary(files.Store).Get("default").EnabledMods));

            // Test navigation back and forward
            vm.BackToGameSetup();
            Assert.True(vm.IsSetupDetecting);
            Assert.False(vm.IsSetupExtras);

            vm.SetupStep = SetupStep.Extras;
            Assert.True(vm.IsSetupExtras);

            // Test skippable (Skip closes setup)
            vm.SkipSetup();
            Assert.False(vm.SetupOpen);

            // The active profile retained the toggled built-in mod
            Assert.Equal("builtin-console", Assert.Single(new ProfileLibrary(files.Store).Get("default").EnabledMods));
        }
        finally { window.Close(); }
    }
}
