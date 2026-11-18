using Jamm.Core;

namespace Jamm.Cli;

internal static class LineageCommand
{
    internal static int Run(string[] arguments)
    {
        if (arguments.Length != 3)
            throw new ArgumentException("lineage needs vanilla, foundation, and legacy mod files. See 'cgfmm --help'.");

        FileAnalysis vanilla = Load(arguments[0]);
        FileAnalysis foundation = Load(arguments[1]);
        FileAnalysis legacy = Load(arguments[2]);
        var files = new[] { vanilla, foundation, legacy };
        foreach (var file in files.Where(x => x.Error is not null)) Console.Error.WriteLine(file.Error);
        if (CommandExitCodes.ForFiles(files) != 0) return 1;

        var report = LineageDiagnostic.Compare(vanilla, foundation, legacy);
        Console.WriteLine($"Lineage comparison: {report.VanillaName} -> reference {report.FoundationName} -> {report.LegacyModName}");
        Console.WriteLine("Exact foundation matches are content matches; they do not prove historical ancestry.");
        foreach (LineageClassification classification in Enum.GetValues<LineageClassification>())
        {
            var items = report.Items.Where(x => x.Classification == classification).ToArray();
            Console.WriteLine($"{classification}: {items.Length}");
            foreach (var item in items)
                Console.WriteLine($"  {item.AssetType.Label()}/{item.Name}");
        }
        return 0;
    }

    private static FileAnalysis Load(string path) => GameFileAnalyzer.Analyze(path, progress: Console.Error.WriteLine);
}
