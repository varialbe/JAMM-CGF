using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>Reports instance references in decompilable code without modifying the archive.</summary>
public static class InstanceReferenceAnalyzer
{
    /// <summary>Analyzes instance references. Supply the original archive to classify added objects.</summary>
    public static IReadOnlyList<InstanceReference> Analyze(string archivePath, string? baseArchivePath = null,
        Action<string>? progress = null, Action<string>? warning = null)
    {
        using var loaded = GameFileLoader.Load(archivePath, progress);
        using var ancestor = baseArchivePath is null ? null : GameFileLoader.Load(baseArchivePath, progress);
        var facts = InstanceGuard.ObjectFacts.From(loaded.Data, ancestor?.Data);
        var texts = CodeDecompiler.ReadAll(loaded.Data, Path.GetFileName(archivePath), warning ?? (_ => { }),
            AssetSymbols.Ambiguous([SemanticReferences.Names(loaded.Data)]), AnalysisOptions.Default, progress);
        List<InstanceReference> result = [];
        foreach (var (name, text) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
            if (text.Parent is null && text.Format == "gml")
                InstanceGuard.Rewrite(name, text.Text, facts, result, out _);
        return result;
    }
}
