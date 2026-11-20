using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui.Tests;

public sealed class BaseModHeadlessTests
{
    [AvaloniaFact]
    public async Task ProfileGroupsAndIndentsAddonsAndKeepsThemBelowTheirBase()
    {
        using var files = new TestFiles();
        var library = new ModLibrary(files.Store);
        var addon = library.Add(files.Mod("Addon", "x = 2;\n")).Mod;
        var vanilla = library.Add(files.Mod("Vanilla", "x = 3;\n")).Mod;
        var foundation = library.Add(files.Mod("Overhaul", "x = 1;\n")).Mod;
        var profiles = new ProfileLibrary(files.Store);
        profiles.Create("grouped", [addon.Id, vanilla.Id, foundation.Id]);
        profiles.Use("grouped");

        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        await vm.SetProfileRoleAsync(foundation.Id, "base");
        await vm.SetProfileRoleAsync(addon.Id, "addon", foundation.Id);
        Assert.Equal(new[] { foundation.Id, addon.Id, vanilla.Id }, vm.Order.Select(m => m.Id));
        Assert.Equal("Base", vm.Mods.Single(m => m.Id == foundation.Id).RoleLine);
        Assert.Equal("Addon of Overhaul", vm.Mods.Single(m => m.Id == addon.Id).RoleLine);
        Assert.Equal(20, vm.Order[1].ProfileIndent.Left);
        await vm.MoveAsync(1, 0);
        await vm.MoveAsync(0, 1);
        Assert.Equal(new[] { foundation.Id, addon.Id, vanilla.Id }, vm.Order.Select(m => m.Id));
        Assert.Equal(vm.Order.Select(m => m.Id), profiles.Get("grouped").Mods);

        vm.AdvancedMode = true;
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var orderList = window.FindControl<ListBox>("OrderList")!;
            var rows = orderList.GetVisualDescendants().OfType<ModListRow>().ToArray();
            var baseRow = Assert.Single(rows, row => (row.DataContext as ModRow)?.Id == foundation.Id);
            var addonRow = Assert.Single(rows, row => (row.DataContext as ModRow)?.Id == addon.Id);
            var baseLabel = Assert.Single(baseRow.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "Base");
            var addonLabel = Assert.Single(addonRow.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "Addon of Overhaul");
            Assert.True(baseLabel.IsEffectivelyVisible);
            Assert.True(addonLabel.IsEffectivelyVisible);
            var basePoint = baseLabel.TranslatePoint(default, window)!.Value;
            var addonPoint = addonLabel.TranslatePoint(default, window)!.Value;
            Assert.True(basePoint.Y < addonPoint.Y);
            Assert.Equal(20, addonPoint.X - basePoint.X, 1);
        }
        finally { window.Close(); }
    }
}
