using System.Diagnostics;
using System.Text.Json;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Cli;

internal static class CheckCommand
{
    internal static int Run(string[] arguments)
    {
        List<string> paths = [];
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int i = 0; i < arguments.Length; i++)
        {
            string arg = arguments[i];
            if (arg == "--verbose") continue;
            if (arg is "--base" or "--json" or "--details" or "--patches-dir")
            {
                if (++i == arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"{arg} needs a value.");
                if (!options.TryAdd(arg, arguments[i])) throw new ArgumentException($"{arg} was given twice.");
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unknown option '{arg}'.");
            else paths.Add(arg);
        }
        if (paths.Count < 2 || !options.TryGetValue("--base", out string? basePath))
            throw new ArgumentException("check needs at least two mods or patches and --base <data.win>.");
        string[] inputs = [basePath, .. paths];
        string? output = options.GetValueOrDefault("--json");
        if (output is not null) OutputFile.Validate(output, inputs);
        string cacheDir = options.GetValueOrDefault("--patches-dir", Path.Combine(output is null ? "." : Path.GetDirectoryName(Path.GetFullPath(output))!, "patches"));
        string?[] caches = paths.Select(path => IsPatch(path) ? null : Path.Combine(cacheDir,
            Path.GetFileNameWithoutExtension(path) + ".cgfmm-patch.json")).ToArray();
        if (caches.Where(c => c is not null).Distinct(StringComparer.Ordinal).Count() != caches.Count(c => c is not null))
            throw new ArgumentException("Mod filenames share a cache name. Generate distinct patches with diff --out and pass those patches to check.");
        foreach (string cache in caches.OfType<string>())
        {
            OutputFile.Validate(cache, inputs);
            if (output is not null) OutputFile.Validate(output, [cache]);
        }
        var timer = Stopwatch.StartNew();
        string hash = DiffCommand.Hash(basePath);
        List<ModPatch> patches = [];
        for (int i = 0; i < paths.Count; i++)
        {
            ModPatch patch;
            if (IsPatch(paths[i]))
            {
                try { patch = PatchJson.Deserialize(File.ReadAllText(paths[i])); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
                { throw new InvalidDataException($"Cannot read patch '{Path.GetFileName(paths[i])}': {ex.Message} Regenerate it with diff --force.", ex); }
            }
            else
            {
                patch = DiffCommand.Obtain(basePath, paths[i], caches[i]!, null, false, inputs, out bool reused);
                Console.Error.WriteLine($"{patch.Mod.Name}: {(reused ? "reused" : "created")} patch '{Path.GetFileName(caches[i])}'.");
            }
            patches.Add(patch);
        }
        if (patches.Any(p => p.Base.Sha256 != hash))
            throw new InvalidDataException($"Selected --base '{Path.GetFileName(basePath)}' has SHA-256 {hash}. "
                + string.Join("; ", patches.Select(p => $"{p.Mod.Name ?? p.Mod.FileName} targets {p.Base.FileName} (SHA-256 {p.Base.Sha256})"))
                + ". Recompute patches against this same base or choose their exact base archive.");
        var report = ConflictAnalyzer.Analyze(patches);
        timer.Stop();
        if (output is not null)
            OutputFile.Write(output, JsonSerializer.Serialize(new { elapsedSeconds = timer.Elapsed.TotalSeconds, report }, JsonOptions) + "\n", inputs);
        Print(report, timer.Elapsed.TotalSeconds);
        if (options.TryGetValue("--details", out string? asset)) Details(report, patches, asset);
        return report.ExitCode;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static bool IsPatch(string path) => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static void Print(ConflictReport report, double elapsed)
    {
        Console.WriteLine("Load order (later mod wins by default when building):");
        for (int i = 0; i < report.LoadOrder.Count; i++)
        {
            var mod = report.LoadOrder[i];
            Console.WriteLine($"  {i + 1}. {mod.Name}: +{mod.Added} changed {mod.Changed} removed {mod.Removed}; warnings {mod.Warnings}");
        }
        foreach (string status in new[] { "CONFLICT", "AUTO", "INFO", "WARNING" })
        {
            Console.WriteLine($"\n{status} ({report.Counts[status]} findings)");
            Console.WriteLine($"{"Kind",-12} {"Family",-14} {"Asset",-48} Mods / explanation");
            foreach (var item in report.Findings.Where(f => f.Severity == status))
                Console.WriteLine($"{item.Kind,-12} {item.Family,-14} {item.Asset,-48} {string.Join(" + ", item.Mods)}: {item.Message}");
        }
        Console.WriteLine($"\n{string.Join("; ", report.Counts.Select(c => $"{c.Key}: {c.Value}"))}; elapsed {elapsed:F3} s.");
        Console.WriteLine(report.ExitCode == 3 ? "Decisions required. Use --details <asset> to inspect a finding. No game files were written." : "No conflicts found. No game files were written.");
    }

    private static void Details(ConflictReport report, IReadOnlyList<ModPatch> patches, string asset)
    {
        var findings = report.Findings.Where(f => f.Asset == asset).ToArray();
        Console.WriteLine($"\nDetails: {asset}");
        if (report.CodeMerges.TryGetValue(asset, out var code))
        {
            Console.WriteLine("Merged GML preview (unresolved changes have conflict markers):");
            Console.WriteLine(code.Text);
            foreach (var hunk in code.Hunks.Where(h => h.Status == "CONFLICT"))
            {
                Console.WriteLine($"Ancestor line {hunk.Start + 1}; choices: {string.Join(", ", hunk.Choices)}");
                if (hunk.KeepBoth is not null) { Console.WriteLine("Suggested keepBoth insertion:"); Console.WriteLine(hunk.KeepBoth); }
            }
        }
        foreach (var finding in findings)
        {
            Console.WriteLine($"{finding.Severity} ({finding.Kind}) {finding.Family}: {finding.Message}");
            foreach (var field in finding.Fields ?? [])
            {
                Console.WriteLine($"{field.Path} | base: {Value(field.Base)}");
                Console.WriteLine(string.Join(" | ", field.Versions.Select(v => $"{v.Mod}: {Value(v.Value)}")));
            }
            foreach (string source in finding.Sources ?? []) Console.WriteLine($"  {source}");
        }
        if (findings.Length == 0 && !report.CodeMerges.ContainsKey(asset))
            Console.WriteLine("No finding for this asset. Choose a name from the table.");
        if (findings.Any(f => f.Kind == "disassembly"))
            foreach (var (patch, i) in patches.Select((p, i) => (p, i)))
                foreach (var entry in patch.Changes["code"].Added.Concat(patch.Changes["code"].Changed).Where(a => a.Name == asset))
                { Console.WriteLine($"{report.LoadOrder[i].Name} ({entry.ModCode?.Format ?? "missing text"}):"); Console.WriteLine(entry.ModCode?.Text); }
    }
    private static string Value(JsonElement? value) => value?.GetRawText() ?? "<absent>";
}
