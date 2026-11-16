using System.Runtime.ExceptionServices;

namespace Jamm.Core;

internal static class AnalysisWork
{
    internal static void For(int count, AnalysisOptions options, Action<int> work)
    {
        // Capture failures by input index, preserving the sequential path's contextual exception type.
        var errors = new Exception?[count];
        Parallel.For(0, count, options.Parallel, i =>
        {
            try { work(i); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { errors[i] = ex; }
        });
        var error = errors.FirstOrDefault(e => e is OperationCanceledException) ?? errors.FirstOrDefault(e => e is not null);
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
