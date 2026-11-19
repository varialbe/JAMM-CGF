using UndertaleModLib;
using Jamm.Core.Patching;

namespace Jamm.Core.Acceptance;

/// <summary>Canonical content item kinds tracked in a pack acceptance report.</summary>
public static class AcceptanceKind
{
    /// <summary>Mod-added GameMaker object asset.</summary>
    public const string Object = "object";
    /// <summary>Mod-added sprite asset.</summary>
    public const string Sprite = "sprite";
    /// <summary>Mod-added sound asset.</summary>
    public const string Sound = "sound";
    /// <summary>Mod-added room asset.</summary>
    public const string Room = "room";
    /// <summary>Mod-added script asset.</summary>
    public const string Script = "script";
    /// <summary>Mod-added fish catalog entry (legacy <c>CatalogData</c> or JAMM API).</summary>
    public const string Fish = "fish";
    /// <summary>Mod-added boat table or <c>jamm_boat_register</c> entry.</summary>
    public const string Boat = "boat";
    /// <summary>Mod-added rod table or <c>jamm_rod_register</c> entry.</summary>
    public const string Rod = "rod";
    /// <summary>Mod-added lure table or <c>jamm_lure_register</c> entry.</summary>
    public const string Lure = "lure";
    /// <summary>Mod-added tool table or <c>jamm_tool_register</c> entry.</summary>
    public const string Tool = "tool";
    /// <summary>Mod-added hat (<c>GearData</c>) table or <c>jamm_hat_register</c> entry.</summary>
    public const string Hat = "hat";
    /// <summary>Mod-added upgrade table or <c>jamm_upgrade_register</c> entry.</summary>
    public const string Upgrade = "upgrade";
    /// <summary>Mod-added or mod-increased object instance placement count in a room.</summary>
    public const string Placement = "placement";
    /// <summary>Mod code conflict hunk discarded by build resolution.</summary>
    public const string Hunk = "hunk";
}

/// <summary>Arrival status of a single mod content item in the merged archive.</summary>
public static class AcceptanceStatus
{
    /// <summary>The item arrived in the merged archive with matching identity and position/ownership.</summary>
    public const string Present = "Present";
    /// <summary>The item did not arrive in the merged archive or was discarded by conflict resolution.</summary>
    public const string Lost = "Lost";
    /// <summary>An asset with this identity exists in the merged archive, but another mod's version replaced it.</summary>
    public const string ModifiedByOther = "Modified-by-other";
    /// <summary>The item arrived in the merged archive at a different catalog coordinate or table slot index.</summary>
    public const string Relocated = "Relocated";
}

/// <summary>One mod-contributed content item and its arrival outcome in the merged archive.</summary>
public sealed record ContentItem(string Kind, string Identity, string Status, string Detail);

/// <summary>A pack-level runtime risk check warning discovered in the merged archive or plan.</summary>
public sealed record RuntimeRisk(string Code, string Severity, string Target, string Message);

/// <summary>Per-mod acceptance checklist and summary counts.</summary>
public sealed record ModAcceptance(
    string Mod,
    string FileName,
    IReadOnlyList<ContentItem> Items,
    int ApiRegistrationCount = 0)
{
    /// <summary>Items with status <see cref="AcceptanceStatus.Present"/>.</summary>
    public int PresentCount => Items.Count(i => i.Status == AcceptanceStatus.Present);

    /// <summary>Items with status <see cref="AcceptanceStatus.Relocated"/>.</summary>
    public int RelocatedCount => Items.Count(i => i.Status == AcceptanceStatus.Relocated);

    /// <summary>Items that arrived intact (<see cref="AcceptanceStatus.Present"/> or <see cref="AcceptanceStatus.Relocated"/>).</summary>
    public int ArrivedCount => Items.Count(i => i.Status is AcceptanceStatus.Present or AcceptanceStatus.Relocated);

    /// <summary>Items with status <see cref="AcceptanceStatus.ModifiedByOther"/>.</summary>
    public int ModifiedByOtherCount => Items.Count(i => i.Status == AcceptanceStatus.ModifiedByOther);

    /// <summary>Items with status <see cref="AcceptanceStatus.Lost"/>.</summary>
    public int LostCount => Items.Count(i => i.Status == AcceptanceStatus.Lost);

    /// <summary>Total tracked items for this mod.</summary>
    public int TotalCount => Items.Count;
}

/// <summary>Machine-checkable per-mod acceptance report and pack-level runtime risks for a build.</summary>
public sealed record PackAcceptanceReport(
    IReadOnlyList<ModAcceptance> Mods,
    IReadOnlyList<RuntimeRisk> Risks,
    IReadOnlyDictionary<string, int>? ApiRegistrations = null);

/// <summary>Input mod archive and optional prepared patch for pack acceptance analysis.</summary>
public sealed record AcceptanceModInput(
    string Name,
    string FileName,
    UndertaleData Data,
    ModPatch? Patch = null);
