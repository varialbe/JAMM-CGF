using CommunityToolkit.Mvvm.ComponentModel;

namespace Jamm.Gui.ViewModels;

/// <summary>A finding or individual code hunk with reusable, persistent choices.</summary>
public sealed class ConflictRow(string severity, string key, string kind, string mods, string explanation,
    string details, string[] choices, string? choice) : ObservableObject
{
    private string? selectedChoice = choice;
    /// <summary>CONFLICT, AUTO, INFO or WARNING.</summary>
    public string Severity { get; } = severity;
    /// <summary>Qualified asset key, optionally with zero-based ancestor line.</summary>
    public string Key { get; } = key;
    /// <summary>Nature of this finding.</summary>
    public string Kind { get; } = kind;
    /// <summary>Participating display names.</summary>
    public string Mods { get; } = mods;
    /// <summary>Plain-language explanation.</summary>
    public string Explanation { get; } = explanation;
    /// <summary>Local-only, read-only detail text.</summary>
    public string Details { get; } = details;
    /// <summary>Valid winning mods and keep-both when permitted.</summary>
    public string[] Choices { get; } = choices;
    /// <summary>Whether the finding permits an explicit decision.</summary>
    public bool CanChoose => Choices.Length > 0;
    /// <summary>Effective saved selection, including entry-wide inheritance; null follows load order.</summary>
    public string? SelectedChoice { get => selectedChoice; set => SetProperty(ref selectedChoice, value); }
}
