using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jamm.Gui;

/// <summary>Choice returned by the Add mod dialog.</summary>
public enum AddModChoice
{
    /// <summary>Select one or more .win or .zip files via file picker.</summary>
    Files,
    /// <summary>Select a ModKit package folder containing jamm-mod.json via folder picker.</summary>
    Folder
}

/// <summary>Dialog offering mod file picker, ModKit folder picker, and newcomer help text.</summary>
public sealed class AddModDialog : Window
{
    /// <summary>Action button to pick .win or .zip files.</summary>
    public Button AddFilesButton { get; }
    /// <summary>Action button to pick a ModKit package folder.</summary>
    public Button AddFolderButton { get; }
    /// <summary>Button to open the ModKit getting started documentation.</summary>
    public Button GuideButton { get; }
    /// <summary>Cancel button.</summary>
    public Button CancelButton { get; }
    /// <summary>Help text describing ModKit folder packages.</summary>
    public TextBlock HelpTextBlock { get; }

    /// <summary>Creates the Add mod dialog.</summary>
    public AddModDialog()
    {
        Title = "Add mods · JAMM";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };

        panel.Children.Add(new TextBlock
        {
            Text = "Add mods to JAMM",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Select packaged mod archives (.win, .zip) or choose a ModKit package folder to build and import.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" }
        });

        var optionsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };

        AddFilesButton = new Button
        {
            Content = "Add mod files (.win, .zip)…",
            Margin = new Thickness(0, 0, 10, 8),
            Padding = new Thickness(14, 8)
        };
        AddFilesButton.Click += (_, _) => Close(AddModChoice.Files);
        optionsPanel.Children.Add(AddFilesButton);

        AddFolderButton = new Button
        {
            Content = "Add mod folder (ModKit)…",
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14, 8)
        };
        AddFolderButton.Click += (_, _) => Close(AddModChoice.Folder);
        optionsPanel.Children.Add(AddFolderButton);

        panel.Children.Add(optionsPanel);

        var helpPanel = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current?.FindResource("SystemControlForegroundBaseLowBrush") as IBrush,
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var helpStack = new StackPanel { Spacing = 8 };

        HelpTextBlock = new TextBlock
        {
            Text = "Made your own mod with ModKit? Add its folder.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Medium
        };
        helpStack.Children.Add(HelpTextBlock);

        GuideButton = new Button
        {
            Content = "Getting started guide ↗",
            FontSize = 12,
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        GuideButton.Click += async (_, _) =>
        {
            string guidePath = MainWindow.FindModKitGuidePath();
            if (File.Exists(guidePath) && Launcher is { } launcher)
            {
                await launcher.LaunchUriAsync(new Uri(Path.GetFullPath(guidePath)));
            }
            else if (Launcher is { } webLauncher)
            {
                await webLauncher.LaunchUriAsync(new Uri("https://github.com/varialbe/JAMM-CGF/blob/experimental/docs/modding/getting-started.md"));
            }
        };
        helpStack.Children.Add(GuideButton);
        helpPanel.Child = helpStack;
        panel.Children.Add(helpPanel);

        CancelButton = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        CancelButton.Click += (_, _) => Close(null);
        panel.Children.Add(CancelButton);

        Content = panel;
    }
}
