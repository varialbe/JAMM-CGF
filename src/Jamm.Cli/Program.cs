using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jamm.Cli;
using Jamm.Core;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
bool verbose = false;
try
{
    return Run(args);
}
catch (Exception ex)
{
    string message = ex.Message;
    foreach (string argument in args.Where(x => !x.StartsWith('-') && x.IndexOfAny(['/', '\\']) >= 0))
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(argument));
        try { message = message.Replace(Path.GetFullPath(argument), name, StringComparison.Ordinal); }
        catch (ArgumentException) { /* Preserve the original usage error for invalid paths. */ }
        message = message.Replace(argument, name, StringComparison.Ordinal);
    }
    Console.Error.WriteLine($"cgfmm: {message}");
    Console.Error.WriteLine("Check the paths and command options. Use 'cgfmm --help' for usage.");
    if (verbose) Console.Error.WriteLine(ex);
    return 1;
}

int Run(string[] arguments)
{
    string? dataDirectory = null;
    List<string> filtered = [];
    for (int i = 0; i < arguments.Length; i++)
    {
        if (arguments[i] == "--") { filtered.AddRange(arguments[i..]); break; }
        if (arguments[i] == "--verbose") { verbose = true; continue; }
        // Preserve management option values even when they look like global options.
        if (arguments[i] is "--name" or "--mods" or "--profile")
        {
            filtered.Add(arguments[i]);
            if (i + 1 < arguments.Length) filtered.Add(arguments[++i]);
            continue;
        }
        if (arguments[i] != "--data-dir") { filtered.Add(arguments[i]); continue; }
        if (dataDirectory is not null) throw new ArgumentException("--data-dir was given twice.");
        if (++i == arguments.Length) throw new ArgumentException("--data-dir needs a directory.");
        dataDirectory = arguments[i];
    }
    arguments = filtered.ToArray();
    if (arguments.Length == 0 || arguments[0] is "--help" or "-h" or "help")
    {
        Console.WriteLine("""
            JAMM-CGF — inspect, diff, check, build, install and restore
            cgfmm compat-scan --base <data.win> --db <compat.db> --export <directory>
            cgfmm diff <original.win> <mod.win> [--name <name>] [--out <file>] [--force] [--json-summary] [--show <asset>] [--verbose]
            cgfmm lineage <vanilla.win> <foundation.win> <legacy-mod.win>
            cgfmm check <mod-or-patch> [...] --base <data.win> [--json <file>] [--details <asset>] [--patches-dir <dir>] [--asset-literals] [--verbose]
            cgfmm check --profile <name> [--strict] [--json <file>] [--details <asset>]
            cgfmm build --base <data.win> [--order <mod-or-patch>,...] [--out <merged.win>] [--resolve <file.json>] [--install <game dir>] [--acceptance-json <file>] [--no-repack] [--no-big-money] [--no-fair-play] [--fish-value-cap <n|0>] [--dry-run] [--verbose]
            cgfmm build --profile <name> [--strict] [--out <merged.win>] [--no-repack] [--dry-run]
            cgfmm config show [--json] | config set <key> <value>
            cgfmm builtin list | builtin add <perf|smooth|hd-lite|console>
            cgfmm add <file.win|file.zip|modkit dir> [--name <name>]
            cgfmm modkit new <dir> [--template fish|upgrade|boat|hat]
            cgfmm modkit check <dir>
            cgfmm modkit build <dir> [--base <data.win>] [--out <file.win>] [--no-repack]
            cgfmm list [--json] | remove <mod ID or name>
            cgfmm profile create <name> --mods a,b,c | list [--json] | show <name> [--json] | use <name> | delete <name>
            cgfmm profile resolve <name> <asset> <mod|keep-both>
            cgfmm profile repack <name> on|off
            cgfmm profile bigmoney <name> on|off
            cgfmm profile fairplay <name> on|off
            cgfmm profile valuecap <name> <n|0>
            cgfmm profile base <name> <mod> | addon <name> <mod> <base> | role <name> <mod> none
            cgfmm profile preset <name> weak-pc|default
            cgfmm profile builtin <name> <id> on|off
            cgfmm profile perf <name> mode|grid on|off
            cgfmm profile perf <name> interval <ticks>
            cgfmm profile unlockfix <profile> <mod> on|off
            cgfmm apply [--profile <name>] [--yes]
            cgfmm play [--profile <name>] [--yes]
            cgfmm restore [--game <game dir>] [--verbose]
            Management storage: --data-dir <dir>, then CGFMM_DATA_DIR, then $XDG_DATA_HOME/cgfmm.
            Config keys: base, gameDir, wine.mode (wine|proton|proton-flatpak), wine.prefix, wine.binary, proton.path,
                         proton.compatDataPath, proton.clientInstallPath, steam.appId, useSharedCompatibilityResults.
            --verbose works with every command; --yes skips the apply/play install confirmation.
            cgfmm verify <data.win> [--verbose]
            cgfmm audit instances <data.win> [--json <file>] [--all]
            cgfmm inspect <data.win> [--verbose]
            cgfmm compare <original> <mod> [<mod> ...] [--json <file>] [--verbose]
            cgfmm phase0 --original <data.win> --mods <file or dir> [--out <report.md>] [--verbose]
            Exit codes: 0 = completed, 1 = input/output or usage failure, 2 = inspect/diff: no comparable VM bytecode, 3 = check: conflicts found.
            """);
        return 0;
    }
    string command = arguments[0];
    if (command == "modkit") return ModKitCommand.Run(arguments[1..], dataDirectory);
    if (command == "compat-scan") return CompatScanCommand.Run(arguments[1..], dataDirectory);
    if (command == "audit") return AuditCommand.Run(arguments[1..]);
    if (command is "builtin" or "config" or "add" or "list" or "remove" or "profile" or "apply" or "play"
        || command == "restore" && !arguments.Contains("--game", StringComparer.Ordinal))
        return ManagementCommand.Run(command, arguments[1..], dataDirectory, verbose);
    if (command is "build" or "restore" or "verify") return BuildCommand.Run(command, arguments[1..], verbose, dataDirectory);
    if (command == "check") return CheckCommand.Run(arguments[1..], dataDirectory);
    if (command == "diff") return DiffCommand.Run(arguments[1..]);
    if (command == "lineage") return LineageCommand.Run(arguments[1..]);
    List<string> positional = [];
    Dictionary<string, string> options = new(StringComparer.Ordinal);
    var allowed = command switch
    {
        "inspect" => Array.Empty<string>(),
        "compare" => new[] { "--json" },
        "phase0" => new[] { "--original", "--mods", "--out" },
        _ => throw new ArgumentException($"Unknown command '{command}'.")
    };
    for (int i = 1; i < arguments.Length; i++)
    {
        if (!arguments[i].StartsWith("--", StringComparison.Ordinal)) positional.Add(arguments[i]);
        else
        {
            string option = arguments[i];
            if (!allowed.Contains(option, StringComparer.Ordinal)) throw new ArgumentException($"Unknown option '{option}'.");
            if (++i == arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Option '{option}' needs a path.");
            if (!options.TryAdd(option, arguments[i])) throw new ArgumentException($"Option '{option}' was given twice.");
        }
    }
    if (command == "inspect")
    {
        if (positional.Count != 1) throw new ArgumentException("inspect needs exactly one input file.");
        var file = Load(positional[0], includeAssets: false);
        Console.Write(TextReport.Inspection(file));
        return CommandExitCodes.ForFiles([file], inspecting: true);
    }
    string originalPath;
    string[] modPaths;
    if (command == "compare")
    {
        if (positional.Count < 2) throw new ArgumentException("compare needs an original and at least one mod file.");
        originalPath = positional[0];
        modPaths = positional.Skip(1).ToArray();
    }
    else
    {
        if (positional.Count != 0 || !options.TryGetValue("--original", out string? original)
            || !options.TryGetValue("--mods", out string? mods))
            throw new ArgumentException("phase0 needs --original <data.win> and --mods <file or dir>.");
        originalPath = original;
        modPaths = Directory.Exists(mods)
            ? Directory.EnumerateFiles(mods).Where(x => string.Equals(Path.GetExtension(x), ".win", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal).ToArray()
            : [mods];
        if (modPaths.Length == 0) throw new ArgumentException($"No .win mod files found in '{Path.GetFileName(Path.TrimEndingDirectorySeparator(mods))}'. Add mod files or select a mod file directly.");
    }
    string[] inputs = [originalPath, .. modPaths];
    string? output = command == "phase0" ? options.GetValueOrDefault("--out", "docs/reports/phase0.md")
        : options.GetValueOrDefault("--json");
    if (output is not null) OutputFile.Validate(output, inputs);
    List<FileAnalysis> files = [Load(originalPath)];
    List<ModComparison> comparisons = [];
    foreach (string path in modPaths)
    {
        var mod = Load(path);
        files.Add(mod);
        if (files[0].Error is null && mod.Error is null)
            comparisons.Add(AssetComparer.Compare(files[0], mod));
    }
    var result = new Phase0Result(files, comparisons);
    if (command == "compare")
    {
        foreach (var file in files.Where(x => x.Error is not null)) Console.WriteLine(file.Error);
        foreach (var comparison in comparisons) Console.Write(TextReport.Comparison(comparison));
        foreach (var file in files.Where(x => x.Warnings.Count > 0))
            Console.WriteLine($"{file.FileName}: {file.Warnings.Count} warning(s); see JSON for details.");
        if (output is not null)
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            OutputFile.Write(output, JsonSerializer.Serialize(result, jsonOptions), inputs);
            Console.WriteLine($"JSON: {Path.GetFileName(output)}");
        }
    }
    else
    {
        OutputFile.Write(output!, TextReport.Markdown(result), inputs);
        Console.Write(TextReport.Summary(result, output!));
    }
    return CommandExitCodes.ForFiles(files);
}

FileAnalysis Load(string path, bool includeAssets = true)
{
    var file = GameFileAnalyzer.Analyze(path, includeAssets, message => Console.Error.WriteLine(message));
    if (verbose && file.Diagnostic is not null) Console.Error.WriteLine(file.Diagnostic);
    return file;
}
