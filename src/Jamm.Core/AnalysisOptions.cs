namespace Jamm.Core;

/// <summary>Bounds independent analysis and texture work and controls the disposable manager analysis cache.</summary>
public sealed record AnalysisOptions
{
    /// <summary>Maximum workers, leaving one core free by default. Set to one for sequential work.</summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
    /// <summary>Disables reusable base analysis, for diagnostics and sequential comparisons.</summary>
    public bool UseCache { get; init; } = true;
    /// <summary>Manager data directory override; otherwise CGFMM_DATA_DIR or the XDG default is used.</summary>
    public string? DataDirectory { get; init; }
    /// <summary>Optional phase timing observer, called serially with elapsed wall time.</summary>
    public Action<string, TimeSpan>? Timing { get; init; }
    internal ParallelOptions Parallel => new() { MaxDegreeOfParallelism = MaxDegreeOfParallelism > 0
        ? MaxDegreeOfParallelism : throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism), "Use at least one analysis worker.") };
    internal static AnalysisOptions Default => new()
    {
        MaxDegreeOfParallelism = int.TryParse(Environment.GetEnvironmentVariable("CGFMM_ANALYSIS_WORKERS"), out int n) && n > 0
            ? n : Math.Max(1, Environment.ProcessorCount - 1),
        UseCache = Environment.GetEnvironmentVariable("CGFMM_ANALYSIS_CACHE") != "0"
    };
}
