using System.Text.Json;
using Jamm.Core.Building;

namespace Jamm.Cli;

/// <summary><c>cgfmm audit instances</c>: lists code that dereferences objects which may have no active instance.</summary>
internal static class AuditCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 2 || args[0] != "instances")
            throw new ArgumentException("Use: cgfmm audit instances <data.win> [--json <file>] [--all]");
        string archive = Path.GetFullPath(args[1]);
        string? json = null;
        bool all = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--json" && i + 1 < args.Length && json is null) json = args[++i];
            else if (args[i] == "--all" && !all) all = true;
            else throw new ArgumentException($"Unknown or repeated option '{args[i]}'. Use --json <file> or --all.");
        }
        if (!File.Exists(archive)) throw new FileNotFoundException($"'{archive}' was not found. Pass the path of a data.win to audit.");
        var refs = InstanceGuard.Audit(archive, message => Console.Error.Write($"\r{message}".PadRight(60)));
        Console.Error.WriteLine();
        if (json is not null)
            File.WriteAllText(json, JsonSerializer.Serialize(refs, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Instance audit of {Path.GetFileName(archive)}: {refs.Count} risky reference(s), {refs.Count(r => r.Guarded)} guarded by JAMM builds.");
        foreach (var group in refs.GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()} in {group.Select(r => r.Code).Distinct().Count()} code entries; objects: {string.Join(", ", group.Select(r => r.Object).Distinct().Take(20))}");
            foreach (var r in all ? group : group.Take(5))
                Console.WriteLine($"    {r.Code}:{r.Line}  {r.Snippet}");
        }
        return 0;
    }
}
