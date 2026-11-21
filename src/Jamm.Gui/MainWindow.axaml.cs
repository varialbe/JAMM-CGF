using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui;

/// <summary>The desktop shell; delegates all state changes and archive work to its view model.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Calculates window size and position clamped and centered within the target screen working area.</summary>
    public static (double Width, double Height, PixelPoint Position) CalculateBounds(
        PixelRect workingArea, double scaling, double requestedWidth, double requestedHeight, double minWidth = 960, double minHeight = 560)
    {
        scaling = scaling > 0 ? scaling : 1.0;
        double workWidth = workingArea.Width / scaling;
        double workHeight = workingArea.Height / scaling;
        if (workWidth <= 0 || workHeight <= 0)
            return (requestedWidth, requestedHeight, new PixelPoint(0, 0));

        double effectiveMinWidth = Math.Min(minWidth, workWidth);
        double effectiveMinHeight = Math.Min(minHeight, workHeight);

        double targetWidth = requestedWidth > 0 ? requestedWidth : 1200;
        double targetHeight = requestedHeight > 0 ? requestedHeight : 800;

        double clampedWidth = Math.Clamp(targetWidth, effectiveMinWidth, workWidth);
        double clampedHeight = Math.Clamp(targetHeight, effectiveMinHeight, workHeight);

        int pixelWidth = (int)Math.Round(clampedWidth * scaling);
        int pixelHeight = (int)Math.Round(clampedHeight * scaling);
        int posX = workingArea.X + Math.Max(0, (workingArea.Width - pixelWidth) / 2);
        int posY = workingArea.Y + Math.Max(0, (workingArea.Height - pixelHeight) / 2);

        return (clampedWidth, clampedHeight, new PixelPoint(posX, posY));
    }

    /// <summary>Clamps the window size to fit the screen's working area and centers it.</summary>
    public void ClampToScreen(Screen? screen = null)
    {
        screen ??= Screens.ScreenFromWindow(this) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;

        var (w, h, pos) = CalculateBounds(screen.WorkingArea, screen.Scaling, Width, Height, MinWidth, MinHeight);
        if (MinWidth > w) MinWidth = w;
        if (MinHeight > h) MinHeight = h;
        Width = w;
        Height = h;
        Position = pos;
    }

    private async void BuiltinToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Avalonia.Controls.ToggleSwitch { DataContext: BuiltinRow row })
            await vm.ToggleBuiltinAsync(row.Id);
    }

    private async void WeakPcClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { CanEdit: true } vm) await vm.ToggleWeakPcAsync();
    }

    private async void ProfileRepackClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { CanEditProfile: true } vm) await vm.ToggleProfileRepackAsync();
    }

    private async void ProfileBigMoneyClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { CanEditProfile: true } vm) await vm.ToggleProfileBigMoneyAsync();
    }

    private async void ProfilePerfModeClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { CanEditProfile: true } vm)
            await vm.UpdateProfilePerfAsync(startEnabled: !vm.ProfilePerf.StartEnabled);
    }

    private async void ProfileFishGridClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { CanEditProfile: true } vm)
            await vm.UpdateProfilePerfAsync(fishGrid: !vm.ProfilePerf.FishGrid);
    }

    private async void ProfileFishIntervalChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (Model is { CanEditProfile: true } vm && e.NewValue is >= 1 and <= 60 and decimal value
            && value == decimal.Truncate(value) && vm.ProfilePerf.Interval != value)
            await vm.UpdateProfilePerfAsync(interval: (int)value);
    }

    private MainViewModel? Model => DataContext as MainViewModel;
    private bool initialized;
    private Point? dragStart;
    private int dragIndex = -1;
    /// <summary>Creates a window without reading local game files.</summary>
    public MainWindow() : this(true) { }

    /// <summary>Creates a shell with optional initialization for neutral headless rendering.</summary>
    public MainWindow(bool initializeOnOpen)
    {
        InitializeComponent();
        ClampToScreen();
        MainViewModel? errorModel = null;
        DataContextChanged += (_, _) =>
        {
            if (errorModel is not null) errorModel.Error -= ShowError;
            errorModel = Model;
            if (errorModel is not null) errorModel.Error += ShowError;
        };
        Closed += (_, _) => { if (errorModel is not null) errorModel.Error -= ShowError; };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (Model is { CanEdit: true } vm)
            {
                var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
                if (paths.Length > 0) await vm.AddAsync(paths);
            }
        });
        OrderList.AddHandler(PointerPressedEvent, (_, e) => { dragStart = e.GetPosition(OrderList);
            var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
            dragIndex = item?.DataContext is ModRow row && Model is { } vm ? vm.Order.IndexOf(row) : OrderList.SelectedIndex;
            if (dragIndex >= 0) OrderList.SelectedIndex = dragIndex; }, RoutingStrategies.Bubble, true);
        OrderList.AddHandler(PointerReleasedEvent, async (_, e) =>
        {
            if (dragStart is { } start && Math.Abs(e.GetPosition(OrderList).Y - start.Y) > 16 && Model is { } vm)
            {
                var target = (OrderList.InputHitTest(e.GetPosition(OrderList)) as Visual)?.FindAncestorOfType<ListBoxItem>();
                if (target?.DataContext is ModRow row) await vm.MoveAsync(dragIndex, vm.Order.IndexOf(row));
            }
            dragStart = null; dragIndex = -1;
        }, RoutingStrategies.Bubble, true);
        Opened += async (_, _) =>
        {
            ClampToScreen();
            if (!initializeOnOpen || initialized || Model is null) return;
            initialized = true;
            await Model.InitializeAsync();
            // First-run detection and setup actions are shown in the main window.
            if (Model.Config.CheckForUpdates)
            {
                try { await OfficialUpdater.CheckAndOfferAsync(ConfirmAsync); }
                catch (Exception) { Model.ReportError("Could not check official JAMM updates. Check your internet connection and try again."); }
            }
        };
        MainTabs.SelectionChanged += async (_, e) =>
        {
            if (e.Source == MainTabs && MainTabs.SelectedIndex == 1 && Model is { } vm && vm.Browse.Mods.Count == 0 && !vm.Browse.IsLoading) await vm.Browse.RefreshAsync();
        };
        Closed += (_, _) => Model?.Browse.Dispose();
        Closing += (_, e) => { if (Model?.IsBusy == true) { e.Cancel = true; Model.Cancel(); } };
    }
    private async void CardPageClick(object? sender, RoutedEventArgs e) { if (sender is Control { DataContext: CatalogRow row }) await OpenPublicPageAsync(row.Mod.SourcePage); e.Handled = true; }
    private async void CardDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { CanEdit: true } vm || sender is not Control { DataContext: CatalogRow row }) return;
        e.Handled = true;
        if (vm.Browse.Selected != row || vm.Browse.IsLoading) { vm.Browse.Selected = row; await vm.Browse.SelectAsync(); }
        await vm.Browse.DownloadAsync(false, ConfirmAsync);
    }
    private async void ShowError(string message)
    {
        var dialog = new Window { Title = "Could not complete action · JAMM", Width = 600, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = new Button { Content = "OK" };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close } };
        await dialog.ShowDialog(this);
    }
    private async void ChooseGameClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Choose the complete legacy Cat Goes Fishing folder", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path && Model is { } vm) await vm.ChooseGameFolderAsync(path);
    }
    private async void CheckGamesClick(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.SearchGamesAsync(); }
    private void CancelSetupClick(object? sender, RoutedEventArgs e) => Model?.CancelSetup();
    private void FinishSetupClick(object? sender, RoutedEventArgs e) => Model?.FinishSetup();
    private void SkipSetupClick(object? sender, RoutedEventArgs e) => Model?.SkipSetup();
    private void BackToGameSetupClick(object? sender, RoutedEventArgs e) => Model?.BackToGameSetup();
    private async void UseDetectedClick(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.UseDetectedGameAsync(); }
    private async void CopyDepotClick(object? sender, RoutedEventArgs e) { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(Jamm.Core.Management.SteamGameDetection.LegacyCommand); }
    private async void OpenSteamConsoleClick(object? sender, RoutedEventArgs e) { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(Jamm.Core.Management.SteamGameDetection.LegacyCommand); Model?.OpenSteamConsole(); }
    private void UndoChangesClick(object? sender, RoutedEventArgs e) => Model?.UndoChanges();
    private async void ToggleModClick(object? sender, RoutedEventArgs e) { if (Model is { CanEdit: true } vm && sender is Control { DataContext: ModRow row }) await vm.ToggleModAsync(row.Id); }
    private async void BrowseFileChanged(object? sender, SelectionChangedEventArgs e) { if (Model is { } vm && e.AddedItems.Count > 0) await vm.Browse.SelectFileAsync(); }
    private void BrowseCancelClick(object? sender, RoutedEventArgs e) => Model?.Browse.CancelReads();
    private async void BrowseRefreshClick(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.Browse.RefreshAsync(); }
    private async void BrowseSelectionChanged(object? sender, SelectionChangedEventArgs e) { if (Model is { } vm && e.Source == BrowseList && e.AddedItems.Count > 0) await vm.Browse.SelectAsync(); }
    private async void BrowseResolveClick(object? sender, RoutedEventArgs e) { if (Model is { CanEdit: true } vm) await vm.Browse.SelectAsync(); }
    private async void BrowseAddClick(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.Browse.DownloadAsync(false, ConfirmAsync); }
    private async void BrowseInstallClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } vm) return;
        await vm.Browse.DownloadAsync(true, ConfirmAsync);
        if (vm.Conflicts.Any(c => c.Severity == "CONFLICT")) MainTabs.SelectedIndex = 0;
    }
    private async void BrowseSourceClick(object? sender, RoutedEventArgs e) { if (Model?.Browse.Selected?.Mod is { } mod) await OpenPublicPageAsync(mod.FileUrl ?? mod.SourcePage); }
    private async void BrowseCatalogClick(object? sender, RoutedEventArgs e) { if (Model?.Browse.Selected?.Mod is { } mod) await OpenPublicPageAsync(mod.SourcePage); }
    private async Task OpenPublicPageAsync(string url)
    {
        try { await Launcher.LaunchUriAsync(Jamm.Core.Browsing.ModHttp.PublicUri(url)); }
        catch (Exception ex) { await ConfirmAsync("Could not open source page: " + ex.Message); }
    }
    private async void SettingsClick(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.OpenSettingsAsync(() => new SettingsWindow(vm).ShowDialog(this)); }
    private async void AddClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } vm) return;
        await vm.AfterSetupAsync(async () =>
        {
            var dialog = new AddModDialog();
            var choice = await dialog.ShowDialog<AddModChoice?>(this);
            if (choice == AddModChoice.Files)
            {
                var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Add local mods", AllowMultiple = true, FileTypeFilter = [new("Mods (.win, .zip)") { Patterns = ["*.win", "*.zip"] }] });
                var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
                if (paths.Length > 0) await vm.AddAsync(paths);
            }
            else if (choice == AddModChoice.Folder)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Add mod folder (ModKit)", AllowMultiple = false });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folderPath)
                {
                    await vm.AddAsync([folderPath]);
                }
            }
        });
    }

    private async void AddFolderClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } vm) return;
        await vm.AfterSetupAsync(async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Add mod folder (ModKit)", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folderPath)
            {
                await vm.AddAsync([folderPath]);
            }
        });
    }

    internal async Task RebuildModAsync(ModRow row)
    {
        if (Model is not { CanEdit: true } vm) return;
        await vm.RebuildModAsync(row.Id, async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new()
            {
                Title = $"Locate ModKit folder for '{row.Name}'",
                AllowMultiple = false
            });
            return folders.FirstOrDefault()?.TryGetLocalPath();
        });
    }

    private async void LibraryRebuildClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ModRow row })
            await RebuildModAsync(row);
    }

    private async void LibraryToolbarRebuildClick(object? sender, RoutedEventArgs e)
    {
        if (Model?.SelectedMod is { } mod)
            await RebuildModAsync(mod);
    }

    private async void OpenModKitGuideClick(object? sender, RoutedEventArgs e)
    {
        await OpenModKitGuideAsync();
    }

    internal async Task OpenModKitGuideAsync()
    {
        try
        {
            string guidePath = FindModKitGuidePath();
            if (File.Exists(guidePath) && Launcher is { } launcher)
            {
                await launcher.LaunchUriAsync(new Uri(Path.GetFullPath(guidePath)));
                return;
            }
            if (Launcher is { } webLauncher)
            {
                await webLauncher.LaunchUriAsync(new Uri("https://github.com/varialbe/JAMM-CGF/blob/experimental/docs/modding/getting-started.md"));
            }
        }
        catch (Exception ex)
        {
            await ConfirmAsync("Could not open ModKit guide: " + ex.Message);
        }
    }

    internal static string FindModKitGuidePath()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "modding", "getting-started.md"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs", "modding", "getting-started.md"),
            Path.Combine(AppContext.BaseDirectory, "docs", "modding", "getting-started.md"),
            Path.Combine(Directory.GetCurrentDirectory(), "docs", "modding", "getting-started.md")
        ];
        foreach (var path in candidates)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return Path.GetFullPath(candidates[0]);
    }
    private async void RemoveClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { } vm && LibraryList.SelectedItem is ModRow row && await ConfirmAsync($"Remove '{row.Name}' from the local library?\nProfiles that reference it must be deleted first. Your source mod file is kept.")) await vm.RemoveAsync(row.Id);
    }
    private async void ProfileChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Model is { CanEdit: true } vm && ProfilePicker.SelectedItem is string name && name != vm.SelectedProfile) await vm.SelectProfileAsync(name);
    }
    private async void NewProfileClick(object? sender, RoutedEventArgs e) { if (Model is { } vm && await PromptAsync("New profile", "my-mods") is { } name) await vm.CreateProfileAsync(name); }
    private async void RenameProfileClick(object? sender, RoutedEventArgs e) { if (Model is { SelectedProfile: not null } vm && await PromptAsync("Rename profile", vm.SelectedProfile) is { } name) await vm.RenameProfileAsync(name); }
    private async void DeleteProfileClick(object? sender, RoutedEventArgs e) { if (Model is { SelectedProfile: not null } vm && await ConfirmAsync($"Delete profile '{vm.SelectedProfile}'? Installed files and backups are kept.")) await vm.DeleteProfileAsync(); }
    private async void UpClick(object? sender, RoutedEventArgs e) => await MoveSelectedAsync(-1);
    private async void DownClick(object? sender, RoutedEventArgs e) => await MoveSelectedAsync(1);
    private async Task MoveSelectedAsync(int offset)
    {
        if (Model is { } vm && OrderList.SelectedItem is ModRow row)
        {
            int index = vm.Order.IndexOf(row);
            await vm.MoveAsync(index, index + offset);
            OrderList.SelectedItem = row;
        }
    }
    private async void ProfileRoleClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { CanEditProfile: true } vm || OrderList.SelectedItem is not ModRow row) return;
        string[] roles = ["none", "base", "addon"];
        var candidates = vm.Order.Where(other => other.Id != row.Id).ToArray();
        var role = new ComboBox { ItemsSource = new[] { "Vanilla", "Base", "Addon" }, SelectedIndex = Array.IndexOf(roles, row.ProfileRole) };
        var ancestor = new ComboBox
        {
            ItemsSource = candidates.Select(mod => mod.Name).ToArray(),
            PlaceholderText = "Choose a base mod",
            SelectedIndex = Array.FindIndex(candidates, mod => mod.Id == row.BaseId)
        };
        var parent = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Base mod" }, ancestor } };
        var save = new Button { Content = "Save" };
        var cancel = new Button { Content = "Cancel" };
        var dialog = new Window
        {
            Title = $"Profile role · {row.Name}", Width = 420, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 12,
                Children = { new TextBlock { Text = "Role in this profile" }, role, parent,
                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, save } } }
            }
        };
        void Refresh()
        {
            parent.IsVisible = role.SelectedIndex == 2;
            save.IsEnabled = role.SelectedIndex >= 0 && (!parent.IsVisible || ancestor.SelectedIndex >= 0);
        }
        role.SelectionChanged += (_, _) => Refresh();
        ancestor.SelectionChanged += (_, _) => Refresh();
        save.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        Refresh();
        if (await dialog.ShowDialog<bool>(this))
            await vm.SetProfileRoleAsync(row.Id, roles[role.SelectedIndex], parent.IsVisible ? candidates[ancestor.SelectedIndex].Id : null);
    }
    private async void CheckClick(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.CheckAsync(); }
    private async void InstallClick(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.InstallAsync(ConfirmAsync); }
    private async void PlayClick(object? sender, RoutedEventArgs e) { if (Model is { } vm) await vm.PlayAsync(ConfirmAsync); }
    private async void RestoreClick(object? sender, RoutedEventArgs e) { if (Model is { } vm && await ConfirmAsync("Restore the verified original data.win? All backups will be retained.")) await vm.RestoreAsync(); }
    private void CancelClick(object? sender, RoutedEventArgs e) => Model?.Cancel();
    private async void CopyLogClick(object? sender, RoutedEventArgs e) { if (Clipboard is { } clipboard && Model is { } vm) await clipboard.SetTextAsync(vm.Log); }
    private async void DetailsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConflictRow row })
        {
            var dialog = new Window { Title = row.Key + " · Local conflict details", Width = 900, Height = 650, Content = new TextBox { Text = row.Details, IsReadOnly = true, AcceptsReturn = true, FontFamily = "monospace", Margin = new Thickness(16) } };
            await dialog.ShowDialog(this);
        }
    }
    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new Window { Title = "Confirm · JAMM", Width = 600, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new Button { Content = "Confirm", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var no = new Button { Content = "Cancel" };
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 18, Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { no, yes } } } };
        return await dialog.ShowDialog<bool>(this);
    }
    private async Task<string?> PromptAsync(string title, string initial)
    {
        var input = new TextBox { Text = initial };
        var save = new Button { Content = "Save" };
        var dialog = new Window { Title = title, Width = 400, SizeToContent = SizeToContent.Height, Content = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = { input, save } }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        save.Click += (_, _) => dialog.Close(input.Text);
        return await dialog.ShowDialog<string?>(this);
    }
}
