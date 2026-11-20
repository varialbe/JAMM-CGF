using Jamm.Core.Building;
using Jamm.Core.Merging;

namespace Jamm.Core.Management;

/// <summary>Stores ordered profiles and validates all selected mods and resolutions before use.</summary>
public sealed class ProfileLibrary(ManagerStore store)
{
    private string FilePath(string name) => store.Path("profiles", ManagerStore.Name(name) + ".json");

    /// <summary>Lists saved profiles in ordinal name order.</summary>
    public IReadOnlyList<ModProfile> List() => Directory.EnumerateFiles(store.Path("profiles"), "*.json")
        .Order(StringComparer.Ordinal).Select(p => Get(Path.GetFileNameWithoutExtension(p))).ToArray();

    /// <summary>Reads one profile by its safe name.</summary>
    public ModProfile Get(string name)
    {
        string path = FilePath(name);
        if (!File.Exists(path)) throw new FileNotFoundException($"Profile '{name}' was not found. Use 'profile list' or create it first.");
        var profile = store.Read<ModProfile>(path);
        if (profile.Name != name || profile.Mods is null || profile.Mods.Distinct(StringComparer.Ordinal).Count() != profile.Mods.Length || profile.Resolutions is null
            || profile.DisabledMods is null || profile.DisabledMods.Except(profile.Mods).Any())
            throw new InvalidDataException($"Profile '{name}' is invalid. Recreate it with a nonempty, unique mod list.");
        foreach (string mod in profile.Mods) ManagerStore.Name(mod);
        return profile;
    }

    /// <summary>Creates a new profile with resolved library IDs, preserving the supplied order.</summary>
    public ModProfile Create(string name, IEnumerable<string> mods)
    {
        string path = FilePath(name);
        if (File.Exists(path)) throw new IOException($"Profile '{name}' already exists. Choose another name or delete it first.");
        var library = new ModLibrary(store);
        string[] ids = mods.Select(m => library.Find(m).Id).ToArray();
        if (ids.Length == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("A profile needs at least one mod, with no duplicates.");
        var result = new ModProfile(name, ids, new(StringComparer.Ordinal));
        store.Write(path, result);
        return result;
    }

    /// <summary>Updates order and enablement, retaining choices unless their winning mod is removed.</summary>
    public ModProfile Update(string name, IEnumerable<string> mods, IEnumerable<string> disabled, Action<string>? notice = null)
    {
        var profile = Get(name);
        var library = new ModLibrary(store);
        var ids = mods.Select(m => library.Find(m).Id).ToArray();
        var off = disabled.ToArray();
        if (ids.Distinct().Count() != ids.Length || off.Except(ids).Any())
            throw new ArgumentException("Profile order must contain unique library mods; disabled mods must be in that order.");
        var removedNames = profile.Mods.Except(ids, StringComparer.Ordinal).Select(library.Find).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var removed = profile.Resolutions.Where(r => removedNames.Contains(r.Value)).ToArray();
        var resolutions = new Dictionary<string, string>(profile.Resolutions, StringComparer.Ordinal);
        foreach (var choice in removed) resolutions.Remove(choice.Key);
        var result = profile with { Mods = ids, DisabledMods = off, Resolutions = resolutions };
        store.Write(FilePath(name), result);
        foreach (var choice in removed) notice?.Invoke($"Removed resolution for {choice.Key}: winning mod '{choice.Value}' was removed from profile '{name}'. Review conflicts before playing.");
        return result;
    }

    /// <summary>Creates an empty profile ready for GUI selection.</summary>
    public ModProfile CreateEmpty(string name)
    {
        string path = FilePath(name);
        if (File.Exists(path)) throw new IOException($"Profile '{name}' already exists. Choose another name.");
        var result = new ModProfile(name, [], new(StringComparer.Ordinal));
        store.Write(path, result);
        return result;
    }

    /// <summary>Renames a profile without changing its order, disabled mods or resolutions.</summary>
    public void Rename(string name, string newName)
    {
        var profile = Get(name);
        if (File.Exists(FilePath(newName))) throw new IOException($"Profile '{newName}' already exists. Choose another name.");
        store.Write(FilePath(newName), profile with { Name = newName });
        if (store.Config().ActiveProfile == name) Use(newName);
        File.Delete(FilePath(name));
    }

    /// <summary>Selects an existing profile as the default for apply and play.</summary>
    public void Use(string name) { Get(name); store.SaveConfig(store.Config() with { ActiveProfile = name }); }

    /// <summary>Sets whether builds of this profile should repack textures.</summary>
    public void SetRepack(string name, bool enabled) =>
        store.Write(FilePath(name), Get(name) with { NoRepack = !enabled });

    /// <summary>Deletes a profile and clears its active selection if necessary.</summary>
    public void Delete(string name)
    {
        Get(name);
        if (store.Config().ActiveProfile == name) store.SaveConfig(store.Config() with { ActiveProfile = null });
        File.Delete(FilePath(name));
    }

    internal Dictionary<string, string> ActiveResolutions(ModProfile profile)
    {
        var library = new ModLibrary(store);
        var disabled = profile.DisabledMods.Select(library.Find).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var active = profile.Resolutions.Where(r => !disabled.Contains(r.Value)).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        string[] groupKeys = active.Keys.Where(IsUpgradeGroupKey).ToArray();
        if (groupKeys.Length == 0) return active;

        var patches = profile.EnabledMods.Select(id => library.Patch(library.Find(id))).ToArray();
        if (patches.Length == 0)
        {
            foreach (string key in groupKeys) active.Remove(key);
            return active;
        }
        var report = ConflictAnalyzer.Analyze(patches, allowSingle: true);
        var liveGroups = report.Findings.Where(f => f.Kind == "upgrade-group" && f.Severity == "CONFLICT")
            .ToDictionary(f => $"{f.Family}/{f.Asset}", f => f.Mods.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (string key in groupKeys)
            if (!liveGroups.TryGetValue(key, out var participants) || !participants.Contains(active[key])) active.Remove(key);
        return active;
    }

    /// <summary>Records a valid asset choice, translating a library ID or name to the patch display name.</summary>
    public ModProfile Resolve(string name, string asset, string choice, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var profile = Get(name);
        var library = new ModLibrary(store);
        string winner = choice == "keep-both" ? choice : library.Find(choice).Name;
        if (choice != "keep-both" && !profile.Mods.Select(library.Find).Any(m => m.Name == winner))
            throw new ArgumentException($"Mod '{choice}' is not in profile '{name}'. Choose one of its mods.");
        if (IsUpgradeGroupKey(asset))
        {
            var enabledPatches = profile.EnabledMods.Select(id => library.Patch(library.Find(id), progress, cancellationToken)).ToArray();
            var report = ConflictAnalyzer.Analyze(enabledPatches, allowSingle: true, cancellationToken: cancellationToken);
            var group = report.Findings.FirstOrDefault(f => f.Kind == "upgrade-group" && $"{f.Family}/{f.Asset}" == asset);
            if (group is null || !group.Mods.Contains(winner, StringComparer.Ordinal))
                throw new ArgumentException($"'{asset}' is not an active legacy upgrade group for the selected mods. Run check again and choose a reported group.");
        }
        var updated = new Dictionary<string, string>(profile.Resolutions, StringComparer.Ordinal) { [asset] = winner };
        BuildPlan.Create(profile.EnabledMods.Select(id => library.Patch(library.Find(id), progress, cancellationToken)).ToArray(), ActiveResolutions(profile with { Resolutions = updated }));
        var result = profile with { Resolutions = updated };
        store.Write(FilePath(name), result);
        return result;
    }

    private static bool IsUpgradeGroupKey(string key) => key.StartsWith("code/upgrade-slot[", StringComparison.Ordinal)
        && key.EndsWith(']') && int.TryParse(key.AsSpan("code/upgrade-slot[".Length, key.Length - "code/upgrade-slot[".Length - 1), out _);
}
