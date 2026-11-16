using System.Text;
using System.Text.Json;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>A selected semantic asset, with its verified payload source and optional merged GML.</summary>
public sealed record PlannedAsset(string Family, string Name, int Source, AssetPatch? Patch, JsonElement? Snapshot, string? Gml);

/// <summary>Deterministic asset choices and human-readable decisions for a build.</summary>
public sealed record BuildPlan(IReadOnlyList<PlannedAsset> Assets, IReadOnlyList<string> Decisions)
{
    /// <summary>Combines patches against one exact ancestor, resolving conflicts in load order.</summary>
    public static BuildPlan Create(IReadOnlyList<ModPatch> patches, IReadOnlyDictionary<string, string>? resolutions = null)
    {
        if (patches.Count == 0) throw new ArgumentException("build needs at least one mod.");
        resolutions ??= new Dictionary<string, string>();
        if (resolutions.Any(r => string.IsNullOrWhiteSpace(r.Key) || string.IsNullOrWhiteSpace(r.Value)))
            throw new ArgumentException("Resolution keys and winning mod names must be nonempty strings.");
        // Analyze a single patch twice to reuse exactly the Phase 2 ancestor merge, too.
        var report = ConflictAnalyzer.Analyze(patches.Count == 1 ? [patches[0], patches[0]] : patches);
        string[] names = patches.Select(p => p.Mod.Name ?? Path.GetFileNameWithoutExtension(p.Mod.FileName)).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new ArgumentException("Build mod names must be unique. Relabel duplicate names with diff --name.");
        List<string> decisions = [];
        List<PlannedAsset> assets = [];
        HashSet<string> used = new(StringComparer.Ordinal);
        var versions = patches.SelectMany((p, i) => p.Changes.SelectMany(c =>
            c.Value.Added.Concat(c.Value.Changed).Select(a => new Version(i, c.Key, a.Name, a))
                .Concat(c.Value.Removed.Select(n => new Version(i, c.Key, n, null)))))
            .GroupBy(v => (v.Family, v.Name)).OrderBy(g => g.Key.Family, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList());
        var familyCounts = versions.Keys.GroupBy(k => k.Name).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        Dictionary<(string Family, string Name), string> choices = [];
        foreach (var (key, entries) in versions)
        {
            string qualified = $"{key.Family}/{key.Name}";
            string? resolutionKey = resolutions.ContainsKey(qualified) ? qualified
                : resolutions.TryGetValue(key.Name, out var bare)
                    && (bare != "keep-both" || key.Family == "code")
                    && (familyCounts[key.Name] == 1
                        || report.Findings.Any(f => f.Severity == "CONFLICT" && f.Family == key.Family && f.Asset == key.Name))
                    ? key.Name : null;
            string? choice = resolutionKey is null ? null : resolutions[resolutionKey];
            if (resolutionKey is not null)
            {
                used.Add(resolutionKey);
                choices[key] = choice!;
            }
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
                if (choices.ContainsKey((removed.Family, removed.Name)) || winner <= removed.Source) continue;
                var surviving = versions[(removed.Family, removed.Name)].Where(v => v.Asset is not null).ToList();
                assets.Remove(removed);
                if (surviving.Count > 0) assets.Add(Select(removed.Family, removed.Name, surviving, null)!);
                referenceChoices[(removed.Family, removed.Name)] = names[winner];
                decisions.Add($"reference {removed.Family}/{removed.Name}: {names[winner]} retains the base asset and surviving edits required by a surviving reference.");
                retained = true;
            }
        } while (retained);
        foreach (var removed in assets.Where(a => a.Patch is null))
        {
            var references = ReferencesTo(removed);
            if (references.Count > 0)
                throw new InvalidDataException($"Cannot delete {removed.Family}/{removed.Name} selected by '{patches[removed.Source].Mod.FileName}': surviving {references[0].Source} still references it. Keep the target using --resolve or remove the reference in the referring asset.");
        }

        List<(int Mod, string Source)> ReferencesTo(PlannedAsset removed)
        {
            var selectedAssets = assets.ToDictionary(a => (a.Family, a.Name));
            var sources = patches.SelectMany(p => p.References ?? []).Select(r => (r.Family, r.Asset))
                .Concat(versions.Keys.Where(k => k.Family is "code" or "objects" or "rooms" or "scripts" or "timelines")
                    .Select(k => (k.Family, Asset: k.Name))).Distinct();
            List<(int Mod, string Source)> result = [];
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
                    references = ancestor is not null
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
                result.Add((winner, $"{source.Family}/{source.Asset}"));
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
            var last = ordered[^1];
            if (last.Asset is null)
            {
                if (family is "strings" or "variables" or "functions")
                { decisions.Add($"INFO {family}/{name}: retain base definition; bytecode may reference it."); return null; }
                return new(family, name, last.Index, null, null, null);
            }
            // A deletion resets this asset's load-order history. Later edits still merge.
            ordered = ordered.Skip(ordered.FindLastIndex(v => v.Asset is null) + 1).ToList();
            JsonElement snapshot = last.Asset.Mod;
            if (family is "objects" or "rooms" or "scripts" or "functions" or "paths" or "timelines" or "fonts"
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
                var texts = ordered.Select(v => new MergeVersion(names[v.Index], v.Asset!.ModCode?.Text
                    ?? throw new InvalidDataException($"Missing GML for '{name}'. Regenerate the patch."))).ToArray();
                MergeResult merge = ordered[0].Asset!.BaseCode is { Format: "gml" } ancestor
                    ? ThreeWayMerge.Merge(ancestor.Text, texts) : ThreeWayMerge.Added(texts);
                gml = ResolveCode(ordered[0].Asset!.BaseCode?.Text ?? "", merge, choice);
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
        return new(assets, decisions);
    }

    private sealed record Version(int Index, string Family, string Name, AssetPatch? Asset);

    private static string ResolveCode(string ancestor, MergeResult merge, string? choice)
    {
        if (choice == "keep-both" && (!merge.HasConflicts || merge.Hunks.Any(h => h.Status == "CONFLICT" && h.KeepBoth is null)))
            throw new ArgumentException("keep-both requires only list/insertion conflicts in the selected code entry. Choose a mod for overlapping replacements.");
        if (!merge.HasConflicts) return merge.Text;
        string[] split = ancestor.Split('\n');
        string[] lines = ancestor.Length == 0 ? [] : split.Select((s, i) => i < split.Length - 1 ? s + "\n" : s).Where(s => s.Length > 0).ToArray();
        StringBuilder text = new();
        int cursor = 0;
        foreach (var hunk in merge.Hunks)
        {
            text.Append(string.Concat(lines[cursor..hunk.Start]));
            text.Append(hunk.Status == "CONFLICT" && choice == "keep-both" ? hunk.KeepBoth
                : hunk.Versions.LastOrDefault(v => v.Name == choice)?.Text ?? hunk.Versions[^1].Text);
            cursor = hunk.Start + hunk.Length;
        }
        return text.Append(string.Concat(lines[cursor..])).ToString();
    }
}
