using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace Jamm.Gui.ViewModels;

/// <summary>Resolves optional artwork delivered alongside the application, never from game archives.</summary>
public static class ModIconResolver
{
    /// <summary>Finds catalog or known local artwork and then the default, returning null for the geometric placeholder.</summary>
    public static string? Resolve(string? catalogId, string name, string? directory = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "Assets", "ModIcons");
        string lower = name.ToLowerInvariant();
        string? slug = lower.Contains("abyss") ? "abyss" : lower.Contains("cave") ? "cave" : lower.Contains("smooth") ? "smooth"
            : lower.Contains("hdlite") || lower.Contains("hd lite") ? "hdlite" : lower.Contains("hd") ? "hd" : lower.Contains("perf") ? "perf" : null;
        string? candidate = catalogId is not null && catalogId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? "litterbox-" + catalogId + ".svg"
            : slug is not null ? "local-" + slug + ".svg" : null;
        if (candidate is not null && File.Exists(Path.Combine(directory, candidate))) return Path.Combine(directory, candidate);
        string fallback = Path.Combine(directory, "default.svg");
        return File.Exists(fallback) ? fallback : null;
    }
    /// <summary>Renders the resolved SVG, falling back safely for missing or malformed artwork.</summary>
    public static IImage? Load(string? catalogId, string name, string? directory = null)
    {
        try { return Resolve(catalogId, name, directory) is { } path ? new SvgImage { Source = SvgSource.Load(path) } : null; }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.Xml.XmlException) { return null; }
    }
}
