using CommunityToolkit.Mvvm.ComponentModel;

namespace Jamm.Gui.ViewModels;

/// <summary>Library identity, compatibility and profile enablement without exposing game payloads.</summary>
public sealed class ModRow(string id, string name, string badge, bool cached, long size) : ObservableObject
{
    private bool enabled;
    private Avalonia.Media.IImage? icon;
    private bool baseline;
    /// <summary>Whether enablement differs from the saved selection.</summary>
    public bool IsPending => Enabled != baseline;
    /// <summary>Accessible switch label.</summary>
    public string EnableName => "Enable " + Name;
    internal bool SavedEnabled => baseline;
    internal void AcceptChanges() { baseline = Enabled; OnPropertyChanged(nameof(IsPending)); }
    internal void UndoChanges() => Enabled = baseline;
    /// <summary>Catalog artwork, or the view's neutral geometric placeholder.</summary>
    public Avalonia.Media.IImage? Icon { get => icon; internal set => SetProperty(ref icon, value); }
    /// <summary>Computed additions, provenance and compatibility for the expanded row.</summary>
    private string summary = "Content summary unavailable. Check this mod in Browse mods.";
    /// <summary>Notifies the view when automatic facts arrive.</summary>
    public string Summary { get => summary; internal set => SetProperty(ref summary, value); }
    /// <summary>Catalog identity used to associate artwork without matching display names.</summary>
    public string? CatalogId { get; internal set; }
    /// <summary>Immutable library ID.</summary>
    public string Id { get; } = id;
    /// <summary>User-facing display name.</summary>
    public string Name { get; } = name;
    /// <summary>Base compatibility description.</summary>
    public string Badge { get; } = badge;
    /// <summary>Readable archive size and cache state.</summary>
    public string Metadata { get; } = $"{size / 1048576.0:0.0} MB · Patch {(cached ? "cached ✓" : "missing")}";
    /// <summary>Whether this mod participates in the active profile.</summary>
    public bool Enabled { get => enabled; set { if (SetProperty(ref enabled, value)) { OnPropertyChanged(nameof(IsPending)); OnPropertyChanged(nameof(ToggleLabel)); OnPropertyChanged(nameof(ToggleColor)); } } }
    /// <summary>Explicit action for the current state.</summary>
    public string ToggleLabel => Enabled ? "Disable" : "Enable";
    /// <summary>Green enabled and grey disabled state.</summary>
    public string ToggleColor => Enabled ? "#258549" : "#656565";
}
