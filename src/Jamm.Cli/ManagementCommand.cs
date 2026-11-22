using System.Text.Json;
using Jamm.Core.Management;

namespace Jamm.Cli;

internal static class ManagementCommand
{
    internal static int Run(string command, string[] arguments, string? dataDirectory, bool verbose = false)
    {
        var store = new ManagerStore(dataDirectory);
        var library = new ModLibrary(store);
        var profiles = new ProfileLibrary(store);
        var installer = new ProfileInstaller(store);
        List<string> positional = [];
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        HashSet<string> flags = new(StringComparer.Ordinal);
        string[] allowedOptions = command switch
        {
            "add" => ["--name"], "profile" => ["--mods"], "apply" or "play" => ["--profile"], _ => []
        };
        string[] allowedFlags = command switch
        {
            "list" or "profile" or "config" => ["--json"], "apply" or "play" => ["--yes"], _ => []
        };
        for (int i = 0; i < arguments.Length; i++)
        {
            string arg = arguments[i];
            if (arg == "--") { positional.AddRange(arguments[(i + 1)..]); break; }
            if (allowedFlags.Contains(arg, StringComparer.Ordinal))
            { if (!flags.Add(arg)) throw new ArgumentException($"{arg} was given twice."); }
            else if (allowedOptions.Contains(arg, StringComparer.Ordinal))
            {
                if (++i == arguments.Length) throw new ArgumentException($"{arg} needs a value.");
                if (!options.TryAdd(arg, arguments[i])) throw new ArgumentException($"{arg} was given twice.");
            }
            else if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option '{arg}'.");
            else positional.Add(arg);
        }
        void Count(int count)
        {
            if (positional.Count != count) throw new ArgumentException($"Invalid arguments for {command}. See 'cgfmm --help'.");
        }
        void Json<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        bool json = flags.Contains("--json");
        switch (command)
        {
            case "builtin":
                if (positional.SequenceEqual(new[] { "list" }))
                {
                    foreach (var mod in BuiltinMods.Catalog) Console.WriteLine($"{mod.Id}: {mod.Name} — {mod.Description}");
                    Console.WriteLine();
                    Console.WriteLine("Hotkeys:");
                    foreach (var h in BuiltinMods.HotkeyEntries)
                        Console.WriteLine($"  {h.Key,-16} : {h.Description}");
                    Console.WriteLine();
                    Console.WriteLine("Console commands:");
                    foreach (var c in BuiltinMods.ConsoleCommands)
                        Console.WriteLine($"  {c.Key,-16} : {c.Description}");
                }
                else if (positional.Count == 2 && positional[0] == "add")
                {
                    var builtinAdded = new BuiltinMods(store).Add(positional[1], Console.WriteLine);
                    Console.WriteLine($"Added {builtinAdded.Mod.Name} ({builtinAdded.Mod.Id}). Enable it in a profile, then Check/Install or Play.");
                }
                else throw new ArgumentException("Use 'cgfmm builtin list' or 'cgfmm builtin add <perf|smooth|hd-lite|console>'.");
                return 0;
            case "config":
                if (positional.Count == 1 && positional[0] == "show")
                {
                    if (json) Json(store.Config());
                    else
                    {
                        var config = store.Config();
                        Console.WriteLine($"Data directory: {store.Root}");
                        foreach (var (key, value) in new (string, string?)[]
                        {
                            ("base", config.Base), ("gameDir", config.GameDir),
                            ("wine.mode", config.Wine.Mode), ("wine.prefix", config.Wine.Prefix), ("wine.binary", config.Wine.Binary),
                            ("proton.path", config.Proton.Path), ("proton.compatDataPath", config.Proton.CompatDataPath),
                            ("proton.clientInstallPath", config.Proton.ClientInstallPath), ("steam.appId", config.Steam.AppId),
                            ("activeProfile", config.ActiveProfile)
                        }) Console.WriteLine($"{key} = {value ?? "(unset)"}");
                    }
                }
                else if (positional.Count == 3 && positional[0] == "set" && !json)
                {
                    var updated = store.Set(positional[1], positional[2]);
                    Console.WriteLine(positional[1] == "proton.compatDataPath" && updated.Proton.CompatDataPath is null
                        ? $"Cleared {positional[1]} (using default)."
                        : $"Set {positional[1]}.");
                }
                else throw new ArgumentException("Use config show [--json] or config set <key> <value>.");
                break;
            case "add":
                Count(1);
                var added = library.Add(positional[0], options.GetValueOrDefault("--name"), Progress);
                Console.WriteLine($"{(added.AlreadyAdded ? "Already added" : "Added")} {added.Mod.Name} ({added.Mod.Id})."
                    + (added.Mod.Role is { Role: "addon" } ? $" {added.Mod.Role.Label}." : " Patch cached."));
                foreach (string warning in added.Warnings) Console.Error.WriteLine($"WARNING: {warning}");
                break;
            case "list":
                Count(0);
                var list = library.List();
                if (json) Json(list);
                else if (list.Count == 0) Console.WriteLine("No mods yet. Use 'cgfmm add <file.win or file.zip>'.");
                else foreach (var entry in list)
                    Console.WriteLine($"{entry.Mod.Id}  {entry.Mod.Name} [{entry.Mod.Role?.Label ?? "Vanilla"}]: {entry.Compatibility}; patch {(entry.PatchCached ? "cached" : "missing or stale")}."
                        + (entry.Mod.SuggestBase && entry.Mod.Role is null ? " Possible base mod." : ""));
                break;
            case "remove":
                Count(1); library.Remove(positional[0]); Console.WriteLine($"Removed '{positional[0]}' from the library. Backups retained."); break;
            case "profile":
                if (positional.Count == 0) throw new ArgumentException("Choose a profile action. See 'cgfmm --help'.");
                string action = positional[0];
                if (options.Count > 0 && action != "create" || json && action is not "show" and not "list")
                    throw new ArgumentException("--mods is for profile create; --json is for profile show/list.");
                switch (action)
                {
                    case "create":
                        Count(2);
                        if (!options.TryGetValue("--mods", out string? mods)) throw new ArgumentException("profile create needs --mods a,b,c in load order.");
                        profiles.Create(positional[1], mods.Split(',', StringSplitOptions.TrimEntries));
                        Console.WriteLine($"Created profile '{positional[1]}'. Use 'profile use {positional[1]}' to select it."); break;
                    case "list":
                        Count(1);
                        var all = profiles.List();
                        if (json) Json(all);
                        else if (all.Count == 0) Console.WriteLine("No profiles yet. Use profile create <name> --mods a,b,c.");
                        else foreach (var p in all) Console.WriteLine($"{(store.Config().ActiveProfile == p.Name ? "*" : " ")} {p.Name}: {string.Join(", ", p.Mods.Select(id => library.Find(id).Name))}");
                        break;
                    case "show":
                        Count(2);
                        var profile = profiles.Get(positional[1]);
                        if (json) Json(profile);
                        else
                        {
                            Console.WriteLine($"Profile {profile.Name} (later mods win within each group):");
                            Console.WriteLine($"  Preset: {profile.Preset}");
                            PrintGroup(profiles.Group(profile), includeDisabled: true);
                            foreach (var (asset, choice) in profile.Resolutions) Console.WriteLine($"  Resolve {asset}: {choice}");
                            Console.WriteLine($"  Texture repacking: {(profile.NoRepack ? "off" : "on for modded builds")}");
                            Console.WriteLine($"  Big money (above $999,999,999, compact display): {(profile.NoBigMoney ? "off" : "on for modded builds")}");
                            Console.WriteLine($"  Fair-Play: {(profile.FairPlay ? "on" : "off")}");
                            Console.WriteLine($"  Fish sale cap: {(profile.FishValueCap == 0 ? "off" : profile.FishValueCap.ToString("N0"))}");
                            Console.WriteLine($"  Perf defaults: fish scheduler/F8 {(profile.Perf.StartEnabled ? "on" : "off")}, fish sleep {(profile.Perf.FishSleep ? "on" : "off")} (margin {profile.Perf.FishSleepMargin}), fish grid hooks {(profile.Perf.FishGrid ? "on" : "off")}, decor sleep {(profile.Perf.DecorSleep ? "on" : "off")}, distant fish every {profile.Perf.Interval} ticks (existing INI settings take precedence when installed).");
                            foreach (string id in profile.UnlockFixMods ?? [])
                                Console.WriteLine($"  Repair legacy shop unlocks: {library.Find(id).Name}");
                        }
                        break;
                    case "use": Count(2); profiles.Use(positional[1]); Console.WriteLine($"Active profile: {positional[1]}."); break;
                    case "repack":
                        Count(3);
                        profiles.SetRepack(positional[1], OnOff(positional[2]));
                        Console.WriteLine($"Texture repacking for '{positional[1]}': {positional[2]}.");
                        break;
                    case "bigmoney":
                        Count(3);
                        profiles.SetBigMoney(positional[1], OnOff(positional[2]));
                        Console.WriteLine($"Money above $999,999,999 with compact display for '{positional[1]}': {positional[2]}.");
                        break;
                    case "fairplay":
                        Count(3);
                        profiles.SetFairPlay(positional[1], OnOff(positional[2]));
                        Console.WriteLine($"Fair-Play for '{positional[1]}': {positional[2]}.");
                        break;
                    case "valuecap":
                        Count(3);
                        long cap = BuildCommand.ParseFishValueCap(positional[2]);
                        profiles.SetFishValueCap(positional[1], cap);
                        Console.WriteLine($"Fish sale cap for '{positional[1]}': {(cap == 0 ? "off" : cap.ToString("N0"))}.");
                        break;
                    case "preset":
                        Count(3);
                        profiles.SetPreset(positional[1], positional[2], Progress);
                        Console.WriteLine($"Preset for '{positional[1]}': {positional[2]}. Apply the profile or press Play to rebuild.");
                        break;
                    case "builtin":
                        Count(4);
                        profiles.SetBuiltin(positional[1], positional[2], OnOff(positional[3]), Progress);
                        Console.WriteLine($"Set {positional[2]} for '{positional[1]}' to {positional[3]}.");
                        break;
                    case "perf":
                        Count(4);
                        _ = positional[2] switch
                        {
                            "mode" => profiles.SetPerf(positional[1], startEnabled: OnOff(positional[3])),
                            "grid" => profiles.SetPerf(positional[1], fishGrid: OnOff(positional[3])),
                            "sleep" => profiles.SetPerf(positional[1], fishSleep: OnOff(positional[3])),
                            "sleep-margin" when int.TryParse(positional[3], out int margin) => profiles.SetPerf(positional[1], fishSleepMargin: margin),
                            "decor" => profiles.SetPerf(positional[1], decorSleep: OnOff(positional[3])),
                            "interval" when int.TryParse(positional[3], out int interval) => profiles.SetPerf(positional[1], interval: interval),
                            _ => throw new ArgumentException("Use profile perf <name> mode|grid|sleep|decor on|off or interval|sleep-margin <value>.")
                        };
                        Console.WriteLine($"Set Perf {positional[2]} for '{positional[1]}' to {positional[3]}.");
                        break;
                    case "delete": Count(2); profiles.Delete(positional[1]); Console.WriteLine($"Deleted profile '{positional[1]}'."); break;
                    case "unlockfix":
                        Count(4);
                        bool unlockFix = positional[3] switch
                        {
                            "on" => true,
                            "off" => false,
                            _ => throw new ArgumentException("Use profile unlockfix <profile> <mod> on|off.")
                        };
                        profiles.SetUnlockFix(positional[1], positional[2], unlockFix);
                        Console.WriteLine($"Legacy shop unlock repair for '{positional[2]}' in '{positional[1]}': {positional[3]}.");
                        break;
                    case "resolve": Count(4); profiles.Resolve(positional[1], positional[2], positional[3], Console.WriteLine); Console.WriteLine($"Saved resolution for '{positional[2]}'."); break;
                    case "base":
                    case "addon":
                    case "role":
                        Count(action == "base" ? 3 : 4);
                        if (action == "role" && positional[3] != "none") throw new ArgumentException("Use profile role <name> <mod> none.");
                        var updatedRole = profiles.SetRole(positional[1], positional[2], action == "role" ? "none" : action,
                            action == "addon" ? positional[3] : null);
                        PrintGroup(profiles.Group(updatedRole), includeDisabled: true);
                        break;
                    default: throw new ArgumentException($"Unknown profile action '{action}'. See 'cgfmm --help'.");
                }
                break;
            case "restore":
                Count(0); Console.WriteLine($"Restored data.win (SHA-256 {installer.Restore()}). Original backups retained."); break;
            case "apply":
            case "play":
                Count(0);
                string? selected = options.GetValueOrDefault("--profile");
                GameLaunchCommand? launch = command == "play" ? GameLauncher.Create(store.Config(), store.Root) : null;
                if (launch is not null) GameLauncher.Validate(launch);
                if (installer.IsInstalled(selected, Console.WriteLine))
                {
                    if (selected is not null) profiles.Use(selected);
                    Console.WriteLine("Selected profile is already installed; archive and backup hashes verified.");
                }
                else
                {
                    var prepared = installer.Prepare(selected, Progress);
                    try
                    {
                        PrintGroup(profiles.Group(prepared.Profile));
                        foreach (string decision in prepared.Build.Plan.Decisions) Console.WriteLine($"Decision: {decision}");
                        foreach (string warning in prepared.Build.Verification!.Warnings) Console.Error.WriteLine($"WARNING: {warning}");
                        if (prepared.Build.Acceptance is { } acceptance)
                        {
                            Console.Write(TextReport.AcceptanceSummary(acceptance));
                            if (verbose) Console.Write(TextReport.AcceptanceDetails(acceptance));
                        }
                        Console.WriteLine($"Built and verified profile '{prepared.Profile.Name}' ({prepared.Build.SizeBytes:N0} bytes). Install into '{prepared.Config.GameDir}'?");
                        if (!flags.Contains("--yes"))
                        {
                            Console.Write("Install this build? [y/N] ");
                            string? answer = Console.ReadLine()?.Trim();
                            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine("Installation cancelled. The temporary build will be removed; the game is unchanged."); return 1;
                            }
                        }
                        installer.Install(prepared);
                        Console.WriteLine($"Installed profile '{prepared.Profile.Name}'. Verified original backups retained.");
                    }
                    finally { installer.Discard(prepared); }
                }
                if (launch is not null)
                {
                    if (store.Config().Wine.Mode == "proton" && store.Config().Proton.CompatDataPath is null)
                        store.EnsureProtonDataDirectory();
                    GameLauncher.Launch(launch);
                    Console.WriteLine($"Launched from '{launch.WorkingDirectory}': {launch.Display()}");
                }
                break;
        }
        return 0;
    }

    internal static void PrintGroup(IEnumerable<ProfileMod> rows, bool includeDisabled = false)
    {
        Console.WriteLine("Profile mods");
        foreach (var row in rows.Where(r => includeDisabled || r.Enabled))
            Console.WriteLine($"{(row.Role.Role == "addon" ? "    " : "  ")}{row.Mod.Name} ({row.Mod.Id}) [{row.RoleLabel}]{(row.Enabled ? "" : " (disabled)")}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static bool OnOff(string value) => value switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException("Choose 'on' or 'off'.")
    };
    private static void Progress(string message) => Console.Error.WriteLine(message);
}
