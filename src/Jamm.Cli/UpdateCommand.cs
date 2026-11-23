using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Cli;

internal static class UpdateCommand
{
    public static int Run(string[] args, string? dataDirectory)
    {
        string? profile = null;
        bool json = false, check = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--check": check = true; break;
                case "--json": json = true; break;
                case "--profile":
                    if (++i >= args.Length) throw new ArgumentException("Option '--profile' needs a name.");
                    profile = args[i]; break;
                default: throw new ArgumentException($"Unknown update option '{args[i]}'.");
            }
        }
        if (!check) throw new ArgumentException("Use 'cgfmm update --check [--profile <name>] [--json]'.");
        var store = new ManagerStore(dataDirectory);
        using var http = new ModHttp();
        var catalog = BrowseCatalogs.Create(store, http);
        var updates = new CatalogUpdates(store, catalog).CheckAsync(profile).GetAwaiter().GetResult();
        if (json) Console.WriteLine(JsonSerializer.Serialize(updates.Select(x => new
        {
            x.Id, x.InstalledVersion, x.AvailableVersion, x.SizeBytes, x.Changelog
        }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        else if (updates.Count == 0) Console.WriteLine("All catalog mods in this profile are up to date.");
        else
        {
            foreach (var item in updates)
                Console.WriteLine($"{item.Name} ({item.Id}): {item.InstalledVersion ?? "unknown"} → {item.AvailableVersion} · {(item.SizeBytes is { } size ? $"{size / 1048576d:0.0} MB" : "size unknown")}{(string.IsNullOrWhiteSpace(item.Changelog) ? "" : $"\n  {item.Changelog}")}");
            Console.WriteLine("Updates are available.");
        }
        return updates.Count == 0 ? 0 : 10;
    }
}
