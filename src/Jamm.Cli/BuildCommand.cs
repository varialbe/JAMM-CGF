using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jamm.Core;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;

namespace Jamm.Cli;

internal static class BuildCommand
{
    private static readonly JsonSerializerOptions AcceptanceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static int Run(string command, string[] arguments, bool verbose = false, string? dataDirectory = null)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        List<string> positional = [];
        bool dryRun = false, noRepack = false, strict = false, noBigMoney = false;
        string[] allowed = command switch
        {
            "build" => ["--base", "--order", "--out", "--resolve", "--install", "--acceptance-json", "--profile"],
            "restore" => ["--game"], _ => []
        };
        for (int i = 0; i < arguments.Length; i++)
        {
            string arg = arguments[i];
            if (command == "build" && arg == "--dry-run")
            { if (dryRun) throw new ArgumentException("--dry-run was given twice."); dryRun = true; }
            else if (command == "build" && arg == "--no-big-money")
            { if (noBigMoney) throw new ArgumentException("--no-big-money was given twice."); noBigMoney = true; }
            else if (command == "build" && arg == "--no-repack")
            { if (noRepack) throw new ArgumentException("--no-repack was given twice."); noRepack = true; }
            else if (command == "build" && arg == "--strict")
            { if (strict) throw new ArgumentException("--strict was given twice."); strict = true; }
            else if (allowed.Contains(arg, StringComparer.Ordinal))
            {
                if (++i == arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"{arg} needs a value.");
                if (!options.TryAdd(arg, arguments[i])) throw new ArgumentException($"{arg} was given twice.");
            }
            else if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option '{arg}'.");
            else positional.Add(arg);
        }
        var timer = Stopwatch.StartNew();
        if (command == "verify")
        {
            if (positional.Count != 1) throw new ArgumentException("verify needs exactly one data.win file.");
            var verified = ArchiveVerifier.Verify(positional[0], progress: Progress);
            Console.WriteLine($"Verified '{Path.GetFileName(positional[0])}': {verified.DecompiledEntries} code entries decompile and compile; references valid.");
            Counts(verified.Inspection.Counts);
            Console.WriteLine($"Size: {verified.Inspection.FileSize:N0} bytes; elapsed {timer.Elapsed.TotalSeconds:F3} s.");
            foreach (string warning in verified.Warnings) Console.WriteLine($"WARNING: {warning}");
            return 0;
        }
        if (positional.Count > 0) throw new ArgumentException($"{command} accepts named options only.");
        if (command == "restore")
        {
            if (!options.TryGetValue("--game", out string? game)) throw new ArgumentException("restore needs --game <game dir>.");
            Console.WriteLine($"Restored data.win (SHA-256 {GameInstaller.Restore(game)}). Original backup retained."); return 0;
        }
        string output = options.GetValueOrDefault("--out", Path.Combine("build", "data.win"));
        if (options.TryGetValue("--profile", out string? profile))
        {
            if (options.Keys.Any(key => key is not "--profile" and not "--out"))
                throw new ArgumentException("Use --profile with --out, --no-repack, --dry-run or --strict. Use 'cgfmm apply --profile' to install, and 'profile resolve' to save choices.");
            var store = new ManagerStore(dataDirectory);
            var profiles = new ProfileLibrary(store);
            ManagementCommand.PrintGroup(profiles.Group(profiles.Get(profile)));
            var built = new ManagerOperations(store).Build(profile, output, dryRun, noRepack, strict, Progress);
            foreach (string decision in built.Plan.Decisions) Console.WriteLine($"Decision: {decision}");
            Counts(built.Counts);
            if (dryRun) Console.WriteLine($"Dry run: build passed. Would write '{Path.GetFullPath(output)}'.");
            else
            {
                foreach (string warning in built.Verification!.Warnings) Console.WriteLine($"WARNING: {warning}");
                Console.WriteLine($"Built and verified '{Path.GetFullPath(output)}': {built.SizeBytes:N0} bytes.");
            }
            return 0;
        }
        if (strict) throw new ArgumentException("--strict requires --profile <name>.");
        if (!options.TryGetValue("--base", out string? basePath))
            throw new ArgumentException("build needs --base <data.win>. Pass --order <mod1>,<mod2>,... to include mods.");
        string[] paths = options.TryGetValue("--order", out string? order) ? order.Split(',', StringSplitOptions.TrimEntries) : [];
        if (paths.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("--order contains an empty mod path.");
        string[] protectedPaths = [basePath, .. paths, .. options.TryGetValue("--resolve", out string? resolvePath) ? new[] { resolvePath } : []];
        BuildFiles.ValidateNewOutput(output, protectedPaths, dryRun);
        Dictionary<string, string>? resolutions = resolvePath is null ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(resolvePath))
                ?? throw new InvalidDataException("The resolution file must be a JSON object mapping asset names to mod names or keep-both.");
        string baseHash = BuildFiles.Hash(basePath);
        string? acceptanceJson = options.GetValueOrDefault("--acceptance-json");
        if (acceptanceJson is not null)
            OutputFile.Validate(acceptanceJson, [.. protectedPaths, output]);
        string? install = options.GetValueOrDefault("--install");
        if (install is not null)
            BuildFiles.ValidateNewOutput(output, new[] { "data.win", "data.win.cgfmm-original", "data.win.cgfmm-build.json" }.Select(n => Path.Combine(install, n)), dryRun);
        List<BuildInput> inputs = [];
        for (int i = 0; i < paths.Length; i++)
        {
            ModPatch patch;
            string archive;
            if (paths[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                patch = PatchJson.Deserialize(File.ReadAllText(paths[i]));
                archive = Locate(paths[i], patch);
            }
            else
            {
                archive = paths[i];
                if (dryRun)
                {
                    using var original = GameFileLoader.Load(basePath, Progress);
                    using var mod = GameFileLoader.Load(archive, Progress);
                    patch = PatchBuilder.Create(original, mod, progress: Progress);
                }
                else
                {
                    string cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "patches", Path.GetFileNameWithoutExtension(archive) + ".cgfmm-patch.json");
                    OutputFile.Validate(cache, [.. protectedPaths, output]);
                    patch = DiffCommand.Obtain(basePath, archive, cache, null, false, protectedPaths, out bool reused);
                    Progress($"{patch.Mod.Name}: {(reused ? "reused" : "created")} patch");
                }
            }
            if (patch.Base.Sha256 != baseHash) throw new InvalidDataException($"Patch '{patch.Mod.Name}' requires a different base SHA-256. Regenerate it against the selected --base.");
            inputs.Add(new(patch, archive));
        }
        protectedPaths = [.. protectedPaths, .. inputs.Select(i => i.ArchivePath)];
        BuildFiles.ValidateNewOutput(output, protectedPaths, dryRun);
        if (acceptanceJson is not null)
            OutputFile.Validate(acceptanceJson, [.. protectedPaths, output]);
        if (install is not null) GameInstaller.Validate(install, baseHash, [.. protectedPaths.Skip(1), output], basePath);
        var result = ArchiveBuilder.Build(basePath, inputs, output, resolutions, dryRun, Progress, noRepack: noRepack, bigMoney: !noBigMoney);
        foreach (string decision in result.Plan.Decisions) Console.WriteLine($"Decision: {decision}");
        foreach (var item in result.RemappedReferences ?? new Dictionary<string, int>())
            Console.WriteLine($"Remapped references: {item.Key}: {item.Value} (changed/added source alternatives; conflict selections verified separately)");
        if (inputs.Count > 0)
            Console.WriteLine($"Semantic reference verification: PASS ({result.SemanticEntries} code entries; selected mod hunks and unchanged base).");
        Counts(result.Counts);
        if (result.Acceptance is { } acceptance)
        {
            Console.Write(TextReport.AcceptanceSummary(acceptance));
            if (verbose) Console.Write(TextReport.AcceptanceDetails(acceptance));
            if (acceptanceJson is not null)
            {
                OutputFile.Write(acceptanceJson, JsonSerializer.Serialize(acceptance, AcceptanceJsonOptions) + Environment.NewLine, [.. protectedPaths, output]);
                Console.WriteLine($"Acceptance JSON: {Path.GetFullPath(acceptanceJson)}");
            }
        }
        if (dryRun)
            Console.WriteLine((inputs.Count == 0 ? "Dry run: would copy the original archive to" : "Dry run: merge and compilation passed; would write") +
                $" '{Path.GetFullPath(output)}'" +
                (install is null ? ". No files written." : $" and install in '{Path.GetFileName(install)}'. No files written."));
        else
        {
            foreach (string warning in result.Verification!.Warnings) Console.WriteLine($"WARNING: {warning}");
            Console.WriteLine($"Built and verified '{Path.GetFullPath(output)}': {result.SizeBytes:N0} bytes.");
            if (install is not null)
            {
                GameInstaller.Install(output, install, baseHash, protectedPaths.Skip(1), basePath, result.Plan.Catalog);
                Console.WriteLine("Installed verified build; SHA-256 verified original backup retained.");
            }
        }
        Console.WriteLine($"Decisions: {result.Plan.Decisions.Count}; elapsed {timer.Elapsed.TotalSeconds:F3} s. In-game desktop testing is still required.");
        return 0;
    }

    internal static string Locate(string patchPath, ModPatch patch)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(patchPath))!;
        if (Path.GetFileName(patch.Mod.FileName) != patch.Mod.FileName) throw new InvalidDataException("Patch mod.fileName must be a filename without directories.");
        string? configured = Environment.GetEnvironmentVariable("JAMM_MODS_DIR");
        string parent = Path.GetDirectoryName(directory)!;
        var candidates = new[] { Path.Combine(directory, patch.Mod.FileName), Path.Combine(parent, patch.Mod.FileName),
            Path.Combine(parent, "mods", patch.Mod.Sha256, "data.win"), Path.Combine(parent, "download-cache", patch.Mod.FileName),
            Path.Combine(Environment.CurrentDirectory, patch.Mod.FileName) }
            .Concat(configured is null ? [] : Directory.Exists(configured) ? Directory.EnumerateFiles(configured, "*.win") : [])
            .Distinct(StringComparer.Ordinal);
        foreach (string candidate in candidates)
            if (File.Exists(candidate) && BuildFiles.Hash(candidate) == patch.Mod.Sha256) return candidate;
        throw new FileNotFoundException($"Missing exact mod archive '{patch.Mod.FileName}' (SHA-256 {patch.Mod.Sha256}). Put the .win beside its patch or set JAMM_MODS_DIR to its folder; JSON contains no binary payloads.");
    }

    private static void Progress(string message) => Console.Error.WriteLine(message);
    private static void Counts(IReadOnlyDictionary<AssetKind, int> counts)
    {
        foreach (var (kind, count) in counts.OrderBy(c => c.Key)) Console.WriteLine($"  {kind}: {count}");
    }
}
