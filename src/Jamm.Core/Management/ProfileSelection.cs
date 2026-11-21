using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Management;

internal sealed record ProfileSelection(string VanillaPath, string VanillaHash, IReadOnlyList<ProfileMod> Group,
    BuildInput[] Vanilla, BuildInput[] Addons, IReadOnlyList<ConflictItem> Warnings)
{
    internal const string AddonPrefix = "addons/";
    internal IEnumerable<BuildInput> Inputs => Vanilla.Concat(Addons);
    internal string? BaseArchive => Group.Any(row => row.Enabled && row.Role.Role == "base") ? Vanilla[0].ArchivePath : null;

    internal static ProfileSelection Load(ManagerStore store, ModProfile profile, bool strict, Action<string>? progress,
        CancellationToken cancellationToken)
    {
        string vanillaPath = store.Config().Base ?? throw new ArgumentException("Choose a legacy base data.win in Settings first.");
        string vanillaHash = BuildFiles.Hash(vanillaPath);
        var library = new ModLibrary(store);
        new BuiltinMods(store).RefreshStale(profile.EnabledMods, progress, cancellationToken);
        var group = new ProfileLibrary(store).Group(profile).Where(row => row.Enabled).ToArray();
        var bases = group.Where(row => row.Role.Role == "base").Select(row => row.Mod).ToArray();
        if (bases.Length > 1)
            throw new InvalidDataException($"Profile '{profile.Name}' has two or more enabled base mods ({string.Join(", ", bases.Select(m => m.Name))}). Keep one enabled base or change the other roles.");
        var ancestor = bases.SingleOrDefault();
        List<ConflictItem> warnings = [];
        foreach (var row in group)
        {
            if (row.Role.Role == "addon")
            {
                if (ancestor is null || row.Ancestor?.Id != ancestor.Id)
                    throw new InvalidDataException($"Addon '{row.Mod.Name}' requires base '{row.Ancestor?.Name ?? row.Role.Base}', which is missing or disabled in profile '{profile.Name}'. Add and enable that mod with the Base role.");
                if (row.Role.Mismatch(ancestor) is { } mismatch)
                {
                    string message = $"[CGF-BASE-002] {row.Mod.Name} requires a different version or hash of base {ancestor.Name} ({mismatch}). Select the matching base archive.";
                    if (strict) throw new InvalidDataException(message);
                    warnings.Add(new("WARNING", "base", "profile", row.Mod.Name, [row.Mod.Name, ancestor.Name], message + " Continuing with the enabled base."));
                }
            }
            else if (row.Role.Role == "none" && ancestor is not null)
            {
                warnings.Add(new("WARNING", "base", "profile", row.Mod.Name, [row.Mod.Name, ancestor.Name],
                    $"[CGF-BASE-001] {row.Mod.Name} targets vanilla but profile uses base {ancestor.Name}; expect conflicts. Both mods may replace the same vanilla assets."));
            }
        }

        List<BuildInput> vanilla = [], addons = [];
        foreach (var row in group)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mod = row.Mod;
            string archive = library.Archive(mod);
            if (row.Role.Role == "addon")
            {
                addons.Add(new(library.PatchAgainst(mod, library.Archive(ancestor!), ancestor!.Sha256, progress, cancellationToken), archive)
                    { UnlockFix = UnlockFix(profile, mod) });
                continue;
            }
            if (mod.BaseSha256 != vanillaHash)
                throw new InvalidDataException($"'{mod.Name}' was imported against a different vanilla data.win. Select its original archive in Settings or re-add the mod.");
            var patch = mod.Role is { Role: "addon" } || !File.Exists(library.PatchPath(mod))
                ? library.PatchAgainst(mod, vanillaPath, vanillaHash, progress, cancellationToken)
                : library.Patch(mod, progress, cancellationToken);
            vanilla.Add(new(patch, archive) { UnlockFix = UnlockFix(profile, mod) });
        }
        return new(vanillaPath, vanillaHash, group, [.. vanilla], [.. addons], warnings);
    }

    private static bool UnlockFix(ModProfile profile, ManagedMod mod) =>
        profile.UnlockFixMods?.Contains(mod.Id, StringComparer.Ordinal) == true;

    internal ConflictReport Analyze(CancellationToken cancellationToken)
    {
        var first = ConflictAnalyzer.Analyze(Vanilla.Select(i => i.Patch).ToArray(), allowSingle: true, cancellationToken);
        if (Addons.Length == 0) return first with { Findings = [.. Warnings, .. first.Findings] };
        var addons = ConflictAnalyzer.Analyze(Addons.Select(i => i.Patch).ToArray(), allowSingle: true, cancellationToken);
        var code = new SortedDictionary<string, MergeResult>(first.CodeMerges, StringComparer.Ordinal);
        foreach (var (asset, merge) in addons.CodeMerges) code.Add(AddonPrefix + asset, merge);
        var findings = addons.Findings.Select(f => f with { ResolutionPrefix = AddonPrefix });
        var summaries = first.LoadOrder.Concat(addons.LoadOrder).ToDictionary(m => m.Name, StringComparer.Ordinal);
        return first with
        {
            LoadOrder = Group.Select(row => summaries[row.Mod.Name]).ToArray(),
            CodeMerges = code,
            FieldMerges = [.. first.FieldMerges, .. addons.FieldMerges],
            Findings = [.. Warnings, .. first.Findings, .. findings]
        };
    }
}
