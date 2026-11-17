using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jamm.Core.Management;

namespace Jamm.Gui.ViewModels;

/// <summary>A one-click bundled mod with controls visible before generation.</summary>
public sealed class BuiltinRow(BuiltinMod definition) : ObservableObject
{
    private bool enabled;
    /// <summary>Stable generator key.</summary>
    public string Id => definition.Id;
    /// <summary>Display name.</summary>
    public string Name => definition.Name;
    /// <summary>Description including hotkeys.</summary>
    public string Description => definition.Description;
    internal void Refresh(bool value) { Enabled = value; OnPropertyChanged(nameof(Enabled)); }
    /// <summary>Whether enabled in the active profile.</summary>
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
}

public sealed partial class MainViewModel
{
    /// <summary>Bundled mods presented after a base is configured.</summary>
    public ObservableCollection<BuiltinRow> Builtins { get; } = new(BuiltinMods.Catalog.Select(d => new BuiltinRow(d)));
    /// <summary>Whether a configured base makes bundled mods available.</summary>
    public bool HasBuiltins => Config.Base is not null;

    /// <summary>Formatted hotkeys and console commands help text.</summary>
    public string HotkeysHelpText => BuiltinMods.HotkeysHelpText;

    /// <summary>Single source of truth for in-game hotkeys and console commands.</summary>
    public IReadOnlyList<BuiltinHotkey> Hotkeys => BuiltinMods.Hotkeys;

    /// <summary>Shows the wide group above the simple mod list.</summary>
    public bool ShowBuiltinHeader => HasBuiltins && !AdvancedMode;

    private void RefreshBuiltins()
    {
        foreach (var row in Builtins)
            row.Refresh(Mods.FirstOrDefault(m => m.Id == "builtin-" + row.Id)?.Enabled ?? false);
        OnPropertyChanged(nameof(HasBuiltins));
        OnPropertyChanged(nameof(ShowBuiltinHeader));
    }

    /// <summary>Generates and imports a bundled mod, then saves enablement in the active profile.</summary>
    public Task ToggleBuiltinAsync(string id) => RunAsync("Preparing built-in mod", async (token, progress) =>
    {
        try
        {
            var row = Builtins.Single(r => r.Id == id);
            var existing = Mods.FirstOrDefault(m => m.Id == "builtin-" + id);
            if (existing?.Enabled == true) existing.Enabled = false;
            else
            {
                await Task.Run(() => new BuiltinMods(store).Add(id, progress.Report, token), token);
                await ReloadAsync();
                Mods.Single(m => m.Id == "builtin-" + id).Enabled = true;
            }
            await ApplyPendingAsync(progress);
            RefreshBuiltins();
            Conflicts.Clear();
            Message($"{row.Name}: {(row.Enabled ? "enabled" : "disabled")}. Check/Install or Play to apply.");
        }
        finally { RefreshBuiltins(); }
    });
}
