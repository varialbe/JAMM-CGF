using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Jamm.Core.Browsing;
using Jamm.Core.Management;

namespace Jamm.Gui.ViewModels;

/// <summary>Native catalog row with an optional bounded thumbnail.</summary>
public sealed class CatalogRow(CatalogMod mod) : ObservableObject
{
    private Bitmap? icon;
    private string adds = "Checking on selection…", compatibility = "?", reason = "Unsure: select this mod to check its content.", color = "#D6A500";
    /// <summary>Proven resource counts.</summary>
    public string Adds { get => adds; internal set => SetProperty(ref adds, value); }
    /// <summary>Accessible compatibility symbol.</summary>
    public string CompatibilityIcon { get => compatibility; internal set => SetProperty(ref compatibility, value); }
    /// <summary>Compatibility reason for tooltip and assistive technology.</summary>
    public string CompatibilityReason { get => reason; internal set => SetProperty(ref reason, value); }
    /// <summary>Compatibility color.</summary>
    public string CompatibilityColor { get => color; internal set => SetProperty(ref color, value); }
    /// <summary>Public metadata and stable catalog identity.</summary>
    public CatalogMod Mod { get; } = mod;
    /// <summary>Display name.</summary>
    public string Name => Mod.Name;
    /// <summary>Author, categories and release status.</summary>
    public string Summary => Mod.Summary;
    /// <summary>Short plain-text description.</summary>
    public string Excerpt => Mod.Description.Length > 180 ? Mod.Description[..180] + "…" : Mod.Description;
    /// <summary>Optional catalog thumbnail, never game data extracted locally.</summary>
    public Bitmap? Icon { get => icon; internal set => SetProperty(ref icon, value); }
}

/// <summary>Discovery state is independent of local management; writes delegate to the owner's operation guard.</summary>
public sealed class BrowseViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel owner;
    private readonly ManagerStore store;
    private readonly ModHttp http;
    private readonly IModCatalogProvider catalog;
    private readonly IModDownloadResolver resolver;
    private CancellationTokenSource? reading;
    private CatalogRow[] all = [];
    private ManagedMod[] localMods = [];
    private string[] enabledIds = [], installedIds = [];
    private CatalogRow? selected;
    private ModDownloadFile? selectedFile;
    private string search = "", category = "All categories", release = "All releases", sort = "Name", status = "Choose Retry to load Litterbox.", detailStatus = "Select a mod for details.", membership = "Available in catalog", transferState = "";
    private bool loading, indeterminate = true;
    private double progress;
    /// <summary>Creates public discovery services, with injectable providers for deterministic tests.</summary>
    public BrowseViewModel(MainViewModel owner, ManagerStore store, ModHttp? http = null, IModCatalogProvider? catalog = null, IModDownloadResolver? resolver = null)
    {
        this.owner = owner; this.store = store; this.http = http ?? new();
        this.catalog = catalog ?? new LitterboxCatalogProvider(store, this.http);
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
    /// <summary>Direct actions require a released file, configured base and the shared idle state.</summary>
    public bool CanDownload => !owner.IsBusy && !IsLoading && Selected?.Mod.HasDownload == true && SelectedFile is not null && owner.HasOnlineBase;
    /// <summary>Installation additionally requires a selected profile and recognized game.</summary>
    public bool CanDownloadInstall => CanDownload && owner.CanInstallOnline;
    /// <summary>Raises derived command states after any manager operation.</summary>
    public void NotifyActions() { OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(CanDownloadInstall)); }

    private void Filter()
    {
        var rows = all.Where(r => (string.IsNullOrWhiteSpace(Search) || (r.Name + " " + r.Mod.Author + " " + r.Mod.Description).Contains(Search, StringComparison.OrdinalIgnoreCase))
            && (Category == "All categories" || r.Mod.Categories.Contains(Category))
            && (Release == "All releases" || r.Mod.IsPreview == (Release == "Preview")));
        rows = Sort switch { "Newest" => rows.OrderByDescending(r => r.Mod.CreatedUtc), "Rating" => rows.OrderByDescending(r => r.Mod.Rating), _ => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase) };
        Mods.Clear(); foreach (var row in rows) Mods.Add(row);
        OnPropertyChanged(nameof(EmptyMessage));
    }
    /// <summary>Explicit empty result explanation.</summary>
    public string EmptyMessage => Mods.Count == 0 && !IsLoading ? "No mods match. Change filters or Retry." : "";
    /// <summary>Loads public metadata or a timestamped offline snapshot.</summary>
    public async Task RefreshAsync()
    {
        if (owner.IsBusy) return;
        reading?.Cancel(); var current = new CancellationTokenSource(); reading = current; IsLoading = true; Status = "Loading Litterbox…";
        try
        {
            var snapshot = await catalog.LoadAsync(current.Token);
            if (reading != current) return;
            foreach (var row in all) row.Icon?.Dispose();
            all = snapshot.Mods.Select(m => new CatalogRow(m)).ToArray();
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
                if (row.Mod.IconUrl is not { } url) continue;
                try
                {
                    var bytes = await http.ReadAsync(url, Math.Min(256 * 1024, budget), current.Token); budget -= bytes.Length;
                    using var stream = new MemoryStream(bytes); row.Icon = Bitmap.DecodeToWidth(stream, 72);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException or NotSupportedException or TimeoutException) { /* Optional image; text remains usable. */ }
            }
        }
        catch (OperationCanceledException) { if (reading == current) Status = "Catalog loading cancelled. Retry when ready."; }
        catch (Exception ex) { if (reading == current) Status = "Could not load catalog: " + ex.Message + " Retry or use the local Manager."; }
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
        row.CompatibilityIcon = "?"; row.CompatibilityColor = "#D6A500";
        row.CompatibilityReason = "Unsure: checking selected file.";
        row.Adds = "Checking content…";
        string archive = await new ModDownloadCache(store, http).FetchFileAsync(chosen, resolver, null, token);
        var facts = await Task.Run(() => new ModFactAnalyzer(store).Analyze(archive, token), token);
        token.ThrowIfCancellationRequested();
        row.Adds = facts.Adds.Length == 0 ? "No proven additions." : "Adds\n" + string.Join("\n", facts.Adds);
        row.CompatibilityIcon = facts.Compatibility == "Compatible" ? "✓" : facts.Compatibility == "Incompatible" ? "✕" : "?";
        row.CompatibilityColor = facts.Compatibility == "Compatible" ? "#259D50" : facts.Compatibility == "Incompatible" ? "#D74040" : "#D6A500";
        row.CompatibilityReason = facts.Compatibility + ": " + facts.Reason;
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
            var ids = localMods.Where(m => m.Origins.Any(o => o.Catalog == "litterbox" && o.ModId == Selected.Mod.Id)).Select(m => m.Id).ToArray();
            string state = ids.Length == 0 ? "Available in catalog; no proven local import" : ids.Intersect(enabledIds).Any() ? "Enabled in selected profile" : "In library";
            if (ids.Intersect(installedIds).Any()) state += " · Included in currently installed profile";
            Membership = (transferState.Length == 0 || transferState == state ? "" : transferState + " · ") + state;
        }
        catch (Exception ex) { Membership = "Local status unavailable: " + ex.Message; }
    }
    /// <summary>Downloads, imports and enables under the existing serialized operation; advanced mode can also install.</summary>
    public Task DownloadAsync(bool install, Func<string, Task<bool>> confirm)
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
                    }
                }), token);
                lock (transferGate) transferring = 0;
                transferState = "Importing"; RefreshMembership(); IsIndeterminate = true;
                var origin = new ModOrigin("litterbox", mod.Id, file.SourceUrl, file.Provider, file.Id, file.ReleaseId, file.Version);
                var result = await Task.Run(() =>
                {
                    var library = new ModLibrary(store);
                    string name = new string(mod.Name.Where(c => c != ',' && !char.IsControl(c)).Take(160).ToArray()).Trim();
                    if (name.Length == 0) name = "Litterbox " + mod.Id;
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
                        if (!p.EnabledMods.Contains(result.Mod.Id)) profiles.Update(profile, p.Mods.Contains(result.Mod.Id) ? p.Mods : [.. p.Mods, result.Mod.Id], p.DisabledMods.Where(id => id != result.Mod.Id));
                    }, token);
                    transferState = "Enabled in selected profile";
                }
                await owner.ReloadAfterDownloadAsync(); reloaded = true; RefreshMembership();
                progressText.Report("Mod enabled. Press Play to apply your mods and start the game.");
                if (install) await owner.InstallDownloadAsync(confirm, token, progressText);
            }
            catch (OperationCanceledException) { lock (transferGate) { transferring = 0; transferState = "Cancelled"; } throw; }
            catch { lock (transferGate) { transferring = 0; transferState = "Failed"; } throw; }
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
    public void Dispose() { reading?.Cancel(); foreach (var row in all) row.Icon?.Dispose(); foreach (var image in Images) image.Dispose(); http.Dispose(); }
}
