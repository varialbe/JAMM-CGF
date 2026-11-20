using System.Text.Json;
using System.Text.Json.Serialization;
using Jamm.Core.Patching;

namespace Jamm.Core.Merging;

/// <summary>A concrete value from a participating mod; absent is distinct from JSON null.</summary>
public sealed record ModValue(string Mod, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonConverter(typeof(OptionalJsonConverter))] JsonElement? Value);

/// <summary>One overlapping field, including ancestor and all compared versions.</summary>
public sealed record FieldConflict(string Path, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonConverter(typeof(OptionalJsonConverter))] JsonElement? Base, IReadOnlyList<ModValue> Versions);

/// <summary>A deterministic finding. Kinds distinguish list/index, binary, deletion and reference problems.</summary>
public sealed record ConflictItem(string Severity, string Kind, string Family, string Asset,
    IReadOnlyList<string> Mods, string Message, IReadOnlyList<FieldConflict>? Fields = null,
    string? SuggestedResolution = null, IReadOnlyList<string>? Sources = null)
{
    /// <summary>Ancestor group qualifier for profile conflicts.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResolutionPrefix { get; init; }
    /// <summary>Profile resolution key for this asset.</summary>
    [JsonIgnore]
    public string ResolutionKey => $"{ResolutionPrefix}{Family}/{Asset}";
    /// <summary>Key of the code merge in the enclosing report.</summary>
    [JsonIgnore]
    public string CodeMergeKey => ResolutionPrefix + Asset;
}

/// <summary>Per-mod summary in the original load order.</summary>
public sealed record ModSummary(string Name, string FileName, string Sha256, int Added, int Changed, int Removed, int Warnings);

/// <summary>A field merge preview. Conflicting values provisionally follow load order; findings retain the required decisions.</summary>
public sealed record FieldMerge(string Family, string Asset, JsonElement Snapshot, bool RequiresDecision);

/// <summary>A portable report and reusable code/field results; it contains no game model types.</summary>
public sealed record ConflictReport(string BaseSha256, IReadOnlyList<ModSummary> LoadOrder,
    IReadOnlyList<ConflictItem> Findings, SortedDictionary<string, MergeResult> CodeMerges,
    IReadOnlyList<FieldMerge> FieldMerges)
{
    /// <summary>Finding counts, including explicit zero counts.</summary>
    public SortedDictionary<string, int> Counts => new(new[] { "CONFLICT", "AUTO", "INFO", "WARNING" }
        .ToDictionary(s => s, s => Findings.Count(f => f.Severity == s)), StringComparer.Ordinal);
    /// <summary>Exit status for a successfully completed check.</summary>
    public int ExitCode => Findings.Any(f => f.Severity == "CONFLICT") ? 3 : 0;
}
