using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jamm.Core.Building;
using Jamm.Core.Browsing;
using Jamm.Core.Management;
using Jamm.Core.Merging;
using Jamm.Core.ModKit;

namespace Jamm.Gui.ViewModels;

/// <summary>Desktop workflow over the shared Core services. All archive operations run on worker threads.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ManagerStore store;
    private readonly Func<CancellationToken, IProgress<string>, DetectedGame[]> searchGames;
    private CancellationTokenSource? cancellation;
    private bool busy, loading, cancellable;
    private string status = "Ready", log = "", gameStatus = "Choose a legacy game folder in Settings.", validation = "Choose a legacy base data.win.";
    private string? selectedProfile;
    private bool knownGame, canRestore;
    private bool updatingConflictChoices;
    private IReadOnlyDictionary<string, string> conflictResolutions = new Dictionary<string, string>();
    /// <summary>Creates a manager model without opening game archives on the calling thread.</summary>
    public MainViewModel(ManagerStore store, ModHttp? browseHttp = null, IModCatalogProvider? catalog = null, IModDownloadResolver? resolver = null, Func<CancellationToken, IProgress<string>, DetectedGame[]>? gameSearch = null)
    {
        this.store = store;
        searchGames = gameSearch ?? ((token, progress) => SteamGameDetection.Detect(token, progress).Concat(GameFolderSearch.Scan(GameFolderSearch.Roots(), token, progress)).DistinctBy(g => g.Archive).ToArray());
        Browse = new BrowseViewModel(this, store, browseHttp, catalog, resolver);
        OtherGames.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasOtherGames)); OnPropertyChanged(nameof(OtherGamesHeader)); };
    }
    /// <summary>Current library rows.</summary>
    public ObservableCollection<ModRow> Mods { get; } = [];
    /// <summary>Active profile order, including disabled mods.</summary>
    public ObservableCollection<ModRow> Order { get; } = [];
    /// <summary>Available profile names.</summary>
    public ObservableCollection<string> Profiles { get; } = [];
    /// <summary>Grouped findings in severity order.</summary>
    public ObservableCollection<ConflictRow> Conflicts { get; } = [];
    /// <summary>Busy state excludes every mutating operation.</summary>
    public bool IsBusy { get => busy; private set { SetProperty(ref busy, value); NotifyButtons(); Browse?.NotifyActions(); } }
    /// <summary>Enables lightweight editing while idle.</summary>
    public bool CanEdit => !IsBusy;
    /// <summary>Enables order editing once a profile has been selected.</summary>
    public bool CanEditProfile => CanEdit && SelectedProfile is not null;
    /// <summary>Enables conflict checks for a nonempty selection.</summary>
    public bool CanCheck => !IsBusy && PendingCount == 0 && SelectedProfile is not null && Order.Any(m => m.Enabled);
    /// <summary>Installation requires a known game and an enabled selection.</summary>
    public bool CanInstall => CanCheck && knownGame;
    /// <summary>Play uses the same checked installation workflow.</summary>
    public bool CanPlay => !IsBusy && (SelectedProfile is not null || PendingCount > 0) && knownGame;
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
    private ModRow? selectedMod;
    /// <summary>Currently selected mod in the library list.</summary>
    public ModRow? SelectedMod { get => selectedMod; set => SetProperty(ref selectedMod, value); }
    /// <summary>True when the local library contains at least one mod.</summary>
    public bool HasMods => Mods.Count > 0;
    /// <summary>Selected profile; switching is persisted via SelectProfileAsync.</summary>
    public string? SelectedProfile { get => selectedProfile; private set => SetProperty(ref selectedProfile, value); }
    /// <summary>Reads shared configuration for the settings editor.</summary>
    public ManagerConfig Config => store.Config();

    private void NotifyButtons()
    {
        foreach (string property in new[] { nameof(CanEdit), nameof(CanEditProfile), nameof(CanCheck), nameof(CanInstall), nameof(CanPlay), nameof(CanRestore), nameof(CanCancel), nameof(HasDetectedLegacy), nameof(PendingCount), nameof(CanUndo), nameof(StatusTitle), nameof(StatusExplanation), nameof(HasMods) }) OnPropertyChanged(property);
    }
    private double operationProgress;
    private bool operationIndeterminate = true;
    private string operationStep = "Ready";
    /// <summary>Current plain-language processing step.</summary>
    public string OperationStep { get => operationStep; private set => SetProperty(ref operationStep, value); }
    /// <summary>Completed fraction within the current step.</summary>
    public double OperationProgress { get => operationProgress; private set => SetProperty(ref operationProgress, value); }
    /// <summary>True when this step has no trustworthy count.</summary>
    public bool OperationIndeterminate { get => operationIndeterminate; private set => SetProperty(ref operationIndeterminate, value); }
    internal void ReportProcessingStep(string message) => Message(message);
    internal void UpdateOperation(string message)
    {
        var count = System.Text.RegularExpressions.Regex.Match(message, @"(\d[\d,]*)/(\d[\d,]*)");
        OperationIndeterminate = !count.Success;
        OperationProgress = count.Success && double.TryParse(count.Groups[2].Value.Replace(",", ""), out double total) && total > 0
            ? 100 * double.Parse(count.Groups[1].Value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture) / total : 0;
        OperationStep = message.StartsWith("Fingerprinting") ? "Reading game content" : message.StartsWith("Decompiling") || message.StartsWith("Resolving")
            ? "Reading game code" : message.StartsWith("Compiling") ? "Building game code" : message.Contains("verification", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("Verifying") ? "Verifying the merged game" : message.StartsWith("Writing") ? "Saving merged game" : message;
        if (count.Success) OperationStep += " · " + count.Value;
    }
    private void Message(string message) { Status = message; OnPropertyChanged(nameof(StatusTitle)); Log += message + Environment.NewLine; UpdateOperation(message); }

    /// <summary>Runs one serialized operation, surfaces a friendly error, and restores button states on every exit.</summary>
    public async Task RunAsync(string label, Func<CancellationToken, IProgress<string>, Task> action, bool supportsCancellation = true)
    {
        if (IsBusy) return;
        cancellable = supportsCancellation;
        cancellation = new();
        IsBusy = true;
        Message(label);
        // Progress posts asynchronously; queued reports must not overwrite a completed
        // operation's outcome or a later operation's status.
        object progressGate = new();
        bool reporting = true;
        var progress = new Progress<string>(message =>
        {
            lock (progressGate)
                if (reporting) Message(message);
        });
        try { await action(cancellation.Token, progress); if (Status == label) Message(label + " complete."); }
        catch (OperationCanceledException) { Message("Cancelled. The game was not replaced."); }
        catch (Exception ex) { ReportError("Could not complete " + label.ToLowerInvariant() + ": " + ex.Message + " Check the selected file or folder and try again."); }
        finally
        {
            lock (progressGate) reporting = false;
            cancellation.Dispose(); cancellation = null; IsBusy = false;
        }
    }
    /// <summary>Requests cancellation at the next safe Core checkpoint.</summary>
    public void Cancel() { if (CanCancel) { cancellation?.Cancel(); Status = "Cancelling at the next safe checkpoint…"; } }
    /// <summary>Loads library and profiles, then recognizes the configured installation.</summary>
    public Task InitializeAsync() => RunAsync("Loading library", async (token, _) =>
    {
        await ReloadAsync();
        if (NeedsSetup) await SearchGamesInternalAsync(token, new Progress<string>(m => SetupMessage = m));
    });
    /// <summary>Persisted expert-mode preference.</summary>
    public bool AdvancedMode { get => Config.AdvancedMode; set { if (IsBusy) return; store.Set("advancedMode", value.ToString()); OnPropertyChanged(); OnPropertyChanged(nameof(ShowBuiltinHeader)); } }
    /// <summary>Displays operation errors in the desktop shell as well as the status line.</summary>
    public event Action<string>? Error;
    internal void ReportError(string text) { Message(text); Error?.Invoke(text); }

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
        IProgress<string> refreshProgress = new Progress<string>(Message);
        var pending = Mods.Where(m => m.IsPending).ToDictionary(m => m.Id, m => m.Enabled);
        string? previousProfile = SelectedProfile;
        var data = await Task.Run(() => (Mods: new ModLibrary(store).List(refreshProgress.Report), Profiles: new ProfileLibrary(store).List(), Config: store.Config(), State: new ManagerOperations(store).Status()));
        string? currentBaseHash = data.Config.Base is { } configuredBase && File.Exists(configuredBase)
            ? await Task.Run(() => BuildFiles.Hash(configuredBase)) : null;
        var summaries = await Task.Run(() => data.Mods.ToDictionary(m => m.Mod.Id, m =>
        {
            try
            {
                var library = new ModLibrary(store);
                var patch = library.Patch(m.Mod);
                var report = library.SanitizerReport(m.Mod);
                string factsPath = Path.Combine(store.Root, "analysis", $"{m.Mod.Sha256}-{m.Mod.BaseSha256}-{ModFactAnalyzer.Version}.json");
                var facts = currentBaseHash == m.Mod.BaseSha256 && File.Exists(factsPath) ? System.Text.Json.JsonSerializer.Deserialize<ModFacts>(File.ReadAllText(factsPath),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;
                string[] adds = facts?.Adds ?? ModFactAnalyzer.Extract(patch, []);
                string verdict = facts is null ? "? " + m.Compatibility : (facts.Compatibility == "Compatible" ? "✓ " : facts.Compatibility == "Incompatible" ? "✕ " : "? ") + facts.Compatibility + ": " + facts.Reason;
                string sourceLine = m.Mod.SourceFolder is not null
                    ? "ModKit folder: " + m.Mod.SourceFolder
                    : "Source: " + (m.Mod.Origins.FirstOrDefault()?.SourcePage ?? m.Mod.SourceFileName);
                string text = (adds.Length == 0 ? "No proven additions." : "Adds\n" + string.Join("\n", adds)) + "\n"
                    + sourceLine + "\nCompatibility: " + verdict;
                return (Summary: text, Report: (Jamm.Core.Patching.ModSanitizerReport?)report);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { return (Summary: "Summary unavailable: " + ex.Message, Report: null); }
        }));
        usableBase = data.Config.Base is { } basePath && await Task.Run(() => SteamGameDetection.Inspect(basePath).Usable);
        loading = true;
        try
        {
            Mods.Clear(); Profiles.Clear(); Order.Clear(); Conflicts.Clear();
            foreach (var m in data.Mods)
            {
                var info = summaries[m.Mod.Id];
                bool isModKit = m.Mod.IsModKit || m.Mod.SourceFolder is not null;
                Mods.Add(new(m.Mod.Id, m.Mod.Name, m.Compatibility, m.PatchCached, new FileInfo(Path.Combine(store.Root, "mods", m.Mod.Id, "data.win")).Length)
                {
                    Summary = info.Summary,
                    CatalogId = m.Mod.Origins.FirstOrDefault(o => o.Catalog == "litterbox")?.ModId,
                    IsJammMod = info.Report?.IsJammMod ?? false,
                    RemovedPerformanceCount = info.Report?.RemovedPerformanceCount ?? 0,
                    RemovedHudCount = info.Report?.RemovedHudCount ?? 0,
                    RemovedGameplayCount = info.Report?.RemovedGameplayCount ?? 0,
                    SanitizedDetails = info.Report?.FormatDetails() ?? "",
                    KeepAuthorPerformanceChanges = m.Mod.KeepAuthorPerformanceChanges,
                    KeepAuthorHudDesign = m.Mod.KeepAuthorHudDesign,
                    KeepAuthorGameplayChanges = m.Mod.KeepAuthorGameplayChanges,
                    IsModKit = isModKit,
                    SourceFolder = m.Mod.SourceFolder,
                    PackageVersion = m.Mod.Version,
                    Author = m.Mod.Author,
                    Description = m.Mod.Description
                });
            }
            RefreshLocalIcons();
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
                row.AcceptChanges();
                if (previousProfile == SelectedProfile && pending.TryGetValue(row.Id, out bool staged)) row.Enabled = staged;
                row.PropertyChanged += async (_, e) =>
                {
                    if (loading) return;
                    if (e.PropertyName == nameof(ModRow.Enabled)) { Conflicts.Clear(); RefreshBuiltins(); NotifyButtons(); }
                    else if (e.PropertyName == nameof(ModRow.KeepAuthorPerformanceChanges)) await SetKeepAuthorPerformanceChangesAsync(row);
                    else if (e.PropertyName == nameof(ModRow.KeepAuthorHudDesign)) await SetKeepAuthorHudDesignAsync(row);
                    else if (e.PropertyName == nameof(ModRow.KeepAuthorGameplayChanges)) await SetKeepAuthorGameplayChangesAsync(row);
                };
            }
            RefreshBuiltins();
            knownGame = data.State.Known; canRestore = data.State.CanRestore; GameStatus = data.State.Message;
            OnPropertyChanged(nameof(Config)); OnPropertyChanged(nameof(NeedsSetup)); OnPropertyChanged(nameof(ConflictCounts)); NotifyButtons();
        }
        finally { loading = false; }
        await Browse.UpdateMembershipAsync();
        await Browse.ApplyAutomaticFactsAsync(cancellation?.Token ?? CancellationToken.None);
    }
    internal void RefreshLocalIcons()
    {
        foreach (var row in Mods) row.Icon = ModIconResolver.Load(row.CatalogId, row.Name) ?? Browse.CatalogIcon(row.CatalogId);
    }
    /// <summary>Imports .win/.zip files using the exact CLI library rules.</summary>
    public Task AddAsync(IEnumerable<string> paths)
    {
        var savedPaths = paths.ToArray();
        if (savedPaths.Length == 0) return Task.CompletedTask;
        return AfterSetupAsync(() => AddReadyAsync(savedPaths));
    }
    private Task AddReadyAsync(IEnumerable<string> paths) => RunAsync("Adding mods", async (token, progress) =>
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
                string trimmed = Path.TrimEndingDirectorySeparator(path);
                bool isFolder = Directory.Exists(trimmed);
                if (isFolder)
                {
                    ReportError($"Could not add mod folder '{path}': {ex.Message}");
                }
                else
                {
                    ReportError($"Could not add mod '{path}': {ex.Message} Choose a legacy .win or supported .zip mod and try again.");
                }
            }
        }
        await ReloadAsync();
    });

    /// <summary>Rebuilds a ModKit folder mod from its source folder, updating its library copy and cached patch.</summary>
    public Task RebuildModAsync(string modId, Func<Task<string?>>? promptFolder = null) => RunAsync("Rebuilding mod", async (token, progress) =>
    {
        var library = new ModLibrary(store);
        var mod = library.Find(modId);
        string? folder = mod.SourceFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            if (promptFolder is not null)
            {
                folder = await promptFolder();
                if (string.IsNullOrWhiteSpace(folder))
                {
                    Message("Rebuild cancelled; no folder selected.");
                    return;
                }
                mod = library.SetSourceFolder(mod.Id, folder);
            }
            else
            {
                ReportError($"Could not rebuild '{mod.Name}': No source folder is stored for this mod.");
                return;
            }
        }
        if (!Directory.Exists(folder))
        {
            ReportError($"Could not rebuild '{mod.Name}': The ModKit folder '{folder}' was not found. Has it been moved or deleted?");
            return;
        }
        if (!File.Exists(Path.Combine(folder, ModKitPackage.ManifestFileName)))
        {
            ReportError($"Could not rebuild '{mod.Name}': Missing '{ModKitPackage.ManifestFileName}' in '{folder}'. Add a jamm-mod.json manifest or check the folder path.");
            return;
        }
        try
        {
            var result = await Task.Run(() => library.Rebuild(mod.Id, progress: progress.Report, cancellationToken: token), token);
            foreach (string warning in result.Warnings) Message(warning);
            Message($"Rebuilt {result.Mod.Name}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportError($"Could not rebuild '{mod.Name}': {ex.Message}");
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
    /// <summary>Persists checkbox state and ordering while retaining saved conflict choices.</summary>
    public Task SaveOrderAsync() => RunAsync("Saving load order", async (_, progress) =>
    {
        if (SelectedProfile is null) throw new ArgumentException("Create or select a profile before enabling mods.");
        var ids = Order.Select(m => m.Id).ToArray(); var disabled = Order.Where(m => !m.SavedEnabled).Select(m => m.Id).ToArray();
        await Task.Run(() => new ProfileLibrary(store).Update(SelectedProfile, ids, disabled, progress.Report));
        Conflicts.Clear(); OnPropertyChanged(nameof(ConflictCounts)); NotifyButtons(); Message("Load order saved. Check again to review conflicts.");
    }, false);
    /// <summary>Stages enablement without building, installing or persisting the selection.</summary>
    public Task ToggleModAsync(string id)
    {
        if (!IsBusy && Mods.FirstOrDefault(m => m.Id == id) is { } row) row.Enabled = !row.Enabled;
        return Task.CompletedTask;
    }
    /// <summary>Persists the Advanced per-mod opt-out to keep or strip the author's performance changes.</summary>
    public Task SetKeepAuthorPerformanceChangesAsync(ModRow row) => RunAsync("Updating performance changes setting", async (_, _) =>
    {
        await Task.Run(() => new ModLibrary(store).SetKeepAuthorPerformanceChanges(row.Id, row.KeepAuthorPerformanceChanges));
        Conflicts.Clear(); OnPropertyChanged(nameof(ConflictCounts)); NotifyButtons();
        Message($"{row.Name}: {(row.KeepAuthorPerformanceChanges ? "keeping the author's performance changes" : "using JAMM's sanitized patch")}.");
    }, false);
    /// <summary>Persists the Advanced per-mod opt-out to keep or strip the author's HUD design.</summary>
    public Task SetKeepAuthorHudDesignAsync(ModRow row) => RunAsync("Updating HUD design setting", async (_, _) =>
    {
        await Task.Run(() => new ModLibrary(store).SetKeepAuthorHudDesign(row.Id, row.KeepAuthorHudDesign));
        Conflicts.Clear(); OnPropertyChanged(nameof(ConflictCounts)); NotifyButtons();
        Message($"{row.Name}: {(row.KeepAuthorHudDesign ? "keeping the author's HUD design" : "protecting base HUD design")}.");
    }, false);
    /// <summary>Persists the Advanced per-mod opt-out to keep or sanitize the author's gameplay/sonar changes.</summary>
    public Task SetKeepAuthorGameplayChangesAsync(ModRow row) => RunAsync("Updating gameplay compatibility setting", async (_, _) =>
    {
        await Task.Run(() => new ModLibrary(store).SetKeepAuthorGameplayChanges(row.Id, row.KeepAuthorGameplayChanges));
        Conflicts.Clear(); OnPropertyChanged(nameof(ConflictCounts)); NotifyButtons();
        Message($"{row.Name}: {(row.KeepAuthorGameplayChanges ? "keeping the author's gameplay/sonar changes" : "enforcing vanilla sonar progression")}.");
    }, false);
    /// <summary>Number of switches waiting to be applied.</summary>
    public int PendingCount => Mods.Count(m => m.IsPending);
    /// <summary>Whether staged changes can be discarded.</summary>
    public bool CanUndo => !IsBusy && PendingCount > 0;
    /// <summary>Plain-language heading, independent of technical build messages.</summary>
    public string StatusTitle => IsBusy ? (Status.Contains("install", StringComparison.OrdinalIgnoreCase) ? "Installing…" : "Working…") : PendingCount > 0 ? $"{PendingCount} change{(PendingCount == 1 ? "" : "s")} waiting" : "Ready to play";
    /// <summary>Explains the next player action.</summary>
    public string StatusExplanation => IsBusy ? "Please wait while JAMM prepares your game." : PendingCount > 0
        ? "Press Play to apply your changes and start the game." : "Choose your mods, then press Play to start the game.";
    /// <summary>Restores the saved enablement without touching game files.</summary>
    public void UndoChanges()
    {
        if (!CanUndo) return;
        foreach (var row in Mods) row.UndoChanges();
        RefreshBuiltins();
        NotifyButtons();
    }
    private async Task ApplyPendingAsync(IProgress<string> progress)
    {
        if (PendingCount == 0) return;
        if (SelectedProfile is null)
        {
            var profiles = new ProfileLibrary(store);
            string name = profiles.List().FirstOrDefault()?.Name ?? "default";
            if (!profiles.List().Any(p => p.Name == name)) profiles.CreateEmpty(name);
            profiles.Use(name); SelectedProfile = name;
            if (!Profiles.Contains(name)) Profiles.Add(name);
        }
        await Task.Run(() => new ProfileLibrary(store).Update(SelectedProfile!, Order.Select(m => m.Id), Order.Where(m => !m.Enabled).Select(m => m.Id), progress.Report));
        foreach (var row in Mods) row.AcceptChanges();
        NotifyButtons();
    }
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
    public Task CheckAsync() => RunAsync("Checking conflicts", async (token, progress) =>
    {
        var report = await Task.Run(() => new ManagerOperations(store).Check(SelectedProfile!, token, progress.Report), token);
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
    public Task ResolveAsync(string key, string choice) => RunAsync("Saving conflict choice", async (token, progress) =>
    {
        try
        {
            var profile = await Task.Run(() => new ProfileLibrary(store).Resolve(SelectedProfile!, key, choice, progress.Report, token));
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
        if (await Task.Run(() => installer.IsInstalled(SelectedProfile, progress.Report, token), token)) { Message("This exact profile is already installed and verified."); return; }
        var prepared = await Task.Run(() => installer.Prepare(SelectedProfile, progress.Report, token), token);
        try
        {
            foreach (string decision in prepared.Build.Plan.Decisions) Message(decision);
            token.ThrowIfCancellationRequested();
            string gameFile = Path.Combine(prepared.Config.GameDir!, "data.win");
            if (prepared.Config.ConfirmedInstallDirectory != prepared.Config.GameDir && !await confirm($"Replace:\n{gameFile}\n\nVerified original backups:\n{gameFile}.cgfmm-original\n{Path.Combine(store.Root, "backups", prepared.BaseSha256 + ".win")}\n\nInstall verified profile '{prepared.Profile.Name}'?")) { Message("Installation declined. The game was not replaced."); return; }
            token.ThrowIfCancellationRequested();
            cancellable = false; NotifyButtons();
            await Task.Run(() => installer.Install(prepared, retainIndependentBase: true));
            store.Set("confirmedInstallDirectory", prepared.Config.GameDir!);
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
        await ApplyPendingAsync(progress);
        if (Order.Any(m => m.Enabled))
        {
            await InstallInternalAsync(confirm, token, progress);
            if (!await Task.Run(() => new ProfileInstaller(store).IsInstalled(SelectedProfile))) return;
        }
        else
        {
            var state = await Task.Run(() => new ManagerOperations(store).Status(), token);
            if (!state.Known) throw new IOException(state.Message);
            if (state.CanRestore)
            {
                token.ThrowIfCancellationRequested(); cancellable = false; NotifyButtons();
                await Task.Run(() => new ProfileInstaller(store).Restore());
                await ReloadAsync();
            }
        }
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
