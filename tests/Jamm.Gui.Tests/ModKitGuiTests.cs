using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jamm.Core.ModKit;
using Jamm.Gui.ViewModels;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Gui.Tests;

/// <summary>Tests for ModKit folder mods in the Avalonia GUI (Add dialog, drag/drop, badges, details, Rebuild action, help text).</summary>
public sealed class ModKitGuiTests
{
    private static void CustomizeForModKit(UndertaleData data)
    {
        foreach (string name in new[]
        {
            "Fish", "FishSmall", "FishMedium", "FishLarge", "FishHuge",
            "Boat", "Gear", "Rod", "BaseFish", "CatalogData", "SaveManager"
        })
        {
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        }
        data.GameObjects.ByName("FishSmall")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("FishMedium")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("FishLarge")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("FishHuge")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("BaseFish")!.ParentId = data.GameObjects.ByName("FishSmall");

        data.GameObjects.ByName("CatalogData")!.EventHandlerFor(EventType.Create, 0u, data);
        data.GameObjects.ByName("SaveManager")!.EventHandlerFor(EventType.Step, 0u, data);
    }

    [AvaloniaFact]
    public void AddModDialogHasRequiredOptionsHelpTextAndGuideButton()
    {
        var dialog = new AddModDialog();
        try
        {
            dialog.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Add mod files (.win, .zip)…", dialog.AddFilesButton.Content);
            Assert.Equal("Add mod folder (ModKit)…", dialog.AddFolderButton.Content);
            Assert.Equal("Made your own mod with ModKit? Add its folder.", dialog.HelpTextBlock.Text);
            Assert.Equal("Getting started guide ↗", dialog.GuideButton.Content);
            Assert.Equal("Cancel", dialog.CancelButton.Content);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task AddValidModKitFolderPopulatesRowMetadataAndModKitBadge()
    {
        using var files = new TestFiles(customize: CustomizeForModKit);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();

        string pkgDir = Path.Combine(files.Root, "test-fish");
        ModKitPackage.Scaffold(pkgDir, "fish");

        await vm.AddAsync([pkgDir]);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Mods);
        var mod = vm.Mods[0];

        Assert.True(mod.IsModKit);
        Assert.Equal("Test Fish", mod.Name);
        Assert.Equal("1.0.0", mod.PackageVersion);
        Assert.Equal("v1.0.0", mod.ModKitVersionLine);
        Assert.Equal("Mod Author", mod.Author);
        Assert.Equal("by Mod Author", mod.ModKitAuthorLine);
        Assert.Contains("Adds a custom fish", mod.Description);
        Assert.Equal(Path.GetFullPath(pkgDir), mod.SourceFolder);
        Assert.Contains(pkgDir, mod.SourceFolderLine);
        Assert.Contains(pkgDir, mod.Summary);
    }

    [AvaloniaFact]
    public async Task AddInvalidModKitFolderSurfacesFriendlyErrorMessageWithoutStackTrace()
    {
        using var files = new TestFiles(customize: CustomizeForModKit);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();

        List<string> errors = [];
        vm.Error += errors.Add;

        // 1. Missing jamm-mod.json in an empty directory
        string emptyDir = Path.Combine(files.Root, "empty-folder");
        Directory.CreateDirectory(emptyDir);
        await vm.AddAsync([emptyDir]);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Mods);
        var err1 = Assert.Single(errors);
        Assert.Contains("Missing 'jamm-mod.json'", err1);
        Assert.DoesNotContain("at Jamm.", err1); // No stack trace!

        // 2. Invalid JSON in jamm-mod.json
        errors.Clear();
        string badJsonDir = Path.Combine(files.Root, "bad-json");
        Directory.CreateDirectory(badJsonDir);
        File.WriteAllText(Path.Combine(badJsonDir, "jamm-mod.json"), "{ invalid: 123");
        await vm.AddAsync([badJsonDir]);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Mods);
        var err2 = Assert.Single(errors);
        Assert.Contains("Invalid JSON in 'jamm-mod.json'", err2);
        Assert.DoesNotContain("at Jamm.", err2);

        // 3. Missing sprite referenced in manifest
        errors.Clear();
        string missingSpriteDir = Path.Combine(files.Root, "missing-sprite");
        ModKitPackage.Scaffold(missingSpriteDir, "fish");
        File.Delete(Path.Combine(missingSpriteDir, "art", "sample_fish.png"));
        await vm.AddAsync([missingSpriteDir]);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Mods);
        var err3 = Assert.Single(errors);
        Assert.Contains("$.fish[0].sprite", err3);
        Assert.Contains("was not found inside the package directory", err3);
        Assert.DoesNotContain("at Jamm.", err3);
    }

    [AvaloniaFact]
    public async Task RebuildRefreshesModContentAndMetadataAfterAuthorEdits()
    {
        using var files = new TestFiles(customize: CustomizeForModKit);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();

        string pkgDir = Path.Combine(files.Root, "editable-fish");
        ModKitPackage.Scaffold(pkgDir, "fish");
        await vm.AddAsync([pkgDir]);
        Dispatcher.UIThread.RunJobs();

        var initial = vm.Mods[0];
        Assert.Equal("1.0.0", initial.PackageVersion);

        // Edit jamm-mod.json
        string manifestPath = Path.Combine(pkgDir, ModKitPackage.ManifestFileName);
        string json = File.ReadAllText(manifestPath);
        json = json.Replace("\"version\": \"1.0.0\"", "\"version\": \"2.0.0\"")
                   .Replace("\"author\": \"Mod Author\"", "\"author\": \"Master Angler\"")
                   .Replace("\"description\": \"Adds a custom fish created with JAMM ModKit.\"",
                            "\"description\": \"Version 2.0 with deeper water spawning.\"");
        File.WriteAllText(manifestPath, json);

        await vm.RebuildModAsync(initial.Id);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Rebuilt", vm.Status);
        Assert.Single(vm.Mods);
        var updated = vm.Mods[0];
        Assert.Equal("2.0.0", updated.PackageVersion);
        Assert.Equal("Master Angler", updated.Author);
        Assert.Equal("Version 2.0 with deeper water spawning.", updated.Description);
        Assert.True(updated.IsModKit);
    }

    [AvaloniaFact]
    public async Task RebuildExplainsWhenFolderIsGoneOrDeleted()
    {
        using var files = new TestFiles(customize: CustomizeForModKit);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();

        List<string> errors = [];
        vm.Error += errors.Add;

        string pkgDir = Path.Combine(files.Root, "deletable-fish");
        ModKitPackage.Scaffold(pkgDir, "fish");
        await vm.AddAsync([pkgDir]);
        Dispatcher.UIThread.RunJobs();

        var mod = vm.Mods[0];

        // Delete the source folder
        Directory.Delete(pkgDir, true);

        await vm.RebuildModAsync(mod.Id);
        Dispatcher.UIThread.RunJobs();

        var err = Assert.Single(errors);
        Assert.Contains("was not found", err);
        Assert.Contains("Has it been moved or deleted?", err);
        Assert.DoesNotContain("at Jamm.", err);
    }

    [AvaloniaFact]
    public async Task RebuildPromptsAndSavesSourceFolderIfMissingFromMetadata()
    {
        using var files = new TestFiles(customize: CustomizeForModKit);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();

        string pkgDir = Path.Combine(files.Root, "re-linked-fish");
        ModKitPackage.Scaffold(pkgDir, "fish");
        await vm.AddAsync([pkgDir]);
        Dispatcher.UIThread.RunJobs();

        var mod = vm.Mods[0];

        // Simulate a mod that didn't have its SourceFolder set yet in mod.json
        string modMetaPath = Path.Combine(files.Store.Root, "mods", mod.Id, "mod.json");
        string metaJson = File.ReadAllText(modMetaPath).Replace(Path.GetFullPath(pkgDir), "");
        File.WriteAllText(modMetaPath, metaJson);
        await vm.InitializeAsync(); // reload

        bool prompted = false;
        await vm.RebuildModAsync(mod.Id, promptFolder: () =>
        {
            prompted = true;
            return Task.FromResult<string?>(pkgDir);
        });
        Dispatcher.UIThread.RunJobs();

        Assert.True(prompted);
        Assert.Contains("Rebuilt", vm.Status);
        var reloaded = vm.Mods[0];
        Assert.Equal(Path.GetFullPath(pkgDir), reloaded.SourceFolder);
    }

    [AvaloniaFact]
    public async Task MainWindowRendersModKitBadgesButtonsAndHelpText()
    {
        using var files = new TestFiles(customize: CustomizeForModKit);
        var vm = new MainViewModel(files.Store, gameSearch: (_, _) => []);
        await vm.InitializeAsync();

        var window = new MainWindow(false) { DataContext = vm };
        try
        {
            window.Show();
            void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            Layout();

            // 1. Empty state: Help text should be visible when there are no mods
            var textBlocks = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(textBlocks, t => t.Text == "Made your own mod with ModKit? Add its folder.");

            // 2. Buttons: "+ Add mods" and "Add mod folder (ModKit)…" must be present
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Contains(buttons, b => Equals(b.Content, "+ Add mods"));
            Assert.Contains(buttons, b => Equals(b.Content, "Add mod folder (ModKit)…"));

            // 3. Add a ModKit mod and re-layout
            string pkgDir = Path.Combine(files.Root, "gui-fish");
            ModKitPackage.Scaffold(pkgDir, "fish");
            await vm.AddAsync([pkgDir]);
            Layout();

            var rows = window.GetVisualDescendants().OfType<ModListRow>().ToArray();
            var row = Assert.Single(rows);

            // ModKit badge in row header
            var rowBadges = row.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == "ModKit").ToArray();
            Assert.NotEmpty(rowBadges);

            // Expand row details
            row.IsExpanded = true;
            Layout();

            // Rebuild button in details
            var rebuildButton = row.FindControl<Button>("RebuildButton");
            Assert.NotNull(rebuildButton);
            Assert.True(rebuildButton.IsEffectivelyVisible);
            Assert.Equal("Rebuild", rebuildButton.Content);

            // Details should include author line, version line
            var detailsText = row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains(detailsText, t => t != null && t.Contains("Mod Author"));
            Assert.Contains(detailsText, t => t != null && t.Contains("v1.0.0"));
        }
        finally
        {
            window.Close();
        }
    }
}
