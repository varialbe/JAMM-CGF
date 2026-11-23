using System.Text.Json;
using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Cli;

internal static class UpdateCommand
{
    public static int Run(string[] args, string? dataDirectory)
    {
        string? profile = null;
        bool json = false, check = false, apply = false, rollback = false, yes = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--check": check = true; break;
                case "--apply": apply = true; break;
                case "--rollback": rollback = true; break;
                case "--yes": yes = true; break;
                case "--json": json = true; break;
                case "--profile":
                    if (++i >= args.Length) throw new ArgumentException("Option '--profile' needs a name.");
                    profile = args[i]; break;
                default: throw new ArgumentException($"Unknown update option '{args[i]}'.");
            }
        }
        var store = new ManagerStore(dataDirectory);
        using var http = new ModHttp();
        var catalog = BrowseCatalogs.Create(store, http);
        var resolver = new GameJoltDownloadResolver(http);
        var workflow = new ModUpdateWorkflow(store, catalog, resolver, http);
        if (rollback)
        {
            string save = workflow.Rollback(profile);
            Console.WriteLine($"Restored updater packages and previous data.win. Save backup: {(string.IsNullOrEmpty(save) ? "unavailable" : save)}");
            if (!string.IsNullOrEmpty(save)) { Console.Write("Restore this save backup now? [y/N] "); if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true) RestoreSave(save, store.Config(), store.Root); }
            return 0;
        }
        if (apply)
        {
            if (!yes && !Environment.UserInteractive) throw new ArgumentException("Use 'cgfmm update --apply --yes' in non-interactive mode.");
            var prepared = workflow.PrepareAsync(profile).GetAwaiter().GetResult();
            var reasons = prepared.Conflicts.Where(c => c.Reasons.Count > 0).ToArray();
            Console.WriteLine(reasons.Length == 0 ? $"No save conflicts detected. Save backup: {(prepared.SaveBackup.Length == 0 ? "unavailable" : prepared.SaveBackup)}" : "Save conflict warning:");
            foreach (var (item, why) in reasons) Console.WriteLine($"  {item.Name}: {string.Join("; ", why)}");
            Console.WriteLine($"Save backup: {(prepared.SaveBackup.Length == 0 ? "could not be made; verify your Wine/Proton prefix" : prepared.SaveBackup)}");
            bool approved = yes;
            if (!approved) { Console.Write("Install these updates? [y/N] "); approved = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true; }
            if (!approved) { new ProfileInstaller(store).Discard(prepared.Build); workflow.Rollback(); Console.WriteLine("Update cancelled. The game was not replaced."); return 1; }
            workflow.Install(prepared.Build);
            Console.WriteLine("Updates installed and verified. Use 'cgfmm update --rollback' to undo.");
            return 0;
        }
        if (!check) throw new ArgumentException("Use 'cgfmm update --check [--profile <name>] [--json]', --apply, or --rollback.");
        var updates = workflow.CheckAsync(profile).GetAwaiter().GetResult();
        var selectedProfile = new ProfileLibrary(store).Get(profile ?? store.Config().ActiveProfile
            ?? throw new ArgumentException("No active profile. Use 'profile use <name>' or pass --profile <name>."));
        var enabled = selectedProfile.EnabledMods.ToHashSet(StringComparer.Ordinal);
        var unlinked = new ModLibrary(store).Metadata().Where(m => enabled.Contains(m.Id) && !m.Origins.Any()).ToArray();
        if (json) Console.WriteLine(JsonSerializer.Serialize(new { updates = updates.Select(x => new
        {
            x.Id, x.InstalledVersion, x.AvailableVersion, x.SizeBytes, x.Changelog
        }), unlinkedMods = unlinked.Select(m => new { m.Id, m.Name }) }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        else if (updates.Count == 0 && unlinked.Length == 0) Console.WriteLine("All catalog mods in this profile are up to date.");
        else
        {
            foreach (var item in updates)
                Console.WriteLine($"{item.Name} ({item.Id}): {item.InstalledVersion ?? "unknown"} → {item.AvailableVersion} · {(item.SizeBytes is { } size ? $"{size / 1048576d:0.0} MB" : "size unknown")}{(string.IsNullOrWhiteSpace(item.Changelog) ? "" : $"\n  {item.Changelog}")}");
            if (updates.Count > 0) Console.WriteLine("Updates are available.");
            if (unlinked.Length > 0)
            {
                Console.WriteLine("Not linked to a catalog: " + string.Join(", ", unlinked.Select(m => m.Name)) + ". Run 'cgfmm link --auto'.");
            }
        }
        return updates.Count == 0 ? 0 : 10;
    }

    private static void RestoreSave(string backup, ManagerConfig config, string storeRoot)
    {
        string? local = SaveGamePaths.LocalAppData(config, storeRoot);
        string from = Path.Combine(backup, "Cat_Goes_Fishing"), to = Path.Combine(local ?? "", "Cat_Goes_Fishing");
        if (string.IsNullOrEmpty(local) || !Directory.Exists(from)) { Console.WriteLine("Save restore is unavailable; copy the backup manually."); return; }
        if (Directory.Exists(to)) Directory.Delete(to, true);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);
        Console.WriteLine("Save backup restored.");
    }
}
