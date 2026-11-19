using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

internal static partial class LegacyUpgradeRelocation
{
    private const int VanillaMaxEffectSlot = 21;

    private sealed record EffectSlotRef(
        int Slot,
        GmlReferences.Token Token,
        bool IsGuardPlusOne,
        bool IsInitZero);

    private static HashSet<int> FindBaseEffectSlots(IReadOnlyList<ModPatch> patches)
    {
        HashSet<int> slots = [];
        foreach (var patch in patches)
        {
            var code = patch.Changes.GetValueOrDefault("code");
            if (code is null) continue;
            foreach (var asset in code.Changed)
            {
                if (asset.BaseCode is not { Format: "gml" } baseGml) continue;
                string text = LineDiff.Normalize(baseGml.Text);
                foreach (var r in CollectEffectSlotRefs(text, asset.Name))
                    slots.Add(r.Slot);
            }
        }
        return slots;
    }

    private static int FindMaxEffectSlotAcrossAllMods(IReadOnlyList<ModPatch> patches)
    {
        int max = VanillaMaxEffectSlot;
        foreach (var patch in patches)
        {
            var code = patch.Changes.GetValueOrDefault("code");
            if (code is null) continue;
            foreach (var asset in code.Added.Concat(code.Changed))
            {
                if (asset.BaseCode is { Format: "gml" } baseGml)
                {
                    foreach (var r in CollectEffectSlotRefs(LineDiff.Normalize(baseGml.Text), asset.Name))
                        max = Math.Max(max, r.Slot);
                }
                if (asset.ModCode is { Format: "gml" } modGml)
                {
                    foreach (var r in CollectEffectSlotRefs(LineDiff.Normalize(modGml.Text), asset.Name))
                        max = Math.Max(max, r.Slot);
                }
            }
        }
        return max;
    }

    private static void RelocateEffectSlotsForGroup(
        ModPatch[] patches,
        (int Mod, Upgrade Upgrade)[] contenders,
        int keptMod,
        string keptIdentity,
        string movedIdentity,
        int[] movedMods,
        Dictionary<int, Dictionary<string, HashSet<int>>> uniqueLinesByMod,
        HashSet<int> baseEffectSlots,
        Dictionary<int, HashSet<int>> currentUniqueSlotsByMod,
        ref int nextEffectSlot,
        List<string> decisions)
    {
        if (movedMods.Length == 0) return;

        // Gather all candidate effect slots K > 21 used in the moved group's unique hunks.
        SortedSet<int> candidateSlots = [];
        foreach (int m in movedMods)
        {
            if (currentUniqueSlotsByMod.TryGetValue(m, out var modSlots))
                foreach (int s in modSlots)
                    candidateSlots.Add(s);
        }
        if (candidateSlots.Count == 0) return;

        // Order other contenders so keptMod is checked first, then earlier contenders in load order.
        var otherContenders = contenders
            .Where(c => !string.Equals(c.Upgrade.Identity, movedIdentity, StringComparison.Ordinal))
            .OrderBy(c => c.Mod == keptMod ? 0 : 1)
            .ThenBy(c => c.Mod)
            .ToArray();

        foreach (int oldEffectSlot in candidateSlots)
        {
            var collidingOther = otherContenders.FirstOrDefault(c =>
                currentUniqueSlotsByMod.TryGetValue(c.Mod, out var otherSlots) && otherSlots.Contains(oldEffectSlot));
            if (collidingOther.Upgrade is null) continue;

            int targetEffectSlot = nextEffectSlot;
            bool failed = false;
            List<(int Mod, ModPatch Patch, int ReferenceCount)> pending = [];

            foreach (int m in movedMods)
            {
                if (!TryRewriteEffectSlot(patches[m], uniqueLinesByMod[m], oldEffectSlot, targetEffectSlot,
                        out var updatedPatch, out int refCount, out string? warning))
                {
                    decisions.Add($"WARNING: effect slot Rod.upgrades[{oldEffectSlot}] in '{Name(patches[m])}' was not moved: {warning}");
                    failed = true;
                    break;
                }
                pending.Add((m, updatedPatch, refCount));
            }

            if (failed) continue;

            nextEffectSlot++;
            foreach (var item in pending)
            {
                patches[item.Mod] = item.Patch;
                if (currentUniqueSlotsByMod.TryGetValue(item.Mod, out var set))
                {
                    set.Remove(oldEffectSlot);
                    set.Add(targetEffectSlot);
                }

                string otherName = collidingOther.Mod == keptMod ? keptIdentity : collidingOther.Upgrade.Identity;
                decisions.Add(
                    $"[CGF-RELOCATE-004] Effect slot Rod.upgrades[{oldEffectSlot}] of \"{movedIdentity}\" ({Name(patches[item.Mod])}) " +
                    $"moved to {targetEffectSlot} (also used by \"{otherName}\" ({Name(patches[collidingOther.Mod])})); " +
                    $"rewrote {item.ReferenceCount} references.");
            }
        }
    }

    private static HashSet<int> CollectUniqueEffectSlots(
        ModPatch patch,
        Dictionary<string, HashSet<int>> uniqueLines,
        HashSet<int> baseEffectSlots)
    {
        HashSet<int> result = [];
        var code = patch.Changes.GetValueOrDefault("code");
        if (code is null) return result;

        foreach (var asset in code.Added.Concat(code.Changed))
        {
            if (!uniqueLines.TryGetValue(asset.Name, out var lines) || lines.Count == 0) continue;
            if (asset.ModCode is not { Format: "gml" } gml) continue;
            string text = LineDiff.Normalize(gml.Text);
            foreach (var r in CollectEffectSlotRefs(text, asset.Name))
            {
                if (r.Slot > VanillaMaxEffectSlot && !baseEffectSlots.Contains(r.Slot) && lines.Contains(r.Token.Line))
                    result.Add(r.Slot);
            }
        }
        return result;
    }

    private static bool TryRewriteEffectSlot(
        ModPatch patch,
        Dictionary<string, HashSet<int>> uniqueLines,
        int oldSlot,
        int newSlot,
        out ModPatch updatedPatch,
        out int refCount,
        out string? warning)
    {
        updatedPatch = patch;
        refCount = 0;
        warning = null;

        var code = patch.Changes.GetValueOrDefault("code");
        if (code is null) { warning = "the patch has no code changes to inspect"; return false; }

        List<(AssetPatch Asset, string Text, int Count)> updates = [];
        foreach (var asset in code.Added.Concat(code.Changed))
        {
            bool hasUniqueLines = uniqueLines.TryGetValue(asset.Name, out var lines) && lines.Count > 0;
            bool isRodOther10 = asset.Name == "gml_Object_Rod_Other_10";
            if (!hasUniqueLines && !isRodOther10) continue;
            if (asset.ModCode is not { Format: "gml" } gml)
            {
                if (!hasUniqueLines) continue;
                warning = $"'{asset.Name}' is not available as GML";
                return false;
            }

            string text = LineDiff.Normalize(gml.Text);
            lines ??= [];
            AnalyzeEffectSlotCode(text, asset.Name, oldSlot, newSlot, lines,
                out string? rewritten, out int count, out bool uncertain, out int uncertainLine);
            if (uncertain)
            {
                warning = $"'{asset.Name}:{uncertainLine}' contains a dynamic index near effect slot {oldSlot}";
                return false;
            }
            if (rewritten is not null)
                updates.Add((asset, rewritten, count));
        }

        if (updates.Count == 0) return true;

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
        updatedPatch = patch with { Changes = changes };
        refCount = updates.Sum(u => u.Count);
        return true;
    }

    private static void AnalyzeEffectSlotCode(
        string text,
        string entry,
        int oldSlot,
        int newSlot,
        HashSet<int> uniqueLines,
        out string? rewritten,
        out int refCount,
        out bool uncertain,
        out int uncertainLine)
    {
        rewritten = null;
        refCount = 0;
        uncertain = false;
        uncertainLine = 0;

        var tokens = GmlReferences.Tokens(text);
        if (tokens.Count == 0) return;

        var matchingBrace = MatchDelimiters(tokens, "{", "}");
        var matchingParen = MatchDelimiters(tokens, "(", ")");
        var matchingBracket = MatchDelimiters(tokens, "[", "]");
        ComputeObjectScopes(tokens, entry, matchingBrace, matchingParen, out _, out bool[] inRod, out _);

        var refs = CollectEffectSlotRefs(tokens, entry, inRod);
        Dictionary<(int Start, int Length), string> edits = [];
        HashSet<(int Start, int Length)> classifiedTokens = [];
        bool rewrittenInitInUnique = false;
        List<(int InsertPos, string LineText)> initInsertions = [];

        string newLit = newSlot.ToString(CultureInfo.InvariantCulture);
        string newPlusOneLit = (newSlot + 1).ToString(CultureInfo.InvariantCulture);

        foreach (var r in refs)
        {
            if (r.Slot != oldSlot) continue;
            var key = (r.Token.Start, r.Token.Length);
            classifiedTokens.Add(key);
            if (uniqueLines.Contains(r.Token.Line))
            {
                edits[key] = r.IsGuardPlusOne ? newPlusOneLit : newLit;
                if (r.IsInitZero) rewrittenInitInUnique = true;
            }
        }

        // Fail closed on dynamic indices near oldSlot in unique lines.
        if (HasDynamicEffectIndexNear(tokens, inRod, matchingBracket, oldSlot, uniqueLines, classifiedTokens, out uncertainLine))
        {
            uncertain = true;
            return;
        }

        // If Rod_Other_10 initializes oldSlot next to the vanilla init block inside a shared hunk,
        // keep the shared line untouched and append an init line for newSlot right after it.
        if (entry == "gml_Object_Rod_Other_10" && !rewrittenInitInUnique)
        {
            var sharedInit = refs.FirstOrDefault(r => r.Slot == oldSlot && r.IsInitZero && !uniqueLines.Contains(r.Token.Line));
            if (sharedInit is not null)
            {
                int pos = FindLineEnd(text, sharedInit.Token.Start);
                string indent = ExtractLineIndent(text, sharedInit.Token.Start);
                bool qualified = IsQualifiedRodUpgradesAtToken(tokens, sharedInit.Token);
                string prefix = qualified ? "Rod." : "";
                initInsertions.Add((pos, $"\n{indent}{prefix}upgrades[{newLit}] = 0;"));
            }
        }

        if (edits.Count == 0 && initInsertions.Count == 0) return;

        refCount = edits.Count;
        List<(int Start, int Length, string Replacement)> allEdits = [
            .. edits.Select(e => (e.Key.Start, e.Key.Length, e.Value)),
            .. initInsertions.Select(ins => (ins.InsertPos, 0, ins.LineText))
        ];

        StringBuilder sb = new(text);
        foreach (var edit in allEdits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
            sb.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        rewritten = sb.ToString();
    }

    private static List<EffectSlotRef> CollectEffectSlotRefs(string text, string entry)
    {
        var tokens = GmlReferences.Tokens(text);
        if (tokens.Count == 0) return [];
        var matchingBrace = MatchDelimiters(tokens, "{", "}");
        var matchingParen = MatchDelimiters(tokens, "(", ")");
        ComputeObjectScopes(tokens, entry, matchingBrace, matchingParen, out _, out bool[] inRod, out _);
        return CollectEffectSlotRefs(tokens, entry, inRod);
    }

    private static List<EffectSlotRef> CollectEffectSlotRefs(
        IReadOnlyList<GmlReferences.Token> tokens,
        string entry,
        bool[] inRod)
    {
        List<EffectSlotRef> refs = [];
        int braceDepth = 0;
        bool isRodOther10 = entry == "gml_Object_Rod_Other_10";

        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Value == "{") { braceDepth++; continue; }
            if (tokens[i].Value == "}") { if (braceDepth > 0) braceDepth--; continue; }

            // 1. Rod.upgrades[N] or local upgrades[N] in Rod scope
            if (TryMatchUpgradesArrayAccess(tokens, i, inRod, out int bracketOpen))
            {
                if (bracketOpen + 2 < tokens.Count
                    && tokens[bracketOpen + 1].Kind == 'n'
                    && tokens[bracketOpen + 2].Value == "]"
                    && int.TryParse(tokens[bracketOpen + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int slot))
                {
                    bool isInitZero = isRodOther10 && braceDepth == 0
                        && bracketOpen + 4 < tokens.Count
                        && tokens[bracketOpen + 3].Value == "="
                        && tokens[bracketOpen + 4].Kind == 'n'
                        && tokens[bracketOpen + 4].Value == "0"
                        && IsExpressionEnd(tokens, bracketOpen + 5);
                    refs.Add(new(slot, tokens[bracketOpen + 1], IsGuardPlusOne: false, isInitZero));
                }
            }

            // 2. array_length_1d(Rod.upgrades) > K / >= K+1 (and reversed K < / K+1 <=)
            if (TryMatchArrayLengthUpgrades(tokens, i, inRod, out int callEnd))
            {
                if (callEnd + 2 < tokens.Count && tokens[callEnd + 1].Value is (">" or ">=")
                    && tokens[callEnd + 2].Kind == 'n'
                    && IsExpressionEnd(tokens, callEnd + 3)
                    && int.TryParse(tokens[callEnd + 2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int rhsVal))
                {
                    bool plusOne = tokens[callEnd + 1].Value == ">=";
                    int slot = plusOne ? rhsVal - 1 : rhsVal;
                    if (slot >= 0) refs.Add(new(slot, tokens[callEnd + 2], IsGuardPlusOne: plusOne, IsInitZero: false));
                }

                if (i >= 2 && tokens[i - 1].Value is ("<" or "<=")
                    && tokens[i - 2].Kind == 'n'
                    && IsExpressionStart(tokens, i - 3)
                    && int.TryParse(tokens[i - 2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int lhsVal))
                {
                    bool plusOne = tokens[i - 1].Value == "<=";
                    int slot = plusOne ? lhsVal - 1 : lhsVal;
                    if (slot >= 0) refs.Add(new(slot, tokens[i - 2], IsGuardPlusOne: plusOne, IsInitZero: false));
                }
            }
        }
        return refs;
    }

    private static bool HasDynamicEffectIndexNear(
        IReadOnlyList<GmlReferences.Token> tokens,
        bool[] inRod,
        Dictionary<int, int> matchingBracket,
        int oldSlot,
        HashSet<int> uniqueLines,
        HashSet<(int Start, int Length)> classifiedTokens,
        out int uncertainLine)
    {
        uncertainLine = 0;
        string oldLit = oldSlot.ToString(CultureInfo.InvariantCulture);
        string oldPlusOneLit = (oldSlot + 1).ToString(CultureInfo.InvariantCulture);

        var varsAssignedK = CollectVarsAssignedLiteral(tokens, oldLit);
        var varsAssignedKPlusOne = CollectVarsAssignedLiteral(tokens, oldPlusOneLit);

        for (int i = 0; i < tokens.Count; i++)
        {
            // Check dynamic upgrades[...] accesses.
            if (TryMatchUpgradesArrayAccess(tokens, i, inRod, out int bracketOpen)
                && matchingBracket.TryGetValue(bracketOpen, out int bracketClose))
            {
                bool isSimpleLiteral = bracketClose == bracketOpen + 2 && tokens[bracketOpen + 1].Kind == 'n';
                if (!isSimpleLiteral)
                {
                    bool accessUnique = false;
                    for (int k = bracketOpen; k <= bracketClose; k++)
                        if (uniqueLines.Contains(tokens[k].Line)) { accessUnique = true; break; }

                    for (int k = bracketOpen + 1; k < bracketClose; k++)
                    {
                        var tok = tokens[k];
                        if (tok.Kind == 'n' && tok.Value == oldLit && accessUnique)
                        {
                            uncertainLine = tok.Line;
                            return true;
                        }
                        if (tok.Kind == 'i' && varsAssignedK.Contains(tok.Value)
                            && (accessUnique || HasUniqueAssignment(tokens, tok.Value, oldLit, uniqueLines)))
                        {
                            uncertainLine = accessUnique ? tok.Line : FindAssignmentLine(tokens, tok.Value, oldLit, uniqueLines);
                            return true;
                        }
                    }

                    if (accessUnique && HasNearbyUnclassifiedLiteral(tokens, bracketOpen, bracketClose, oldLit, classifiedTokens))
                    {
                        uncertainLine = tokens[bracketOpen].Line;
                        return true;
                    }
                }
            }

            // Check dynamic array_length_1d(Rod.upgrades) comparisons.
            if (TryMatchArrayLengthUpgrades(tokens, i, inRod, out int callEnd))
            {
                bool callUnique = uniqueLines.Contains(tokens[i].Line);
                if (callEnd + 2 < tokens.Count && tokens[callEnd + 1].Value is (">" or ">=" or "<" or "<=" or "==" or "!="))
                {
                    bool isPlusOneOp = tokens[callEnd + 1].Value is (">=" or "<");
                    int rhsEnd = ScanExpressionEnd(tokens, callEnd + 2);
                    bool isSimpleLiteral = rhsEnd == callEnd + 2 && tokens[callEnd + 2].Kind == 'n';
                    if (!isSimpleLiteral)
                    {
                        for (int k = callEnd + 2; k <= rhsEnd && k < tokens.Count; k++)
                        {
                            var tok = tokens[k];
                            bool rhsUnique = callUnique || uniqueLines.Contains(tok.Line);
                            if (tok.Kind == 'n' && (tok.Value == oldLit || (isPlusOneOp && tok.Value == oldPlusOneLit)) && rhsUnique)
                            {
                                uncertainLine = tok.Line;
                                return true;
                            }
                            if (tok.Kind == 'i' && (varsAssignedK.Contains(tok.Value) || (isPlusOneOp && varsAssignedKPlusOne.Contains(tok.Value)))
                                && (rhsUnique || HasUniqueAssignment(tokens, tok.Value, oldLit, uniqueLines)
                                    || (isPlusOneOp && HasUniqueAssignment(tokens, tok.Value, oldPlusOneLit, uniqueLines))))
                            {
                                uncertainLine = tokens[i].Line;
                                return true;
                            }
                        }
                    }
                }
                if (i >= 2 && tokens[i - 1].Value is (">" or ">=" or "<" or "<=" or "==" or "!="))
                {
                    bool isPlusOneOp = tokens[i - 1].Value is ("<=" or ">");
                    int lhsStart = ScanExpressionStart(tokens, i - 2);
                    bool isSimpleLiteral = lhsStart == i - 2 && tokens[i - 2].Kind == 'n';
                    if (!isSimpleLiteral)
                    {
                        for (int k = lhsStart; k <= i - 2; k++)
                        {
                            var tok = tokens[k];
                            bool lhsUnique = callUnique || uniqueLines.Contains(tok.Line);
                            if (tok.Kind == 'n' && (tok.Value == oldLit || (isPlusOneOp && tok.Value == oldPlusOneLit)) && lhsUnique)
                            {
                                uncertainLine = tok.Line;
                                return true;
                            }
                            if (tok.Kind == 'i' && (varsAssignedK.Contains(tok.Value) || (isPlusOneOp && varsAssignedKPlusOne.Contains(tok.Value)))
                                && (lhsUnique || HasUniqueAssignment(tokens, tok.Value, oldLit, uniqueLines)
                                    || (isPlusOneOp && HasUniqueAssignment(tokens, tok.Value, oldPlusOneLit, uniqueLines))))
                            {
                                uncertainLine = tokens[i].Line;
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool TryMatchUpgradesArrayAccess(
        IReadOnlyList<GmlReferences.Token> tokens,
        int i,
        bool[] inRod,
        out int bracketOpen)
    {
        bracketOpen = -1;
        if (tokens[i].Kind != 'i') return false;

        if (tokens[i].Value == "Rod"
            && (i == 0 || tokens[i - 1].Value != ".")
            && i + 3 < tokens.Count
            && tokens[i + 1].Value == "."
            && tokens[i + 2].Kind == 'i' && tokens[i + 2].Value == "upgrades"
            && tokens[i + 3].Value == "[")
        {
            bracketOpen = i + 3;
            return true;
        }

        if (tokens[i].Value == "upgrades"
            && inRod[i]
            && (i == 0 || tokens[i - 1].Value != ".")
            && i + 1 < tokens.Count
            && tokens[i + 1].Value == "[")
        {
            bracketOpen = i + 1;
            return true;
        }

        return false;
    }

    private static bool TryMatchArrayLengthUpgrades(
        IReadOnlyList<GmlReferences.Token> tokens,
        int i,
        bool[] inRod,
        out int callEnd)
    {
        callEnd = -1;
        if (tokens[i].Kind != 'i' || tokens[i].Value is not ("array_length_1d" or "array_length")) return false;
        if (i > 0 && tokens[i - 1].Value == ".") return false;
        if (i + 3 >= tokens.Count || tokens[i + 1].Value != "(") return false;

        if (i + 5 < tokens.Count
            && tokens[i + 2].Kind == 'i' && tokens[i + 2].Value == "Rod"
            && tokens[i + 3].Value == "."
            && tokens[i + 4].Kind == 'i' && tokens[i + 4].Value == "upgrades"
            && tokens[i + 5].Value == ")")
        {
            callEnd = i + 5;
            return true;
        }

        if (tokens[i + 2].Kind == 'i' && tokens[i + 2].Value == "upgrades"
            && inRod[i + 2]
            && tokens[i + 3].Value == ")")
        {
            callEnd = i + 3;
            return true;
        }

        return false;
    }

    private static bool HasNearbyUnclassifiedLiteral(
        IReadOnlyList<GmlReferences.Token> tokens,
        int bracketOpen,
        int bracketClose,
        string literal,
        HashSet<(int Start, int Length)> classifiedTokens)
    {
        int from = Math.Max(0, bracketOpen - 15);
        int to = Math.Min(tokens.Count - 1, bracketClose + 15);
        for (int k = from; k <= to; k++)
        {
            var tok = tokens[k];
            if (tok.Kind == 'n' && tok.Value == literal && !classifiedTokens.Contains((tok.Start, tok.Length)))
                return true;
        }
        return false;
    }

    private static bool HasUniqueAssignment(
        IReadOnlyList<GmlReferences.Token> tokens,
        string varName,
        string literal,
        HashSet<int> uniqueLines) =>
        FindAssignmentLine(tokens, varName, literal, uniqueLines) > 0;

    private static int FindAssignmentLine(
        IReadOnlyList<GmlReferences.Token> tokens,
        string varName,
        string literal,
        HashSet<int> uniqueLines)
    {
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind == 'i' && tokens[i].Value == varName
                && (i == 0 || tokens[i - 1].Value != ".")
                && tokens[i + 1].Value == "="
                && tokens[i + 2].Kind == 'n' && tokens[i + 2].Value == literal
                && IsExpressionEnd(tokens, i + 3)
                && uniqueLines.Contains(tokens[i + 2].Line))
            {
                return tokens[i + 2].Line;
            }
        }
        return 0;
    }

    private static int ScanExpressionEnd(IReadOnlyList<GmlReferences.Token> tokens, int start)
    {
        int depth = 0;
        for (int i = start; i < tokens.Count; i++)
        {
            string v = tokens[i].Value;
            if (v is "(" or "[") depth++;
            else if (v is ")" or "]")
            {
                if (depth == 0) return i - 1;
                depth--;
            }
            else if (depth == 0 && (v is (";" or "," or "}" or "&&" or "||" or "and" or "or")
                || (i + 1 < tokens.Count && v is ("&" or "|") && tokens[i + 1].Value == v)))
            {
                return i - 1;
            }
        }
        return tokens.Count - 1;
    }

    private static int ScanExpressionStart(IReadOnlyList<GmlReferences.Token> tokens, int end)
    {
        int depth = 0;
        for (int i = end; i >= 0; i--)
        {
            string v = tokens[i].Value;
            if (v is ")" or "]") depth++;
            else if (v is "(" or "[")
            {
                if (depth == 0) return i + 1;
                depth--;
            }
            else if (depth == 0 && (v is (";" or "{" or "," or "&&" or "||" or "and" or "or" or "!" or "if" or "else" or "while" or "return")
                || (i > 0 && v is ("&" or "|") && tokens[i - 1].Value == v)))
            {
                return i + 1;
            }
        }
        return 0;
    }

    private static int FindLineEnd(string text, int index)
    {
        int nl = text.IndexOf('\n', index);
        return nl < 0 ? text.Length : nl;
    }

    private static string ExtractLineIndent(string text, int index)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int end = lineStart;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t')) end++;
        return text[lineStart..end];
    }

    private static bool IsQualifiedRodUpgradesAtToken(IReadOnlyList<GmlReferences.Token> tokens, GmlReferences.Token numToken)
    {
        for (int i = 3; i < tokens.Count; i++)
        {
            if (ReferenceEquals(tokens[i], numToken))
                return i >= 4 && tokens[i - 4].Value == "Rod" && tokens[i - 3].Value == "." && tokens[i - 2].Value == "upgrades";
        }
        return false;
    }

    private static bool IsRodUpgradeSlotWriteHunk(MergeHunk hunk)
    {
        var lines = hunk.Versions.Select(v => v.Text.Split('\n')).ToArray();
        var common = lines.Skip(1).Aggregate(new HashSet<string>(lines[0].Select(l => l.Trim()), StringComparer.Ordinal),
            (set, next) => { set.IntersectWith(next.Select(l => l.Trim())); return set; });
        bool anyUnique = false;
        foreach (var versionLines in lines)
        {
            foreach (string line in versionLines)
            {
                if (line.Trim().Length == 0 || common.Contains(line.Trim())) continue;
                anyUnique = true;
                if (!RodUpgradeSlotWrite().IsMatch(line)) return false;
            }
        }
        return anyUnique;
    }

    [GeneratedRegex(@"^\s*(?:Rod\.)?upgrades\s*\[\s*\d+\s*\]\s*=[^=]", RegexOptions.CultureInvariant)]
    private static partial Regex RodUpgradeSlotWrite();
}
