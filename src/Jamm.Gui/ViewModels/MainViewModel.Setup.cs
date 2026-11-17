using System.Collections.ObjectModel;
using Jamm.Core.Management;

namespace Jamm.Gui.ViewModels;

/// <summary>Step within the first-run setup flow.</summary>
public enum SetupStep
{
    /// <summary>Detecting or choosing the legacy game folder.</summary>
    DetectGame,
    /// <summary>Optional bundled JAMM extras (Perf, Smooth, HD Lite, Console).</summary>
    Extras
}

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
        SetupStep = SetupStep.DetectGame;
        SetupOpen = true;
        SetupMessage = "First choose the legacy game so JAMM can read your mods. Your requested action will continue after Use this.";
        if (DetectedGames.Count == 0) await SearchGamesAsync();
    }
    private Func<Task>? pendingAction;
    private bool setupOpen;
    /// <summary>Whether setup is currently expanded.</summary>
    public bool SetupOpen { get => setupOpen; set => SetProperty(ref setupOpen, value); }
    private SetupStep setupStep = SetupStep.DetectGame;
    /// <summary>Active step in the setup dialog.</summary>
    public SetupStep SetupStep
    {
        get => setupStep;
        set
        {
            if (SetProperty(ref setupStep, value))
            {
                OnPropertyChanged(nameof(IsSetupDetecting));
                OnPropertyChanged(nameof(IsSetupExtras));
            }
        }
    }
    /// <summary>Whether setup is currently on the legacy game detection step.</summary>
    public bool IsSetupDetecting => SetupStep == SetupStep.DetectGame;
    /// <summary>Whether setup is currently on the optional JAMM extras step.</summary>
    public bool IsSetupExtras => SetupStep == SetupStep.Extras;
    /// <summary>Read-only classified search results.</summary>
    public ObservableCollection<DetectedGame> DetectedGames { get; } = [];
    /// <summary>Unusable results displayed in a collapsed group.</summary>
    public ObservableCollection<DetectedGame> OtherGames { get; } = [];
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
    /// <summary>Whether the current OS is Linux for Linux-specific setup actions.</summary>
    public bool IsLinux => OperatingSystem.IsLinux();
    /// <summary>Builds the OS-specific guidance when no legacy depot is detected.</summary>
    public static string MissingLegacyMessage(bool isLinux) => isLinux
        ? "No usable legacy game found. Run 'install.sh --get-game', or open the Steam console ('steam steam://open/console', 'flatpak run com.valvesoftware.Steam steam://open/console', 'snap run steam steam://open/console', or start Steam with -console), run the command below, then choose the downloaded folder or Check again. Current Steam builds cannot load these mods."
        : "No usable legacy game found. Press Win+R and open steam://open/console (or start Steam with -console), run the command below, then choose the downloaded folder or Check again. Current Steam builds cannot load these mods.";
    /// <summary>Opens the Steam console on Linux using the variant-specific binary without a shell.</summary>
    public void OpenSteamConsole()
    {
        if (!OperatingSystem.IsLinux()) return;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (fileName, args) = SteamGameDetection.LinuxConsoleCommand(home);
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo(fileName) { UseShellExecute = false };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            System.Diagnostics.Process.Start(info);
            Message($"Opened Steam console via {fileName} {string.Join(' ', args)}. Paste the legacy download command in Steam's Console tab.");
        }
        catch (Exception ex)
        {
            ReportError($"Could not open Steam console with '{fileName}': {ex.Message} Run 'install.sh --get-game' or start Steam with -console.");
        }
    }
    /// <summary>Searches common roots and mounted drives on a worker thread.</summary>
    public Task SearchGamesAsync() => RunAsync("Searching for legacy game", SearchGamesInternalAsync);
    private async Task SearchGamesInternalAsync(CancellationToken token, IProgress<string> progress)
    {
        SetupStep = SetupStep.DetectGame;
        SetupOpen = true;
        SetupMessage = "Searching Steam libraries and mounted drives… You can cancel this search or choose a folder afterwards.";
        bool searching = true;
        var setupProgress = new Progress<string>(text => { if (searching) { SetupMessage = text; progress.Report(text); } });
        DetectedGame[] found;
        try { found = await Task.Run(() => searchGames(token, setupProgress), token); }
        finally { searching = false; }
        DetectedGames.Clear(); OtherGames.Clear();
        foreach (var game in SteamGameDetection.OrderCandidates(found))
            if (game.Usable) DetectedGames.Add(game); else OtherGames.Add(game);
        DetectedLegacy = DetectedGames.FirstOrDefault(g => g.Suitability == GameSuitability.SupportedOriginal);
        SetupMessage = found.Any(g => g.Usable) ? "Select a legacy game and press Use this. JAMM creates a separate modded copy." : MissingLegacyMessage(OperatingSystem.IsLinux());
        if (pendingAction is not null) SetupMessage += " Your requested mod action will continue after Use this.";
    }
    /// <summary>Validates a manually chosen whole game folder using the same inspector.</summary>
    public Task ChooseGameFolderAsync(string folder) => RunAsync("Checking game folder", async (token, _) =>
    {
        SetupStep = SetupStep.DetectGame;
        SetupOpen = true;
        string archive = Path.Combine(folder, "data.win");
        if (!File.Exists(Path.Combine(folder, "Cat Goes Fishing.exe"))) throw new IOException($"'{folder}' has no Cat Goes Fishing.exe. Choose the complete legacy game folder.");
        var game = await Task.Run(() => SteamGameDetection.Inspect(archive, token), token);
        var results = game.Usable ? DetectedGames : OtherGames;
        if (!results.Any(g => g.Archive == game.Archive))
        {
            results.Add(game);
            var sorted = SteamGameDetection.OrderCandidates(results).ToArray();
            results.Clear(); foreach (var result in sorted) results.Add(result);
        }
        DetectedLegacy = game.Usable ? game : null;
        SetupMessage = game.Reason;
        if (!game.Usable) throw new IOException($"Cannot use '{folder}': {game.Reason} Choose the legacy depot folder instead.");
    });
    /// <summary>Creates a safe copy, configures the base game, and advances to optional extras.</summary>
    public async Task UseDetectedGameAsync()
    {
        if (!HasDetectedLegacy || IsBusy) return;
        await RunAsync("Creating modded game", async (token, _) =>
        {
            await Task.Run(() => SteamGameDetection.Use(store, DetectedLegacy!, token, ModdedDirectory), token);
            await ReloadAsync();
            RefreshBuiltins();
            SetupStep = SetupStep.Extras;
            SetupMessage = "Separate modded game ready. Choose optional JAMM extras below or click Done/Skip to continue.";
            Message(Config.Wine.Mode != "direct" && Config.Proton.Path is null
                ? "Game copy ready. Install Proton - Experimental in Steam, then select its Proton folder in Settings before Play."
                : $"Game copy ready: {Config.GameDir}. Runner: {Config.Wine.Mode}. Add or download mods, then press Play.");
        });
        if (!NeedsSetup && pendingAction is { } resume) { pendingAction = null; await resume(); }
    }
    /// <summary>Completes the first-run setup flow and closes the setup panel.</summary>
    public void FinishSetup()
    {
        SetupOpen = false;
        SetupStep = SetupStep.DetectGame;
    }
    /// <summary>Skips optional extras and closes the setup panel.</summary>
    public void SkipSetup() => FinishSetup();
    /// <summary>Returns from the optional extras step back to the game detection step.</summary>
    public void BackToGameSetup()
    {
        SetupStep = SetupStep.DetectGame;
    }
    /// <summary>Closes setup and discards the pending action.</summary>
    public void CancelSetup() { Cancel(); pendingAction = null; SetupOpen = false; SetupStep = SetupStep.DetectGame; }
}
