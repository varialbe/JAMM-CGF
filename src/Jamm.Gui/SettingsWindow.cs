using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui;

/// <summary>First-run and runner configuration using the same persisted keys as cgfmm config.</summary>
public sealed class SettingsWindow : Window
{
    private readonly Dictionary<string, TextBox> fields = new(StringComparer.Ordinal);
    /// <summary>Creates a settings editor with immediate legacy validation and native pickers.</summary>
    public SettingsWindow(MainViewModel model)
    {
        Title = "Legacy game & runner · JAMM"; Width = 720; Height = 760; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DataContext = model;
        var config = model.Config;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Start with the legacy game", FontSize = 24, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Use a separate copy of the complete August 2023 game folder. Keep the base archive outside the modded folder.", TextWrapping = TextWrapping.Wrap });
        Add("gameDir", "Legacy game folder", config.GameDir, true);
        Add("base", "Read-only base data.win", config.Base, false, true);
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        validation.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.Validation)));
        panel.Children.Add(validation);
        var guide = new Button { Content = "How to get the legacy build ↗" };
        guide.Click += async (_, _) => { if (Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri("https://github.com/varialbe/JAMM-CGF/blob/experimental/docs/legacy-version.md")); };
        panel.Children.Add(guide);
        Add("wine.mode", "Runner: direct (Windows), wine, proton or proton-flatpak", config.Wine.Mode);
        Add("wine.binary", "Wine executable (PATH name or path)", config.Wine.Binary);
        Add("wine.prefix", "Wine prefix (optional)", config.Wine.Prefix, true);
        Add("proton.path", "Proton directory (optional)", config.Proton.Path, true);
        Add("proton.compatDataPath", "Proton compatibility directory (optional)", config.Proton.CompatDataPath, true);
        fields["proton.compatDataPath"].Watermark = "Blank uses active runner default";
        panel.Children.Add(new TextBlock
        {
            Text = "Leave blank to use the active runner default. In Flatpak mode, JAMM uses Steam’s compatibility data folder.",
            Opacity = 0.7,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        Add("proton.clientInstallPath", "Steam installation directory (optional)", config.Proton.ClientInstallPath, true);
        Add("steam.appId", "Steam App ID", config.Steam.AppId);
        var shared = new CheckBox { Content = "Use shared compatibility results (no account, no cookies)", IsChecked = config.UseSharedCompatibilityResults };
        panel.Children.Add(shared);
        var updates = new CheckBox { Content = "Check official GitHub releases on startup; ask before downloading an installer", IsChecked = config.CheckForUpdates };
        panel.Children.Add(updates);
        var save = new Button { Content = "Validate & save settings", HorizontalAlignment = HorizontalAlignment.Left };
        save.Bind(IsEnabledProperty, new Binding(nameof(MainViewModel.CanEdit)));
        save.Click += async (_, _) =>
        {
            var values = fields.ToDictionary(p => p.Key, p => p.Value.Text ?? "");
            values["useSharedCompatibilityResults"] = (shared.IsChecked == true).ToString();
            values["checkForUpdates"] = (updates.IsChecked == true).ToString();
            await model.SaveSettingsAsync(values);
            if (model.Status == "Settings saved.") fields["proton.compatDataPath"].Text = model.Config.Proton.CompatDataPath;
        };
        panel.Children.Add(save);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; status.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.Status))); panel.Children.Add(status);
        Content = new ScrollViewer { Content = panel };
        Opened += async (_, _) => { if (!string.IsNullOrWhiteSpace(fields["base"].Text)) await model.ValidateBaseAsync(fields["base"].Text!); };
        fields["base"].LostFocus += async (_, _) => { if (!model.IsBusy && !string.IsNullOrWhiteSpace(fields["base"].Text)) await model.ValidateBaseAsync(fields["base"].Text!); };
        void Add(string key, string label, string? value, bool folder = false, bool file = false)
        {
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var box = new TextBox { Text = value }; fields[key] = box; row.Children.Add(box);
            box.Bind(IsEnabledProperty, new Binding(nameof(MainViewModel.CanEdit)));
            if (folder || file)
            {
                var browse = new Button { Content = "Browse…" }; Grid.SetColumn(browse, 1); row.Children.Add(browse);
                browse.Bind(IsEnabledProperty, new Binding(nameof(MainViewModel.CanEdit)));
                browse.Click += async (_, _) =>
                {
                    string? path;
                    if (folder) path = (await StorageProvider.OpenFolderPickerAsync(new() { Title = label, AllowMultiple = false })).FirstOrDefault()?.TryGetLocalPath();
                    else path = (await StorageProvider.OpenFilePickerAsync(new() { Title = label, FileTypeFilter = [new("Game archive") { Patterns = ["*.win"] }] })).FirstOrDefault()?.TryGetLocalPath();
                    if (path is null) return;
                    box.Text = path;
                    if (key == "gameDir" && string.IsNullOrWhiteSpace(fields["base"].Text)) fields["base"].Text = System.IO.Path.Combine(path, "data.win");
                    if (key is "base" or "gameDir" && !string.IsNullOrWhiteSpace(fields["base"].Text)) await model.ValidateBaseAsync(fields["base"].Text!);
                };
            }
            panel.Children.Add(row);
        }
    }
}
