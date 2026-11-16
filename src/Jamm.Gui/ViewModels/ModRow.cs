using CommunityToolkit.Mvvm.ComponentModel;

namespace Jamm.Gui.ViewModels;

/// <summary>Library identity, compatibility and profile enablement without exposing game payloads.</summary>
public sealed class ModRow(string id, string name, string badge, bool cached, long size) : ObservableObject
{
    private bool enabled;
    /// <summary>Immutable library ID.</summary>
    public string Id { get; } = id;
    /// <summary>User-facing display name.</summary>
    public string Name { get; } = name;
    /// <summary>Base compatibility description.</summary>
    public string Badge { get; } = badge;
    /// <summary>Readable archive size and cache state.</summary>
    public string Metadata { get; } = $"{size / 1048576.0:0.0} MB · Patch {(cached ? "cached ✓" : "missing")}";
    /// <summary>Whether this mod participates in the active profile.</summary>
    public bool Enabled { get => enabled; set => SetProperty(ref enabled, value); }
}
