using Jamm.Core.Building;
using Jamm.Core.Patching;

namespace Jamm.Core.Management;

internal static class ProfileArchiveBuilder
{
    internal static BuildResult Build(ManagerStore store, ModProfile profile, ProfileSelection selection, string output,
        bool dryRun, bool noRepack, Action<string>? progress, CancellationToken cancellationToken)
    {
        string[] protectedPaths = [selection.VanillaPath, .. selection.Inputs.Select(i => i.ArchivePath)];
        if (store.Config().GameDir is { } gameDir)
            protectedPaths = [.. protectedPaths, Path.Combine(gameDir, "data.win")];
        BuildFiles.ValidateNewOutput(output, protectedPaths, dryRun);
        var options = AnalysisOptions.Default with { DataDirectory = store.Root };
        var resolutions = new ProfileLibrary(store).ResolutionsByAncestor(profile, selection);
        if (selection.Addons.Length == 0)
        {
            var build = ArchiveBuilder.BuildLayer(selection.VanillaPath, selection.Vanilla, output, resolutions.Vanilla,
                dryRun, progress, cancellationToken, options, noRepack, additionalProtectedPaths: protectedPaths, perfSettings: profile.Perf);
            return build with { Plan = build.Plan with { Decisions = [.. selection.Warnings.Select(w => w.Message), .. build.Plan.Decisions] } };
        }

        string intermediate = store.Path("builds", $".base-{Guid.NewGuid():N}.win");
        try
        {
            var first = ArchiveBuilder.BuildLayer(selection.VanillaPath, selection.Vanilla, intermediate, resolutions.Vanilla,
                false, progress, cancellationToken, options, noRepack: true, intermediate: true);
            string ancestorPath = selection.BaseArchive!;
            // Express the first pass's selected changes in the addons' coordinates. Disjoint edits
            // from either pass can then merge, and addon conflicts use their actual ancestor.
            var accumulated = CreateDelta(ancestorPath, intermediate, selection, first, options, progress, cancellationToken);
            var finalInputs = new[] { new BuildInput(accumulated, intermediate) }.Concat(selection.Addons).ToArray();
            var final = ArchiveBuilder.BuildLayer(ancestorPath, finalInputs, output, resolutions.Addons,
                dryRun, progress, cancellationToken, options, noRepack, additionalProtectedPaths: protectedPaths, hasMergedBase: true, perfSettings: profile.Perf);
            return final with
            {
                Base = first.Base,
                Plan = final.Plan with
                {
                    Catalog = [.. first.Plan.Catalog, .. final.Plan.Catalog],
                    Decisions = [.. selection.Warnings.Select(w => w.Message), .. first.Plan.Decisions, .. final.Plan.Decisions],
                    DiscardedHunks = [.. first.Plan.DiscardedHunks, .. final.Plan.DiscardedHunks]
                }
            };
        }
        finally { File.Delete(intermediate); }
    }

    private static ModPatch CreateDelta(string ancestorPath, string intermediate, ProfileSelection selection, BuildResult first,
        AnalysisOptions options, Action<string>? progress, CancellationToken cancellationToken)
    {
        void Report(string message) { cancellationToken.ThrowIfCancellationRequested(); progress?.Invoke(message); }
        using var ancestor = GameFileLoader.Load(ancestorPath, Report, cancellationToken, options);
        using var merged = GameFileLoader.Load(intermediate, Report, cancellationToken, options);
        if (ancestor.Inspection.Sha256 != selection.Vanilla[0].Patch.Mod.Sha256
            || merged.Inspection.Sha256 != first.Verification!.Inspection.Sha256)
            throw new IOException("A base archive changed while building. Retry with stable archive copies.");
        return PatchBuilder.Create(ancestor, merged, selection.Vanilla[0].Patch.Mod.Name, Report, options);
    }
}
