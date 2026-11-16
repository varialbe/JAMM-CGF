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
    private readonly ModDownloadService downloads;
    private CancellationTokenSource? reading;
    private CatalogRow[] all = [];
    private ManagedMod[] localMods = [];
    private string[] enabledIds = [], installedIds = [];
    private CatalogRow? selected;
    private ModDownloadFile? selectedFile;
    private string search = "", category = "All categories", release = "All releases", sort = "Name", status = "Choose Retry to load Litterbox.", detailStatus = "Select a mod for details.", membership = "Available in catalog", transferState = "";
    private bool loading, indeterminate = true, addToProfile;
    private double progress;
    /// <summary>Creates public discovery services, with injectable providers for deterministic tests.</summary>
    public BrowseViewModel(MainViewModel owner, ManagerStore store, ModHttp? http = null, IModCatalogProvider? catalog = null, IModDownloadResolver? resolver = null)
    {
        this.owner = owner; this.store = store; this.http = http ?? new();
        this.catalog = catalog ?? new LitterboxCatalogProvider(store, this.http);
        this.resolver = resolver ?? new GameJoltDownloadResolver(this.http);
        downloads = new(store, this.http);
    }
    /// <summary>Filtered native rows.</summary>
    public ObservableCollection<CatalogRow> Mods { get; } = [];
    /// <summary>Available category labels.</summary>
    public ObservableCollection<string> Categories { get; } = ["All categories"];
    /// <summary>Explicit release filters.</summary>
    public string[] Releases { get; } = ["All releases", "Released", "Preview"];
    /// <summary>Sort keys supported by the catalog metadata.</summary>
    public string[] Sorts { get; } = ["Name", "Newest", "Rating"];
    /// <summary>Explicit file choices; no file is selected implicitly.</summary>
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
    /// <summary>Whether add-only also enables the imported mod in the selected profile.</summary>
    public bool AddToProfile { get => addToProfile; set => SetProperty(ref addToProfile, value); }
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
                catch (Exception ex) when (ex is HttpRequestException or IOException or ArgumentException or NotSupportedException or TimeoutException) { /* Optional image; text remains usable. */ }
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
        DetailStatus = "Resolving source… Compatibility: unknown. Version: unknown until a release is resolved.";
        try
        {
            var resolution = await resolver.ResolveAsync(row.Mod, current.Token);
            if (reading != current || row != Selected) return;
            foreach (var file in resolution.Files) Files.Add(file);
            DetailStatus = (resolution.Notice ?? "Choose a file/release below.") + (resolution.Files.Any(f => f.Version is not null) ? " " : " Version: unknown. ") + "Compatibility with your base: unknown until import. "
                + (row.Mod.VerifiedCreator ? "Verified creator is a catalog badge, not a compatibility guarantee." : "");
            IsLoading = false;
            foreach (string url in new[] { row.Mod.BannerUrl }.Concat(row.Mod.Screenshots ?? []).OfType<string>().Take(4))
            {
                try
                {
                    var bytes = await http.ReadAsync(url, 1048576, current.Token);
                    if (reading != current || row != Selected) return;
                    using var stream = new MemoryStream(bytes); Images.Add(Bitmap.DecodeToWidth(stream, 600));
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or ArgumentException or NotSupportedException or TimeoutException) { /* Optional image. */ }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (reading == current) DetailStatus = "Could not resolve this source: " + ex.Message + " Open source page or select again to retry."; }
        finally { if (reading == current) { reading = null; IsLoading = false; } current.Dispose(); }
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
            Membership = (transferState.Length == 0 ? "" : transferState + " · ") + state;
        }
        catch (Exception ex) { Membership = "Local status unavailable: " + ex.Message; }
    }
    /// <summary>Downloads, imports and optionally activates/installs under the existing serialized operation.</summary>
    public Task DownloadAsync(bool install, Func<string, Task<bool>> confirm)
    {
        if (!CanDownload || install && !CanDownloadInstall) return Task.CompletedTask;
        var mod = Selected!.Mod; var file = SelectedFile!; string? profile = owner.SelectedProfile; bool enable = install || AddToProfile;
        return owner.RunAsync("Downloading mod", async (token, progressText) =>
        {
            string? path = null; bool reloaded = false, imported = false; int transferring = 1;
            try
            {
                transferState = "Downloading"; RefreshMembership(); Progress = 0; IsIndeterminate = true;
                var uri = await resolver.GetFileUriAsync(file, token);
                path = await downloads.DownloadAsync(uri, new Progress<ModDownloadProgress>(p =>
                {
                    if (Volatile.Read(ref transferring) == 0) return;
                    IsIndeterminate = p.Total is null or <= 0; Progress = p.Total > 0 ? 100d * p.Received / p.Total.Value : 0;
                    transferState = $"Downloading: {p.Received / 1048576d:0.0} MB"; Membership = transferState;
                }), token);
                Interlocked.Exchange(ref transferring, 0);
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
                if (enable && profile is not null)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Run(() =>
                    {
                        var profiles = new ProfileLibrary(store); var p = profiles.Get(profile);
                        if (!p.EnabledMods.Contains(result.Mod.Id)) profiles.Update(profile, p.Mods.Contains(result.Mod.Id) ? p.Mods : [.. p.Mods, result.Mod.Id], p.DisabledMods.Where(id => id != result.Mod.Id));
                    }, token);
                    transferState = "Enabled in selected profile";
                }
                await owner.ReloadAfterDownloadAsync(); reloaded = true; RefreshMembership();
                progressText.Report("Imported into library. Use the Manager to review load order and conflicts before installation.");
                if (install) await owner.InstallDownloadAsync(confirm, token, progressText);
            }
            catch (OperationCanceledException) { transferState = "Cancelled"; throw; }
            catch { transferState = "Failed"; throw; }
            finally
            {
                Interlocked.Exchange(ref transferring, 0);
                if (path is not null && File.Exists(path)) File.Delete(path);
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
    /// <summary>Cancels discovery reads without affecting local manager operations.</summary>
    public void Dispose() { reading?.Cancel(); foreach (var row in all) row.Icon?.Dispose(); foreach (var image in Images) image.Dispose(); http.Dispose(); }
}
