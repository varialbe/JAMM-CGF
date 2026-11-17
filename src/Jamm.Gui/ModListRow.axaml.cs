using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Jamm.Gui;

/// <summary>A compact mod row with independent expansion and pending enablement controls.</summary>
public sealed partial class ModListRow : UserControl
{
    /// <summary>Creates the mod row.</summary>
    public ModListRow() => InitializeComponent();

    /// <summary>Whether the mod's artwork and compatibility summary are shown.</summary>
    public bool IsExpanded
    {
        get => Details.IsVisible;
        set { Details.IsVisible = value; Chevron.Text = value ? "⌃" : "⌄"; }
    }

    private void HeaderClick(object? sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;

    private void HeaderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        IsExpanded = !IsExpanded;
        e.Handled = true;
    }

    private async void RebuildClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ModRow row && this.FindAncestorOfType<MainWindow>() is { } window)
            await window.RebuildModAsync(row);
    }
}
