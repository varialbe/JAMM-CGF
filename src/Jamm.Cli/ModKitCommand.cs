using Jamm.Core.Management;
using Jamm.Core.ModKit;

namespace Jamm.Cli;

internal static class ModKitCommand
{
    internal static int Run(string[] arguments, string? dataDirectory)
    {
        if (arguments.Length == 0 || arguments[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine("""
                JAMM ModKit — scaffold, validate, and build folder mods
                cgfmm modkit new <dir> [--template fish|upgrade|boat|hat]
                cgfmm modkit check <dir>
                cgfmm modkit build <dir> [--base <data.win>] [--out <file.win>]
                """);
            return 0;
        }

        string sub = arguments[0];
        string[] allowed = sub switch
        {
            "new" => ["--template"],
            "check" => [],
            "build" => ["--base", "--out"],
            _ => throw new ArgumentException($"Unknown modkit subcommand '{sub}'. Use new, check, or build.")
        };

        List<string> positional = [];
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int i = 1; i < arguments.Length; i++)
        {
            string arg = arguments[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
            }
            else
            {
                if (!allowed.Contains(arg, StringComparer.Ordinal))
                    throw new ArgumentException($"Unknown option '{arg}' for modkit {sub}.");
                if (++i == arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Option '{arg}' needs a value.");
                if (!options.TryAdd(arg, arguments[i]))
                    throw new ArgumentException($"Option '{arg}' was given twice.");
            }
        }

        if (positional.Count != 1)
            throw new ArgumentException($"modkit {sub} needs exactly one package directory.");

        string dir = positional[0];
        switch (sub)
        {
            case "new":
                string template = options.GetValueOrDefault("--template", "fish");
                string manifest = ModKitPackage.Scaffold(dir, template);
                Console.WriteLine($"Created ModKit '{template}' template at '{Path.GetFullPath(dir)}' ({Path.GetFileName(manifest)}).");
                Console.WriteLine($"Next: edit jamm-mod.json, run 'cgfmm modkit check {dir}', then 'cgfmm modkit build {dir}' or 'cgfmm add {dir}'.");
                return 0;

            case "check":
                var pkg = ModKitPackage.Load(dir);
                Console.WriteLine($"ModKit package '{pkg.Name}' ({pkg.Id} v{pkg.Version}) is valid: " +
                    $"{pkg.Areas.Count} area(s), {pkg.Sprites.Count} sprite recipe(s), {pkg.Fish.Count} fish, {pkg.Boats.Count} boat(s), {pkg.Hats.Count} hat(s), {pkg.Upgrades.Count} upgrade(s).");
                return 0;

            case "build":
                var loaded = ModKitPackage.Load(dir);
                string? basePath = options.GetValueOrDefault("--base") ?? new ManagerStore(dataDirectory).Config().Base;
                if (string.IsNullOrWhiteSpace(basePath))
                    throw new ArgumentException("Set the configured legacy base first ('cgfmm config set base <data.win>') or pass --base <data.win>.");
                string outPath = options.GetValueOrDefault("--out", Path.Combine(Path.GetFullPath(dir), $"{loaded.Id}.win"));
                var result = ModKitBuilder.Build(dir, basePath, outPath, Console.Error.WriteLine);
                Console.WriteLine($"Built and verified '{result.OutputPath}': {result.SpritesAdded} sprite(s), {result.ObjectsAdded} object(s), {result.ScriptsAdded} script(s).");
                return 0;

            default:
                throw new ArgumentException($"Unknown modkit subcommand '{sub}'.");
        }
    }
}
