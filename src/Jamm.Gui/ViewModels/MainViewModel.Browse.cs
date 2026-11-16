using Jamm.Core.Management;

namespace Jamm.Gui.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Native online discovery, independent from local manager loading.</summary>
    public BrowseViewModel Browse { get; }
    internal bool HasOnlineBase { get { try { return !NeedsSetup; } catch { return false; } } }
    internal bool CanInstallOnline => knownGame && SelectedProfile is not null && HasOnlineBase;
    internal Task ReloadAfterDownloadAsync() => ReloadAsync();
    internal async Task InstallDownloadAsync(Func<string, Task<bool>> confirm, CancellationToken token, IProgress<string> progress)
    {
        var report = await Task.Run(() => new ManagerOperations(store).Check(SelectedProfile!, token, progress.Report), token);
        var resolutions = new ProfileLibrary(store).Get(SelectedProfile!).Resolutions;
        PopulateConflicts(report, resolutions);
        // Discovery never silently accepts newly introduced overlaps. Continue in the existing Manager UI.
        if (Conflicts.Any(c => c.Severity == "CONFLICT" && (!c.CanChoose || c.SelectedChoice is null)))
        {
            Message("Added to profile. Review conflicts in Manager, save choices, then press Install. The game was not replaced.");
            return;
        }
        await InstallInternalAsync(confirm, token, progress);
    }
}
