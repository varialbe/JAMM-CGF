using System.Text.Json.Serialization;

namespace Jamm.Core;

/// <summary>A content hash and matching key; string values use their SHA-256 as the key.</summary>
public sealed record AssetFingerprint(string Name, string Fingerprint);

/// <summary>Read-only metadata extracted from a game file; stores only its file name.</summary>
public sealed record GameInspection(
    string FileName, long FileSize, string Sha256, string GameName, string DisplayName,
    string GameMakerVersion, string DetectedGameMakerVersion, string DetectedBranch,
    byte BytecodeVersion, ulong Timestamp, bool IsYyc, bool BytecodeSupported,
    IReadOnlyDictionary<AssetKind, int> Counts);

/// <summary>A file's inspection, fingerprints, warnings, or an isolated read/analysis failure; no input path.</summary>
public sealed record FileAnalysis(
    string FileName, GameInspection? Inspection, IReadOnlyList<string> Warnings, string? Error,
    [property: JsonIgnore] IReadOnlyDictionary<AssetKind, IReadOnlyList<AssetFingerprint>> Assets,
    [property: JsonIgnore] string? Diagnostic = null);

/// <summary>The relationship of an asset to its counterpart in the original.</summary>
public enum AssetClassification
{
    /// <summary>Same name and content fingerprint.</summary>
    Identical,
    /// <summary>Same name, different content fingerprint.</summary>
    Changed,
    /// <summary>Present only in the mod.</summary>
    Added,
    /// <summary>Present only in the original.</summary>
    Removed
}

/// <summary>A classified asset, exposing hashes rather than game content.</summary>
public sealed record AssetDifference(string Name, AssetClassification Classification,
    string? OriginalFingerprint, string? ModFingerprint);

/// <summary>Counts and complete asset classifications for one asset type.</summary>
public sealed record AssetComparison(AssetKind AssetType, int Identical, int Changed, int Added,
    int Removed, IReadOnlyList<AssetDifference> Assets)
{
    /// <summary>Number of original assets, excluding additions.</summary>
    public int OriginalCount => Identical + Changed + Removed;
}

/// <summary>A mod's compatibility and content comparison with the original.</summary>
public sealed record ModComparison(string OriginalFileName, string ModFileName, bool SameGameMakerVersion,
    bool SameBytecodeVersion, bool SameGameName, bool HasComparableBytecode,
    double? OriginalAssetsUnchangedPercent, double? OriginalCodeUnchangedPercent,
    string Verdict, IReadOnlyList<AssetComparison> AssetTypes);

/// <summary>Serializable results of a comparison or feasibility run; contains no extracted game content.</summary>
public sealed record Phase0Result(IReadOnlyList<FileAnalysis> Files, IReadOnlyList<ModComparison> Comparisons);
