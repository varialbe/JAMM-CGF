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
    [AvaloniaFact]
    public async Task ExpandedSwitchIsAccessibleAndSpaceStagesChange()
    {
        using var files = new TestFiles(); files.Store.Set("useSharedCompatibilityResults", "false");
        new ModLibrary(files.Store).Add(files.Mod("Keyboard mod", "x = 1;\n"));
        var vm = new MainViewModel(files.Store); await vm.InitializeAsync();
        var window = new MainWindow(false) { DataContext = vm }; window.Show();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var expander = window.GetVisualDescendants().OfType<Expander>().First(e => e.DataContext is ModRow && e.IsEffectivelyVisible);
        expander.IsExpanded = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var toggle = expander.GetVisualDescendants().OfType<ToggleSwitch>().Single();
        Assert.Equal("Enable Keyboard mod", AutomationProperties.GetName(toggle));
        Assert.NotNull(toggle.KnobTransitions);
        File.WriteAllText(Path.Combine(files.Root, "default.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\"><circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"green\"/></svg>");
        Assert.NotNull(ModIconResolver.Load(null, "Unknown", files.Root));
        toggle.Focus(); window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs(); Assert.True(Assert.Single(vm.Mods).IsPending);
        Assert.Empty(new ProfileLibrary(files.Store).List());
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        if (Environment.GetEnvironmentVariable("JAMM_PHASE14_SCREENSHOT") is { Length: > 0 } path)
        { using var frame = window.CaptureRenderedFrame()!; frame.Save(path); }
        window.Close(); vm.Browse.Dispose();
    }
}
