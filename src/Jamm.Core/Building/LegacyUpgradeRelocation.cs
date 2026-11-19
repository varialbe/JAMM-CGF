using System.Globalization;
using System.Text;
using System.Text.Json;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>Describes an appended upgrade icon frame relocated inside <c>spr_upgradeicons</c>.</summary>
internal sealed record UpgradeIconRelocation(
    int SourceMod,
    int? KeptMod,
    int SourceSlot,
    int TargetSlot,
    int SourceFrame,
    int TargetFrame,
    bool Fallback);

/// <summary>Relocates colliding distinct legacy upgrade additions before conflict analysis.</summary>
internal static partial class LegacyUpgradeRelocation
{
    private const string CreateEntry = "gml_Object_UpgradeData_Create_0";
    private const string UpgradeIconsSprite = "spr_upgradeicons";
    private sealed record Upgrade(int Slot, string Identity);
    private sealed record RewriteOutcome(ModPatch Patch, int ReferenceCount, int CodeEntryCount);

    internal sealed record Result(
        ModPatch[] Patches,
        IReadOnlyList<string> Decisions,
        IReadOnlyList<UpgradeIconRelocation> IconRelocations,
        IReadOnlyList<SharedUnlockHunk> SharedUnlocks);

    internal static Result Prepare(IReadOnlyList<ModPatch> source)
    {
        ModPatch[] patches = source.ToArray();
        string? ancestor = patches.Select(p => Create(p)?.BaseCode?.Text).FirstOrDefault(t => t is not null);
        if (ancestor is null) return new(patches, [], [], []);

        var baseUpgrades = ReadBaseUpgrades(ancestor);
        if (baseUpgrades.Count == 0) return new(patches, [], [], []);

        var modUpgrades = new Dictionary<int, Upgrade>[patches.Length];
        var claims = new Upgrade[patches.Length][];
        for (int m = 0; m < patches.Length; m++)
        {
            string modText = Create(patches[m])?.ModCode?.Text ?? ancestor;
            modUpgrades[m] = ReadUpgrades(modText);
            claims[m] = modUpgrades[m].Values
                .Where(u => !baseUpgrades.ContainsKey(u.Slot))
                .OrderBy(u => u.Slot)
                .ToArray();
        }

        int maxBase = baseUpgrades.Keys.DefaultIfEmpty(-1).Max();
        int maxClaimed = Math.Max(maxBase, modUpgrades.SelectMany(u => u.Keys).DefaultIfEmpty(-1).Max());
        int next = maxClaimed + 1;
        int highestAllocated = maxClaimed;
        bool anyRelocated = false;

        List<string> decisions = [];
        List<UpgradeIconRelocation> iconRelocations = [];
        List<SharedUnlockHunk> sharedUnlocks = [];
        int maxClaimSlot = claims.SelectMany(c => c).Select(u => u.Slot).DefaultIfEmpty(-1).Max();
        HashSet<int> baseEffectSlots = FindBaseEffectSlots(patches);
        int nextEffectSlot = Math.Max(VanillaMaxEffectSlot, FindMaxEffectSlotAcrossAllMods(patches)) + 1;

        for (int slot = 0; slot <= maxClaimSlot; slot++)
        {
            var contenders = claims
                .SelectMany((items, mod) => items.Where(u => u.Slot == slot).Select(u => (Mod: mod, Upgrade: u)))
                .OrderBy(x => x.Mod)
                .ToArray();
            if (contenders.Select(c => c.Upgrade.Identity).Distinct(StringComparer.Ordinal).Count() < 2)
                continue;

            var (uniqueLinesByMod, sharedHunks) = ComputeUniqueLines(patches, contenders.Select(c => (c.Mod, c.Upgrade.Identity)).ToArray());
            var currentUniqueEffectSlotsByMod = contenders.ToDictionary(
                c => c.Mod,
                c => CollectUniqueEffectSlots(patches[c.Mod], uniqueLinesByMod[c.Mod], baseEffectSlots));
            int keptMod = ChooseKeptMod(patches, contenders, uniqueLinesByMod, slot);
            string keptIdentity = contenders.First(c => c.Mod == keptMod).Upgrade.Identity;

            // Group non-kept contenders by identity so duplicate claims of one non-kept upgrade move together.
            var movedIdentities = contenders
                .Where(c => c.Upgrade.Identity != keptIdentity)
                .GroupBy(c => c.Upgrade.Identity, StringComparer.Ordinal)
                .OrderBy(g => g.Min(x => x.Mod))
                .ToArray();

            foreach (var identityGroup in movedIdentities)
            {
                int target = next;
                bool groupFailed = false;
                List<(int Mod, RewriteOutcome Outcome, UpgradeIconRelocation? Icon, string? IconWarning)> pending = [];

                foreach (var contender in identityGroup.OrderBy(x => x.Mod))
                {
                    if (!TryRewriteMod(patches[contender.Mod], uniqueLinesByMod[contender.Mod], slot, target,
                            out var outcome, out var warning))
                    {
                        decisions.Add($"WARNING: upgrade slot {slot} in '{Name(patches[contender.Mod])}' was not relocated: {warning}");
                        groupFailed = true;
                        break;
                    }

                    var (icon, iconWarning) = PlanIconRelocation(patches, contender.Mod, keptMod, slot, target);
                    pending.Add((contender.Mod, outcome, icon, iconWarning));
                }

                if (groupFailed) continue;

                next++;
                highestAllocated = Math.Max(highestAllocated, target);
                anyRelocated = true;

                foreach (var item in pending)
                {
                    patches[item.Mod] = item.Outcome.Patch;
                    if (item.Icon is not null) iconRelocations.Add(item.Icon);
                    if (item.IconWarning is not null) decisions.Add(item.IconWarning);

                    string iconPhrase = item.Icon is null
                        ? "icon frame n/a"
                        : item.Icon.Fallback
                            ? $"icon frame fallback (duplicated frame 0 at {item.Icon.TargetFrame})"
                            : $"icon frame {item.Icon.SourceFrame}->{item.Icon.TargetFrame}";

                    decisions.Add(
                        $"[CGF-RELOCATE-003] Upgrade \"{identityGroup.Key}\" ({Name(patches[item.Mod])}) moved from slot {slot} to {target}; " +
                        $"rewrote {item.Outcome.ReferenceCount} typed references in {item.Outcome.CodeEntryCount} code entries; {iconPhrase}.");
                }

                RelocateEffectSlotsForGroup(
                    patches,
                    contenders,
                    keptMod,
                    keptIdentity,
                    identityGroup.Key,
                    pending.Select(p => p.Mod).ToArray(),
                    uniqueLinesByMod,
                    baseEffectSlots,
                    currentUniqueEffectSlotsByMod,
                    ref nextEffectSlot,
                    decisions);

                CollectSharedUnlockHunksForGroup(
                    sharedHunks,
                    slot,
                    target,
                    identityGroup.Key,
                    keptMod,
                    pending.Select(p => p.Mod).ToArray(),
                    sharedUnlocks);
            }
        }

        if (anyRelocated)
        {
            for (int m = 0; m < patches.Length; m++)
                patches[m] = NormalizeUpgradeBounds(patches[m], highestAllocated);
        }

        return new(patches, decisions, iconRelocations, sharedUnlocks);
    }

    private static AssetPatch? Create(ModPatch patch) =>
        patch.Changes.GetValueOrDefault("code")?.Added
            .Concat(patch.Changes.GetValueOrDefault("code")?.Changed ?? [])
            .FirstOrDefault(a => a.Name == CreateEntry);

    private static Dictionary<int, Upgrade> ReadBaseUpgrades(string text)
    {
        var upgrades = ReadUpgrades(text);
        if (upgrades.Count > 0) return upgrades;

        // Synthetic test bases may populate vanilla names via a loop: for (i = 0; i <= 17; i++) name[i] = ...
        int? bound = FindCreateLoopMaxIndex(text);
        if (bound is { } max && max >= 0)
        {
            for (int i = 0; i <= max; i++)
                upgrades[i] = new(i, $"__base_upgrade_{i}");
        }
        return upgrades;
    }

    private static int? FindCreateLoopMaxIndex(string text)
    {
        var tokens = GmlReferences.Tokens(LineDiff.Normalize(text));
        int? best = null;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != "for") continue;
            if (!TryMatchCountBoundLoop(tokens, i, out string? loopVar, out int boundTokenIndex, out bool inclusive, out int bodyEnd))
                continue;
            bool touchesUpgradeArray = false;
            for (int j = boundTokenIndex + 1; j <= bodyEnd && j < tokens.Count; j++)
            {
                if (tokens[j].Kind == 'i' && tokens[j].Value is "unlocks" or "name" or "cost" or "desc" or "desc2"
                    && j + 3 < tokens.Count && tokens[j + 1].Value == "[" && tokens[j + 2].Value == loopVar && tokens[j + 3].Value == "]")
                {
                    touchesUpgradeArray = true;
                    break;
                }
            }
            if (!touchesUpgradeArray) continue;
            if (!int.TryParse(tokens[boundTokenIndex].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int bound)) continue;
            int maxIdx = inclusive ? bound : bound - 1;
            if (maxIdx >= 0) best = best is null ? maxIdx : Math.Max(best.Value, maxIdx);
        }
        return best;
    }

    private static Dictionary<int, Upgrade> ReadUpgrades(string text)
    {
        Dictionary<int, Upgrade> upgrades = [];
        var tokens = GmlReferences.Tokens(LineDiff.Normalize(text));
        for (int i = 0; i + 5 < tokens.Count; i++)
        {
            int nameIdx = i;
            if (tokens[i].Kind == 'i' && tokens[i].Value == "UpgradeData"
                && i + 1 < tokens.Count && tokens[i + 1].Value == ".")
            {
                nameIdx = i + 2;
            }
            else if (i > 0 && tokens[i - 1].Value == ".")
            {
                continue;
            }

            if (nameIdx + 5 >= tokens.Count) continue;
            if (tokens[nameIdx].Kind != 'i' || tokens[nameIdx].Value != "name") continue;
            if (tokens[nameIdx + 1].Value != "[" || tokens[nameIdx + 2].Kind != 'n' || tokens[nameIdx + 3].Value != "]"
                || tokens[nameIdx + 4].Value != "=" || tokens[nameIdx + 5].Kind != 's')
                continue;

            if (int.TryParse(tokens[nameIdx + 2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot))
                upgrades[slot] = new(slot, tokens[nameIdx + 5].Value);
        }
        return upgrades;
    }

    private static (Dictionary<int, Dictionary<string, HashSet<int>>> UniqueLines, List<ContenderSharedHunk> SharedHunks) ComputeUniqueLines(
        IReadOnlyList<ModPatch> patches,
        IReadOnlyList<(int Mod, string Identity)> contenders)
    {
        var result = contenders.ToDictionary(c => c.Mod, _ => new Dictionary<string, HashSet<int>>(StringComparer.Ordinal));
        List<ContenderSharedHunk> sharedHunks = [];
        var entriesByMod = contenders.ToDictionary(c => c.Mod, c =>
        {
            var code = patches[c.Mod].Changes.GetValueOrDefault("code");
            return (code?.Added.Concat(code.Changed) ?? []).ToDictionary(a => a.Name, StringComparer.Ordinal);
        });

        string[] allEntries = entriesByMod.Values.SelectMany(d => d.Keys).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string entryName in allEntries)
        {
            var participants = contenders.Where(c => entriesByMod[c.Mod].ContainsKey(entryName)).ToArray();
            if (participants.Length == 0) continue;

            var firstAsset = entriesByMod[participants[0].Mod][entryName];
            if (firstAsset.BaseCode is null)
            {
                // Added code entry: shared if byte-identical to any distinct-identity contender's added entry.
                var addedGml = participants
                    .Select(p => (Participant: p, Gml: entriesByMod[p.Mod][entryName].ModCode))
                    .Where(x => x.Gml is { Format: "gml" })
                    .Select(x => (x.Participant, Norm: LineDiff.Normalize(x.Gml!.Text)))
                    .ToArray();
                foreach (var group in addedGml.GroupBy(x => x.Norm, StringComparer.Ordinal))
                {
                    if (group.Select(x => x.Participant.Identity).Distinct(StringComparer.Ordinal).Count() >= 2)
                        sharedHunks.Add(new(entryName, group.Select(x => x.Participant.Mod).ToHashSet(), IsAdded: true, 0, 0, group.Key));
                }
                foreach (var p in participants)
                {
                    var asset = entriesByMod[p.Mod][entryName];
                    if (asset.ModCode is not { Format: "gml" } gml) continue;
                    string norm = LineDiff.Normalize(gml.Text);
                    bool shared = participants.Any(o => o.Identity != p.Identity
                        && entriesByMod[o.Mod][entryName].ModCode is { Format: "gml" } otherGml
                        && LineDiff.Normalize(otherGml.Text) == norm);
                    if (!shared)
                        result[p.Mod][entryName] = Enumerable.Range(1, CountLines(norm)).ToHashSet();
                }
                continue;
            }

            // Changed code entry: compare all GML contenders against the same ancestor with ThreeWayMerge.
            var gmlParticipants = participants
                .Where(p => entriesByMod[p.Mod][entryName].ModCode?.Format == "gml"
                    && entriesByMod[p.Mod][entryName].BaseCode?.Format == "gml")
                .ToArray();
            if (gmlParticipants.Length == 0) continue;

            string ancestor = LineDiff.Normalize(entriesByMod[gmlParticipants[0].Mod][entryName].BaseCode!.Text);
            var versions = gmlParticipants
                .Select(p => new MergeVersion(p.Mod.ToString(CultureInfo.InvariantCulture),
                    LineDiff.Normalize(entriesByMod[p.Mod][entryName].ModCode!.Text)))
                .ToArray();
            var merge = ThreeWayMerge.Merge(ancestor, versions);

            foreach (var hunk in merge.Hunks)
            {
                foreach (var textGroup in hunk.Versions.GroupBy(v => v.Text, StringComparer.Ordinal))
                {
                    var mods = textGroup.Select(v => int.Parse(v.Name, CultureInfo.InvariantCulture)).ToHashSet();
                    if (mods.Select(m => contenders.First(c => c.Mod == m).Identity).Distinct(StringComparer.Ordinal).Count() >= 2)
                        sharedHunks.Add(new(entryName, mods, IsAdded: false, hunk.Start, hunk.Length, textGroup.Key));
                }
            }

            foreach (var p in gmlParticipants)
            {
                string key = p.Mod.ToString(CultureInfo.InvariantCulture);
                HashSet<int> unique = [];
                int modLine = 1;
                int cursor = 0;
                foreach (var hunk in merge.Hunks)
                {
                    modLine += hunk.Start - cursor;
                    var version = hunk.Versions.FirstOrDefault(v => v.Name == key);
                    if (version is null)
                    {
                        modLine += hunk.Length;
                    }
                    else
                    {
                        int lineCount = CountLines(version.Text);
                        bool shared = hunk.Versions.Any(v => v.Name != key
                            && contenders.First(c => c.Mod.ToString(CultureInfo.InvariantCulture) == v.Name).Identity != p.Identity
                            && string.Equals(v.Text, version.Text, StringComparison.Ordinal));
                        if (!shared)
                        {
                            for (int l = 0; l < lineCount; l++)
                                unique.Add(modLine + l);
                        }
                        modLine += lineCount;
                    }
                    cursor = hunk.Start + hunk.Length;
                }
                result[p.Mod][entryName] = unique;
            }
        }
        return (result, sharedHunks);
    }

    private static int ChooseKeptMod(
        IReadOnlyList<ModPatch> patches,
        (int Mod, Upgrade Upgrade)[] contenders,
        Dictionary<int, Dictionary<string, HashSet<int>>> uniqueLinesByMod,
        int slot)
    {
        // Check which contenders have unique (non-shared) typed references outside UpgradeData_Create_0.
        bool[] hasUniqueExternal = contenders.Select(c => HasUniqueExternalTypedRef(patches[c.Mod], uniqueLinesByMod[c.Mod], slot)).ToArray();
        int uniqueCount = hasUniqueExternal.Count(x => x);
        if (uniqueCount > 0 && uniqueCount < contenders.Length)
        {
            // Keep the lineage origin (the claimant whose external refs are all shared / none unique).
            for (int i = 0; i < contenders.Length; i++)
                if (!hasUniqueExternal[i]) return contenders[i].Mod;
        }
        // If both (or neither) have unique external refs, keep the earliest in load order.
        return contenders[0].Mod;
    }

    private static bool HasUniqueExternalTypedRef(ModPatch patch, Dictionary<string, HashSet<int>> uniqueLines, int slot)
    {
        var code = patch.Changes.GetValueOrDefault("code");
        if (code is null) return false;
        foreach (var asset in code.Added.Concat(code.Changed))
        {
            if (asset.Name == CreateEntry || asset.ModCode is not { Format: "gml" } gml) continue;
            if (!uniqueLines.TryGetValue(asset.Name, out var lines) || lines.Count == 0) continue;
            AnalyzeCode(LineDiff.Normalize(gml.Text), asset.Name, slot, slot, lines,
                out var edits, out bool uncertain, out _, out _);
            if (edits.Count > 0 || uncertain) return true;
        }
        return false;
    }

    private static bool TryRewriteMod(
        ModPatch patch,
        Dictionary<string, HashSet<int>> uniqueLines,
        int oldSlot,
        int newSlot,
        out RewriteOutcome outcome,
        out string warning)
    {
        outcome = new(patch, 0, 0);
        warning = "";
        var code = patch.Changes.GetValueOrDefault("code");
        if (code is null) { warning = "the patch has no code changes to inspect"; return false; }

        List<(AssetPatch Asset, string Text, int Count)> updates = [];
        foreach (var asset in code.Added.Concat(code.Changed))
        {
            if (!uniqueLines.TryGetValue(asset.Name, out var lines) || lines.Count == 0) continue;
            if (asset.ModCode is not { Format: "gml" } gml)
            {
                warning = $"'{asset.Name}' is not available as GML";
                return false;
            }
            string text = LineDiff.Normalize(gml.Text);
            AnalyzeCode(text, asset.Name, oldSlot, newSlot, lines,
                out var edits, out bool uncertain, out int uncertainLine, out string? rewritten);
            if (uncertain)
            {
                warning = $"'{asset.Name}:{uncertainLine}' contains an upgrade-typed use that cannot be classified";
                return false;
            }
            if (edits.Count > 0 && rewritten is not null)
                updates.Add((asset, rewritten, edits.Count));
        }

        if (updates.Count == 0)
        {
            warning = "no typed references to the claimed slot were found";
            return false;
        }

        AssetPatch Update(AssetPatch asset)
        {
            var update = updates.FirstOrDefault(u => ReferenceEquals(u.Asset, asset));
            return update.Asset is null ? asset : asset with { ModCode = asset.ModCode! with { Text = update.Text } };
        }

        var changes = new SortedDictionary<string, AssetChanges>(patch.Changes, StringComparer.Ordinal);
        changes["code"] = code with
        {
            Added = code.Added.Select(Update).ToArray(),
            Changed = code.Changed.Select(Update).ToArray()
        };
        outcome = new(patch with { Changes = changes }, updates.Sum(u => u.Count), updates.Count);
        return true;
    }

    private static void AnalyzeCode(
        string text,
        string entry,
        int oldSlot,
        int newSlot,
        HashSet<int> uniqueLines,
        out Dictionary<(int Start, int Length), string> edits,
        out bool uncertain,
        out int uncertainLine,
        out string? rewritten)
    {
        edits = [];
        uncertain = false;
        uncertainLine = 0;
        rewritten = null;

        string oldLit = oldSlot.ToString(CultureInfo.InvariantCulture);
        string newLit = newSlot.ToString(CultureInfo.InvariantCulture);
        var tokens = GmlReferences.Tokens(text);
        if (tokens.Count == 0) return;

        var matchingBrace = MatchDelimiters(tokens, "{", "}");
        var matchingParen = MatchDelimiters(tokens, "(", ")");
        var matchingBracket = MatchDelimiters(tokens, "[", "]");

        ComputeObjectScopes(tokens, entry, matchingBrace, matchingParen,
            out bool[] inUpgradeData, out bool[] inRod, out bool[] inUpgradeCard);
        bool inCalcCost = entry == "gml_Script_calculate_upgrade_cost";

        var excludedTokens = new HashSet<(int Start, int Length)>();
        var countBoundTokens = new HashSet<(int Start, int Length)>();

        CollectCountBoundTokens(tokens, entry, inUpgradeData, matchingBrace, matchingParen, countBoundTokens);

        // 1. UpgradeData.<field>[N] and local <field>[N] in UpgradeData context (except rod-indexed upg/object/cost_base).
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i') continue;
            bool qualifiedUpgradeData = i >= 2 && tokens[i - 1].Value == "." && tokens[i - 2].Value == "UpgradeData";
            bool localUpgradeData = inUpgradeData[i] && (i == 0 || tokens[i - 1].Value != ".");
            if (!qualifiedUpgradeData && !localUpgradeData) continue;

            string field = tokens[i].Value;
            if (field is "upg" or "object" or "cost_base")
            {
                if (i + 1 < tokens.Count && tokens[i + 1].Value == "[" && matchingBracket.TryGetValue(i + 1, out int closeIdx))
                {
                    for (int k = i + 2; k < closeIdx; k++)
                        if (tokens[k].Kind == 'n' && tokens[k].Value == oldLit)
                            excludedTokens.Add((tokens[k].Start, tokens[k].Length));
                }
                continue;
            }

            if (i + 3 < tokens.Count && tokens[i + 1].Value == "[" && tokens[i + 2].Kind == 'n'
                && tokens[i + 2].Value == oldLit && tokens[i + 3].Value == "]")
            {
                TryAddEdit(edits, tokens[i + 2], newLit, uniqueLines);
            }
        }

        // 2. UpgradeData.upg[a, b] ==/!=/= N (and reversed N ==/!= UpgradeData.upg[a, b]).
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != "upg") continue;
            bool qualified = i >= 2 && tokens[i - 1].Value == "." && tokens[i - 2].Value == "UpgradeData";
            bool local = inUpgradeData[i] && (i == 0 || tokens[i - 1].Value != ".");
            if (!qualified && !local) continue;
            if (i + 1 >= tokens.Count || tokens[i + 1].Value != "[" || !matchingBracket.TryGetValue(i + 1, out int closeIdx))
                continue;

            if (closeIdx + 2 < tokens.Count && tokens[closeIdx + 1].Value is ("==" or "!=" or "=")
                && tokens[closeIdx + 2].Kind == 'n' && tokens[closeIdx + 2].Value == oldLit
                && IsExpressionEnd(tokens, closeIdx + 3))
            {
                TryAddEdit(edits, tokens[closeIdx + 2], newLit, uniqueLines);
            }

            int exprStart = qualified ? i - 2 : i;
            if (exprStart >= 2 && tokens[exprStart - 1].Value is ("==" or "!=")
                && tokens[exprStart - 2].Kind == 'n' && tokens[exprStart - 2].Value == oldLit
                && IsExpressionStart(tokens, exprStart - 3))
            {
                TryAddEdit(edits, tokens[exprStart - 2], newLit, uniqueLines);
            }
        }

        // Rod.upgrades[] is NOT indexed by upgrade id: Rod_Other_10 maps ids to effect slots
        // (vanilla: id 14 -> upgrades[18], id 15 -> upgrades[19], …). Effect slots stay untouched; only the
        // `case N:` label that maps the relocated id to its effect slot is rewritten (section 6).

        // 4. UnlockUpgrade(N) and upgrade_was_purchased(N).
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || (i > 0 && tokens[i - 1].Value == ".")) continue;
            if (tokens[i].Value is not ("UnlockUpgrade" or "upgrade_was_purchased")) continue;
            if (tokens[i + 1].Value == "(" && tokens[i + 2].Kind == 'n' && tokens[i + 2].Value == oldLit && tokens[i + 3].Value == ")")
                TryAddEdit(edits, tokens[i + 2], newLit, uniqueLines);
        }

        // 5. argument0 ==/!= N inside calculate_upgrade_cost, and card_id ==/!= N inside UpgradeCard.
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i') continue;
            bool isUpgradeVar = (inCalcCost && tokens[i].Value == "argument0" && (i == 0 || tokens[i - 1].Value != "."))
                || (tokens[i].Value == "card_id" && (inUpgradeCard[i] && (i == 0 || tokens[i - 1].Value != ".")
                    || (i >= 2 && tokens[i - 1].Value == "." && tokens[i - 2].Value == "UpgradeCard")));
            if (!isUpgradeVar) continue;

            if (i + 2 < tokens.Count && tokens[i + 1].Value is ("==" or "!=")
                && tokens[i + 2].Kind == 'n' && tokens[i + 2].Value == oldLit && IsExpressionEnd(tokens, i + 3))
            {
                TryAddEdit(edits, tokens[i + 2], newLit, uniqueLines);
            }
            if (i >= 2 && tokens[i - 1].Value is ("==" or "!=")
                && tokens[i - 2].Kind == 'n' && tokens[i - 2].Value == oldLit && IsExpressionStart(tokens, i - 3))
            {
                TryAddEdit(edits, tokens[i - 2], newLit, uniqueLines);
            }
        }

        // 6. case N: inside switches whose subject is a proven upgrade-id expression.
        RewriteUpgradeSwitchCases(tokens, matchingBrace, matchingParen, matchingBracket, inUpgradeData, inCalcCost, oldLit, newLit, uniqueLines, edits);

        // 7. Fail closed on unclassified upgrade-typed uses of oldLit inside unique lines.
        var upgradeSourcedVars = CollectVarsAssignedLiteral(tokens, oldLit);
        var varsUsedInUpgradeSink = CollectVarsInUpgradeSinks(tokens, inUpgradeData, inRod, inUpgradeCard, inCalcCost, matchingBracket);
        foreach (string v in upgradeSourcedVars)
        {
            if (varsUsedInUpgradeSink.Contains(v))
            {
                // Find the assignment token line inside uniqueLines.
                for (int i = 0; i + 2 < tokens.Count; i++)
                {
                    if (tokens[i].Kind == 'i' && tokens[i].Value == v && tokens[i + 1].Value == "="
                        && tokens[i + 2].Kind == 'n' && tokens[i + 2].Value == oldLit && uniqueLines.Contains(tokens[i + 2].Line))
                    {
                        uncertain = true;
                        uncertainLine = tokens[i + 2].Line;
                        return;
                    }
                }
            }
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            if (tok.Kind != 'n' || tok.Value != oldLit || !uniqueLines.Contains(tok.Line)) continue;
            var key = (tok.Start, tok.Length);
            if (edits.ContainsKey(key) || excludedTokens.Contains(key) || countBoundTokens.Contains(key)) continue;

            if (LooksLikeUnclassifiedUpgradeUse(tokens, i, entry, inUpgradeData, inUpgradeCard, inCalcCost))
            {
                uncertain = true;
                uncertainLine = tok.Line;
                return;
            }
        }

        if (edits.Count > 0)
        {
            StringBuilder sb = new(text);
            foreach (var edit in edits.OrderByDescending(e => e.Key.Start))
                sb.Remove(edit.Key.Start, edit.Key.Length).Insert(edit.Key.Start, edit.Value);
            rewritten = sb.ToString();
        }
    }

    private static bool LooksLikeUnclassifiedUpgradeUse(
        IReadOnlyList<GmlReferences.Token> tokens,
        int index,
        string entry,
        bool[] inUpgradeData,
        bool[] inUpgradeCard,
        bool inCalcCost)
    {
        // Restrict window to same statement (bounded by ; { } :).
        int from = index;
        while (from > 0 && index - from < 6 && tokens[from - 1].Value is not (";" or "{" or "}" or ":")) from--;
        int to = index;
        while (to + 1 < tokens.Count && to - index < 6 && tokens[to + 1].Value is not (";" or "{" or "}" or ":")) to++;

        // Ignore spawn_order[N] = N in UpgradeMenu_Create_0 (menu ordering, not an upgrade effect/data sink).
        if (entry == "gml_Object_UpgradeMenu_Create_0"
            && Enumerable.Range(from, to - from + 1).Any(j => tokens[j].Kind == 'i' && tokens[j].Value == "spawn_order"))
            return false;

        // Rod.upgrades[N] / local upgrades[N] index an effect slot, not an upgrade id: a classified non-id use.
        if (index >= 2 && index + 1 < tokens.Count && tokens[index - 1].Value == "[" && tokens[index - 2].Value == "upgrades"
            && tokens[index + 1].Value == "]")
            return false;

        // Ignore cost[X] = N literal value assignments in UpgradeData.
        if (inUpgradeData[index] && index >= 2 && tokens[index - 1].Value == "=" && IsExpressionEnd(tokens, index + 1))
        {
            for (int j = from; j < index - 1; j++)
                if (tokens[j].Kind == 'i' && tokens[j].Value is "cost" or "cost_base" && j + 1 < tokens.Count && tokens[j + 1].Value == "[")
                    return false;
        }

        for (int j = from; j <= to; j++)
        {
            var t = tokens[j];
            if (t.Kind == 'i')
            {
                if (t.Value.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
                    || t.Value.Contains("unlocks", StringComparison.OrdinalIgnoreCase)
                    || t.Value == "card_id")
                {
                    return true;
                }
            }
            else if (t.Kind == 's')
            {
                if (t.Value.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
                    || t.Value.Contains("unlocks", StringComparison.OrdinalIgnoreCase)
                    || t.Value.StartsWith("upg", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Inside UpgradeData_* / UpgradeCard_* / calculate_upgrade_cost, a call passing N (e.g. my_helper(18))
        // or dynamic local var = N is suspicious unless it is a standard math/drawing builtin.
        if ((inUpgradeData[index] || inUpgradeCard[index] || inCalcCost)
            && index >= 2 && tokens[index - 1].Value is "(" or ","
            && index + 1 < tokens.Count && tokens[index + 1].Value is ")" or ",")
        {
            int call = index - 1;
            int depth = 0;
            while (call > 0)
            {
                if (tokens[call].Value == ")") depth++;
                else if (tokens[call].Value == "(")
                {
                    if (depth == 0) break;
                    depth--;
                }
                call--;
            }
            if (call >= 1 && tokens[call].Value == "(" && tokens[call - 1].Kind == 'i')
            {
                string fn = tokens[call - 1].Value;
                if (!fn.StartsWith("draw_", StringComparison.Ordinal)
                    && !fn.StartsWith("lengthdir_", StringComparison.Ordinal)
                    && fn is not ("string" or "max" or "min" or "clamp" or "abs" or "floor" or "ceil" or "round" or "power"))
                    return true;
            }
        }

        return false;
    }

    private static void RewriteUpgradeSwitchCases(
        IReadOnlyList<GmlReferences.Token> tokens,
        Dictionary<int, int> matchingBrace,
        Dictionary<int, int> matchingParen,
        Dictionary<int, int> matchingBracket,
        bool[] inUpgradeData,
        bool inCalcCost,
        string oldLit,
        string newLit,
        HashSet<int> uniqueLines,
        Dictionary<(int Start, int Length), string> edits)
    {
        var upgradeSwitchVars = CollectProvenUpgradeVars(tokens, inUpgradeData, inCalcCost, matchingBracket);
        // Track brace scopes: true if that brace opens an upgrade-id switch, false for any other { ... }.
        Dictionary<int, bool> switchBodies = [];
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != "switch") continue;
            int subjStart = i + 1;
            int subjEnd;
            int bodyOpen;
            if (tokens[subjStart].Value == "(" && matchingParen.TryGetValue(subjStart, out int closeParen))
            {
                subjStart++;
                subjEnd = closeParen - 1;
                bodyOpen = closeParen + 1;
            }
            else
            {
                bodyOpen = subjStart;
                while (bodyOpen < tokens.Count && tokens[bodyOpen].Value != "{") bodyOpen++;
                subjEnd = bodyOpen - 1;
            }
            if (bodyOpen >= tokens.Count || tokens[bodyOpen].Value != "{" || !matchingBrace.ContainsKey(bodyOpen)) continue;
            bool isUpgradeSwitch = IsProvenUpgradeSubject(tokens, subjStart, subjEnd, inUpgradeData, inCalcCost, upgradeSwitchVars, matchingBracket);
            switchBodies[bodyOpen] = isUpgradeSwitch;
        }

        Stack<bool> braceStack = new();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Value == "{")
            {
                braceStack.Push(switchBodies.TryGetValue(i, out bool isUpg) && isUpg);
            }
            else if (tokens[i].Value == "}")
            {
                if (braceStack.Count > 0) braceStack.Pop();
            }
            else if (braceStack.Count > 0 && braceStack.Peek()
                && tokens[i].Kind == 'i' && tokens[i].Value == "case"
                && i + 2 < tokens.Count && tokens[i + 1].Kind == 'n' && tokens[i + 1].Value == oldLit
                && tokens[i + 2].Value == ":")
            {
                TryAddEdit(edits, tokens[i + 1], newLit, uniqueLines);
            }
        }
    }

    private static HashSet<string> CollectProvenUpgradeVars(
        IReadOnlyList<GmlReferences.Token> tokens,
        bool[] inUpgradeData,
        bool inCalcCost,
        Dictionary<int, int> matchingBracket)
    {
        HashSet<string> vars = new(StringComparer.Ordinal);
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || (i > 0 && tokens[i - 1].Value == ".") || tokens[i + 1].Value != "=") continue;
            int rhsEnd = i + 2;
            while (rhsEnd < tokens.Count && tokens[rhsEnd].Value is not (";" or "}")) rhsEnd++;
            if (IsProvenUpgradeSubject(tokens, i + 2, rhsEnd - 1, inUpgradeData, inCalcCost, vars, matchingBracket))
                vars.Add(tokens[i].Value);
        }
        return vars;
    }

    private static bool IsProvenUpgradeSubject(
        IReadOnlyList<GmlReferences.Token> tokens,
        int start,
        int end,
        bool[] inUpgradeData,
        bool inCalcCost,
        HashSet<string> provenVars,
        Dictionary<int, int> matchingBracket)
    {
        while (start <= end && tokens[start].Value == "(" && tokens[end].Value == ")") { start++; end--; }
        if (start > end) return false;

        if (start == end && tokens[start].Kind == 'i')
        {
            string name = tokens[start].Value;
            return name == "card_id"
                || (inCalcCost && name == "argument0")
                || provenVars.Contains(name);
        }

        if (end - start == 2 && tokens[start].Kind == 'i' && tokens[start].Value is ("UpgradeCard" or "other")
            && tokens[start + 1].Value == "." && tokens[start + 2].Value == "card_id")
        {
            return true;
        }

        // UpgradeData.upg[...] or local upg[...] in UpgradeData, or spawn_order[...]
        int idx = start;
        if (idx + 2 <= end && tokens[idx].Value == "UpgradeData" && tokens[idx + 1].Value == ".")
            idx += 2;
        if (idx + 1 <= end && tokens[idx].Kind == 'i'
            && (tokens[idx].Value == "spawn_order"
                || (tokens[idx].Value == "upg" && (idx > start || inUpgradeData[idx])))
            && tokens[idx + 1].Value == "["
            && matchingBracket.TryGetValue(idx + 1, out int closeBracket)
            && closeBracket == end)
        {
            return true;
        }

        return false;
    }

    private static HashSet<string> CollectVarsAssignedLiteral(IReadOnlyList<GmlReferences.Token> tokens, string literal)
    {
        HashSet<string> vars = new(StringComparer.Ordinal);
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind == 'i' && (i == 0 || tokens[i - 1].Value != ".")
                && tokens[i + 1].Value == "=" && tokens[i + 2].Kind == 'n' && tokens[i + 2].Value == literal
                && IsExpressionEnd(tokens, i + 3))
            {
                vars.Add(tokens[i].Value);
            }
        }
        return vars;
    }

    private static HashSet<string> CollectVarsInUpgradeSinks(
        IReadOnlyList<GmlReferences.Token> tokens,
        bool[] inUpgradeData,
        bool[] inRod,
        bool[] inUpgradeCard,
        bool inCalcCost,
        Dictionary<int, int> matchingBracket)
    {
        HashSet<string> vars = new(StringComparer.Ordinal);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i') continue;
            // UpgradeData.<field>[v] or local <field>[v]
            if (i + 3 < tokens.Count && tokens[i + 1].Value == "[" && tokens[i + 2].Kind == 'i' && tokens[i + 3].Value == "]")
            {
                bool isUpgradeDataField = (i >= 2 && tokens[i - 1].Value == "." && tokens[i - 2].Value == "UpgradeData"
                        && tokens[i].Value is not ("upg" or "object" or "cost_base"))
                    || (inUpgradeData[i] && (i == 0 || tokens[i - 1].Value != ".")
                        && tokens[i].Value is not ("upg" or "object" or "cost_base"));
                if (isUpgradeDataField)
                    vars.Add(tokens[i + 2].Value);
            }
            // UnlockUpgrade(v) / upgrade_was_purchased(v)
            if (tokens[i].Value is ("UnlockUpgrade" or "upgrade_was_purchased")
                && (i == 0 || tokens[i - 1].Value != ".")
                && i + 3 < tokens.Count && tokens[i + 1].Value == "(" && tokens[i + 2].Kind == 'i' && tokens[i + 3].Value == ")")
            {
                vars.Add(tokens[i + 2].Value);
            }
            // UpgradeData.upg[a, b] ==/!=/= v
            if (tokens[i].Value == "upg" && ((i >= 2 && tokens[i - 1].Value == "." && tokens[i - 2].Value == "UpgradeData")
                || (inUpgradeData[i] && (i == 0 || tokens[i - 1].Value != ".")))
                && i + 1 < tokens.Count && tokens[i + 1].Value == "[" && matchingBracket.TryGetValue(i + 1, out int closeIdx))
            {
                if (closeIdx + 2 < tokens.Count && tokens[closeIdx + 1].Value is ("==" or "!=" or "=")
                    && tokens[closeIdx + 2].Kind == 'i' && IsExpressionEnd(tokens, closeIdx + 3))
                    vars.Add(tokens[closeIdx + 2].Value);
            }
            // card_id == v in UpgradeCard, argument0 == v in calculate_upgrade_cost
            if (((inUpgradeCard[i] && tokens[i].Value == "card_id") || (inCalcCost && tokens[i].Value == "argument0"))
                && (i == 0 || tokens[i - 1].Value != ".")
                && i + 2 < tokens.Count && tokens[i + 1].Value is ("==" or "!=")
                && tokens[i + 2].Kind == 'i' && IsExpressionEnd(tokens, i + 3))
            {
                vars.Add(tokens[i + 2].Value);
            }
        }
        return vars;
    }

    private static (UpgradeIconRelocation? Relocation, string? Warning) PlanIconRelocation(
        IReadOnlyList<ModPatch> patches,
        int sourceMod,
        int keptMod,
        int oldSlot,
        int newSlot)
    {
        int sourceFrame = 1 + oldSlot;
        int targetFrame = 1 + newSlot;
        bool hasSpriteContext = patches.Any(p =>
            p.BaseAssets?.GetValueOrDefault("sprites")?.Contains(UpgradeIconsSprite, StringComparer.Ordinal) == true
            || p.Changes.GetValueOrDefault("sprites")?.Changed.Any(s => s.Name == UpgradeIconsSprite) == true
            || p.Changes.GetValueOrDefault("sprites")?.Added.Any(s => s.Name == UpgradeIconsSprite) == true);
        if (!hasSpriteContext) return (null, null);

        var relocSprite = FindSpritePatch(patches[sourceMod], UpgradeIconsSprite);
        if (relocSprite is null || !TryReadSpriteDimensions(relocSprite.Mod, out int w, out int h, out int frameCount)
            || frameCount <= sourceFrame)
        {
            string warn = $"WARNING: upgrade slot {oldSlot}->{newSlot} in '{Name(patches[sourceMod])}' has no icon frame {sourceFrame} in {UpgradeIconsSprite}; using duplicated frame 0 at {targetFrame}.";
            return (new(sourceMod, keptMod, oldSlot, newSlot, sourceFrame, targetFrame, Fallback: true), warn);
        }

        // Check dimension compatibility against kept/base spr_upgradeicons if available.
        var referenceSnap = FindSpritePatch(patches[keptMod], UpgradeIconsSprite)?.Mod ?? relocSprite.Base;
        if (referenceSnap is { ValueKind: JsonValueKind.Object } refObj
            && TryReadSpriteDimensions(refObj, out int refW, out int refH, out _)
            && (refW != w || refH != h))
        {
            string warn = $"WARNING: {UpgradeIconsSprite} dimensions ({w}x{h}) in '{Name(patches[sourceMod])}' differ from reference ({refW}x{refH}); using duplicated frame 0 at {targetFrame}.";
            return (new(sourceMod, keptMod, oldSlot, newSlot, sourceFrame, targetFrame, Fallback: true), warn);
        }

        return (new(sourceMod, keptMod, oldSlot, newSlot, sourceFrame, targetFrame, Fallback: false), null);
    }

    private static AssetPatch? FindSpritePatch(ModPatch patch, string name) =>
        patch.Changes.GetValueOrDefault("sprites")?.Changed.FirstOrDefault(s => s.Name == name)
        ?? patch.Changes.GetValueOrDefault("sprites")?.Added.FirstOrDefault(s => s.Name == name);

    private static bool TryReadSpriteDimensions(JsonElement snap, out int width, out int height, out int frameCount)
    {
        width = height = frameCount = 0;
        if (snap.ValueKind != JsonValueKind.Object) return false;
        if (!snap.TryGetProperty("width", out var w) || !w.TryGetInt32(out width)) return false;
        if (!snap.TryGetProperty("height", out var h) || !h.TryGetInt32(out height)) return false;
        if (snap.TryGetProperty("frameCount", out var fc) && fc.TryGetInt32(out frameCount)) return true;
        if (snap.TryGetProperty("frames", out var frames) && frames.ValueKind == JsonValueKind.Array)
        {
            frameCount = frames.GetArrayLength();
            return true;
        }
        return false;
    }

    private static ModPatch NormalizeUpgradeBounds(ModPatch patch, int newMax)
    {
        var code = patch.Changes.GetValueOrDefault("code");
        if (code is null) return patch;

        bool anyChanged = false;
        AssetPatch[] RewriteList(IReadOnlyList<AssetPatch> items)
        {
            var list = new AssetPatch[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var asset = items[i];
                if (asset.ModCode is not { Format: "gml" } gml) { list[i] = asset; continue; }
                string text = LineDiff.Normalize(gml.Text);
                HashSet<int>? changedLines = null;
                if (asset.BaseCode is { Format: "gml" } baseGml)
                {
                    changedLines = [];
                    int line = 1;
                    foreach (var edit in LineDiff.Compare(LineDiff.Normalize(baseGml.Text), text))
                    {
                        if (edit.Kind == '+') changedLines.Add(line);
                        if (edit.Kind != '-') line++;
                    }
                    if (changedLines.Count == 0) { list[i] = asset; continue; }
                }
                string updated = RewriteUpgradeCountBounds(text, asset.Name, newMax, changedLines);
                if (updated != text)
                {
                    anyChanged = true;
                    list[i] = asset with { ModCode = gml with { Text = updated } };
                }
                else list[i] = asset;
            }
            return list;
        }

        var added = RewriteList(code.Added);
        var changed = RewriteList(code.Changed);
        if (!anyChanged) return patch;

        var changes = new SortedDictionary<string, AssetChanges>(patch.Changes, StringComparer.Ordinal);
        changes["code"] = code with { Added = added, Changed = changed };
        return patch with { Changes = changes };
    }

    private static string RewriteUpgradeCountBounds(string text, string entry, int newMax, HashSet<int>? changedLines)
    {
        var tokens = GmlReferences.Tokens(text);
        if (tokens.Count == 0) return text;
        var matchingBrace = MatchDelimiters(tokens, "{", "}");
        var matchingParen = MatchDelimiters(tokens, "(", ")");
        ComputeObjectScopes(tokens, entry, matchingBrace, matchingParen, out bool[] inUpgradeData, out _, out _);

        var boundTokens = FindUpgradeCountBoundTokens(tokens, entry, inUpgradeData, matchingBrace, matchingParen);
        List<(int Start, int Length, string Value)> edits = [];
        foreach (var (tokenIndex, inclusive) in boundTokens)
        {
            if (changedLines is not null && !changedLines.Contains(tokens[tokenIndex].Line))
                continue;
            if (!int.TryParse(tokens[tokenIndex].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int bound))
                continue;
            int effectiveMax = inclusive ? bound : bound - 1;
            if (effectiveMax < 17 || effectiveMax >= newMax) continue;
            string replacement = (inclusive ? newMax : newMax + 1).ToString(CultureInfo.InvariantCulture);
            edits.Add((tokens[tokenIndex].Start, tokens[tokenIndex].Length, replacement));
        }
        if (edits.Count == 0) return text;
        StringBuilder sb = new(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            sb.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        return sb.ToString();
    }

    private static void CollectCountBoundTokens(
        IReadOnlyList<GmlReferences.Token> tokens,
        string entry,
        bool[] inUpgradeData,
        Dictionary<int, int> matchingBrace,
        Dictionary<int, int> matchingParen,
        HashSet<(int Start, int Length)> result)
    {
        foreach (var (tokenIndex, _) in FindUpgradeCountBoundTokens(tokens, entry, inUpgradeData, matchingBrace, matchingParen))
            result.Add((tokens[tokenIndex].Start, tokens[tokenIndex].Length));
    }

    private static List<(int TokenIndex, bool Inclusive)> FindUpgradeCountBoundTokens(
        IReadOnlyList<GmlReferences.Token> tokens,
        string entry,
        bool[] inUpgradeData,
        Dictionary<int, int> matchingBrace,
        Dictionary<int, int> matchingParen)
    {
        List<(int TokenIndex, bool Inclusive)> result = [];
        for (int i = 0; i + 4 < tokens.Count; i++)
        {
            if (tokens[i].Kind == 'i' && tokens[i].Value == "for"
                && TryMatchCountBoundLoop(tokens, i, out string? loopVar, out int boundTokenIdx, out bool inclusive, out int bodyEnd))
            {
                bool upgradeLoop = false;
                if (inUpgradeData[i])
                {
                    for (int j = boundTokenIdx + 1; j <= bodyEnd && j < tokens.Count; j++)
                    {
                        if (tokens[j].Kind == 'i' && tokens[j].Value is ("unlocks" or "name" or "cost" or "desc" or "desc2")
                            && j + 3 < tokens.Count && tokens[j + 1].Value == "[" && tokens[j + 2].Value == loopVar && tokens[j + 3].Value == "]")
                        {
                            upgradeLoop = true;
                            break;
                        }
                    }
                }
                else if (entry == "gml_Object_Girl_Other_10")
                {
                    for (int j = boundTokenIdx + 1; j <= bodyEnd && j < tokens.Count; j++)
                        if (tokens[j].Kind == 'i' && tokens[j].Value == "gear_upgrade_scalar") { upgradeLoop = true; break; }
                }
                else if (entry == "gml_Object_Gear_Other_15")
                {
                    for (int j = boundTokenIdx + 1; j <= bodyEnd && j < tokens.Count; j++)
                        if (tokens[j].Kind == 'i' && tokens[j].Value == "upgrade_scalar") { upgradeLoop = true; break; }
                }
                else if (entry.StartsWith("gml_Object_UpgradeMenu_", StringComparison.Ordinal))
                {
                    for (int j = boundTokenIdx + 1; j <= bodyEnd && j < tokens.Count; j++)
                        if (tokens[j].Kind == 'i' && tokens[j].Value == "spawn_order") { upgradeLoop = true; break; }
                }
                if (upgradeLoop) result.Add((boundTokenIdx, inclusive));
            }
            else if (entry == "gml_Object_UpgradeMenu_Alarm_0"
                && tokens[i].Kind == 'i' && tokens[i].Value == "iCard"
                && tokens[i + 1].Value is ("<=" or "<")
                && tokens[i + 2].Kind == 'n')
            {
                result.Add((i + 2, tokens[i + 1].Value == "<="));
            }
        }
        return result;
    }

    private static bool TryMatchCountBoundLoop(
        IReadOnlyList<GmlReferences.Token> tokens,
        int forIndex,
        out string? loopVar,
        out int boundTokenIndex,
        out bool inclusive,
        out int bodyEnd)
    {
        loopVar = null;
        boundTokenIndex = -1;
        inclusive = false;
        bodyEnd = -1;

        if (forIndex + 1 >= tokens.Count || tokens[forIndex + 1].Value != "(") return false;
        int pos = forIndex + 2;
        if (pos < tokens.Count && tokens[pos].Value == "var") pos++;
        if (pos + 8 >= tokens.Count || tokens[pos].Kind != 'i' || tokens[pos + 1].Value != "="
            || tokens[pos + 2].Value != "0" || tokens[pos + 3].Value != ";")
            return false;

        string candidateVar = tokens[pos].Value;
        pos += 4;
        if (pos + 4 >= tokens.Count || tokens[pos].Value != candidateVar
            || tokens[pos + 1].Value is not ("<=" or "<")
            || tokens[pos + 2].Kind != 'n'
            || tokens[pos + 3].Value != ";")
            return false;

        int closeParen = -1;
        int depth = 1;
        for (int j = forIndex + 2; j < tokens.Count; j++)
        {
            if (tokens[j].Value == "(") depth++;
            else if (tokens[j].Value == ")" && --depth == 0) { closeParen = j; break; }
        }
        if (closeParen < 0 || closeParen + 1 >= tokens.Count) return false;

        int end = closeParen + 1;
        if (tokens[end].Value == "{")
        {
            depth = 1;
            for (end++; end < tokens.Count; end++)
            {
                if (tokens[end].Value == "{") depth++;
                else if (tokens[end].Value == "}" && --depth == 0) break;
            }
        }
        else
        {
            while (end < tokens.Count && tokens[end].Value != ";") end++;
        }

        loopVar = candidateVar;
        boundTokenIndex = pos + 2;
        inclusive = tokens[pos + 1].Value == "<=";
        bodyEnd = Math.Min(end, tokens.Count - 1);
        return true;
    }

    /// <summary>
    /// Resolves upgrade-specific disjoint table/case insertions and count-bound conflicts automatically,
    /// returning null when a hunk needs a real user decision.
    /// </summary>
    internal static string? UpgradeUnion(string asset, MergeHunk hunk)
    {
        if (hunk.Status != "CONFLICT" || hunk.Versions.Count < 2) return null;

        if (asset.Contains("gml_Object_UpgradeData_", StringComparison.Ordinal))
        {
            string? slotMerged = LegacySlotRelocation.TryUnionDisjointSlots(hunk);
            if (slotMerged is not null) return slotMerged;
        }

        if (asset.Contains("gml_Object_Rod_", StringComparison.Ordinal)
            && IsRodUpgradeSlotWriteHunk(hunk)
            && LegacySlotRelocation.TryUnionDisjointSlots(hunk) is { } rodSlotMerged)
        {
            return rodSlotMerged;
        }

        if ((asset.Contains("gml_Object_UpgradeCard_Other_10", StringComparison.Ordinal)
                || asset.Contains("gml_Object_Rod_Other_10", StringComparison.Ordinal))
            && hunk.Kind == "list" && hunk.KeepBoth is not null
            && TryUnionDisjointCases(hunk, out string? caseMerged))
        {
            return caseMerged;
        }

        if (IsUpgradeBoundAsset(asset) && TryPickMaxCountBoundVersion(hunk, out string? maxBoundVersion))
            return maxBoundVersion;

        return null;
    }

    private static bool IsUpgradeBoundAsset(string asset) =>
        asset.Contains("gml_Object_UpgradeData_", StringComparison.Ordinal)
        || asset.Contains("gml_Object_UpgradeMenu_Alarm_0", StringComparison.Ordinal)
        || asset.Contains("gml_Object_Girl_Other_10", StringComparison.Ordinal)
        || asset.Contains("gml_Object_Gear_Other_15", StringComparison.Ordinal)
        || asset.Contains("gml_Object_SaveManager_Other_13", StringComparison.Ordinal)
        || asset.Contains("gml_Object_SaveManager_Other_14", StringComparison.Ordinal)
        || asset.Contains("gml_Object_SaveManager_Other_15", StringComparison.Ordinal);

    private static bool TryUnionDisjointCases(MergeHunk hunk, out string? merged)
    {
        merged = null;
        HashSet<int> seenCases = [];
        foreach (var version in hunk.Versions)
        {
            var tokens = GmlReferences.Tokens(version.Text);
            if (tokens.Count < 4 || tokens[0].Kind != 'i' || tokens[0].Value != "case"
                || tokens[1].Kind != 'n' || tokens[2].Value != ":"
                || !int.TryParse(tokens[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                return false;

            // Ensure every top-level case in this hunk ends with break; and case IDs are disjoint.
            int depth = 0;
            bool endsWithBreak = tokens.Count >= 2 && tokens[^2].Value == "break" && tokens[^1].Value == ";";
            if (!endsWithBreak) return false;
            List<int> versionCases = [id];
            for (int i = 3; i + 2 < tokens.Count; i++)
            {
                if (tokens[i].Value == "{") depth++;
                else if (tokens[i].Value == "}") depth--;
                else if (depth == 0 && tokens[i].Kind == 'i' && tokens[i].Value == "case"
                    && tokens[i + 1].Kind == 'n' && tokens[i + 2].Value == ":"
                    && int.TryParse(tokens[i + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int nestedCase))
                {
                    versionCases.Add(nestedCase);
                }
            }
            foreach (int c in versionCases)
                if (!seenCases.Add(c)) return false;
        }
        merged = hunk.KeepBoth;
        return true;
    }

    private static bool TryPickMaxCountBoundVersion(MergeHunk hunk, out string? winnerText)
    {
        winnerText = null;
        var tokenized = hunk.Versions.Select(v => (Version: v, Tokens: GmlReferences.Tokens(v.Text))).ToArray();
        int count = tokenized[0].Tokens.Count;
        if (count == 0 || tokenized.Any(x => x.Tokens.Count != count)) return false;

        int diffIndex = -1;
        for (int i = 0; i < count; i++)
        {
            string val = tokenized[0].Tokens[i].Value;
            char kind = tokenized[0].Tokens[i].Kind;
            if (tokenized.All(x => x.Tokens[i].Value == val && x.Tokens[i].Kind == kind)) continue;
            if (diffIndex >= 0) return false; // More than one token differs.
            diffIndex = i;
        }
        if (diffIndex < 1) return false;

        // The differing token must be the RHS numeric bound of <= or < (for i <= N, i < N, iCard <= N, etc.).
        string op = tokenized[0].Tokens[diffIndex - 1].Value;
        if (op is not ("<=" or "<")) return false;
        if (tokenized.Any(x => x.Tokens[diffIndex].Kind != 'n')) return false;

        int bestIdx = -1;
        int bestBound = int.MinValue;
        for (int v = 0; v < tokenized.Length; v++)
        {
            if (!int.TryParse(tokenized[v].Tokens[diffIndex].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                return false;
            int effective = op == "<=" ? n : n - 1;
            if (effective > bestBound)
            {
                bestBound = effective;
                bestIdx = v;
            }
        }
        if (bestIdx < 0) return false;
        winnerText = tokenized[bestIdx].Version.Text;
        return true;
    }

    private static void ComputeObjectScopes(
        IReadOnlyList<GmlReferences.Token> tokens,
        string entry,
        Dictionary<int, int> matchingBrace,
        Dictionary<int, int> matchingParen,
        out bool[] inUpgradeData,
        out bool[] inRod,
        out bool[] inUpgradeCard)
    {
        bool baseUpgradeData = entry.StartsWith("gml_Object_UpgradeData_", StringComparison.Ordinal);
        bool baseRod = entry.StartsWith("gml_Object_Rod_", StringComparison.Ordinal);
        bool baseUpgradeCard = entry.StartsWith("gml_Object_UpgradeCard_", StringComparison.Ordinal);

        inUpgradeData = Enumerable.Repeat(baseUpgradeData, tokens.Count).ToArray();
        inRod = Enumerable.Repeat(baseRod, tokens.Count).ToArray();
        inUpgradeCard = Enumerable.Repeat(baseUpgradeCard, tokens.Count).ToArray();

        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value != "with" || tokens[i + 1].Value != "(") continue;
            if (!matchingParen.TryGetValue(i + 1, out int closeParen) || closeParen + 1 >= tokens.Count) continue;
            string? target = closeParen == i + 3 && tokens[i + 2].Kind == 'i' ? tokens[i + 2].Value : null;

            int end = closeParen + 1;
            if (tokens[end].Value == "{")
            {
                if (!matchingBrace.TryGetValue(end, out end)) continue;
            }
            else
            {
                while (end < tokens.Count && tokens[end].Value != ";") end++;
            }

            for (int j = closeParen + 1; j <= end && j < tokens.Count; j++)
            {
                inUpgradeData[j] = target == "UpgradeData";
                inRod[j] = target == "Rod";
                inUpgradeCard[j] = target == "UpgradeCard";
            }
        }
    }

    private static Dictionary<int, int> MatchDelimiters(IReadOnlyList<GmlReferences.Token> tokens, string open, string close)
    {
        Dictionary<int, int> map = [];
        Stack<int> stack = new();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Value == open) stack.Push(i);
            else if (tokens[i].Value == close && stack.TryPop(out int start)) map[start] = i;
        }
        return map;
    }

    private static void TryAddEdit(
        Dictionary<(int Start, int Length), string> edits,
        GmlReferences.Token token,
        string replacement,
        HashSet<int> uniqueLines)
    {
        if (uniqueLines.Contains(token.Line))
            edits[(token.Start, token.Length)] = replacement;
    }

    private static bool IsExpressionEnd(IReadOnlyList<GmlReferences.Token> tokens, int nextIndex) =>
        nextIndex >= tokens.Count
        || tokens[nextIndex].Value is (";" or ")" or "," or "}" or "]" or "&&" or "||" or "and" or "or")
        || (nextIndex + 1 < tokens.Count && tokens[nextIndex].Value is ("&" or "|") && tokens[nextIndex + 1].Value == tokens[nextIndex].Value);

    private static bool IsExpressionStart(IReadOnlyList<GmlReferences.Token> tokens, int prevIndex) =>
        prevIndex < 0
        || tokens[prevIndex].Value is ("(" or "{" or ";" or "," or "&&" or "||" or "and" or "or" or "!" or "if" or "else" or "while" or "return")
        || (prevIndex > 0 && tokens[prevIndex].Value is ("&" or "|") && tokens[prevIndex - 1].Value == tokens[prevIndex].Value);

    private static int CountLines(string text)
    {
        if (text.Length == 0) return 0;
        int count = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') count++;
        if (text[^1] != '\n') count++;
        return count;
    }

    private static string Name(ModPatch patch) => patch.Mod.Name ?? Path.GetFileNameWithoutExtension(patch.Mod.FileName);
}
