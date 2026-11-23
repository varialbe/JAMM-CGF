namespace Jamm.Core.Browsing;

/// <summary>Evaluates first-party catalog claims against the user's game base and selected profile.</summary>
public static class FirstPartyCompatibilityEvaluator
{
    /// <summary>Returns Compatible, Warning or Incompatible without treating untested combinations as proven safe.</summary>
    public static FirstPartyCompatibilityResult Evaluate(CatalogMod mod, string? baseHash,
        IEnumerable<string>? activeModIds = null, string jammVersion = "0.9.1")
    {
        var compat = mod.Compat;
        if (compat is null) return new("Warning", "This package has no published compatibility data.");
        if (string.IsNullOrWhiteSpace(baseHash)) return new("Warning", "Configure a game base to check this package.");
        if (!string.IsNullOrWhiteSpace(compat.RequiresBase)
            && !string.Equals(baseHash, compat.RequiresBase, StringComparison.OrdinalIgnoreCase))
            return new("Incompatible", "This package requires a different legacy game base.");
        if (!string.IsNullOrWhiteSpace(compat.JammMin) && CompareVersions(jammVersion, compat.JammMin) < 0)
            return new("Incompatible", $"This package requires JAMM {compat.JammMin} or newer.");

        var active = new HashSet<string>(activeModIds ?? [], StringComparer.OrdinalIgnoreCase);
        string[] conflicts = (compat.Conflicts ?? []).Where(active.Contains).ToArray();
        if (conflicts.Length > 0)
            return new("Incompatible", "Conflicts with active profile mod(s): " + string.Join(", ", conflicts) + ".");
        string[] missing = (compat.Requires ?? []).Where(id => !active.Contains(id)).ToArray();
        if (missing.Length > 0)
            return new("Warning", "Requires active profile mod(s): " + string.Join(", ", missing) + ".");
        if (compat.KnownIssues is { Length: > 0 })
            return new("Warning", string.Join(" ", compat.KnownIssues));
        return new("Compatible", "Supported legacy base; no published profile conflicts.");
    }

    private static int CompareVersions(string left, string right)
    {
        if (!Version.TryParse(left.TrimStart('v', 'V'), out var current)
            || !Version.TryParse(right.TrimStart('v', 'V'), out var minimum)) return -1;
        return current.CompareTo(minimum);
    }
}
