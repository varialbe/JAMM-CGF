using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Gui.ViewModels;

/// <summary>Native catalog row with an optional bounded thumbnail.</summary>
public sealed class CatalogRow(CatalogMod mod) : ObservableObject
{
    private Avalonia.Media.IImage? icon;
    private string adds = "Content summary unavailable. Select to check.", compatibility = "?", reason = "Unsure: select this mod to check its content.", color = "#D6A500";
    private string compatibilityLabel = "Unknown";
    /// <summary>Proven resource counts.</summary>
    public string Adds { get => adds; internal set => SetProperty(ref adds, value); }
    /// <summary>Accessible compatibility symbol.</summary>
    public string CompatibilityIcon { get => compatibility; internal set { if (SetProperty(ref compatibility, value)) OnPropertyChanged(nameof(HasCompatibilityInfo)); } }
    /// <summary>Visible compatibility badge text.</summary>
    public string CompatibilityLabel { get => compatibilityLabel; internal set => SetProperty(ref compatibilityLabel, value); }
    /// <summary>Compatibility reason for tooltip and assistive technology.</summary>
    public string CompatibilityReason { get => reason; internal set { if (SetProperty(ref reason, value)) OnPropertyChanged(nameof(HasCompatibilityInfo)); } }
    /// <summary>Explains negative or uncertain compatibility through an accessible info control.</summary>
    public bool HasCompatibilityInfo => CompatibilityIcon != "✓" && !string.IsNullOrWhiteSpace(CompatibilityReason);
    /// <summary>Compatibility color.</summary>
    public string CompatibilityColor { get => color; internal set => SetProperty(ref color, value); }
    /// <summary>Public metadata and stable catalog identity.</summary>
    public CatalogMod Mod { get; } = mod;
    /// <summary>Display name.</summary>
    public string Name => Mod.Name;
    /// <summary>Readable source label shown on Browse actions.</summary>
    public string SourceLabel => Mod.Catalog switch { "gamejolt" => "Game Jolt", "first-party" => "Varialbe catalog", _ => "Litterbox" };
    /// <summary>Author, categories and release status.</summary>
    public string Summary => Mod.Summary;
    /// <summary>Short plain-text description.</summary>
    public string Excerpt => Mod.Description.Length > 180 ? Mod.Description[..180] + "…" : Mod.Description;
    /// <summary>Optional catalog thumbnail, never game data extracted locally.</summary>
    public Avalonia.Media.IImage? Icon { get => icon; internal set => SetProperty(ref icon, value); }
    private int removedPerformanceCount;
    private int removedHudCount;
    private int removedGameplayCount;
    private string sanitizedDetails = "";
    /// <summary>Number of third-party performance changes removed by JAMM's sanitizer.</summary>
    public int RemovedPerformanceCount { get => removedPerformanceCount; internal set { if (SetProperty(ref removedPerformanceCount, value)) { OnPropertyChanged(nameof(HasSanitizedInfo)); OnPropertyChanged(nameof(SanitizedLine)); } } }
    /// <summary>Number of third-party HUD/UI asset changes reverted by JAMM's sanitizer.</summary>
    public int RemovedHudCount { get => removedHudCount; internal set { if (SetProperty(ref removedHudCount, value)) { OnPropertyChanged(nameof(HasSanitizedInfo)); OnPropertyChanged(nameof(SanitizedLine)); } } }
    /// <summary>Number of gameplay/sonar compatibility changes adjusted by JAMM's sanitizer.</summary>
    public int RemovedGameplayCount { get => removedGameplayCount; internal set { if (SetProperty(ref removedGameplayCount, value)) { OnPropertyChanged(nameof(HasSanitizedInfo)); OnPropertyChanged(nameof(SanitizedLine)); } } }
    /// <summary>True when JAMM removed at least one third-party performance, HUD, or gameplay change from this mod.</summary>
    public bool HasSanitizedInfo => RemovedPerformanceCount > 0 || RemovedHudCount > 0 || RemovedGameplayCount > 0;
    /// <summary>Short summary line for removed performance, HUD, and/or gameplay changes.</summary>
    public string SanitizedLine => ModRow.FormatSanitizedLine(RemovedPerformanceCount, RemovedHudCount, RemovedGameplayCount);
    /// <summary>Detailed list of removed performance and HUD changes for the info flyout.</summary>
    public string SanitizedDetails { get => sanitizedDetails; internal set => SetProperty(ref sanitizedDetails, value); }
}

/// <summary>Discovery state is independent of local management; writes delegate to the owner's operation guard.</summary>
public sealed partial class BrowseViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel owner;
    private readonly ManagerStore store;
    private readonly ModHttp http;
    private readonly IModCatalogProvider catalog;
    private readonly IModDownloadResolver resolver;
    private CancellationTokenSource? reading;
    private CatalogRow[] all = [];
    private CatalogMod[] firstPartyCatalog = [];
    private readonly Dictionary<string, Bitmap> localArtwork = [];
    private ManagedMod[] localMods = [];
    private string[] enabledIds = [], installedIds = [];
    private CatalogRow? selected;
    private ModDownloadFile? selectedFile;
    private string search = "", category = "All categories", release = "All releases", sort = "Name", status = "Choose Refresh to load mod catalogs.", detailStatus = "Select a mod for details.", membership = "Available in catalog", transferState = "";
    private bool loading, indeterminate = true;
    private double progress;
    /// <summary>Creates public discovery services, with injectable providers for deterministic tests.</summary>
    public BrowseViewModel(MainViewModel owner, ManagerStore store, ModHttp? http = null, IModCatalogProvider? catalog = null, IModDownloadResolver? resolver = null)
    {
        this.owner = owner; this.store = store; this.http = http ?? new();
        this.catalog = catalog ?? BrowseCatalogs.Create(store, this.http);
        this.resolver = resolver ?? new GameJoltDownloadResolver(this.http);
    }
    /// <summary>Filtered native rows.</summary>
    public ObservableCollection<CatalogRow> Mods { get; } = [];
    /// <summary>Available category labels.</summary>
    public ObservableCollection<string> Categories { get; } = ["All categories"];
    /// <summary>Explicit release filters.</summary>
    public string[] Releases { get; } = ["All releases", "Released", "Preview"];
    /// <summary>Sort keys supported by the catalog metadata.</summary>
    public string[] Sorts { get; } = ["Name", "Newest", "Rating"];
    /// <summary>Advanced file choices; a unique file in the newest release is selected automatically.</summary>
    public ObservableCollection<ModDownloadFile> Files { get; } = [];
    /// <summary>Selected detail images, held only in memory and released on selection change.</summary>
    public ObservableCollection<Bitmap> Images { get; } = [];
    /// <summary>Searches name, author and description.</summary>
    public string Search { get => search; set { if (SetProperty(ref search, value)) Filter(); } }
    internal ModUpdateWorkflow CreateUpdateWorkflow() => new(store, catalog, resolver, http);
    /// <summary>Category filter.</summary>
    public string Category { get => category; set { if (SetProperty(ref category, value)) Filter(); } }
    /// <summary>Released/preview filter.</summary>
    public string Release { get => release; set { if (SetProperty(ref release, value)) Filter(); } }
    /// <summary>Sort key.</summary>
    public string Sort { get => sort; set { if (SetProperty(ref sort, value)) Filter(); } }
    /// <summary>Selected catalog row; resolution is initiated by the view.</summary>
    public CatalogRow? Selected { get => selected; set { if (owner.IsBusy) return; if (SetProperty(ref selected, value)) { SelectedFile = null; Files.Clear();
            foreach (var image in Images) image.Dispose(); Images.Clear();
            DetailStatus = "Select a mod for details."; transferState = ""; RefreshMembership(); NotifyActions(); } } }
    /// <summary>Explicit user-selected file.</summary>
    public ModDownloadFile? SelectedFile { get => selectedFile; set { if (SetProperty(ref selectedFile, value)) NotifyActions(); } }
    /// <summary>Catalog loading/offline/empty/error explanation.</summary>
    public string Status { get => status; private set => SetProperty(ref status, value); }
    /// <summary>Resolution and compatibility explanation.</summary>
    public string DetailStatus { get => detailStatus; private set => SetProperty(ref detailStatus, value); }
    /// <summary>Proven local membership, separate from downloaded/importing/installing state.</summary>
    public string Membership { get => membership; private set => SetProperty(ref membership, value); }
    /// <summary>Discovery reads never disable local manager operations.</summary>
    public bool IsLoading { get => loading; private set { SetProperty(ref loading, value); NotifyActions(); } }
    /// <summary>Byte progress as a percentage when the total size is known.</summary>
    public double Progress { get => progress; private set => SetProperty(ref progress, value); }
    /// <summary>Whether transfer size is unknown.</summary>
    public bool IsIndeterminate { get => indeterminate; private set => SetProperty(ref indeterminate, value); }
    /// <summary>Direct actions require a released file and idle state; missing setup is opened on demand.</summary>
    public bool CanDownload => !owner.IsBusy && !IsLoading && Selected?.Mod.HasDownload == true && SelectedFile is not null;
    /// <summary>Installation additionally requires a selected profile and recognized game.</summary>
    public bool CanDownloadInstall => CanDownload && (owner.NeedsSetup || owner.CanInstallOnline);
    /// <summary>Raises derived command states after any manager operation.</summary>
    public void NotifyActions() { OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(CanDownloadInstall)); }

    internal Avalonia.Media.IImage? CatalogIcon(string? id) => all.FirstOrDefault(r => r.Mod.Id == id)?.Icon
        ?? (id is not null ? localArtwork.GetValueOrDefault(id) : null);
    internal bool IsFirstParty(string? id) => id is not null && firstPartyCatalog.Any(mod => mod.Id == id);
    internal async Task LoadLocalIconsAsync(CancellationToken token)
    {
        var ids = owner.Mods.Select(r => r.CatalogId).OfType<string>().Distinct().Where(id => CatalogIcon(id) is null).ToArray();
        if (ids.Length == 0) return;
        try
        {
            var snapshot = await catalog.LoadAsync(token);
            firstPartyCatalog = snapshot.Mods.Where(m => m.Catalog == "first-party").ToArray();
            foreach (var mod in snapshot.Mods.Where(m => ids.Contains(m.Id) && (m.IconPngUrl ?? m.IconUrl) is not null).Take(60))
            {
                try
                {
                    string? sha = localMods.FirstOrDefault(m => m.Origins.Any(o => o.Catalog == mod.Catalog && o.ModId == mod.Id))?.Sha256;
                    string cachePath = sha is null ? "" : store.ArtworkPath(mod.Id, sha);
                    if (sha is not null && File.Exists(cachePath))
                    {
                        try
                        {
                            using var cached = File.OpenRead(cachePath);
                            localArtwork[mod.Id] = Bitmap.DecodeToWidth(cached, 72);
                            owner.RefreshLocalIcons();
                            continue;
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException) { }
                    }
                    var bytes = await http.ReadAsync(mod.IconPngUrl ?? mod.IconUrl!, 256 * 1024, token);
                    using var stream = new MemoryStream(bytes);
                    localArtwork[mod.Id] = Bitmap.DecodeToWidth(stream, 72);
                    if (sha is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                        string temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, cachePath, true); }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    owner.RefreshLocalIcons();
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException or NotSupportedException or TimeoutException) { }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or TimeoutException) { }
    }
    /// <summary>Persisted opt-in for previews and unsupported download sources.</summary>
    public bool ShowModsWithoutDownload
    {
        get => store.Config().ShowModsWithoutDownload;
        set
        {
            if (owner.IsBusy) return;
            try { store.Set("showModsWithoutDownload", value.ToString()); OnPropertyChanged(); Filter(); }
            catch (Exception ex) { owner.ReportError("Could not save Browse preference: " + ex.Message + " Check Settings and try again."); }
        }
    }
    /// <summary>Persisted visibility of Litterbox entries.</summary>
    public bool ShowLitterboxCatalog { get => store.Config().ShowLitterboxCatalog; set => SetCatalogVisibility("showLitterboxCatalog", value); }
    /// <summary>Persisted visibility of first-party entries.</summary>
    public bool ShowFirstPartyCatalog { get => store.Config().ShowFirstPartyCatalog; set => SetCatalogVisibility("showFirstPartyCatalog", value); }
    /// <summary>Persisted visibility of Game Jolt entries.</summary>
    public bool ShowGameJoltCatalog { get => store.Config().ShowGameJoltCatalog; set => SetCatalogVisibility("showGameJoltCatalog", value); }
    private void SetCatalogVisibility(string key, bool value)
    {
        if (owner.IsBusy) return;
        try { store.Set(key, value.ToString()); OnPropertyChanged(key switch { "showLitterboxCatalog" => nameof(ShowLitterboxCatalog), "showFirstPartyCatalog" => nameof(ShowFirstPartyCatalog), _ => nameof(ShowGameJoltCatalog) }); Filter(); }
        catch (Exception ex) { owner.ReportError("Could not save Browse preference: " + ex.Message + " Check Settings and try again."); }
    }
    /// <summary>Visible results relative to the complete catalog, including hidden entries.</summary>
    public string ShownCount => $"{Mods.Count} of {all.Length} shown";
    private void Filter()
    {
        bool includeWithoutDownload = ShowModsWithoutDownload;
        var rows = all.Where(r => (r.Mod.Catalog switch { "litterbox" => ShowLitterboxCatalog, "first-party" => ShowFirstPartyCatalog, "gamejolt" => ShowGameJoltCatalog, _ => true })
            && (includeWithoutDownload || GameJoltDownloadResolver.SupportsSource(r.Mod))
            && (string.IsNullOrWhiteSpace(Search) || (r.Name + " " + r.Mod.Author + " " + r.Mod.Description).Contains(Search, StringComparison.OrdinalIgnoreCase))
            && (Category == "All categories" || r.Mod.Categories.Contains(Category))
            && (Release == "All releases" || r.Mod.IsPreview == (Release == "Preview")));
        rows = Sort switch { "Newest" => rows.OrderByDescending(r => r.Mod.CreatedUtc), "Rating" => rows.OrderByDescending(r => r.Mod.Rating), _ => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase) };
        Mods.Clear(); foreach (var row in rows) Mods.Add(row);
        OnPropertyChanged(nameof(EmptyMessage)); OnPropertyChanged(nameof(ShownCount));
    }
    /// <summary>Explicit empty result explanation.</summary>
    public string EmptyMessage => Mods.Count == 0 && !IsLoading ? "No mods match. Change filters or Refresh." : "";
    /// <summary>Loads public metadata or a timestamped offline snapshot.</summary>
    public async Task RefreshAsync()
    {
        if (owner.IsBusy) return;
        reading?.Cancel(); var current = new CancellationTokenSource(); reading = current; IsLoading = true; Status = "Loading mod catalogs…";
        try
        {
            var snapshot = await catalog.LoadAsync(current.Token);
            if (reading != current) return;
            firstPartyCatalog = snapshot.Mods.Where(m => m.Catalog == "first-party").ToArray();
            foreach (var row in all) (row.Icon as IDisposable)?.Dispose();
            all = snapshot.Mods.Select(m => new CatalogRow(m)
            { Icon = m.IconPngUrl is null ? ModIconResolver.Load(m.Id, m.Name) : null }).ToArray();
            await ApplyAutomaticFactsAsync(current.Token); owner.RefreshLocalIcons();
            Categories.Clear(); Categories.Add("All categories");
            foreach (string name in all.SelectMany(r => r.Mod.Categories).Distinct().Order()) Categories.Add(name);
            category = "All categories"; OnPropertyChanged(nameof(Category)); Filter();
            Status = $"{snapshot.Mods.Length} mods · Last successful fetch: {snapshot.FetchedUtc.ToLocalTime():g}" + (snapshot.OfflineReason is null ? "" : " · " + snapshot.OfflineReason);
            IsLoading = false; OnPropertyChanged(nameof(EmptyMessage));
            // Small in-memory thumbnail budget; no background refresh and no tracking/statistics requests.
            int budget = 4 * 1048576;
            foreach (var row in all.Take(60))
            {
                current.Token.ThrowIfCancellationRequested(); if (budget <= 0) break;
                if (row.Icon is not null || (row.Mod.IconPngUrl ?? row.Mod.IconUrl) is not { } url) continue;
                try
                {
                    var bytes = await http.ReadAsync(url, Math.Min(256 * 1024, budget), current.Token); budget -= bytes.Length;
                    using var stream = new MemoryStream(bytes); row.Icon = Bitmap.DecodeToWidth(stream, 72); owner.RefreshLocalIcons();
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException or NotSupportedException or TimeoutException) { /* Optional image; text remains usable. */ }
            }
        }
        catch (OperationCanceledException) { if (reading == current) Status = "Catalog loading cancelled. Refresh when ready."; }
        catch (Exception ex) { if (reading == current) Status = "Could not load catalog: " + ex.Message + " Refresh or use the local Manager."; }
        finally { if (reading == current) { reading = null; IsLoading = false; OnPropertyChanged(nameof(EmptyMessage)); } current.Dispose(); }
    }
    /// <summary>Resolves files and loads optional detail images without trusting creator badges for compatibility.</summary>
    public async Task SelectAsync()
    {
        reading?.Cancel(); var current = new CancellationTokenSource(); reading = current;
        var row = Selected; IsLoading = true; Files.Clear(); SelectedFile = null;
        foreach (var image in Images) image.Dispose(); Images.Clear();
        if (row is null) { IsLoading = false; reading = null; current.Dispose(); return; }
        row.CompatibilityIcon = "?"; row.CompatibilityColor = "#D6A500";
        row.CompatibilityLabel = "Unknown";
        row.CompatibilityReason = "Unsure: resolving selected source."; row.Adds = "Checking content…";
        DetailStatus = "Finding the download…";
        try
        {
            var resolution = await resolver.ResolveAsync(row.Mod, current.Token);
            if (reading != current || row != Selected) return;
            foreach (var file in resolution.Files) Files.Add(file);
            SelectedFile = ModFileSelection.Choose(resolution.Files);
            DetailStatus = resolution.Notice ?? "Checking selected download…";
            foreach (string url in new[] { row.Mod.BannerUrl }.Concat(row.Mod.Screenshots ?? []).OfType<string>().Take(4))
            {
                try
                {
                    var bytes = await http.ReadAsync(url, 1048576, current.Token);
                    if (reading != current || row != Selected) return;
                    using var stream = new MemoryStream(bytes); Images.Add(Bitmap.DecodeToWidth(stream, 600));
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException or NotSupportedException or TimeoutException) { /* Optional image. */ }
            }
            if (SelectedFile is { } chosen && owner.HasOnlineBase)
            {
                await CheckFactsAsync(row, chosen, current.Token);
            }
            else
            {
                DetailStatus = resolution.Notice ?? (SelectedFile is null ? "Several downloads are available. Turn on Advanced mode to choose one." : "Choose a game base in Settings to check and download this mod.");
                row.Adds = DetailStatus;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (reading == current) { DetailStatus = "Could not check this mod: " + ex.Message + " Select again to retry."; row.Adds = "Content check unavailable. Select again to retry."; row.CompatibilityReason = "Unsure: " + ex.Message; } }
        finally { if (reading == current) { reading = null; IsLoading = false; } current.Dispose(); }
    }
    private async Task CheckFactsAsync(CatalogRow row, ModDownloadFile chosen, CancellationToken token)
    {
        string baseHash = await Task.Run(() => Jamm.Core.Building.BuildFiles.Hash(store.Config().Base!), token);
        var remote = await new SharedCompatibility(store, http).FindAsync(baseHash, chosen, null, token);
        if (remote is not null) { ShowFacts(row, remote.Facts, $"checked by JAMM server on {remote.CheckedUtc:yyyy-MM-dd HH:mm} UTC"); return; }
        row.CompatibilityIcon = "?"; row.CompatibilityColor = "#D6A500";
        row.CompatibilityReason = "Unsure: checking selected file.";
        row.Adds = "Checking content…";
        DetailStatus = "Downloading for content check…"; IsIndeterminate = true;
        var bytes = new Progress<ModDownloadProgress>(p => { if (token.IsCancellationRequested) return; IsIndeterminate = p.Total is null or <= 0; Progress = p.Total > 0 ? 100d * p.Received / p.Total.Value : 0; DetailStatus = $"Downloading · {p.Received / 1048576d:0.0} MB"; });
        string archive = await new ModDownloadCache(store, http).FetchFileAsync(chosen, resolver, bytes, token);
        IsIndeterminate = true; DetailStatus = "Analyzing mod content…";
        IProgress<string> steps = new Progress<string>(m => { if (!token.IsCancellationRequested) { owner.ReportProcessingStep(m); DetailStatus = owner.OperationStep; IsIndeterminate = owner.OperationIndeterminate; Progress = owner.OperationProgress; } });
        var facts = await Task.Run(() => new ModFactAnalyzer(store).Analyze(archive, token, steps.Report), token);
        token.ThrowIfCancellationRequested();
        ShowFacts(row, facts, "checked on this PC");
    }
    private void ShowFacts(CatalogRow row, ModFacts facts, string source)
    {
        row.Adds = facts.Adds.Length == 0 ? "No proven additions." : "Adds\n" + string.Join("\n", facts.Adds);
        row.CompatibilityIcon = facts.Compatibility == "Compatible" ? "✓" : facts.Compatibility == "Incompatible" ? "✕" : "?";
        row.CompatibilityLabel = facts.Compatibility switch { "Compatible" => "Compatible", "Incompatible" => "Incompatible", _ => "Warning" };
        row.CompatibilityColor = facts.Compatibility == "Compatible" ? "#259D50" : facts.Compatibility == "Incompatible" ? "#D74040" : "#D6A500";
        row.CompatibilityReason = facts.Compatibility + ": " + facts.Reason + " · " + source;
        var removed = facts.RemovedPerformanceChanges ?? [];
        row.RemovedPerformanceCount = removed.Count(r => r.Category != Jamm.Core.Patching.ModSanitizer.HudStyle && r.Category != Jamm.Core.Patching.ModSanitizer.GameplaySonar);
        row.RemovedHudCount = removed.Count(r => r.Category == Jamm.Core.Patching.ModSanitizer.HudStyle);
        row.RemovedGameplayCount = removed.Count(r => r.Category == Jamm.Core.Patching.ModSanitizer.GameplaySonar);
        row.SanitizedDetails = removed.Count == 0 ? "" : string.Join("\n", removed.Select(r => $"[{r.Category}] {r.Entry}: {r.Summary}"));
        DetailStatus = facts.Reason;
    }
    /// <summary>Checks an explicitly chosen advanced release without changing the catalog selection.</summary>
    public async Task SelectFileAsync()
    {
        if (IsLoading || owner.IsBusy || Selected is not { } row || SelectedFile is not { } file) return;
        reading?.Cancel(); using var current = new CancellationTokenSource(); reading = current; IsLoading = true;
        try { await CheckFactsAsync(row, file, current.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { row.Adds = "Content check unavailable. Select again to retry."; row.CompatibilityReason = "Unsure: " + ex.Message; }
        finally { if (reading == current) { reading = null; IsLoading = false; } }
    }
    /// <summary>Computes proven profile/installation membership off the UI thread after local changes.</summary>
    public async Task UpdateMembershipAsync()
    {
        var data = await Task.Run(() =>
        {
            var mods = new ModLibrary(store).Metadata().ToArray();
            var profiles = new ProfileLibrary(store);
            string[] enabled = owner.SelectedProfile is { } name ? profiles.Get(name).EnabledMods : [];
            var installer = new ProfileInstaller(store); var installed = installer.InstalledProfileName();
            string[] active = installed is not null && installer.IsInstalled(installed) ? profiles.Get(installed).EnabledMods : [];
            return (mods, enabled, active);
        });
        (localMods, enabledIds, installedIds) = data; RefreshMembership();
    }
    /// <summary>Uses provenance rather than a name match to describe local state.</summary>
    public void RefreshMembership()
    {
        NotifyActions();
        if (Selected is null) { Membership = "Select a mod"; return; }
        try
        {
            var ids = localMods.Where(m => m.Origins.Any(o => o.Catalog == Selected.Mod.Catalog && o.ModId == Selected.Mod.Id)).Select(m => m.Id).ToArray();
            string state = ids.Length == 0 ? "Available in catalog; no proven local import" : ids.Intersect(enabledIds).Any() ? "Enabled in selected profile" : "In library";
            if (ids.Intersect(installedIds).Any()) state += " · Included in currently installed profile";
            Membership = (transferState.Length == 0 || transferState == state ? "" : transferState + " · ") + state;
        }
        catch (Exception ex) { Membership = "Local status unavailable: " + ex.Message; }
    }
    /// <summary>Downloads, imports and enables under the existing serialized operation; advanced mode can also install.</summary>
    public Task DownloadAsync(bool install, Func<string, Task<bool>> confirm)
    {
        if (owner.IsBusy || IsLoading) return Task.CompletedTask;
        if (!CanDownload)
        {
            owner.ReportError($"Cannot download '{Selected?.Name ?? "selected mod"}': {DetailStatus} Choose a supported file in Advanced mode or open the mod's source page.");
            return Task.CompletedTask;
        }
        var requestedMod = Selected; var requestedFile = SelectedFile;
        return owner.AfterSetupAsync(() =>
        {
            Selected = requestedMod; SelectedFile = requestedFile;
            return DownloadReadyAsync(install, confirm);
        });
    }
    private Task DownloadReadyAsync(bool install, Func<string, Task<bool>> confirm)
    {
        if (!CanDownload || install && !CanDownloadInstall) return Task.CompletedTask;
        var mod = Selected!.Mod; var file = SelectedFile!; string? profile = owner.SelectedProfile;
        return owner.RunAsync("Downloading mod", async (token, progressText) =>
        {
            string? path = null; bool reloaded = false, imported = false; int transferring = 1; object transferGate = new();
            try
            {
                transferState = "Downloading"; RefreshMembership(); Progress = 0; IsIndeterminate = true;
                path = await new ModDownloadCache(store, http).FetchFileAsync(file, resolver, new Progress<ModDownloadProgress>(p =>
                {
                    lock (transferGate)
                    {
                    if (transferring == 0) return;
                    IsIndeterminate = p.Total is null or <= 0; Progress = p.Total > 0 ? 100d * p.Received / p.Total.Value : 0;
                    transferState = $"Downloading: {p.Received / 1048576d:0.0} MB"; Membership = transferState;
                    owner.UpdateOperation(p.Total > 0 ? $"Downloading {p.Received}/{p.Total.Value} bytes" : transferState);
                    }
                }), token);
                string baseHash = await Task.Run(() => Jamm.Core.Building.BuildFiles.Hash(store.Config().Base!), token);
                string archiveHash = await Task.Run(() => Jamm.Core.Building.BuildFiles.Hash(path), token);
                var shared = await new SharedCompatibility(store, http).FindAsync(baseHash, file, archiveHash, token);
                var verifiedFacts = shared?.Facts ?? await Task.Run(() => new ModFactAnalyzer(store).Analyze(path, token, progressText.Report), token);
                ShowFacts(Selected!, verifiedFacts, shared is null ? "checked on this PC" : $"checked by JAMM server on {shared.CheckedUtc:yyyy-MM-dd HH:mm} UTC");
                lock (transferGate) transferring = 0;
                transferState = "Importing"; RefreshMembership(); IsIndeterminate = true;
                var origin = new ModOrigin(mod.Catalog, mod.Id, file.SourceUrl, file.Provider, file.Id, file.ReleaseId, mod.Version ?? file.Version)
                { PackageSha256 = mod.Sha256 };
                var result = await Task.Run(() =>
                {
                    var library = new ModLibrary(store);
                    string name = new string(mod.Name.Where(c => c != ',' && !char.IsControl(c)).Take(160).ToArray()).Trim();
                    if (name.Length == 0) name = mod.Catalog switch { "gamejolt" => "Game Jolt " + mod.Id, "first-party" => "Varialbe " + mod.Id, _ => "Litterbox " + mod.Id };
                    var names = library.Metadata().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
                    string stem = name;
                    for (int suffix = 2; names.Contains(name); suffix++) name = stem + " (" + suffix + ")";
                    return library.Add(path, name, progressText.Report, token, origin);
                }, token);
                imported = true; transferState = "In library";
                foreach (string warning in result.Warnings) progressText.Report(warning);
                {
                    if (profile is null)
                    {
                        var profiles = new ProfileLibrary(store);
                        profile = profiles.List().FirstOrDefault()?.Name;
                        if (profile is null) { profile = "default"; profiles.CreateEmpty(profile); }
                        profiles.Use(profile);
                    }
                    token.ThrowIfCancellationRequested();
                    await Task.Run(() =>
                    {
                        var profiles = new ProfileLibrary(store); var p = profiles.Get(profile);
                        if (!p.EnabledMods.Contains(result.Mod.Id)) profiles.Update(profile, p.Mods.Contains(result.Mod.Id) ? p.Mods : [.. p.Mods, result.Mod.Id], p.DisabledMods.Where(id => id != result.Mod.Id), progressText.Report);
                    }, token);
                    transferState = "Enabled in selected profile";
                }
                await owner.ReloadAfterDownloadAsync(); reloaded = true; RefreshMembership();
                progressText.Report("Mod enabled. Press Play to apply your mods and start the game.");
                if (install) await owner.InstallDownloadAsync(confirm, token, progressText);
            }
            catch (OperationCanceledException) { lock (transferGate) { transferring = 0; transferState = "Cancelled"; } throw; }
            catch (Exception ex)
            {
                lock (transferGate) { transferring = 0; transferState = "Failed"; }
                throw new IOException($"Could not download or add '{mod.Name}' from '{file.SourceUrl}': {ex.Message} Check your connection and retry Download, or add a local .win/.zip file.", ex);
            }
            finally
            {
                lock (transferGate) transferring = 0;
                // Validated content-addressed downloads are retained for selection and import reuse.
                // A completed import remains valid even if cancellation/failure happens during a later profile/install step.
                if (imported && !reloaded)
                {
                    try { await owner.ReloadAfterDownloadAsync(); }
                    catch (Exception ex) { progressText.Report("Imported archive is retained, but the library could not refresh: " + ex.Message); }
                }
                RefreshMembership();
            }
        });
    }
    /// <summary>Cancels selection analysis and catalog requests.</summary>
    public void CancelReads() => reading?.Cancel();
    /// <summary>Cancels discovery reads without affecting local manager operations.</summary>
    public void Dispose() { reading?.Cancel(); foreach (var row in all) (row.Icon as IDisposable)?.Dispose(); foreach (var icon in localArtwork.Values) icon.Dispose(); foreach (var image in Images) image.Dispose(); http.Dispose(); }
}
