using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Jamm.Core.Management;
using Jamm.Gui.ViewModels;

namespace Jamm.Gui;

/// <summary>Desktop entry point using OS theme selection and the shared manager store.</summary>
public sealed partial class App : Application
{
    /// <inheritdoc/>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] args = desktop.Args ?? [];
            int index = Array.IndexOf(args, "--data-dir");
            var window = new MainWindow();
            desktop.MainWindow = window;
            try { window.DataContext = new MainViewModel(new ManagerStore(index >= 0 && index + 1 < args.Length ? args[index + 1] : null)); }
            catch (Exception ex) { window.Content = new Avalonia.Controls.TextBlock { Text = "Cannot open manager storage: " + ex.Message, Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap }; }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
