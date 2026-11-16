using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class Phase14HeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeadersAndSwitchesAreIndependentAndRowGapsStayEightPixels(bool advanced)
    {
        using var files = new TestFiles(configure: false);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        foreach (var name in new[] { "First", "Second", "Third" })
        {
            var mod = new ModRow(name, name, "? Compatibility unknown", false, 0);
            vm.Mods.Add(mod);
            vm.Order.Add(mod);
        }
        vm.AdvancedMode = advanced;
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show();
            void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            Layout();
            var rows = window.GetVisualDescendants().OfType<ModListRow>().Where(r => r.IsEffectivelyVisible).ToArray();
            Assert.Equal(3, rows.Length);
            void CheckGaps()
            {
                for (var i = 1; i < rows.Length; i++)
                {
                    var previous = rows[i - 1].TranslatePoint(default, window)!.Value;
                    var current = rows[i].TranslatePoint(default, window)!.Value;
                    Assert.Equal(8, current.Y - previous.Y - rows[i - 1].Bounds.Height, 1);
                }
            }
            CheckGaps();
            var row = rows[0];
            var header = row.FindControl<Button>("HeaderButton")!;
            var toggle = row.FindControl<ToggleSwitch>("EnableSwitch")!;
            Assert.True(toggle.IsEffectivelyVisible);
            void Click(Control control)
            {
                var point = control.TranslatePoint(new Avalonia.Point(16, control.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Layout();
            }
            var mod = (ModRow)row.DataContext!;
            var enabled = mod.Enabled;
            Click(toggle);
            Assert.Equal(!enabled, mod.Enabled);
            Assert.True(mod.IsPending);
            Assert.False(row.IsExpanded);
            Click(header);
            Assert.True(row.IsExpanded);
            Assert.Equal(!enabled, mod.Enabled);
            CheckGaps();
            Assert.True(toggle.IsEffectivelyVisible);
            Click(toggle);
            Assert.True(row.IsExpanded);
            Assert.Equal(enabled, mod.Enabled);
            Click(toggle);
            Assert.True(row.IsExpanded);
            Assert.Equal(!enabled, mod.Enabled);
            if (!advanced && Environment.GetEnvironmentVariable("JAMM_ROWS_SCREENSHOT") is { Length: > 0 } output)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var frame = window.CaptureRenderedFrame()!;
                frame.Save(output);
            }
            Assert.True(header.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Layout();
            Assert.False(row.IsExpanded);
            CheckGaps();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Layout();
            Assert.True(toggle.IsFocused);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Layout();
            Assert.Equal(enabled, mod.Enabled);
            Assert.False(row.IsExpanded);
            vm.UndoChanges();
            Assert.All(vm.Mods, m => Assert.False(m.IsPending));
        }
        finally { window.Close(); vm.Browse.Dispose(); }
    }

    [AvaloniaFact]
    public async Task CollapsedSwitchIsAccessibleAndSpaceStagesChange()
    {
        using var files = new TestFiles(); files.Store.Set("useSharedCompatibilityResults", "false");
        new ModLibrary(files.Store).Add(files.Mod("Keyboard mod", "x = 1;\n"));
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []); await vm.InitializeAsync();
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var expander = window.GetVisualDescendants().OfType<ModListRow>().First(e => e.DataContext is ModRow && e.IsEffectivelyVisible);
            Assert.False(expander.IsExpanded);
            var toggle = expander.GetVisualDescendants().OfType<ToggleSwitch>().Single();
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.Equal("Enable Keyboard mod", AutomationProperties.GetName(toggle));
            Assert.NotNull(toggle.KnobTransitions);
            File.WriteAllText(Path.Combine(files.Root, "default.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\"><circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"green\"/></svg>");
            Assert.NotNull(ModIconResolver.Load(null, "Unknown", files.Root));
            Assert.True(toggle.Focus()); Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsFocused);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs(); Assert.True(Assert.Single(vm.Mods).IsPending);
            Assert.False(expander.IsExpanded);
            Assert.Empty(new ProfileLibrary(files.Store).List());
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            if (Environment.GetEnvironmentVariable("JAMM_PHASE14_SCREENSHOT") is { Length: > 0 } path)
            { using var frame = window.CaptureRenderedFrame()!; frame.Save(path); }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            vm.Browse.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task SanitizedModRowShowsRemovedPerformanceCountInfoFlyoutAndAdvancedKeepSwitch()
    {
        using var files = new TestFiles();
        files.Store.Set("useSharedCompatibilityResults", "false");
        string modPath = files.Mod("ThirdPartyPerf", "time_scalar = 0.00006;\nx = 2;\n");
        var added = new ModLibrary(files.Store).Add(modPath).Mod;
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var modRow = Assert.Single(vm.Mods);
            Assert.Equal(1, modRow.RemovedPerformanceCount);
            Assert.Equal("JAMM removed 1 performance changes", modRow.SanitizedLine);
            Assert.Contains("[timing]", modRow.SanitizedDetails);

            var rowControl = window.GetVisualDescendants().OfType<ModListRow>().First(r => r.DataContext == modRow && r.IsEffectivelyVisible);
            var infoButton = rowControl.FindControl<Button>("SanitizedInfoButton")!;
            Assert.True(infoButton.IsEffectivelyVisible);
            var flyout = Assert.IsType<Flyout>(infoButton.Flyout);
            flyout.ShowAt(infoButton);
            Dispatcher.UIThread.RunJobs();
            var scroll = Assert.IsType<ScrollViewer>(flyout.Content);
            var text = Assert.IsType<SelectableTextBlock>(scroll.Content);
            Assert.Equal(modRow.SanitizedDetails, text.Text);
            flyout.Hide();

            vm.AdvancedMode = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            rowControl = window.GetVisualDescendants().OfType<ModListRow>().First(r => r.DataContext == modRow && r.IsEffectivelyVisible);
            rowControl.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var keepCheck = rowControl.FindControl<CheckBox>("KeepAuthorPerfCheck")!;
            var keepHudCheck = rowControl.FindControl<CheckBox>("KeepAuthorHudCheck")!;
            Assert.True(keepCheck.IsEffectivelyVisible);
            Assert.True(keepHudCheck.IsEffectivelyVisible);
            Assert.False(modRow.KeepAuthorPerformanceChanges);
            Assert.False(modRow.KeepAuthorHudDesign);
            modRow.KeepAuthorPerformanceChanges = true;
            while (vm.IsBusy) await Task.Delay(10);
            Assert.True(new ModLibrary(files.Store).Find(added.Id).KeepAuthorPerformanceChanges);
            modRow.KeepAuthorHudDesign = true;
            while (vm.IsBusy) await Task.Delay(10);
            Assert.True(new ModLibrary(files.Store).Find(added.Id).KeepAuthorHudDesign);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            vm.Browse.Dispose();
        }
    }
}
