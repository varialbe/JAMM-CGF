using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;
using UndertaleModLib;

namespace Jamm.Gui.Tests;

/// <summary>Unit and headless tests for newcomer onboarding and UX polish (window sizing, setup dialog, unusable folder list, modded copy explanations).</summary>
public sealed class NewcomerUxTests
{
    [Fact]
    public void WindowSizeCalculationClampsToScreenWorkingAreaAndCenters()
    {
        // Case 1: 1280x800 screen working area with default requested 1200x800 -> centered horizontally at 40
        var (w1, h1, pos1) = MainWindow.CalculateBounds(new PixelRect(0, 0, 1280, 800), 1.0, 1200, 800);
        Assert.Equal(1200, w1);
        Assert.Equal(800, h1);
        Assert.Equal(40, pos1.X);
        Assert.Equal(0, pos1.Y);

        // Case 2: Oversized 1320x860 requested on 1280x800 screen -> clamped to 1280x800 and centered at 0, 0
        var (w2, h2, pos2) = MainWindow.CalculateBounds(new PixelRect(0, 0, 1280, 800), 1.0, 1320, 860);
        Assert.Equal(1280, w2);
        Assert.Equal(800, h2);
        Assert.Equal(0, pos2.X);
        Assert.Equal(0, pos2.Y);

        // Case 3: 1280x720 working area (720p monitor) -> clamped height to 720
        var (w3, h3, pos3) = MainWindow.CalculateBounds(new PixelRect(0, 0, 1280, 720), 1.0, 1200, 800);
        Assert.Equal(1200, w3);
        Assert.Equal(720, h3);
        Assert.Equal(40, pos3.X);
        Assert.Equal(0, pos3.Y);

        // Case 4: 1280x720 monitor with 40px taskbar (working area 1280x680 starting at Y=40)
        var (w4, h4, pos4) = MainWindow.CalculateBounds(new PixelRect(0, 40, 1280, 680), 1.0, 1200, 800);
        Assert.Equal(1200, w4);
        Assert.Equal(680, h4);
        Assert.Equal(40, pos4.X);
        Assert.Equal(40, pos4.Y);

        // Case 5: 1920x1080 display -> 1200x800 centered at (360, 140)
        var (w5, h5, pos5) = MainWindow.CalculateBounds(new PixelRect(0, 0, 1920, 1080), 1.0, 1200, 800);
        Assert.Equal(1200, w5);
        Assert.Equal(800, h5);
        Assert.Equal(360, pos5.X);
        Assert.Equal(140, pos5.Y);

        // Case 6: High-DPI display 2560x1440 scaling 2.0 (working area 1280x720 DIPs)
        var (w6, h6, pos6) = MainWindow.CalculateBounds(new PixelRect(0, 0, 2560, 1440), 2.0, 1200, 800);
        Assert.Equal(1200, w6);
        Assert.Equal(720, h6);
        Assert.Equal(80, pos6.X);
        Assert.Equal(0, pos6.Y);

        // Case 7: Small 1024x600 display
        var (w7, h7, pos7) = MainWindow.CalculateBounds(new PixelRect(0, 0, 1024, 600), 1.0, 1200, 800);
        Assert.Equal(1024, w7);
        Assert.Equal(600, h7);
        Assert.Equal(0, pos7.X);
        Assert.Equal(0, pos7.Y);
    }

    [AvaloniaFact]
    public async Task MainWindowHeadersAndButtonsDoNotOverflowAt720p()
    {
        using var files = new TestFiles(customize: data =>
        {
            foreach (string name in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
                Jamm.ModMaker.ModObjects.Create(data, name, false);
            foreach (string name in new[] { "NewWorld", "room11" }) data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });
        });
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        var window = new MainWindow(false) { DataContext = vm, Width = 1200, Height = 720 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            // Settings button must be visible within window bounds
            var settingsButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Settings"));
            Assert.True(settingsButton.IsEffectivelyVisible);
            var settingsPos = settingsButton.TranslatePoint(default, window)!.Value;
            Assert.True(settingsPos.X >= 0);
            Assert.True(settingsPos.X + settingsButton.Bounds.Width <= window.Bounds.Width,
                $"Settings button right ({settingsPos.X + settingsButton.Bounds.Width}) exceeds window width ({window.Bounds.Width})");

            // Simple mode: Hotkeys button must be visible within window bounds
            var hotkeysButton = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && Equals(b.Content, "Hotkeys"));
            Assert.NotNull(hotkeysButton);
            var hotkeysPos = hotkeysButton.TranslatePoint(default, window)!.Value;
            Assert.True(hotkeysPos.X >= 0);
            Assert.True(hotkeysPos.X + hotkeysButton.Bounds.Width <= window.Bounds.Width,
                $"Hotkeys button right ({hotkeysPos.X + hotkeysButton.Bounds.Width}) exceeds window width ({window.Bounds.Width})");

            // Advanced mode: Hotkeys button must also be visible within window bounds
            vm.AdvancedMode = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var advHotkeysButton = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && Equals(b.Content, "Hotkeys"));
            Assert.NotNull(advHotkeysButton);
            var advHotkeysPos = advHotkeysButton.TranslatePoint(default, window)!.Value;
            Assert.True(advHotkeysPos.X >= 0);
            Assert.True(advHotkeysPos.X + advHotkeysButton.Bounds.Width <= window.Bounds.Width);

            // Settings button remains within bounds in Advanced mode
            var advSettingsPos = settingsButton.TranslatePoint(default, window)!.Value;
            Assert.True(advSettingsPos.X + settingsButton.Bounds.Width <= window.Bounds.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SetupDialogActionButtonsStayInFixedFooterOutsideScrollViewer()
    {
        using var files = new TestFiles(false);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        vm.SetupOpen = true;
        vm.SetupStep = SetupStep.DetectGame;
        var window = new MainWindow(false) { DataContext = vm, Width = 1200, Height = 720 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            var setupBorder = window.GetVisualDescendants().OfType<Border>().First(b => b.ZIndex == 10);
            var scrollViewer = setupBorder.GetVisualDescendants().OfType<ScrollViewer>().First();
            var footerBorder = setupBorder.GetVisualDescendants().OfType<Border>().First(b => Grid.GetRow(b) == 1);

            // On DetectGame step: action buttons are in the fixed footer, outside the scrollable body
            string[] detectActionButtons = ["Use this", "Choose folder…", "Check again", "Cancel setup"];
            foreach (var label in detectActionButtons)
            {
                var button = footerBorder.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, label));
                Assert.NotNull(button);
                Assert.True(button.IsEffectivelyVisible);
                Assert.DoesNotContain(button, scrollViewer.GetVisualDescendants().OfType<Button>());
            }

            // Advance to Extras step: action buttons are in the fixed footer, outside the scrollable body
            vm.SetupStep = SetupStep.Extras;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            string[] extrasActionButtons = ["Done", "Skip", "← Back to game folder"];
            foreach (var label in extrasActionButtons)
            {
                var button = footerBorder.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, label));
                Assert.NotNull(button);
                Assert.True(button.IsEffectivelyVisible);
                Assert.DoesNotContain(button, scrollViewer.GetVisualDescendants().OfType<Button>());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task OtherFoldersDropdownHiddenWhenEmptyAndShowsNeutralCountLabel()
    {
        using var files = new TestFiles(false);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        vm.SetupOpen = true;
        vm.SetupStep = SetupStep.DetectGame;
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            var setupBorder = window.GetVisualDescendants().OfType<Border>().First(b => b.ZIndex == 10);
            var expander = setupBorder.GetVisualDescendants().OfType<Expander>().First();

            // When no unusable folders found: HasOtherGames is false and Expander is hidden
            Assert.False(vm.HasOtherGames);
            Assert.False(expander.IsVisible);
            Assert.False(expander.IsEffectivelyVisible);

            // When 1 unusable folder is found: neutral singular label
            vm.OtherGames.Add(new DetectedGame("/games/other1/data.win", false, "Legacy unsupported"));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            Assert.True(vm.HasOtherGames);
            Assert.Equal("Show 1 other folder that was found (not usable)", vm.OtherGamesHeader);
            Assert.True(expander.IsVisible);
            Assert.True(expander.IsEffectivelyVisible);
            Assert.Equal("Show 1 other folder that was found (not usable)", expander.Header);

            // When 3 unusable folders are found: neutral plural label
            vm.OtherGames.Add(new DetectedGame("/games/other2/data.win", false, "Current game YYC"));
            vm.OtherGames.Add(new DetectedGame("/games/other3/data.win", false, "Invalid header"));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            Assert.True(vm.HasOtherGames);
            Assert.Equal("Show 3 other folders that were found (not usable)", vm.OtherGamesHeader);
            Assert.True(expander.IsVisible);
            Assert.True(expander.IsEffectivelyVisible);
            Assert.Equal("Show 3 other folders that were found (not usable)", expander.Header);

            // When cleared: hidden again
            vm.OtherGames.Clear();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            Assert.False(vm.HasOtherGames);
            Assert.False(expander.IsVisible);
            Assert.False(expander.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SeparateModdedCopyHiddenUntilGameSelectedWithExplanation()
    {
        using var files = new TestFiles(false);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();
        vm.SetupOpen = true;
        vm.SetupStep = SetupStep.DetectGame;
        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            var setupBorder = window.GetVisualDescendants().OfType<Border>().First(b => b.ZIndex == 10);

            // Before game selected: HasModdedDirectory is false and label + explanation are hidden
            Assert.False(vm.HasModdedDirectory);
            Assert.DoesNotContain(setupBorder.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == "Separate modded copy:");
            Assert.DoesNotContain(setupBorder.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == "JAMM installs mods into this copy; your original game stays untouched.");

            // When game selected: HasModdedDirectory is true, label, explanation, and path are visible
            string legacyPath = Path.Combine(files.Root, "depot_343782", "data.win");
            vm.DetectedLegacy = new DetectedGame(legacyPath, true, "Supported original");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            Assert.True(vm.HasModdedDirectory);
            Assert.Equal(Path.Combine(files.Root, "depot_343782-modded"), vm.ModdedDirectory);

            Assert.Contains(setupBorder.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == "Separate modded copy:");
            Assert.Contains(setupBorder.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == "JAMM installs mods into this copy; your original game stays untouched.");
            Assert.Contains(setupBorder.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == vm.ModdedDirectory);

            // Deselect game: hides again
            vm.DetectedLegacy = null;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

            Assert.False(vm.HasModdedDirectory);
            Assert.DoesNotContain(setupBorder.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == "Separate modded copy:");
        }
        finally { window.Close(); }
    }
}
