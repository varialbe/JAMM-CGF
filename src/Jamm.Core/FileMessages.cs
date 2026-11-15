using System.Text.RegularExpressions;

namespace Jamm.Core;

internal static partial class FileMessages
{
    // Exceptions from file I/O may contain the input path. Only the leaf name belongs in reports.
    internal static string WithoutPaths(string message, string input)
    {
        try
        {
            string full = Path.GetFullPath(input);
            message = message.Replace(full, Path.GetFileName(full), StringComparison.Ordinal);
        }
        catch (ArgumentException) { /* The original failure may itself be an invalid path. */ }
        // Quoted paths are commonly supplied by the runtime and UTML. Also handle unquoted paths.
        return AbsolutePath().Replace(message, match => Path.GetFileName(match.Value.Replace('\\', '/')));
    }

    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|/)[^\s'""<>:;]+")]
    private static partial Regex AbsolutePath();
}

internal sealed class AssetAnalysisException(string asset, Exception inner)
    : Exception($"Could not fingerprint {asset}.", inner)
{
    internal string Asset { get; } = asset;
}
