using System.Text.Json;
using Jamm.Core;
using Jamm.Core.Building;

namespace Jamm.Cli;

/// <summary>Code audits for instance references and suspicious untyped asset-index literals.</summary>
internal static class AuditCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--asset-refs") return AssetReferences(args[1..]);
        if (args.Length < 2 || args[0] != "instances")
            throw new ArgumentException("Use: cgfmm audit instances <data.win> [--base <original.win>] [--json <file>] [--all], or cgfmm audit --asset-refs <mod-or-build.win> --base <original.win> [--json <file>]");
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

    private static int AssetReferences(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Use: cgfmm audit --asset-refs <mod-or-build.win> --base <original.win> [--json <file>]");
        string archive = Path.GetFullPath(args[0]);
        string? original = null, json = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--base" && i + 1 < args.Length && original is null) original = Path.GetFullPath(args[++i]);
            else if (args[i] == "--json" && i + 1 < args.Length && json is null) json = Path.GetFullPath(args[++i]);
            else throw new ArgumentException($"Unknown or repeated option '{args[i]}'. Use --base <original.win> and --json <file>.");
        }
        if (original is null) throw new ArgumentException("Asset-reference audit needs --base <original.win> so it can identify mod-added asset indices.");
        if (!File.Exists(archive)) throw new FileNotFoundException($"'{archive}' was not found. Pass a mod or merged data.win to audit.");
        if (!File.Exists(original)) throw new FileNotFoundException($"Base archive '{original}' was not found. Pass the exact archive used to build the mod.");
        if (json is not null) OutputFile.Validate(json, [archive, original]);
        using var baseFile = GameFileLoader.Load(original);
        using var modFile = GameFileLoader.Load(archive);
        var findings = AssetLiteralAudit.Analyze(baseFile, modFile);
        if (json is not null)
            OutputFile.Write(json, JsonSerializer.Serialize(findings, new JsonSerializerOptions { WriteIndented = true }) + "\n", [archive, original]);
        foreach (var finding in findings)
            Console.WriteLine($"WARNING: {finding.Mod} / {finding.Code}:{finding.Line} container {finding.Container}, integer {finding.Value}; candidate families: {string.Join(", ", finding.CandidateFamilies)}");
        Console.WriteLine($"Asset-reference audit: {findings.Count} integer literal(s) match added or shifted asset indices. Review asset-like contexts; unrelated numeric constants are reported as candidates.");
        return 0;
    }
}
