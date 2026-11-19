using System.Text;
using System.Text.Json;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>A selected semantic asset, with its verified payload source and optional merged GML.</summary>
public sealed record PlannedAsset(string Family, string Name, int Source, AssetPatch? Patch, JsonElement? Snapshot, string? Gml);

/// <summary>A conflict hunk from a mod that was discarded in favor of another version.</summary>
public sealed record DiscardedHunk(string Mod, string CodeEntry, int Line, string Winner);

/// <summary>Deterministic asset choices and human-readable decisions for a build.</summary>
public sealed record BuildPlan(IReadOnlyList<PlannedAsset> Assets, IReadOnlyList<string> Decisions)
{
    /// <summary>Object identities and deterministic catalog relocations for this build.</summary>
    public IReadOnlyList<CatalogMapping> Catalog { get; init; } = [];

    /// <summary>Code conflict hunks discarded during resolution.</summary>
    public IReadOnlyList<DiscardedHunk> DiscardedHunks { get; init; } = [];

    internal bool HasResolvedHunks { get; init; }
    internal IReadOnlyList<UpgradeIconRelocation> UpgradeIcons { get; init; } = [];

    /// <summary>Combines patches against one exact ancestor, resolving conflicts in load order.</summary>
    public static BuildPlan Create(IReadOnlyList<ModPatch> patches, IReadOnlyDictionary<string, string>? resolutions = null,
        IReadOnlyList<AssetReference>? baseReferences = null)
    {
        if (patches.Count == 0) throw new ArgumentException("build needs at least one mod.");
        resolutions ??= new Dictionary<string, string>();
        if (resolutions.Any(r => string.IsNullOrWhiteSpace(r.Key) || string.IsNullOrWhiteSpace(r.Value)))
            throw new ArgumentException("Resolution keys and winning mod names must be nonempty strings.");
        var relocation = LegacySlotRelocation.Prepare(patches);
        patches = relocation.Patches;
        var catalog = CatalogMerge.Prepare(patches);
        patches = catalog.Patches;
        // Analyze a single patch twice to reuse exactly the Phase 2 ancestor merge, too.
        var report = ConflictAnalyzer.AnalyzePrepared(patches.Count == 1 ? [patches[0], patches[0]] : patches, relocation, catalog.Mapping);
        string[] names = patches.Select(p => p.Mod.Name ?? Path.GetFileNameWithoutExtension(p.Mod.FileName)).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new ArgumentException("Build mod names must be unique. Relabel duplicate names with diff --name.");
        List<string> decisions = [];
        decisions.AddRange(relocation.Decisions);
        if (catalog.Mapping.Length > 0)
            decisions.Add($"Catalog API: retained base entries and registered {catalog.Mapping.Select(m => m.Object).Distinct(StringComparer.Ordinal).Count()} mod fish by object identity; legacy ambiguous progress is retained without guessing.");
        for (int i = 0; i < patches.Count; i++)
            foreach (var family in patches[i].Changes.Where(c => CgfAssetTypes.IndexedFamilies.Contains(c.Key, StringComparer.Ordinal)))
                foreach (string asset in family.Value.Removed)
                    decisions.Add($"INFO: retained {family.Key}/{asset} (removed by {names[i]}) so numeric indices stay valid");
        List<PlannedAsset> assets = [];
        List<DiscardedHunk> discardedHunks = [];
        HashSet<string> used = new(StringComparer.Ordinal);
        var versions = patches.SelectMany((p, i) => p.Changes.SelectMany(c =>
            c.Value.Added.Concat(c.Value.Changed).Select(a => new Version(i, c.Key, a.Name, a))
                .Concat(c.Value.Removed.Select(n => new Version(i, c.Key, n, null)))))
            .GroupBy(v => (v.Family, v.Name)).OrderBy(g => g.Key.Family, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList());
        var familyCounts = versions.Keys.GroupBy(k => k.Name).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Dictionary<(string Family, string Name), string> choices = [];
        Dictionary<string, (string Winner, string GroupKey)> upgradeHunkChoices = new(StringComparer.Ordinal);
        Dictionary<(string Family, string Name), (string Winner, string GroupKey)> upgradeAssetChoices = [];
        foreach (var finding in report.Findings.Where(f => f.Kind == "upgrade-group" && f.Severity == "CONFLICT"))
        {
            string groupKey = $"{finding.Family}/{finding.Asset}";
            if (!resolutions.TryGetValue(groupKey, out string? winner)) continue;
            if (!finding.Mods.Contains(winner, StringComparer.Ordinal))
                throw new ArgumentException($"Resolution for '{groupKey}' selects '{winner}'. Choose one of: {string.Join(", ", finding.Mods)}.");
            used.Add(groupKey);
            choices[(finding.Family, finding.Asset)] = winner;
            foreach (string source in finding.Sources ?? [])
            {
                int slash = source.IndexOf('/');
                if (slash <= 0) continue;
                string family = source[..slash];
                string asset = source[(slash + 1)..];
                if (family == "code" && asset.Contains('#', StringComparison.Ordinal))
                {
                    if (upgradeHunkChoices.TryGetValue(source, out var prior) && prior.Winner != winner)
                        throw new ArgumentException($"Conflicting legacy upgrade group choices affect {source}: '{prior.Winner}' and '{winner}'.");
                    if (resolutions.TryGetValue(source, out string? perHunk) && perHunk != winner)
                        throw new ArgumentException($"Resolution for '{source}' conflicts with legacy upgrade group choice '{winner}'.");
                    upgradeHunkChoices[source] = (winner, groupKey);
                }
                else
                {
                    var key = (Family: family, Name: asset);
                    if (upgradeAssetChoices.TryGetValue(key, out var prior) && prior.Winner != winner)
                        throw new ArgumentException($"Conflicting legacy upgrade group choices affect {source}: '{prior.Winner}' and '{winner}'.");
                    if (resolutions.TryGetValue(source, out string? perAsset) && perAsset != winner)
                        throw new ArgumentException($"Resolution for '{source}' conflicts with legacy upgrade group choice '{winner}'.");
                    upgradeAssetChoices[key] = (winner, groupKey);
                }
            }
        }
        Dictionary<string, string> effectiveResolutions = new(resolutions, StringComparer.Ordinal);
        foreach (var (key, groupChoice) in upgradeHunkChoices)
        {
            string asset = key[..key.LastIndexOf('#')];
            string bare = asset[(asset.IndexOf('/') + 1)..];
            if (resolutions.TryGetValue(asset, out string? entryChoice) && entryChoice != groupChoice.Winner
                || resolutions.TryGetValue(bare, out string? bareChoice) && bareChoice != groupChoice.Winner)
                throw new ArgumentException($"An asset-wide resolution conflicts with legacy upgrade group choice '{groupChoice.Winner}' for '{key}'. Resolve the code hunk and group consistently.");
            effectiveResolutions[key] = groupChoice.Winner;
        }
        foreach (var (key, entries) in versions)
        {
            string qualified = $"{key.Family}/{key.Name}";
            bool hasGroupChoice = upgradeAssetChoices.TryGetValue(key, out var groupChoice);
            if (hasGroupChoice && resolutions.TryGetValue(qualified, out string? explicitQualified) && explicitQualified != groupChoice.Winner
                || hasGroupChoice && resolutions.TryGetValue(key.Name, out string? explicitBare) && explicitBare != groupChoice.Winner)
                throw new ArgumentException($"Resolution for '{qualified}' conflicts with legacy upgrade group choice '{groupChoice.Winner}' in '{groupChoice.GroupKey}'.");
            string? resolutionKey = resolutions.ContainsKey(qualified) ? qualified
                : resolutions.TryGetValue(key.Name, out var bare)
                    && (bare != "keep-both" || key.Family == "code")
                    && (familyCounts[key.Name] == 1
                        || report.Findings.Any(f => f.Severity == "CONFLICT" && f.Family == key.Family && f.Asset == key.Name))
                    ? key.Name : null;
            string? choice = resolutionKey is null ? hasGroupChoice ? groupChoice.Winner : null : resolutions[resolutionKey];
            if (resolutionKey is not null)
            {
                used.Add(resolutionKey);
                choices[key] = choice!;
            }
            else if (hasGroupChoice) choices[key] = groupChoice.Winner;
            var selected = Select(key.Family, key.Name, entries, choice);
            if (selected is not null) assets.Add(selected);
        }

        // Retaining one asset may restore its references to another deleted asset.
        // Re-evaluate until no deletion is overridden, then reject all dangling references.
        Dictionary<(string Family, string Asset), string> referenceChoices = [];
        bool retained;
        do
        {
            retained = false;
            foreach (var removed in assets.Where(a => a.Patch is null).ToArray())
            {
                var references = ReferencesTo(removed);
                int winner = references.Select(r => r.Mod).DefaultIfEmpty(-1).Max();
                bool requiredCode = removed.Family == "code" && references.Any(r => r.Structured);
                if (!requiredCode && (choices.ContainsKey((removed.Family, removed.Name)) || winner <= removed.Source)) continue;
                var surviving = versions[(removed.Family, removed.Name)].Where(v => v.Asset is not null).ToList();
                assets.Remove(removed);
                if (removed.Family == "code") discardedHunks.RemoveAll(h => h.CodeEntry == removed.Name);
                if (surviving.Count > 0) assets.Add(Select(removed.Family, removed.Name, surviving, null)!);
                string reason = requiredCode ? $"surviving {references.First(r => r.Structured).Source}" : names[winner];
                referenceChoices[(removed.Family, removed.Name)] = reason;
                decisions.Add($"reference {removed.Family}/{removed.Name}: {reason} retains the base asset and surviving edits required by a surviving reference.");
                retained = true;
            }
        } while (retained);
        foreach (var removed in assets.Where(a => a.Patch is null))
        {
            var references = ReferencesTo(removed);
            if (references.Count > 0)
                throw new InvalidDataException($"Cannot delete {removed.Family}/{removed.Name} selected by '{patches[removed.Source].Mod.FileName}': surviving {references[0].Source} still references it. Keep the target using --resolve or remove the reference in the referring asset.");
        }

        List<(int Mod, string Source, bool Structured)> ReferencesTo(PlannedAsset removed)
        {
            var selectedAssets = assets.ToDictionary(a => (a.Family, a.Name));
            var sources = patches.SelectMany(p => p.References ?? []).Select(r => (r.Family, r.Asset))
                .Concat((baseReferences ?? []).Select(r => (r.Family, r.Asset)))
                .Concat(versions.Keys.Where(k => k.Family is "code" or "objects" or "rooms" or "scripts" or "timelines")
                    .Select(k => (k.Family, Asset: k.Name))).Distinct();
            List<(int Mod, string Source, bool Structured)> result = [];
            foreach (var source in sources)
            {
                var selected = selectedAssets.GetValueOrDefault((source.Family, source.Asset));
                if (selected is { Patch: null }) continue;
                versions.TryGetValue((source.Family, source.Asset), out var entries);
                bool references;
                if (selected is not null)
                    references = Refers(source.Family, source.Asset, selected.Snapshot, selected.Gml, removed);
                else
                {
                    // An explicit unchanged choice restores the ancestor, not a mod's snapshot.
                    var ancestor = entries?.FirstOrDefault(v => v.Asset?.Base is not null)?.Asset;
                    references = baseReferences is not null && source.Family != "code"
                        ? baseReferences.Any(r => r.Family == source.Family && r.Asset == source.Asset
                            && r.TargetFamily == removed.Family && r.Target == removed.Name)
                        : ancestor is not null
                        ? Refers(source.Family, source.Asset, ancestor.Base, ancestor.BaseCode?.Text, removed)
                        : patches.Where(p => !ChangesSource(p, source.Family, source.Asset))
                            .SelectMany(p => p.References ?? []).Any(r => r.Family == source.Family && r.Asset == source.Asset
                                && r.TargetFamily == removed.Family && r.Target == removed.Name);
                }
                if (!references) continue;
                int winner = -1;
                for (int i = 0; i < patches.Count; i++)
                {
                    if (patches[i].Changes.GetValueOrDefault(removed.Family)?.Removed.Contains(removed.Name, StringComparer.Ordinal) == true) continue;
                    var entry = entries?.FirstOrDefault(v => v.Index == i);
                    if (entry?.Asset is { } asset && Refers(source.Family, source.Asset, asset.Mod, asset.ModCode?.Text, removed)
                        || (patches[i].References ?? []).Any(r => r.Family == source.Family && r.Asset == source.Asset
                            && r.TargetFamily == removed.Family && r.Target == removed.Name)) winner = i;
                }
                result.Add((winner, $"{source.Family}/{source.Asset}", source.Family is "objects" or "rooms" or "scripts" or "timelines"));
            }
            return result;
        }

        static bool ChangesSource(ModPatch patch, string family, string name) => patch.Changes.TryGetValue(family, out var changes)
            && (changes.Added.Concat(changes.Changed).Any(a => a.Name == name) || changes.Removed.Contains(name, StringComparer.Ordinal));

        static bool Refers(string family, string name, JsonElement? snapshot, string? gml, PlannedAsset target) => family == "code"
            ? gml is not null && GmlReferences.Identifiers(gml).Contains(target.Name, StringComparer.Ordinal)
            : snapshot is { } value && GmlReferences.Structured(family, name, value)
                .Any(r => r.TargetFamily == target.Family && r.Target == target.Name);

        PlannedAsset? Select(string family, string name, List<Version> entries, string? choice)
        {
            var ordered = entries.ToList();
            if (choice is not null)
            {
                if (choice != "keep-both" && !ordered.Any(v => names[v.Index] == choice))
                {
                    int baselineIndex = Array.IndexOf(names, choice);
                    if (baselineIndex >= 0 && patches[baselineIndex].BaseAssets?.GetValueOrDefault(family)?.Contains(name, StringComparer.Ordinal) == true)
                    {
                        decisions.Add($"{family}/{name}: {choice} selects the unchanged base asset.");
                        if (family == "code")
                            foreach (var v in ordered.Where(v => v.Asset is not null))
                                discardedHunks.Add(new(names[v.Index], name, 1, choice));
                        return null;
                    }
                    throw new ArgumentException($"Resolution for '{family}/{name}' selects '{choice}', which does not change this asset. Choose {string.Join(", ", ordered.Select(v => names[v.Index]))}.");
                }
                decisions.Add($"{family}/{name}: explicit {choice} resolution.");
                if (choice != "keep-both")
                {
                    var selected = ordered.Single(v => names[v.Index] == choice);
                    ordered.Remove(selected); ordered.Add(selected);
                }
            }
            if (choice == "keep-both" && ordered.Any(v => v.Asset is null))
                throw new ArgumentException($"keep-both cannot resolve deletion of '{family}/{name}'. Choose a mod name.");
            if (family == "sprites" && name == "spr_upgradeicons" && choice is null && relocation.UpgradeIcons.Count > 0)
            {
                var relocatedMods = relocation.UpgradeIcons.Select(i => i.SourceMod).ToHashSet();
                var keptPreferred = ordered.LastOrDefault(v => relocation.UpgradeIcons.Any(i => i.KeptMod == v.Index))
                    ?? ordered.LastOrDefault(v => !relocatedMods.Contains(v.Index));
                if (keptPreferred is not null && !ReferenceEquals(keptPreferred, ordered[^1]))
                {
                    ordered.Remove(keptPreferred);
                    ordered.Add(keptPreferred);
                }
            }
            var last = ordered[^1];
            if (last.Asset is null)
            {
                if (CgfAssetTypes.IndexedFamilies.Contains(family, StringComparer.Ordinal))
                {
                    var surviving = ordered.Where(v => v.Asset is not null).ToList();
                    return surviving.Count == 0 ? null : Select(family, name, surviving, null);
                }
                if (family is "strings" or "variables" or "functions")
                { decisions.Add($"INFO {family}/{name}: retain base definition; bytecode may reference it."); return null; }
                if (family == "code")
                    foreach (var v in ordered.Where(v => v.Asset is not null))
                        discardedHunks.Add(new(names[v.Index], name, 1, names[last.Index]));
                return new(family, name, last.Index, null, null, null);
            }
            // Indexed removals are ignored completely, including their position in edit history.
            // Code and other non-indexed deletions still reset their load-order history.
            int lastRemoval = ordered.FindLastIndex(v => v.Asset is null);
            if (!CgfAssetTypes.IndexedFamilies.Contains(family, StringComparer.Ordinal) && lastRemoval >= 0 && family == "code")
                foreach (var v in ordered.Take(lastRemoval).Where(v => v.Asset is not null))
                    discardedHunks.Add(new(names[v.Index], name, 1, names[ordered[lastRemoval].Index]));
            ordered = CgfAssetTypes.IndexedFamilies.Contains(family, StringComparer.Ordinal)
                ? ordered.Where(v => v.Asset is not null).ToList()
                : ordered.Skip(lastRemoval + 1).ToList();
            JsonElement snapshot = last.Asset.Mod;
            bool wholeAssetGroup = upgradeAssetChoices.ContainsKey((family, name));
            if (!wholeAssetGroup && (family is "objects" or "rooms" or "scripts" or "functions" or "paths" or "timelines" or "fonts" or "sprites")
                && ordered.All(v => v.Asset?.Base is not null))
            {
                snapshot = ordered[0].Asset!.Base!.Value;
                foreach (var v in ordered) snapshot = FieldMergeEngine.Apply(snapshot, v.Asset!.Fields, v.Asset.Mod);
            }
            string? gml = null;
            if (family == "code")
            {
                if (ordered.Any(v => v.Asset?.ModCode is { Format: not "gml" } || v.Asset?.BaseCode is { Format: not "gml" }))
                    throw new InvalidDataException($"Cannot compile code '{name}': a patch contains disassembly. Obtain a successfully decompiled GML patch first.");
                var texts = ordered.OrderBy(v => v.Index).Select(v => new MergeVersion(names[v.Index], v.Asset!.ModCode?.Text
                    ?? throw new InvalidDataException($"Missing GML for '{name}'. Regenerate the patch."))).ToArray();
                MergeResult merge = ordered[0].Asset!.BaseCode is { Format: "gml" } ancestor
                    ? ThreeWayMerge.Merge(ancestor.Text, texts) : ThreeWayMerge.Added(texts);
                gml = ResolveCode(ordered[0].Asset!.BaseCode?.Text ?? "", merge, name == CatalogMerge.Create && choice == "keep-both" ? null : choice, $"code/{name}", effectiveResolutions, used, decisions, discardedHunks);
            }
            else if (choice == "keep-both") throw new ArgumentException($"keep-both is only valid for code insertion conflicts ('{family}/{name}').");
            return new(family, name, last.Index, last.Asset, snapshot, gml);
        }
        foreach (string unknown in resolutions.Keys.Except(used))
            throw new ArgumentException($"Resolution '{unknown}' does not identify a changed asset. Check the asset name in the conflict report.");
        if (patches.Count > 1)
            foreach (var item in report.Findings.Where(f => f.Severity == "CONFLICT"))
            {
                string decision = choices.GetValueOrDefault((item.Family, item.Asset))
                    ?? (item.Kind == "reference" ? referenceChoices.GetValueOrDefault((item.Family, item.Asset)) : null)
                    ?? item.Mods.OrderBy(n => Array.IndexOf(names, n)).Last();
                decisions.Add($"{item.Kind} {item.Family}/{item.Asset}: {decision}" +
                    (item.Kind == "index" ? " (literal slot overlap; runtime execution order still requires an in-game check)" : ""));
            }
        return new(assets, decisions)
        {
            Catalog = catalog.Mapping,
            DiscardedHunks = discardedHunks,
            HasResolvedHunks = true,
            UpgradeIcons = relocation.UpgradeIcons
        };
    }

    private sealed record Version(int Index, string Family, string Name, AssetPatch? Asset);

    private static string ResolveCode(string ancestor, MergeResult merge, string? choice, string asset,
        IReadOnlyDictionary<string, string> resolutions, HashSet<string> used, List<string> decisions,
        List<DiscardedHunk> discardedHunks)
    {
        if (choice == "keep-both" && (!merge.HasConflicts || merge.Hunks.Any(h => h.Status == "CONFLICT" && h.KeepBoth is null && !resolutions.ContainsKey($"{asset}#{h.Start}"))))
            throw new ArgumentException("keep-both requires only list/insertion conflicts in the selected code entry. Choose a mod for overlapping replacements.");
        if (!merge.HasConflicts) return merge.Text;
        string[] split = ancestor.Split('\n');
        string[] lines = ancestor.Length == 0 ? [] : split.Select((s, i) => i < split.Length - 1 ? s + "\n" : s).Where(s => s.Length > 0).ToArray();
        StringBuilder text = new();
        int cursor = 0;
        string entryName = asset.StartsWith("code/", StringComparison.Ordinal) ? asset["code/".Length..] : asset;
        foreach (var hunk in merge.Hunks)
        {
            string key = $"{asset}#{hunk.Start}";
            string? hunkChoice = choice;
            if (resolutions.TryGetValue(key, out string? explicitChoice))
            {
                if (hunk.Status != "CONFLICT" || (explicitChoice == "keep-both" ? hunk.KeepBoth is null : !hunk.Versions.Any(v => v.Name == explicitChoice)))
                    throw new ArgumentException($"Resolution '{key}' cannot use '{explicitChoice}'. Choose a participating mod, or keep-both for list insertions.");
                used.Add(key);
                hunkChoice = explicitChoice;
                decisions.Add($"{key}: explicit {explicitChoice} resolution.");
            }
            text.Append(string.Concat(lines[cursor..hunk.Start]));
            bool catalogUnion = asset.StartsWith("code/gml_Object_CatalogData_", StringComparison.Ordinal)
                && hunk.Status == "CONFLICT" && hunk.KeepBoth is not null && CatalogMerge.DisjointWrites(hunk.Versions.Select(v => v.Text));
            string? slotUnion = LegacySlotRelocation.SlotUnion(asset, hunk);
            if (slotUnion is not null)
            {
                text.Append(slotUnion);
            }
            else if (hunk.Status == "CONFLICT" && (hunkChoice == "keep-both" || catalogUnion))
            {
                text.Append(hunk.KeepBoth);
            }
            else
            {
                var winner = hunk.Versions.LastOrDefault(v => v.Name == hunkChoice) ?? hunk.Versions[^1];
                text.Append(winner.Text);
                if (hunk.Status == "CONFLICT")
                    foreach (var version in hunk.Versions)
                        if (version.Name != winner.Name && !string.Equals(version.Text, winner.Text, StringComparison.Ordinal))
                            discardedHunks.Add(new(version.Name, entryName, hunk.Start + 1, winner.Name));
            }
            cursor = hunk.Start + hunk.Length;
        }
        return text.Append(string.Concat(lines[cursor..])).ToString();
    }
}
