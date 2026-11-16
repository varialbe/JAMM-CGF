using System.Collections.ObjectModel;
using Jamm.Core.Management;

namespace Jamm.Gui.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Opens first-run setup and defers a requested action until setup succeeds.</summary>
    public async Task AfterSetupAsync(Func<Task> action)
    {
        if (IsBusy) return;
        bool checkedSetup = false;
        await RunAsync("Checking game setup", async (token, _) =>
        {
            usableBase = Config.Base is { } configuredBase && await Task.Run(() => SteamGameDetection.Inspect(configuredBase, token).Usable, token);
            OnPropertyChanged(nameof(NeedsSetup)); checkedSetup = true;
        });
        if (!checkedSetup) return;
        if (!NeedsSetup) { await action(); return; }
        pendingAction = action;
        SetupOpen = true;
        SetupMessage = "First choose the legacy game so JAMM can read your mods. Your requested action will continue after Use this.";
        if (DetectedGames.Count == 0) await SearchGamesAsync();
    }
    private Func<Task>? pendingAction;
    private bool setupOpen;
    /// <summary>Whether setup is currently expanded.</summary>
    public bool SetupOpen { get => setupOpen; set => SetProperty(ref setupOpen, value); }
    /// <summary>Read-only classified search results.</summary>
    public ObservableCollection<DetectedGame> DetectedGames { get; } = [];
    private DetectedGame? detectedLegacy;
    /// <summary>Selected game, preferring the recommended legacy manifest.</summary>
    public DetectedGame? DetectedLegacy { get => detectedLegacy; set { SetProperty(ref detectedLegacy, value); if (value is not null) ModdedDirectory = Path.GetDirectoryName(value.Archive)! + "-modded"; OnPropertyChanged(nameof(HasDetectedLegacy)); } }
    private string moddedDirectory = "";
    /// <summary>Separate game copy destination; editable in advanced setup.</summary>
    public string ModdedDirectory { get => moddedDirectory; set => SetProperty(ref moddedDirectory, value); }
    private bool usableBase;
    /// <summary>Whether the configured base has passed inspection.</summary>
    public bool NeedsSetup => !usableBase;
    /// <summary>Whether the selected candidate can be used.</summary>
    public bool HasDetectedLegacy => !IsBusy && DetectedLegacy?.Usable == true;
    private string setupMessage = "";
    /// <summary>First-run progress and next step.</summary>
    public string SetupMessage { get => setupMessage; private set => SetProperty(ref setupMessage, value); }
    /// <summary>Searches common roots and mounted drives on a worker thread.</summary>
    public Task SearchGamesAsync() => RunAsync("Searching for legacy game", SearchGamesInternalAsync);
    private async Task SearchGamesInternalAsync(CancellationToken token, IProgress<string> progress)
    {
        SetupOpen = true;
        SetupMessage = "Searching Steam libraries and mounted drives… You can cancel this search or choose a folder afterwards.";
        bool searching = true;
        var setupProgress = new Progress<string>(text => { if (searching) { SetupMessage = text; progress.Report(text); } });
        DetectedGame[] found;
        try { found = await Task.Run(() => searchGames(token, setupProgress), token); }
        finally { searching = false; }
        DetectedGames.Clear();
        foreach (var game in found) DetectedGames.Add(game);
        DetectedLegacy = found.OrderByDescending(g => g.Usable).ThenByDescending(g => g.Archive.Contains("9039707068725406600", StringComparison.Ordinal)).FirstOrDefault();
        SetupMessage = found.Any(g => g.Usable) ? "Select a legacy game and press Use this. JAMM creates a separate modded copy." : "No usable legacy game found. In Steam open steam://open/console, run the command below, then choose the downloaded folder or Check again. Current Steam builds cannot load these mods.";
        if (pendingAction is not null) SetupMessage += " Your requested mod action will continue after Use this.";
    }
    /// <summary>Validates a manually chosen whole game folder using the same inspector.</summary>
    public Task ChooseGameFolderAsync(string folder) => RunAsync("Checking game folder", async (token, _) =>
    {
        SetupOpen = true;
        string archive = Path.Combine(folder, "data.win");
        if (!File.Exists(Path.Combine(folder, "Cat Goes Fishing.exe"))) throw new IOException($"'{folder}' has no Cat Goes Fishing.exe. Choose the complete legacy game folder.");
        var game = await Task.Run(() => SteamGameDetection.Inspect(archive, token), token);
        if (!DetectedGames.Any(g => g.Archive == game.Archive)) DetectedGames.Add(game);
        DetectedLegacy = game;
        SetupMessage = game.Reason;
        if (!game.Usable) throw new IOException($"Cannot use '{folder}': {game.Reason} Choose the legacy depot folder instead.");
    });
    /// <summary>Creates a safe copy and resumes the action that requested setup.</summary>
    public async Task UseDetectedGameAsync()
    {
        if (!HasDetectedLegacy || IsBusy) return;
        await RunAsync("Creating modded game", async (token, _) =>
        {
            await Task.Run(() => SteamGameDetection.Use(store, DetectedLegacy!, token, ModdedDirectory), token);
            await ReloadAsync();
            SetupOpen = false;
            SetupMessage = "Separate modded game ready. Press Play after enabling your mods.";
            Message(Config.Wine.Mode != "direct" && Config.Proton.Path is null
                ? "Game copy ready. Install Proton - Experimental in Steam, then select its Proton folder in Settings before Play."
                : $"Game copy ready: {Config.GameDir}. Runner: {Config.Wine.Mode}. Add or download mods, then press Play.");
        });
        if (!NeedsSetup && pendingAction is { } resume) { pendingAction = null; await resume(); }
    }
    /// <summary>Closes setup and discards the pending action.</summary>
    public void CancelSetup() { Cancel(); pendingAction = null; SetupOpen = false; }
}
