namespace Jamm.Core;

/// <summary>Combines read-only loading with a compact fingerprint snapshot, disposing the live model.</summary>
public static class GameFileAnalyzer
{
    /// <summary>Inspects one file, isolating read errors from asset analysis errors.</summary>
    public static FileAnalysis Analyze(string path, bool includeAssets = true, Action<string>? progress = null)
    {
        LoadedGameFile loaded;
        string name = Path.GetFileName(path);
        try
        {
            loaded = GameFileLoader.Load(path, progress);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(name, null, [],
                $"could not read {name}: {FileMessages.WithoutPaths(ex.GetBaseException().Message.TrimEnd('.'), path)}. Check that this is a complete, supported data.win file and that it is readable.",
                new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>(), ex.ToString());
        }
        using (loaded)
            return Fingerprint(loaded, includeAssets, progress);
    }

    /// <summary>Takes a snapshot without disposing the caller-owned, read-only live model.</summary>
    public static FileAnalysis Fingerprint(LoadedGameFile loaded, bool includeAssets = true, Action<string>? progress = null)
    {
        List<string> warnings = [.. loaded.Warnings];
        var inspection = loaded.Inspection;
        try
        {
            IReadOnlyDictionary<AssetKind, IReadOnlyList<AssetFingerprint>> assets =
                new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>();
            if (includeAssets)
            {
                progress?.Invoke($"Fingerprinting {inspection.FileName}");
                assets = new ContentFingerprints(warnings.Add).Snapshot(loaded.Data,
                    skipBytecode: inspection.IsYyc || !inspection.BytecodeSupported);
            }
            return new(inspection.FileName, inspection, warnings, null, assets);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            string asset = ex is AssetAnalysisException failure ? failure.Asset : "assets";
            return new(inspection.FileName, inspection, warnings,
                $"could not analyse {asset} in {inspection.FileName}: {FileMessages.WithoutPaths(ex.GetBaseException().Message.TrimEnd('.'), inspection.FileName)}. Check the file version or try another copy of the mod.",
                new Dictionary<AssetKind, IReadOnlyList<AssetFingerprint>>(), ex.ToString());
        }
    }
}
