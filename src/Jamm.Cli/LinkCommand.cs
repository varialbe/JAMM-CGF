using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Cli;

internal static class LinkCommand
{
    public static int Run(string[] args, string? dataDirectory)
    {
        bool auto = false;
        string? profile = null;
        List<string> positional = [];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--auto") { auto = true; continue; }
            if (args[i] == "--profile")
            {
                if (++i == args.Length) throw new ArgumentException("Option '--profile' needs a name.");
                profile = args[i]; continue;
            }
            if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown link option '{args[i]}'.");
            positional.Add(args[i]);
        }
        if (auto == (positional.Count > 0) || (auto && positional.Count != 0) || (!auto && positional.Count != 2))
            throw new ArgumentException("Use 'cgfmm link --auto [--profile <name>]' or 'cgfmm link <mod> <catalog-id>'.");
        if (profile is not null && !auto) throw new ArgumentException("--profile is only valid with 'link --auto'.");

        var store = new ManagerStore(dataDirectory);
        using var http = new ModHttp();
        var snapshot = new FirstPartyCatalogProvider(store, http).LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        var library = new ModLibrary(store);
        if (!auto)
        {
            var mod = library.Find(positional[0]);
            var entry = snapshot.Mods.FirstOrDefault(m => m.Catalog == "first-party" && m.Id == positional[1])
                ?? throw new ArgumentException($"Catalog ID '{positional[1]}' was not found in the first-party catalog.");
            CatalogOriginLinker.Link(library, mod, entry, "explicit");
            Console.WriteLine($"Linked {mod.Name} to {entry.Name} ({entry.Id}) by explicit selection.");
            return 0;
        }

        var mods = library.Metadata();
        if (profile is not null)
        {
            var selected = new ProfileLibrary(store).Get(profile).EnabledMods.ToHashSet(StringComparer.Ordinal);
            mods = mods.Where(m => selected.Contains(m.Id)).ToArray();
        }
        int linked = 0;
        foreach (var mod in mods)
        {
            if (mod.Id.StartsWith("builtin-", StringComparison.Ordinal))
            {
                Console.WriteLine($"Skipped {mod.Name} ({mod.Id}); built-in mods are generated locally and cannot be linked.");
                continue;
            }
            if (mod.Origins.Any(o => o.Catalog == "first-party")) continue;
            var match = CatalogOriginLinker.Match(mod, snapshot);
            if (match.Mod is { } entry)
            {
                CatalogOriginLinker.Link(library, mod, entry, match.Reason);
                Console.WriteLine($"Linked {mod.Name} to {entry.Name} ({entry.Id}) by {match.Reason}.");
                linked++;
            }
            else if (match.Ambiguous) Console.WriteLine($"Not linked: {mod.Name}; {match.Reason}. Use 'cgfmm link <mod> <catalog-id>' to choose explicitly.");
            else Console.WriteLine($"Not linked: {mod.Name}; no unambiguous catalog match. Use 'cgfmm link <mod> <catalog-id>' to choose explicitly.");
        }
        if (linked == 0 && mods.Count == 0) Console.WriteLine("No mods to link.");
        return 0;
    }
}
