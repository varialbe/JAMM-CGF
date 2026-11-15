using Jamm.Core;

namespace Jamm.Cli;

internal static class CommandExitCodes
{
    internal static int ForFiles(IEnumerable<FileAnalysis> files, bool inspecting = false) =>
        files.Any(x => x.Error is not null) ? 1
        : inspecting && files.Any(x => x.Inspection is { IsYyc: true } or { BytecodeSupported: false }) ? 2 : 0;
}
