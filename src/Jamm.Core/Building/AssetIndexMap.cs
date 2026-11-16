namespace Jamm.Core.Building;

/// <summary>Maps indices by exact asset identity, including removals and overlapping additions.</summary>
public sealed class AssetIndexMap
{
    private readonly IReadOnlyList<string> source;
    private readonly Dictionary<string, int> merged;

    /// <summary>Builds an index translation without changing either input table.</summary>
    public AssetIndexMap(IReadOnlyList<string> sourceNames, IReadOnlyList<string> mergedNames)
    {
        source = sourceNames;
        merged = mergedNames.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);
    }

    /// <summary>Resolves an input index, refusing missing assets instead of reusing an unrelated slot.</summary>
    public int Resolve(int index, string family, string context)
    {
        if (index < 0 || index >= source.Count)
            throw new InvalidDataException($"Invalid {family} index {index} in {context}. Correct the typed reference in the source mod.");
        return merged.TryGetValue(source[index], out int result) ? result
            : throw new InvalidDataException($"Dangling reference in {context}: {family}/{source[index]} is missing from the merged build. Keep that asset or select another code version with --resolve.");
    }
}
