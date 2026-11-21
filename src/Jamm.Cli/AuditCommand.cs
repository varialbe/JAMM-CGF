using System.Text.Json;
using Jamm.Core.Building;

namespace Jamm.Cli;

/// <summary><c>cgfmm audit instances</c>: lists code that dereferences objects which may have no active instance.</summary>
internal static class AuditCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 2 || args[0] != "instances")
            throw new ArgumentException("Use: cgfmm audit instances <data.win> [--base <original.win>] [--json <file>] [--all]");
        string archive = Path.GetFullPath(args[1]);
        string? json = null;
        string? original = null;
        bool all = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json" && i + 1 < args.Length && json is null) json = args[++i];
            else if (args[i] == "--base" && i + 1 < args.Length && original is null) original = args[++i];
            else if (args[i] == "--all" && !all) all = true;
            else throw new ArgumentException($"Unknown or repeated option '{args[i]}'. Use --base <original.win>, --json <file> or --all.");
        }
        if (!File.Exists(archive)) throw new FileNotFoundException($"'{archive}' was not found. Pass the path of a data.win to audit.");
        string[] inputs = original is null ? [archive] : [archive, original];
        if (json is not null) OutputFile.Validate(json, inputs);
        var refs = InstanceReferenceAnalyzer.Analyze(archive, original,
            message => Console.Error.Write($"\r{message}".PadRight(60)), message => Console.Error.WriteLine($"\nWarning: {message}"));
        Console.Error.WriteLine();
        if (json is not null)
            OutputFile.Write(json, JsonSerializer.Serialize(refs, new JsonSerializerOptions { WriteIndented = true }), inputs);
        int safeSelectors = refs.Count(r => r.Kind == "with-object");
        Console.WriteLine($"Instance audit of {Path.GetFileName(archive)}: {refs.Count - safeSelectors} risky reference(s), {refs.Count(r => r.Guarded)} guarded by JAMM builds, {safeSelectors} safe with-object selector(s).");
        foreach (var group in refs.GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()} in {group.Select(r => r.Code).Distinct().Count()} code entries; objects: {string.Join(", ", group.Select(r => r.Object).Distinct().Take(20))}");
            foreach (var r in all ? group : group.Take(5))
                Console.WriteLine($"    {r.Code}:{r.Line}  {r.Snippet}");
        }
        return 0;
    }
}
