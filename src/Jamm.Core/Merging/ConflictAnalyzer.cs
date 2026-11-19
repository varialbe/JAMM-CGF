using System.Text.Json;
using Jamm.Core.Diffing;
using Jamm.Core.Patching;

namespace Jamm.Core.Merging;

/// <summary>Checks portable patches in load order, without opening archives or depending on game model types.</summary>
public static class ConflictAnalyzer
{
    private sealed record Change(int Mod, AssetPatch? Asset, bool Added, bool Removed);
    private sealed record Write(int Mod, string Code, IndexWrite Slot);

    /// <summary>Analyzes semantic overlaps and indirect references. All patches must target exactly the same base.</summary>
    public static ConflictReport Analyze(IReadOnlyList<ModPatch> patches, bool allowSingle = false, CancellationToken cancellationToken = default)
    {
        if (patches.Count == 0 || patches.Count < 2 && !allowSingle) throw new ArgumentException("check needs at least two mods or patches.");
        if (patches.Select(p => p.AssetTypesVersion).Distinct().Count() > 1)
            throw new InvalidDataException("Patches use different asset-type definition revisions. Regenerate every patch with diff --force before checking them together.");
        string baseHash = patches[0].Base.Sha256;
        if (patches.Any(p => p.Base.Sha256 != baseHash))
            throw new InvalidDataException("Mods target different bases: " + string.Join("; ", patches.Select(p => $"{Name(p)} targets {p.Base.FileName} (SHA-256 {p.Base.Sha256})")) + ". Recompute every patch against the same --base archive.");
        if (patches.Any(p => p.FormatVersion != PatchJson.FormatVersion))
            throw new InvalidDataException("A patch uses an unsupported format. Recompute it with diff --force.");
        var relocation = Building.LegacySlotRelocation.Prepare(patches);
        patches = relocation.Patches;
        var catalog = Building.CatalogMerge.Prepare(patches);
        patches = catalog.Patches;
        // Labels remain unambiguous even when display names happen to match.
        string[] names = patches.Select((p, i) => patches.Count(q => Name(q) == Name(p)) > 1 ? $"{Name(p)} [{i + 1}]" : Name(p)).ToArray();
        List<ConflictItem> findings = [];
        foreach (string warning in relocation.Decisions.Where(d => d.StartsWith("WARNING:", StringComparison.Ordinal)))
            findings.Add(new("WARNING", "relocation", "code", "boat-slot", names, warning));
        if (catalog.Mapping.Length > 0)
            findings.Add(new("AUTO", "catalog", "code", Building.CatalogMerge.Create,
                names.Distinct(StringComparer.Ordinal).ToArray(),
                "Catalog API retains base fish and allocates distinct slots by object identity; no manual Create winner is needed. See docs/catalog-api.md for legacy save migration."));
        SortedDictionary<string, MergeResult> codeMerges = new(StringComparer.Ordinal);
        List<FieldMerge> fieldMerges = [];
        foreach (string family in patches.SelectMany(p => p.Changes.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assets = new SortedDictionary<string, List<Change>>(StringComparer.Ordinal);
            for (int m = 0; m < patches.Count; m++)
            {
                if (!patches[m].Changes.TryGetValue(family, out var changes)) continue;
                foreach (var asset in changes.Added) Add(asset.Name, new(m, asset, true, false));
                foreach (var asset in changes.Changed) Add(asset.Name, new(m, asset, false, false));
                foreach (string asset in changes.Removed) Add(asset, new(m, null, false, true));
            }
            foreach (var (asset, versions) in assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (versions.Select(v => v.Mod).Distinct().Count() != versions.Count)
                    throw new InvalidDataException($"Ambiguous {family}/{asset}: a patch records more than one change for the same identity. Recompute the patch.");
                if (versions.Any(v => v.Added) && versions.Any(v => !v.Added))
                    throw new InvalidDataException($"Inconsistent ancestor for {family}/{asset}: one patch adds it while another changes/removes it. Recompute against a common base.");
                if (family == "code") Code(asset, versions);
                else if (versions.Count > 1) CompareAssets(family, asset, versions);
                else if (versions[0].Asset is { } unknown && unknown.Mod.GetRawText().Contains("unsupported:", StringComparison.Ordinal))
                    findings.Add(new("WARNING", "binary", family, asset, [names[versions[0].Mod]],
                        "Unsupported content cannot prove equality; inspect or choose a complete asset version.",
                        [new("", unknown.Base, [new(names[versions[0].Mod], unknown.Mod)])]));
                if (family is "objects" or "rooms" or "scripts" or "functions" && versions.All(v => !v.Removed))
                {
                    JsonElement snapshot = versions[0].Asset!.Base ?? versions[0].Asset!.Mod;
                    foreach (var version in versions)
                        snapshot = version.Added ? version.Asset!.Mod : FieldMergeEngine.Apply(snapshot, version.Asset!.Fields, version.Asset.Mod);
                    fieldMerges.Add(new(family, asset, snapshot, findings.Any(f => f.Family == family && f.Asset == asset && f.Severity == "CONFLICT")));
                }
            }
            void Add(string key, Change change)
            {
                if (!assets.TryGetValue(key, out var list)) assets[key] = list = [];
                list.Add(change);
            }
        }
        Indirect();
        foreach (var (patch, i) in patches.Select((p, i) => (p, i)))
            foreach (string warning in patch.Warnings)
                findings.Add(new("WARNING", "patch", "patch", patch.Mod.FileName, [names[i]], warning));
        var ordered = findings.Distinct().OrderBy(f => Rank(f.Severity)).ThenBy(f => f.Family, StringComparer.Ordinal)
            .ThenBy(f => f.Asset, StringComparer.Ordinal).ThenBy(f => f.Kind, StringComparer.Ordinal)
            .ThenBy(f => string.Join("\0", f.Mods), StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal).ToArray();
        return new(baseHash, patches.Select((p, i) => new ModSummary(names[i], p.Mod.FileName, p.Mod.Sha256,
            p.Changes.Values.Sum(c => c.Added.Count), p.Changes.Values.Sum(c => c.Changed.Count), p.Changes.Values.Sum(c => c.Removed.Count), p.Warnings.Count)).ToArray(), ordered, codeMerges, fieldMerges);

        void Code(string asset, List<Change> versions)
        {
            if (versions.Any(v => v.Removed))
            {
                if (versions.Count > 1) Removed("code", asset, versions);
                var surviving = versions.Where(v => !v.Removed).Select(v => v.Asset!).ToArray();
                if (surviving.Length > 0 && surviving.All(a => a.BaseCode?.Format == "gml" && a.ModCode?.Format == "gml"))
                    codeMerges[asset] = ThreeWayMerge.Removal(surviving[0].BaseCode!.Text,
                        versions.Select(v => new MergeVersion(names[v.Mod], v.Removed ? "" : v.Asset!.ModCode!.Text)).ToArray());
                else if (surviving.Length > 0)
                    findings.Add(new("CONFLICT", "disassembly", "code", asset, versions.Select(v => names[v.Mod]).ToArray(),
                        "GML unavailable for the surviving version; select a whole-code version instead of line-merging disassembly."));
                return;
            }
            var entries = versions.Select(v => v.Asset!).ToArray();
            if (entries.Any(e => e.ModCode?.Format != "gml" || (!versions[0].Added && e.BaseCode?.Format != "gml")))
            {
                findings.Add(new("CONFLICT", "disassembly", "code", asset, versions.Select(v => names[v.Mod]).ToArray(),
                    "GML unavailable: disassembly fallback cannot be line-diffed or merged as GML. Re-decompile or select a complete code version."));
                return;
            }
            if (!versions[0].Added && entries.Skip(1).Any(e => LineDiff.Normalize(e.BaseCode!.Text) != LineDiff.Normalize(entries[0].BaseCode!.Text)))
                throw new InvalidDataException($"Patches disagree about ancestor GML for {asset}. Recompute them against the same archive.");
            var texts = versions.Select(v => new MergeVersion(names[v.Mod], v.Asset!.ModCode!.Text)).ToArray();
            MergeResult merge = versions[0].Added ? ThreeWayMerge.Added(texts) : ThreeWayMerge.Merge(entries[0].BaseCode!.Text, texts);
            codeMerges[asset] = merge;
            if (versions.Count < 2) return;
            foreach (var hunk in merge.Hunks.Where(h => h.Versions.Count > 1))
            {
                bool slotUnion = asset.StartsWith("gml_Object_BoatData_", StringComparison.Ordinal)
                    && hunk.Kind == "list" && hunk.Status == "CONFLICT" && hunk.KeepBoth is not null;
                findings.Add(new(slotUnion ? "AUTO" : hunk.Status, hunk.Kind, "code", asset, hunk.Versions.Select(v => v.Name).ToArray(),
                    $"Ancestor line {hunk.Start + 1}: " + (hunk.Status == "INFO" ? "identical code changes." : hunk.Kind == "list"
                    ? "different insertions at the same position (shared list/catalog)." : hunk.Kind == "addition" ? "same-name code additions have different definitions." : "overlapping code edits require a decision."),
                    SuggestedResolution: slotUnion ? "Disjoint boat table slot writes are unioned automatically." : hunk.KeepBoth is null ? null : "keepBoth: keep both insertions in load order"));
            }
            if (merge.Hunks.Any(h => h.Versions.Count == 1))
                findings.Add(new("AUTO", "code", "code", asset, versions.Select(v => names[v.Mod]).ToArray(), "Non-overlapping code hunks are retained automatically in the merge preview."));
            // Counts/parent/children and the semantic snapshot remain independent of text.
            CompareFields("code", asset, versions, metadata: true);
        }

        void CompareAssets(string family, string asset, List<Change> versions)
        {
            if (versions.Any(v => v.Removed)) { Removed(family, asset, versions); return; }
            if (family is "strings" or "variables" or "functions" && versions.All(v => v.Added))
            { findings.Add(new("INFO", "union", family, asset, versions.Select(v => names[v.Mod]).ToArray(), "Definition additions form a union.")); return; }
            if (versions.All(v => v.Added))
            {
                bool equal = versions.All(v => SameSnapshot(family, versions[0].Asset!.Mod, v.Asset!.Mod));
                bool unsupported = versions.Any(v => v.Asset!.Mod.GetRawText().Contains("unsupported:", StringComparison.Ordinal));
                equal &= !unsupported;
                findings.Add(new(equal ? "INFO" : "CONFLICT", "addition", family, asset, versions.Select(v => names[v.Mod]).ToArray(),
                    equal ? "Identical same-name additions are included once." : unsupported ? "Same-name additions contain unsupported content; equality cannot be established." : "Same-name additions have different content; choose which definition to keep.",
                    [new("", null, versions.Select(v => new ModValue(names[v.Mod], v.Asset!.Mod)).ToArray())]));
                return;
            }
            if (family is "objects" or "rooms" or "scripts" or "functions") CompareFields(family, asset, versions);
            else
            {
                bool equal = versions.All(v => SameSnapshot(family, versions[0].Asset!.Mod, v.Asset!.Mod));
                bool unsupported = versions.Any(v => v.Asset!.Mod.GetRawText().Contains("unsupported:", StringComparison.Ordinal));
                findings.Add(new(unsupported ? "CONFLICT" : equal ? "INFO" : "CONFLICT", "binary", family, asset,
                    versions.Select(v => names[v.Mod]).ToArray(), unsupported ? "Unsupported content cannot prove equality; inspect or choose a version."
                    : equal ? "Identical content in every mod." : "Different content; later mod wins by default in Phase 3, subject to your decision.",
                    Differences(versions)));
            }
        }

        IReadOnlyList<FieldConflict> Differences(List<Change> versions) => versions.SelectMany(v => v.Asset!.Fields.Select(f => f.Path))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path => new FieldConflict(path,
                FieldMergeEngine.At(versions[0].Asset!.Base, path), versions.Select(v => new ModValue(names[v.Mod], FieldMergeEngine.At(v.Asset!.Mod, path))).ToArray())).ToArray();

        void CompareFields(string family, string asset, List<Change> versions, bool metadata = false)
        {
            for (int a = 0; a < versions.Count; a++)
                for (int b = a + 1; b < versions.Count; b++)
                {
                    var left = versions[a]; var right = versions[b];
                    List<FieldConflict> collisions = [], identical = [];
                    foreach (var x in left.Asset!.Fields)
                        foreach (var y in right.Asset!.Fields.Where(y => FieldMergeEngine.Overlaps(x.Path, y.Path)))
                        {
                            string path = x.Path.Length <= y.Path.Length ? x.Path : y.Path;
                            var av = FieldMergeEngine.At(left.Asset.Mod, path); var bv = FieldMergeEngine.At(right.Asset.Mod, path);
                            var detail = new FieldConflict(path, FieldMergeEngine.At(left.Asset.Base, path), [new(names[left.Mod], av), new(names[right.Mod], bv)]);
                            (FieldMergeEngine.Equal(av, bv) ? identical : collisions).Add(detail);
                        }
                    string[] mods = [names[left.Mod], names[right.Mod]];
                    if (collisions.Count > 0)
                        findings.Add(new("CONFLICT", "field", family, asset, mods,
                            metadata ? "Code metadata differs; choose compatible counts/parent/children." : "The same field has different values, or a removal overlaps an edit.",
                            collisions.DistinctBy(c => c.Path).ToArray()));
                    if (identical.Count > 0)
                        findings.Add(new("INFO", "field", family, asset, mods, "Identical field changes; duplicate instances/additions are included once.", identical.DistinctBy(c => c.Path).ToArray()));
                    if (!metadata && (left.Asset!.Fields.Any(x => !right.Asset!.Fields.Any(y => FieldMergeEngine.Overlaps(x.Path, y.Path)))
                        || right.Asset!.Fields.Any(y => !left.Asset!.Fields.Any(x => FieldMergeEngine.Overlaps(x.Path, y.Path)))))
                        findings.Add(new("AUTO", "field", family, asset, mods, "Changes to different fields or distinct room instance/tile keys form a union.", Differences([left, right])));
                }
        }

        void Removed(string family, string asset, List<Change> versions)
        {
            bool all = versions.All(v => v.Removed);
            findings.Add(new(all ? "INFO" : "CONFLICT", "removal", family, asset, versions.Select(v => names[v.Mod]).ToArray(),
                all ? "Every participating mod removes this asset." : "One mod removes an asset another changes; choose removal or a surviving version.",
                all ? null : [new("", versions.First(v => !v.Removed).Asset!.Base,
                    versions.Select(v => new ModValue(names[v.Mod], v.Removed ? null : v.Asset!.Mod)).ToArray())]));
        }

        void Indirect()
        {
            var catalog = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var patch in patches)
            {
                foreach (var (family, assets) in patch.BaseAssets ?? [])
                    if (family is "sprites" or "objects" or "sounds" or "scripts" or "rooms")
                        foreach (string asset in assets) Catalog(asset, family);
                foreach (var (family, changes) in patch.Changes.Where(c => c.Key is "sprites" or "objects" or "sounds" or "scripts" or "rooms"))
                    foreach (string asset in changes.Added.Select(a => a.Name).Concat(changes.Removed)) Catalog(asset, family);
            }
            List<AssetReference>[] references = patches.Select(p => (p.References ?? []).Concat(p.Changes.SelectMany(c => c.Value.Added.Concat(c.Value.Changed)
                .SelectMany(a => GmlReferences.Structured(c.Key, a.Name, a.Mod)))).Distinct().ToList()).ToArray();
            List<Write> writes = [];
            for (int m = 0; m < patches.Count; m++)
                foreach (var entry in patches[m].Changes["code"].Added.Concat(patches[m].Changes["code"].Changed))
                {
                    if (entry.ModCode is not { Format: "gml" } code) continue;
                    foreach (string id in GmlReferences.Identifiers(code.Text))
                        if (catalog.TryGetValue(id, out var families))
                            foreach (string family in families) references[m].Add(new("code", entry.Name, family, id));
                    if (entry.BaseCode is { Format: not "gml" }) continue;
                    HashSet<int> changed = [], deleted = []; int line = 1, baseLine = 1;
                    foreach (var edit in LineDiff.Compare(entry.BaseCode?.Text ?? "", code.Text))
                    {
                        if (edit.Kind == '+') changed.Add(line);
                        if (edit.Kind == '-') deleted.Add(baseLine);
                        if (edit.Kind != '-') line++;
                        if (edit.Kind != '+') baseLine++;
                    }
                    var changedSlots = GmlReferences.IndexWrites(entry.BaseCode?.Text ?? "")
                        .Where(slot => Enumerable.Range(slot.Line, slot.EndLine - slot.Line + 1).Any(deleted.Contains))
                        .Select(slot => (slot.Container, slot.Index)).ToHashSet();
                    foreach (var slot in GmlReferences.IndexWrites(code.Text))
                        if (changedSlots.Contains((slot.Container, slot.Index))
                            || Enumerable.Range(slot.Line, slot.EndLine - slot.Line + 1).Any(changed.Contains)) writes.Add(new(m, entry.Name, slot));
                }
            var sourceChanges = patches.SelectMany(p => p.Changes.SelectMany(c =>
                c.Value.Added.Concat(c.Value.Changed).Select(a => (Key: (Family: c.Key, Asset: a.Name), Asset: (AssetPatch?)a, Removed: false))
                .Concat(c.Value.Removed.Select(a => (Key: (Family: c.Key, Asset: a), Asset: (AssetPatch?)null, Removed: true)))))
                .GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.ToArray());
            var previews = fieldMerges.ToDictionary(f => (f.Family, f.Asset));
            HashSet<(string Family, string Asset)> conditional = [];
            var effective = references.SelectMany(r => r).Select(r => (r.Family, r.Asset)).Distinct()
                .ToDictionary(key => key, key => SurvivingReferences(key.Family, key.Asset));
            for (int m = 0; m < references.Length; m++)
                references[m] = references[m].Select(r => (r.Family, r.Asset)).Distinct()
                    .SelectMany(key => effective[key]).Distinct().ToList();
            for (int a = 0; a < patches.Count; a++)
                foreach (var (family, changes) in patches[a].Changes)
                    foreach (string removed in changes.Removed)
                        for (int b = 0; b < patches.Count; b++)
                        {
                            if (a == b) continue;
                            // If B also removes the target there is no cross-mod survivor.
                            if (patches[b].Changes[family].Removed.Contains(removed, StringComparer.Ordinal)) continue;
                            var users = references[b].Where(r => r.TargetFamily == family && r.Target == removed)
                                .Select(r => $"{r.Family}/{r.Asset}").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                            bool dependsOnChoice = references[b].Any(r => r.TargetFamily == family && r.Target == removed && conditional.Contains((r.Family, r.Asset)))
                                || sourceChanges[(family, removed)].Any(c => !c.Removed);
                            if (users.Length > 0) findings.Add(new("CONFLICT", "reference", family, removed, [names[a], names[b]],
                                dependsOnChoice
                                    ? $"{names[a]} removes an asset that may still be used by {names[b]}, depending on unresolved choices ({users.Length} referencing assets)."
                                    : $"{names[a]} removes an asset used by {names[b]} ({users.Length} referencing assets). GML identifier matches require review.", Sources: users));
                        }
            foreach (var group in writes.GroupBy(w => (w.Slot.Container, w.Slot.Index))
                .Where(g => g.Select(w => w.Mod).Distinct().Count() > 1).OrderBy(g => g.Key.Container, StringComparer.Ordinal).ThenBy(g => g.Key.Index, StringComparer.Ordinal))
            {
                string[] sources = group.OrderBy(w => w.Mod).ThenBy(w => w.Code, StringComparer.Ordinal).ThenBy(w => w.Slot.Line)
                    .Select(SourceLabel).Distinct(StringComparer.Ordinal).ToArray();
                bool legacyUpgrade = group.All(w => IsUpgradeSlotWrite(w.Code, w.Slot.Container));
                string[] resolvableAssets = legacyUpgrade ? group.Select(w => w.Code).Distinct(StringComparer.Ordinal)
                    .Where(asset => findings.Any(f => f.Family == "code" && f.Asset == asset && f.Severity == "CONFLICT"
                        && f.Kind is "code" or "list" or "addition" or "disassembly"))
                    .Order(StringComparer.Ordinal).ToArray() : [];
                findings.Add(new("CONFLICT", "index", "code", $"{group.Key.Container}[{group.Key.Index}]", group.Select(w => w.Mod).Distinct().Order().Select(m => names[m]).ToArray(),
                    legacyUpgrade
                        ? $"Legacy upgrade slot collision: {group.Key.Container}[{group.Key.Index}] is written by multiple mods. A slot-level choice is not supported."
                        : $"Literal slot collision: container {group.Key.Container}, index/key {group.Key.Index}.",
                    SuggestedResolution: legacyUpgrade
                        ? "No automatic relocation is safe: legacy slot references may be numeric literals in code and save data, and their complete set cannot be proven from these patches."
                            + (resolvableAssets.Length > 0 ? $" Resolve these code entries per asset: {string.Join(", ", resolvableAssets.Select(a => $"code/{a}"))}." : "")
                        : null,
                    Sources: sources));
            }
            foreach (var group in writes.Where(w => IsUpgradeSlotWrite(w.Code, w.Slot.Container))
                .GroupBy(w => w.Slot.Index, StringComparer.Ordinal)
                .Where(g => g.Select(w => w.Mod).Distinct().Count() > 1)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int[] slotMods = group.Select(w => w.Mod).Distinct().Order().ToArray();
                var nameClaims = slotMods.ToDictionary(mod => mod, mod => AddedUpgradeName(mod, group.Key));
                bool distinctNames = nameClaims.Values.All(name => !string.IsNullOrWhiteSpace(name))
                    && nameClaims.Values.OfType<string>().Distinct(StringComparer.Ordinal).Count() == slotMods.Length;
                string[] sources = group.OrderBy(w => w.Mod).ThenBy(w => w.Code, StringComparer.Ordinal).ThenBy(w => w.Slot.Line)
                    .Select(SourceLabel).Distinct(StringComparer.Ordinal).ToArray();
                string labels = string.Join(" vs ", group.Select(w => w.Mod).Distinct().Order()
                    .Select(mod => nameClaims[mod] is { } name ? $"{name} ({names[mod]})" : names[mod]));
                findings.Add(new("INFO", "upgrade-slot", "code", $"upgrade[{group.Key}]",
                    group.Select(w => w.Mod).Distinct().Order().Select(m => names[m]).ToArray(),
                    $"Legacy mods write upgrade slot {group.Key} in multiple fields ({labels}). No slot renumbering is applied.",
                    Sources: sources));

                if (!distinctNames) continue;
                List<string> groupedSources = [];
                bool ambiguousHunk = false;
                foreach (var assetGroup in group.GroupBy(w => w.Code, StringComparer.Ordinal))
                {
                    if (slotMods.Any(mod => !assetGroup.Any(w => w.Mod == mod))) continue;
                    if (!codeMerges.TryGetValue(assetGroup.Key, out var merge)) continue;
                    foreach (var hunk in merge.Hunks.Where(h => h.Status == "CONFLICT"))
                    {
                        var versionsWithSlotWrite = hunk.Versions.Where(v => HasUpgradeSlotWrite(v.Text, group.Key)).ToArray();
                        if (versionsWithSlotWrite.Length == 0) continue;
                        int[] hunkMods = hunk.Versions.Select(v => Array.IndexOf(names, v.Name)).Where(i => i >= 0).Distinct().Order().ToArray();
                        if (!hunkMods.SequenceEqual(slotMods) || versionsWithSlotWrite.Length != hunk.Versions.Count)
                        {
                            ambiguousHunk = true;
                            break;
                        }
                        groupedSources.Add($"code/{assetGroup.Key}#{hunk.Start}");
                    }
                    if (ambiguousHunk) break;
                }
                if (ambiguousHunk || groupedSources.Count == 0) continue;

                var groupNames = slotMods.Select(mod => names[mod]).ToHashSet(StringComparer.Ordinal);
                string? atlasSource = null;
                var atlasConflict = findings.FirstOrDefault(f => f.Severity == "CONFLICT" && f.Family == "sprites"
                    && f.Asset == "spr_upgradeicons" && f.Kind == "binary"
                    && f.Mods.ToHashSet(StringComparer.Ordinal).SetEquals(groupNames));
                if (atlasConflict is not null && slotMods.All(mod =>
                    patches[mod].BaseAssets?.GetValueOrDefault("sprites")?.Contains("spr_upgradeicons", StringComparer.Ordinal) == true
                    && patches[mod].Changes.GetValueOrDefault("sprites")?.Changed.Any(a => a.Name == "spr_upgradeicons" && a.Base is not null) == true))
                {
                    atlasSource = "sprites/spr_upgradeicons";
                    groupedSources.Add(atlasSource);
                }

                string groupMessage = $"These mods claim added upgrade slot {group.Key} with distinct names ({string.Join(" vs ", slotMods.Select(mod => $"{nameClaims[mod]} ({names[mod]})"))}). Choose one mod for the listed conflicting code hunks";
                if (atlasSource is not null) groupMessage += " and replace the complete spr_upgradeicons sprite asset, including unrelated atlas frames";
                groupMessage += ". Unrelated code hunks remain independently resolvable.";
                findings.Add(new("CONFLICT", "upgrade-group", "code", $"upgrade-slot[{group.Key}]",
                    slotMods.Select(m => names[m]).ToArray(), groupMessage,
                    SuggestedResolution: atlasSource is null
                        ? "The choice applies only to conflicting code hunks with a proven literal write to this added slot."
                        : "The sprite choice replaces the complete spr_upgradeicons asset from the selected mod, including unrelated atlas frames.",
                    Sources: groupedSources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
            }

            string? AddedUpgradeName(int mod, string index)
            {
                var entry = patches[mod].Changes["code"].Added.Concat(patches[mod].Changes["code"].Changed)
                    .FirstOrDefault(a => a.Name == "gml_Object_UpgradeData_Create_0");
                if (entry?.ModCode is not { Format: "gml" } code) return null;
                if (entry.BaseCode is { Format: "gml" } ancestor && GmlReferences.IndexWrites(ancestor.Text)
                    .Any(w => w.Index == index && (w.Container is "name" or "UpgradeData.name"))) return null;
                string?[] namesForSlot = GmlReferences.IndexWrites(code.Text)
                    .Where(w => w.Index == index && (w.Container is "name" or "UpgradeData.name"))
                    .Select(w => LiteralNameAtLine(code.Text, w.Line, index)).Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.Ordinal).ToArray();
                return namesForSlot.Length == 1 ? namesForSlot[0] : null;
            }

            static string? LiteralNameAtLine(string text, int line, string index)
            {
                string[] lines = text.Split('\n');
                if (line < 1 || line > lines.Length) return null;
                var match = System.Text.RegularExpressions.Regex.Match(lines[line - 1],
                    $"^\\s*(?:UpgradeData\\.)?name\\s*\\[\\s*{System.Text.RegularExpressions.Regex.Escape(index)}\\s*\\]\\s*=\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*;?\\s*$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                return match.Success && match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : null;
            }

            static bool HasUpgradeSlotWrite(string text, string index) => GmlReferences.IndexWrites(text)
                .Any(w => w.Index == index && (w.Container is "name" or "UpgradeData.name" or "desc" or "desc2" or "cost" or "unlocks"));

            static bool IsUpgradeSlotWrite(string code, string container) => IsUpgradeDataEvent(code)
                && (container is "name" or "UpgradeData.name" or "desc" or "desc2" or "cost" or "unlocks");

            string SourceLabel(Write w)
            {
                string detail = $"{names[w.Mod]}: {w.Code}:{w.Slot.Line}";
                string? upgradeName = UpgradeName(w);
                return upgradeName is null ? detail : $"{detail} ({upgradeName})";
            }

            string? UpgradeName(Write w)
            {
                if (w.Slot.Container is not ("name" or "UpgradeData.name") || !IsUpgradeDataEvent(w.Code)) return null;
                string text = patches[w.Mod].Changes["code"].Added.Concat(patches[w.Mod].Changes["code"].Changed)
                    .FirstOrDefault(a => a.Name == w.Code)?.ModCode?.Text ?? "";
                return LiteralNameAtLine(text, w.Slot.Line, w.Slot.Index);
            }

            static bool IsUpgradeDataEvent(string code) => code == "gml_Object_UpgradeData_Create_0";

            void Catalog(string asset, string family)
            {
                if (!catalog.TryGetValue(asset, out var families)) catalog[asset] = families = new(StringComparer.Ordinal);
                families.Add(family);
            }

            IReadOnlyList<AssetReference> SurvivingReferences(string family, string asset)
            {
                if (!sourceChanges.TryGetValue((family, asset), out var changes))
                    return references.SelectMany(r => r).Where(r => r.Family == family && r.Asset == asset).Distinct().ToArray();
                if (changes.All(c => c.Removed)) return [];
                bool removed = changes.Any(c => c.Removed);
                if (removed) conditional.Add((family, asset));
                if (previews.TryGetValue((family, asset), out var preview))
                {
                    List<AssetReference> result = [.. GmlReferences.Structured(family, asset, preview.Snapshot)];
                    if (preview.RequiresDecision)
                    {
                        conditional.Add((family, asset));
                        foreach (var field in findings.Where(f => f.Family == family && f.Asset == asset && f.Severity == "CONFLICT")
                            .SelectMany(f => f.Fields ?? []))
                            foreach (var version in field.Versions)
                            {
                                var alternative = FieldMergeEngine.Apply(preview.Snapshot, [new(field.Path, field.Base, version.Value)]);
                                result.AddRange(GmlReferences.Structured(family, asset, alternative));
                            }
                    }
                    return result.Distinct().ToArray();
                }
                if (family == "code")
                {
                    var merge = codeMerges.GetValueOrDefault(asset);
                    if (merge is null || merge.HasConflicts) conditional.Add((family, asset));
                    var texts = merge is { HasConflicts: false } ? new[] { merge.Text }
                        : changes.Where(c => c.Asset?.ModCode?.Format == "gml").Select(c => c.Asset!.ModCode!.Text);
                    return texts.SelectMany(GmlReferences.Identifiers).Distinct(StringComparer.Ordinal)
                        .Where(catalog.ContainsKey).SelectMany(id => catalog[id].Select(target => new AssetReference(family, asset, target, id))).ToArray();
                }
                var snapshots = changes.Where(c => !c.Removed).Select(c => c.Asset!.Mod).ToArray();
                if (snapshots.Skip(1).Any(s => !JsonElement.DeepEquals(snapshots[0], s))) conditional.Add((family, asset));
                return snapshots.SelectMany(s => GmlReferences.Structured(family, asset, s)).Distinct().ToArray();
            }
        }
    }

    private static bool SameSnapshot(string family, JsonElement a, JsonElement b) => family == "texturePages"
        ? FieldMergeEngine.Equal(a.GetProperty("contentHash"), b.GetProperty("contentHash")) : JsonElement.DeepEquals(a, b);
    private static string Name(ModPatch patch) => patch.Mod.Name ?? Path.GetFileNameWithoutExtension(patch.Mod.FileName);
    private static int Rank(string status) => status switch { "CONFLICT" => 0, "AUTO" => 1, "INFO" => 2, _ => 3 };
}
