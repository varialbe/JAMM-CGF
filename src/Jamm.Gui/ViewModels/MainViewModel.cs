using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Merging;

namespace Jamm.Gui.ViewModels;

/// <summary>Desktop workflow over the shared Core services. All archive operations run on worker threads.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ManagerStore store;
    private CancellationTokenSource? cancellation;
    private bool busy, loading, cancellable;
    private string status = "Ready", log = "", gameStatus = "Choose a legacy game folder in Settings.", validation = "Choose a legacy base data.win.";
    private string? selectedProfile;
    private bool knownGame, canRestore;
    private bool updatingConflictChoices;
    private IReadOnlyDictionary<string, string> conflictResolutions = new Dictionary<string, string>();
    /// <summary>Creates a manager model without opening game archives on the calling thread.</summary>
    public MainViewModel(ManagerStore store) { this.store = store; }
    /// <summary>Current library rows.</summary>
    public ObservableCollection<ModRow> Mods { get; } = [];
    /// <summary>Active profile order, including disabled mods.</summary>
    public ObservableCollection<ModRow> Order { get; } = [];
    /// <summary>Available profile names.</summary>
    public ObservableCollection<string> Profiles { get; } = [];
    /// <summary>Grouped findings in severity order.</summary>
    public ObservableCollection<ConflictRow> Conflicts { get; } = [];
    /// <summary>Busy state excludes every mutating operation.</summary>
    public bool IsBusy { get => busy; private set { SetProperty(ref busy, value); NotifyButtons(); } }
    /// <summary>Enables lightweight editing while idle.</summary>
    public bool CanEdit => !IsBusy;
    /// <summary>Enables order editing once a profile has been selected.</summary>
    public bool CanEditProfile => CanEdit && SelectedProfile is not null;
    /// <summary>Enables conflict checks for a nonempty selection.</summary>
    public bool CanCheck => !IsBusy && SelectedProfile is not null && Order.Any(m => m.Enabled);
    /// <summary>Installation requires a known game and an enabled selection.</summary>
    public bool CanInstall => CanCheck && knownGame;
    /// <summary>Play uses the same checked installation workflow.</summary>
    public bool CanPlay => CanInstall;
    /// <summary>Restore is available only for a recognized game with a backup.</summary>
    public bool CanRestore => !IsBusy && knownGame && canRestore;
    /// <summary>Cancellation is unavailable during the short atomic installation/restore stage.</summary>
    public bool CanCancel => IsBusy && cancellable;
    /// <summary>Current operation text.</summary>
    public string Status { get => status; private set => SetProperty(ref status, value); }
    /// <summary>Plain-text operation log, including surfaced errors.</summary>
    public string Log { get => log; private set => SetProperty(ref log, value); }
    /// <summary>Current game-folder identity.</summary>
    public string GameStatus { get => gameStatus; private set => SetProperty(ref gameStatus, value); }
    /// <summary>Legacy base validation result.</summary>
    public string Validation { get => validation; private set => SetProperty(ref validation, value); }
    /// <summary>Finding counts include zeroes and warnings.</summary>
    public string ConflictCounts => string.Join(" · ", new[] { "CONFLICT", "AUTO", "INFO", "WARNING" }.Select(s => $"{s} {Conflicts.Count(c => c.Severity == s)}"));
    /// <summary>Selected profile; switching is persisted via SelectProfileAsync.</summary>
    public string? SelectedProfile { get => selectedProfile; private set => SetProperty(ref selectedProfile, value); }
    /// <summary>Reads shared configuration for the settings editor.</summary>
    public ManagerConfig Config => store.Config();

    private void NotifyButtons()
    {
        foreach (string property in new[] { nameof(CanEdit), nameof(CanEditProfile), nameof(CanCheck), nameof(CanInstall), nameof(CanPlay), nameof(CanRestore), nameof(CanCancel) }) OnPropertyChanged(property);
    }
    private void Message(string message) { Status = message; Log += message + Environment.NewLine; }

    /// <summary>Runs one serialized operation, surfaces a friendly error, and restores button states on every exit.</summary>
    public async Task RunAsync(string label, Func<CancellationToken, IProgress<string>, Task> action, bool supportsCancellation = true)
    {
        if (IsBusy) return;
        cancellable = supportsCancellation;
        cancellation = new();
        IsBusy = true;
        Message(label);
        var progress = new Progress<string>(Message);
        try { await action(cancellation.Token, progress); if (Status == label) Message(label + " complete."); }
        catch (OperationCanceledException) { Message("Cancelled. The game was not replaced."); }
        catch (Exception ex) { Message("Could not complete " + label.ToLowerInvariant() + ": " + ex.Message); }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; }
    }
    /// <summary>Requests cancellation at the next safe Core checkpoint.</summary>
    public void Cancel() { if (CanCancel) { cancellation?.Cancel(); Status = "Cancelling at the next safe checkpoint…"; } }
    /// <summary>Loads library and profiles, then recognizes the configured installation.</summary>
    public Task InitializeAsync() => RunAsync("Loading library", async (_, _) => await ReloadAsync());

    /// <summary>Guards configuration reads and settings construction, including first-run setup.</summary>
    public async Task OpenSettingsAsync(Func<Task> show, bool onlyWhenUnconfigured = false)
    {
        if (IsBusy) return;
        try
        {
            var config = Config;
            if (!onlyWhenUnconfigured || config.Base is null) await show();
        }
        catch (Exception ex) { Message("Could not open settings: " + ex.Message); }
    }

    private async Task ReloadAsync()
    {
        var data = await Task.Run(() => (Mods: new ModLibrary(store).List(), Profiles: new ProfileLibrary(store).List(), Config: store.Config(), State: new ManagerOperations(store).Status()));
        loading = true;
        try
        {
            Mods.Clear(); Profiles.Clear(); Order.Clear(); Conflicts.Clear();
            foreach (var m in data.Mods)
                Mods.Add(new(m.Mod.Id, m.Mod.Name, m.Compatibility, m.PatchCached, new FileInfo(Path.Combine(store.Root, "mods", m.Mod.Id, "data.win")).Length));
            foreach (var p in data.Profiles) Profiles.Add(p.Name);
            SelectedProfile = data.Config.ActiveProfile;
            var profile = data.Profiles.FirstOrDefault(p => p.Name == SelectedProfile);
            if (profile is not null)
            {
                foreach (string id in profile.Mods)
                {
                    var row = Mods.Single(m => m.Id == id); row.Enabled = !profile.DisabledMods.Contains(id); Order.Add(row);
                }
            }
            foreach (var row in Mods)
            {
                if (!Order.Contains(row)) Order.Add(row);
                row.PropertyChanged += async (_, e) => { if (e.PropertyName == nameof(ModRow.Enabled) && !loading && !IsBusy) await SaveOrderAsync(); };
            }
            knownGame = data.State.Known; canRestore = data.State.CanRestore; GameStatus = data.State.Message;
            OnPropertyChanged(nameof(Config)); OnPropertyChanged(nameof(ConflictCounts)); NotifyButtons();
        }
        finally { loading = false; }
    }
    /// <summary>Imports .win/.zip files using the exact CLI library rules.</summary>
    public Task AddAsync(IEnumerable<string> paths) => RunAsync("Adding mods", async (token, progress) =>
    {
        foreach (string path in paths)
        {
            try
            {
                var result = await Task.Run(() => new ModLibrary(store).Add(path, progress: progress.Report, cancellationToken: token), token);
                Message($"Added {result.Mod.Name}");
                foreach (string warning in result.Warnings) Message(warning);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Message($"Could not complete adding '{path}': {ex.Message}");
            }
        }
        await ReloadAsync();
    });
    /// <summary>Removes a confirmed unused library entry, preserving Core's referenced-profile safeguards.</summary>
    public Task RemoveAsync(string id) => RunAsync("Removing mod", async (_, _) => { await Task.Run(() => new ModLibrary(store).Remove(id)); await ReloadAsync(); }, false);
    /// <summary>Creates and selects an initially empty profile.</summary>
    public Task CreateProfileAsync(string name) => RunAsync("Creating profile", async (_, _) => { await Task.Run(() => { var p = new ProfileLibrary(store); p.CreateEmpty(name); p.Use(name); }); await ReloadAsync(); }, false);
    /// <summary>Renames and keeps the active selection.</summary>
    public Task RenameProfileAsync(string name) => RunAsync("Renaming profile", async (_, _) => { await Task.Run(() => new ProfileLibrary(store).Rename(SelectedProfile!, name)); await ReloadAsync(); }, false);
    /// <summary>Deletes a confirmed profile without altering installed files.</summary>
    public Task DeleteProfileAsync() => RunAsync("Deleting profile", async (_, _) => { await Task.Run(() => new ProfileLibrary(store).Delete(SelectedProfile!)); await ReloadAsync(); }, false);
    /// <summary>Switches profiles and reloads their order and enablement.</summary>
    public Task SelectProfileAsync(string name) => RunAsync("Selecting profile", async (_, _) => { await Task.Run(() => new ProfileLibrary(store).Use(name)); await ReloadAsync(); }, false);
    /// <summary>Persists checkbox state and ordering; editing invalidates previously checked choices.</summary>
    public Task SaveOrderAsync() => RunAsync("Saving load order", async (_, _) =>
    {
        if (SelectedProfile is null) throw new ArgumentException("Create or select a profile before enabling mods.");
        var ids = Order.Select(m => m.Id).ToArray(); var disabled = Order.Where(m => !m.Enabled).Select(m => m.Id).ToArray();
        await Task.Run(() => new ProfileLibrary(store).Update(SelectedProfile, ids, disabled));
        Conflicts.Clear(); OnPropertyChanged(nameof(ConflictCounts)); NotifyButtons(); Message("Load order saved. Check again to review conflicts.");
    }, false);
    /// <summary>Moves a mod to an absolute position for drag/drop and keyboard buttons.</summary>
    public async Task MoveAsync(int from, int to)
    {
        if (IsBusy || from < 0 || to < 0 || from >= Order.Count || to >= Order.Count || from == to) return;
        Order.Move(from, to); await SaveOrderAsync();
    }
    /// <summary>Validates a selected base immediately without writing settings.</summary>
    public Task ValidateBaseAsync(string path) => RunAsync("Validating legacy base", async (_, _) =>
    {
        try { Validation = await Task.Run(() => ManagerOperations.ValidateBase(path)); }
        catch (Exception ex) { Validation = ex.Message; throw; }
    }, false);
    /// <summary>Saves documented CLI configuration keys after validating the base.</summary>
    public Task SaveSettingsAsync(IReadOnlyDictionary<string, string> settings) => RunAsync("Saving settings", async (_, _) =>
    {
        if (settings.TryGetValue("base", out string? path)) Validation = await Task.Run(() => ManagerOperations.ValidateBase(path));
        await Task.Run(() => store.SetMany(settings));
        await ReloadAsync(); Message("Settings saved.");
    }, false);
    /// <summary>Runs shared conflict checks and displays per-hunk choices where applicable.</summary>
    public Task CheckAsync() => RunAsync("Checking conflicts", async (token, _) =>
    {
        var report = await Task.Run(() => new ManagerOperations(store).Check(SelectedProfile!, token), token);
        PopulateConflicts(report, new ProfileLibrary(store).Get(SelectedProfile!).Resolutions);
        Message(ConflictCounts);
    });

    private void PopulateConflicts(ConflictReport report, IReadOnlyDictionary<string, string> resolutions)
    {
        conflictResolutions = resolutions;
        Conflicts.Clear();
        foreach (string severity in new[] { "CONFLICT", "AUTO", "INFO", "WARNING" })
        {
            foreach (var finding in report.Findings.Where(f => f.Severity == severity))
            {
                string key = $"{finding.Family}/{finding.Asset}";
                report.CodeMerges.TryGetValue(finding.Asset, out var merge);
                // A code entry may have both list and replacement findings: show each hunk once.
                if (severity == "CONFLICT" && finding.Family == "code" && merge?.HasConflicts == true && finding.Kind is "code" or "list")
                {
                    foreach (var hunk in merge.Hunks.Where(h => h.Status == "CONFLICT"))
                    {
                        string hunkKey = $"{key}#{hunk.Start}";
                        if (Conflicts.Any(c => c.Key == hunkKey)) continue;
                        Add(new(severity, hunkKey, hunk.Kind, string.Join(", ", hunk.Versions.Select(v => v.Name)),
                            hunk.KeepBoth is not null ? "Mods insert different lines here. Choose one, or keep both in load order. Shared catalog slots may still overwrite each other."
                            : "Mods replace the same lines. Choose which version to keep.", merge.Text,
                            [.. hunk.Versions.Select(v => v.Name), .. hunk.KeepBoth is not null ? new[] { "keep-both" } : []], null));
                    }
                }
                else Add(new(severity, key, finding.Kind, string.Join(", ", finding.Mods), finding.Message +
                    (finding.Kind == "index" ? " Review the source code entries in Details. Resolve their code conflicts, disable a conflicting mod, or use mods with distinct slots; verify runtime behavior in-game."
                    : severity == "CONFLICT" ? " This choice applies to all conflicts for this asset." : ""),
                    merge?.Text ?? System.Text.Json.JsonSerializer.Serialize(finding, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                    severity == "CONFLICT" && finding.Kind != "index" ? report.Findings
                        .Where(f => f.Severity == "CONFLICT" && f.Kind != "index" && f.Family == finding.Family && f.Asset == finding.Asset)
                        .SelectMany(f => f.Mods).Distinct(StringComparer.Ordinal).ToArray() : [], null));
            }
        }
        RefreshConflictChoices();
        OnPropertyChanged(nameof(ConflictCounts));
        void Add(ConflictRow row)
        {
            row.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName != nameof(ConflictRow.SelectedChoice) || updatingConflictChoices) return;
                if (!IsBusy && row.CanChoose && row.SelectedChoice is { } choice) await ResolveAsync(row.Key, choice);
                else RefreshConflictChoices();
            };
            Conflicts.Add(row);
        }
    }

    private void RefreshConflictChoices()
    {
        updatingConflictChoices = true;
        try
        {
            foreach (var row in Conflicts)
            {
                string entry = row.Key.Split('#')[0];
                string bare = entry[(entry.IndexOf('/') + 1)..];
                string? choice = conflictResolutions.GetValueOrDefault(row.Key)
                    ?? conflictResolutions.GetValueOrDefault(entry) ?? conflictResolutions.GetValueOrDefault(bare);
                row.SelectedChoice = row.Choices.Contains(choice, StringComparer.Ordinal) ? choice : null;
            }
        }
        finally { updatingConflictChoices = false; }
    }
    /// <summary>Persists an explicit asset or hunk choice through Core validation.</summary>
    public Task ResolveAsync(string key, string choice) => RunAsync("Saving conflict choice", async (_, _) =>
    {
        try
        {
            var profile = await Task.Run(() => new ProfileLibrary(store).Resolve(SelectedProfile!, key, choice));
            conflictResolutions = profile.Resolutions;
            Message($"Saved {key}: {choice}");
        }
        finally { RefreshConflictChoices(); }
    }, false);
    /// <summary>Builds and verifies before asking for a concrete installation confirmation.</summary>
    public Task InstallAsync(Func<string, Task<bool>> confirm) => RunAsync("Preparing installation", async (token, progress) => await InstallInternalAsync(confirm, token, progress));
    private async Task InstallInternalAsync(Func<string, Task<bool>> confirm, CancellationToken token, IProgress<string> progress)
    {
        var installer = new ProfileInstaller(store);
        var state = await Task.Run(() => new ManagerOperations(store).Status(), token);
        if (!state.Known) throw new IOException(state.Message);
        if (await Task.Run(() => installer.IsInstalled(SelectedProfile), token)) { Message("This exact profile is already installed and verified."); return; }
        var prepared = await Task.Run(() => installer.Prepare(SelectedProfile, progress.Report, token), token);
        try
        {
            foreach (string decision in prepared.Build.Plan.Decisions) Message(decision);
            token.ThrowIfCancellationRequested();
            string gameFile = Path.Combine(prepared.Config.GameDir!, "data.win");
            if (!await confirm($"Replace:\n{gameFile}\n\nVerified original backups:\n{gameFile}.cgfmm-original\n{Path.Combine(store.Root, "backups", prepared.BaseSha256 + ".win")}\n\nInstall verified profile '{prepared.Profile.Name}'?")) { Message("Installation declined. The game was not replaced."); return; }
            token.ThrowIfCancellationRequested();
            cancellable = false; NotifyButtons();
            await Task.Run(() => installer.Install(prepared, retainIndependentBase: true));
            foreach (string warning in prepared.Build.Verification!.Warnings) Message(warning);
            await ReloadAsync();
            Message($"Installed and verified: {prepared.Build.Verification!.DecompiledEntries} code entries; {prepared.Build.SemanticEntries} semantic checks. SHA-256 {prepared.Build.Verification.Inspection.Sha256}");
        }
        finally { await Task.Run(() => installer.Discard(prepared)); }
    }
    /// <summary>Validates the runner before installation, then starts it without waiting for the game.</summary>
    public Task PlayAsync(Func<string, Task<bool>> confirm) => RunAsync("Preparing play", async (token, progress) =>
    {
        var command = await Task.Run(() => { var c = GameLauncher.Create(store.Config(), store.Root); GameLauncher.Validate(c); return c; }, token);
        await InstallInternalAsync(confirm, token, progress);
        if (!await Task.Run(() => new ProfileInstaller(store).IsInstalled(SelectedProfile))) return;
        await Task.Run(() => { if (store.Config().Wine.Mode == "proton" && store.Config().Proton.CompatDataPath is null) store.EnsureProtonDataDirectory(); GameLauncher.Launch(command); });
        Message("Started " + command.Display());
    });
    /// <summary>Restores a recognized installation through Core and reports the verified original hash.</summary>
    public Task RestoreAsync() => RunAsync("Restoring original", async (_, _) =>
    {
        var state = await Task.Run(() => new ManagerOperations(store).Status());
        if (!state.Known) throw new IOException(state.Message);
        string hash = await Task.Run(() => new ProfileInstaller(store).Restore());
        await ReloadAsync(); Message("Original restored and SHA-256 verified: " + hash);
    }, false);
}
