using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

/// <summary>Exercises the Weak PC toggle in the Manager tab.</summary>
public sealed class WeakPcHeadlessTests
{
    [AvaloniaFact]
    public async Task TogglePersistsPresetAndRestoresDefaults()
    {
        using var files = new TestFiles();
        files.Store.Set("useSharedCompatibilityResults", "false");
        var library = new ModLibrary(files.Store);
        var perf = library.Add(files.Mod("Perf", "x = 1;\n"), "JAMM Perf", stableId: "builtin-perf").Mod;
        var profiles = new ProfileLibrary(files.Store);
        profiles.CreateEmpty("laptop");
        profiles.Use("laptop");
        profiles.SetRepack("laptop", false);

        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show();
            window.UpdateLayout();
            var toggle = window.FindControl<ToggleSwitch>("WeakPcSwitch")!;
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.False(toggle.IsChecked);
            async Task PressSpace()
            {
                Assert.True(toggle.Focus());
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (vm.IsBusy) await Task.Delay(10, timeout.Token);
            }

            await PressSpace();
            Assert.True(toggle.IsChecked);
            Assert.Equal("weak-pc", profiles.Get("laptop").Preset);
            Assert.Equal(perf.Id, Assert.Single(profiles.Get("laptop").EnabledMods));
            Assert.True(vm.ProfileRepack);

            await PressSpace();
            Assert.False(toggle.IsChecked);
            Assert.Equal("default", profiles.Get("laptop").Preset);
            Assert.Empty(profiles.Get("laptop").Mods);
            Assert.False(vm.ProfileRepack);
        }
        finally { window.Close(); vm.Browse.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ProfileFairPlayAndFishCapAreSavedFromPerformanceSettings()
    {
        using var files = new TestFiles();
        files.Store.Set("useSharedCompatibilityResults", "false");
        var profiles = new ProfileLibrary(files.Store);
        profiles.CreateEmpty("test");
        profiles.Use("test");
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show();
            window.UpdateLayout();
            var button = window.FindControl<Button>("PerformanceSettingsButton")!;
            var flyout = Assert.IsType<Flyout>(button.Flyout);
            flyout.ShowAt(button);
            var panel = Assert.IsType<StackPanel>(flyout.Content);
            var checkbox = panel.Children.OfType<CheckBox>().Single(c => c.Content?.ToString()?.StartsWith("Fair-Play") == true);
            var cap = panel.Children.OfType<NumericUpDown>().Single(n => n.Minimum == 0);
            Assert.True(checkbox.IsChecked);
            Assert.Equal(100_000_000m, cap.Value);
            checkbox.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (vm.IsBusy) await Task.Delay(10, timeout.Token);
            Assert.False(profiles.Get("test").FairPlay);

            cap.Value = 0;
            while (vm.IsBusy) await Task.Delay(10, timeout.Token);
            var saved = profiles.Get("test");
            Assert.Equal(0, saved.FishValueCap);
            Assert.False(saved.FairPlay);
            Assert.False(saved.NoBigMoney);
        }
        finally { window.Close(); vm.Browse.Dispose(); }
    }

}
