using UndertaleModLib;
using UndertaleModLib.Models;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class Phase9eTests
{
    [AvaloniaFact]
    public async Task LocalRowsExpandWithComputedSummaryAndOperationsShowCountedProgress()
    {
        using var files = new TestFiles();
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        await vm.AddAsync([files.Mod("Example", "x = 1;\n", d => d.GameObjects.Add(new() { Name = d.Strings.MakeString("NewObject") }))]);
        var row = Assert.Single(vm.Mods);
        Assert.Contains("1 game objects", row.Summary); Assert.Contains("Source:", row.Summary);
        var window = new MainWindow(false) { DataContext = vm }; window.Show();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var expander = window.GetVisualDescendants().OfType<ModListRow>().First(e => e.DataContext == row);
        expander.IsExpanded = true;
        var finish = new TaskCompletionSource();
        var operation = vm.RunAsync("Preparing mods", async (_, progress) => { progress.Report("Decompiling example.win 812/1839: entry"); await finish.Task; });
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Assert.False(vm.OperationIndeterminate);
        Assert.InRange(vm.OperationProgress, 44, 45);
        Assert.Contains("Reading game code", vm.OperationStep);
        Assert.Contains(window.GetVisualDescendants().OfType<ProgressBar>(), bar => bar.IsVisible && !bar.IsIndeterminate);
        Assert.Contains(window.GetVisualDescendants().OfType<Expander>(), e => Equals(e.Header, "Show details") && !e.IsExpanded);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        if (Environment.GetEnvironmentVariable("JAMM_PHASE9E_SCREENSHOT") is { Length: > 0 } output)
        { using var bitmap = window.CaptureRenderedFrame()!; bitmap.Save(output); }
        finish.SetResult(); await operation;
        window.Close(); vm.Browse.Dispose();
    }
}
