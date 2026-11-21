using Jamm.Core.Management;
using Jamm.ModMaker;

namespace Jamm.Gui.ViewModels;

public sealed partial class MainViewModel
{
    private ModProfile? presetProfile;

    /// <summary>Whether the current profile uses Weak PC.</summary>
    public bool WeakPcEnabled => presetProfile?.Preset == ProfileLibrary.WeakPcPreset;
    /// <summary>Whether texture repacking is on in the current profile.</summary>
    public bool ProfileRepack => presetProfile?.NoRepack != true;
    /// <summary>Current profile defaults. The game can override these from its INI.</summary>
    public PerfSettings ProfilePerf => presetProfile?.Perf ?? new();

    private void ShowPresetSettings(ModProfile? profile)
    {
        presetProfile = profile;
        OnPropertyChanged(nameof(WeakPcEnabled));
        OnPropertyChanged(nameof(ProfileRepack));
        OnPropertyChanged(nameof(ProfilePerf));
    }

    /// <summary>Changes the active profile's preset without installing or launching the game.</summary>
    public Task ToggleWeakPcAsync() => RunAsync("Updating Weak PC preset", async (token, progress) =>
    {
        bool enable = !WeakPcEnabled;
        string? selected = SelectedProfile;
        try
        {
            await Task.Run(() =>
            {
                var profiles = new ProfileLibrary(store);
                string? name = selected ?? profiles.List().FirstOrDefault()?.Name;
                if (name is null) name = profiles.CreateEmpty("default").Name;
                profiles.SetPreset(name, enable ? ProfileLibrary.WeakPcPreset : ProfileLibrary.DefaultPreset, progress.Report, token);
                if (selected is null) profiles.Use(name);
            }, token);
            await ReloadAsync();
            Message(enable ? "Weak PC enabled. Press Play to rebuild." : "Default profile settings restored. Press Play to rebuild.");
        }
        finally { OnPropertyChanged(nameof(WeakPcEnabled)); }
    });

    /// <summary>Changes the active profile's texture repacking setting.</summary>
    public Task ToggleProfileRepackAsync() => UpdatePresetOptionAsync((profiles, name) => profiles.SetRepack(name, !ProfileRepack));

    /// <summary>Changes the active profile's Perf defaults.</summary>
    public Task UpdateProfilePerfAsync(bool? startEnabled = null, bool? fishGrid = null, int? interval = null) =>
        UpdatePresetOptionAsync((profiles, name) => profiles.SetPerf(name, startEnabled, fishGrid, interval));

    private Task UpdatePresetOptionAsync(Action<ProfileLibrary, string> update) => RunAsync("Updating profile performance", async (_, _) =>
    {
        if (SelectedProfile is not { } name) return;
        var profile = await Task.Run(() =>
        {
            var profiles = new ProfileLibrary(store);
            update(profiles, name);
            return profiles.Get(name);
        });
        ShowPresetSettings(profile);
        Message("Profile settings saved. Press Play to rebuild.");
    }, false);
}
