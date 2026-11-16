using Jamm.Core.Building;

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

    /// <summary>Updates the complete order and disabled IDs, clearing choices that may no longer apply.</summary>
    public ModProfile Update(string name, IEnumerable<string> mods, IEnumerable<string> disabled)
    {
        var profile = Get(name);
        var library = new ModLibrary(store);
        var ids = mods.Select(m => library.Find(m).Id).ToArray();
        var off = disabled.ToArray();
        if (ids.Distinct().Count() != ids.Length || off.Except(ids).Any())
            throw new ArgumentException("Profile order must contain unique library mods; disabled mods must be in that order.");
        var result = profile with { Mods = ids, DisabledMods = off, Resolutions = new(StringComparer.Ordinal) };
        store.Write(FilePath(name), result);
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

    /// <summary>Deletes a profile and clears its active selection if necessary.</summary>
    public void Delete(string name)
    {
        Get(name);
        if (store.Config().ActiveProfile == name) store.SaveConfig(store.Config() with { ActiveProfile = null });
        File.Delete(FilePath(name));
    }

    /// <summary>Records a valid asset choice, translating a library ID or name to the patch display name.</summary>
    public ModProfile Resolve(string name, string asset, string choice)
    {
        var profile = Get(name);
        var library = new ModLibrary(store);
        string winner = choice == "keep-both" ? choice : library.Find(choice).Name;
        if (choice != "keep-both" && !profile.Mods.Select(library.Find).Any(m => m.Name == winner))
            throw new ArgumentException($"Mod '{choice}' is not in profile '{name}'. Choose one of its mods.");
        var updated = new Dictionary<string, string>(profile.Resolutions, StringComparer.Ordinal) { [asset] = winner };
        BuildPlan.Create(profile.EnabledMods.Select(id => library.Patch(library.Find(id))).ToArray(), updated);
        var result = profile with { Resolutions = updated };
        store.Write(FilePath(name), result);
        return result;
    }
}
