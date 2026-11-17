using CommunityToolkit.Mvvm.ComponentModel;

namespace Jamm.Gui.ViewModels;

/// <summary>Library identity, compatibility and profile enablement without exposing game payloads.</summary>
public sealed class ModRow(string id, string name, string badge, bool cached, long size) : ObservableObject
{
    private bool enabled;
    private Avalonia.Media.IImage? icon;
    private bool baseline;
    private bool keepAuthorPerformanceChanges;
    private bool keepAuthorHudDesign;
    private int removedPerformanceCount;
    private int removedHudCount;
    private string sanitizedDetails = "";
    private bool isJammMod;
    private bool isModKit;
    private string? sourceFolder;
    private string? packageVersion;
    private string? author;
    private string? description;
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
    /// <summary>Whether this mod was built by JAMM's own ModMaker (obj_jamm_*).</summary>
    public bool IsJammMod { get => isJammMod; internal set { if (SetProperty(ref isJammMod, value)) { OnPropertyChanged(nameof(CanKeepAuthorPerformanceChanges)); OnPropertyChanged(nameof(CanKeepAuthorHudDesign)); } } }
    /// <summary>Number of third-party performance changes removed by JAMM's sanitizer.</summary>
    public int RemovedPerformanceCount { get => removedPerformanceCount; internal set { if (SetProperty(ref removedPerformanceCount, value)) { OnPropertyChanged(nameof(HasSanitizedInfo)); OnPropertyChanged(nameof(SanitizedLine)); } } }
    /// <summary>Number of third-party HUD/UI asset changes reverted by JAMM's sanitizer.</summary>
    public int RemovedHudCount { get => removedHudCount; internal set { if (SetProperty(ref removedHudCount, value)) { OnPropertyChanged(nameof(HasSanitizedInfo)); OnPropertyChanged(nameof(SanitizedLine)); } } }
    /// <summary>True when JAMM removed at least one third-party performance or HUD change from this mod.</summary>
    public bool HasSanitizedInfo => RemovedPerformanceCount > 0 || RemovedHudCount > 0;
    /// <summary>Short summary line for removed performance and/or HUD changes.</summary>
    public string SanitizedLine => (RemovedPerformanceCount, RemovedHudCount) switch
    {
        ( > 0, 0) => $"JAMM removed {RemovedPerformanceCount} performance changes",
        (0, > 0) => $"JAMM removed {RemovedHudCount} HUD changes",
        ( > 0, > 0) => $"JAMM removed {RemovedPerformanceCount} performance changes, {RemovedHudCount} HUD changes",
        _ => ""
    };
    /// <summary>Detailed list of removed performance and HUD changes for the info flyout.</summary>
    public string SanitizedDetails { get => sanitizedDetails; internal set => SetProperty(ref sanitizedDetails, value); }
    /// <summary>Whether the per-mod switch to keep author performance changes is enabled.</summary>
    public bool CanKeepAuthorPerformanceChanges => !IsJammMod;
    /// <summary>Whether the per-mod switch to keep author HUD design is enabled.</summary>
    public bool CanKeepAuthorHudDesign => !IsJammMod;
    /// <summary>Advanced per-mod opt-out that keeps the author's performance changes instead of sanitizing.</summary>
    public bool KeepAuthorPerformanceChanges { get => keepAuthorPerformanceChanges; set => SetProperty(ref keepAuthorPerformanceChanges, value); }
    /// <summary>Advanced per-mod opt-out that keeps the author's HUD design instead of reverting to base HUD art.</summary>
    public bool KeepAuthorHudDesign { get => keepAuthorHudDesign; set => SetProperty(ref keepAuthorHudDesign, value); }
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
    /// <summary>Whether this mod was built or imported from a ModKit package.</summary>
    public bool IsModKit { get => isModKit; set => SetProperty(ref isModKit, value); }
    /// <summary>Source folder path on disk for ModKit folder mods.</summary>
    public string? SourceFolder { get => sourceFolder; set => SetProperty(ref sourceFolder, value); }
    /// <summary>Package version from jamm-mod.json.</summary>
    public string? PackageVersion { get => packageVersion; set => SetProperty(ref packageVersion, value); }
    /// <summary>Package author from jamm-mod.json.</summary>
    public string? Author { get => author; set => SetProperty(ref author, value); }
    /// <summary>Package description from jamm-mod.json.</summary>
    public string? Description { get => description; set => SetProperty(ref description, value); }
    /// <summary>True when Author is non-empty.</summary>
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    /// <summary>True when Description is non-empty.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    /// <summary>Formatted version line for ModKit mods.</summary>
    public string ModKitVersionLine => !string.IsNullOrWhiteSpace(PackageVersion) ? $"v{PackageVersion}" : "";
    /// <summary>Formatted author line for ModKit mods.</summary>
    public string ModKitAuthorLine => !string.IsNullOrWhiteSpace(Author) ? $"by {Author}" : "";
    /// <summary>Formatted source folder line.</summary>
    public string SourceFolderLine => !string.IsNullOrWhiteSpace(SourceFolder) ? $"Folder: {SourceFolder}" : "";
}
