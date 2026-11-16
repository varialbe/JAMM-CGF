using System.Diagnostics;

namespace Jamm.Core;

internal sealed class AnalysisPhase(AnalysisOptions options, string name) : IDisposable
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    public void Dispose() => options.Timing?.Invoke(name, clock.Elapsed);
}
