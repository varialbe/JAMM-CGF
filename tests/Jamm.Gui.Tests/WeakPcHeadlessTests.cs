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
}
