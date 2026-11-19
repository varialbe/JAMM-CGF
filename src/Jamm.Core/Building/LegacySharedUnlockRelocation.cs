using System.Globalization;
using System.Text;
using Jamm.Core.Merging;

namespace Jamm.Core.Building;

/// <summary>Describes a shared hunk that unlocks a kept upgrade slot and must also unlock a relocated slot.</summary>
internal sealed record SharedUnlockHunk(
    string CodeEntry,
    int OldSlot,
    int NewSlot,
    string UpgradeName,
    int KeptMod,
    IReadOnlyList<int> MovedMods,
    bool IsAdded,
    int HunkStart,
    int HunkLength,
    string SharedText);

internal static partial class LegacyUpgradeRelocation
{
    private sealed record ContenderSharedHunk(
        string CodeEntry,
        HashSet<int> SharingMods,
        bool IsAdded,
        int HunkStart,
        int HunkLength,
        string SharedText);

    internal static string SharedUnlockDecision(SharedUnlockHunk hunk) =>
        $"[CGF-RELOCATE-005] Shared unlock of upgrade {hunk.OldSlot} in {hunk.CodeEntry} also unlocks relocated \"{hunk.UpgradeName}\" ({hunk.NewSlot}).";

    private static void CollectSharedUnlockHunksForGroup(
        IReadOnlyList<ContenderSharedHunk> sharedHunks,
        int oldSlot,
        int newSlot,
        string upgradeName,
        int keptMod,
        int[] movedMods,
        List<SharedUnlockHunk> destination)
    {
        foreach (var shared in sharedHunks)
        {
            if (!shared.SharingMods.Contains(keptMod)) continue;
            if (!movedMods.Any(m => shared.SharingMods.Contains(m))) continue;
            if (!ContainsUnlockCall(shared.SharedText, oldSlot)) continue;

            var candidate = new SharedUnlockHunk(
                shared.CodeEntry,
                oldSlot,
                newSlot,
                upgradeName,
                keptMod,
                movedMods,
                shared.IsAdded,
                shared.HunkStart,
                shared.HunkLength,
                shared.SharedText);

            if (!destination.Any(existing =>
                existing.CodeEntry == candidate.CodeEntry
                && existing.OldSlot == candidate.OldSlot
                && existing.NewSlot == candidate.NewSlot
                && existing.IsAdded == candidate.IsAdded
                && existing.HunkStart == candidate.HunkStart
                && existing.HunkLength == candidate.HunkLength
                && string.Equals(existing.SharedText, candidate.SharedText, StringComparison.Ordinal)))
            {
                destination.Add(candidate);
            }
        }
    }

    internal static bool ContainsUnlockCall(string text, int slot)
    {
        string lit = slot.ToString(CultureInfo.InvariantCulture);
        var tokens = GmlReferences.Tokens(text);
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (IsUnlockUpgradeStatement(tokens, i, lit, out _))
                return true;
        }
        return false;
    }

    internal static bool HunkMatchesSharedUnlock(
        SharedUnlockHunk su,
        string entryName,
        bool isAdded,
        MergeHunk hunk,
        string hunkText,
        IReadOnlyList<int> activeMods,
        IReadOnlyList<string> ancestorLines,
        out int spanStart,
        out int spanEnd)
    {
        spanStart = -1;
        spanEnd = -1;
        if (su.CodeEntry != entryName || su.IsAdded != isAdded) return false;
        if (!isAdded && !RangesOverlap(hunk.Start, hunk.Length, su.HunkStart, su.HunkLength)) return false;
        if (!activeMods.Contains(su.KeptMod) && !su.MovedMods.Any(activeMods.Contains)) return false;

        int searchFrom = 0;
        if (!isAdded && hunk.Start < su.HunkStart && su.HunkStart <= ancestorLines.Count)
            searchFrom = Math.Min(hunkText.Length, string.Concat(ancestorLines.Skip(hunk.Start).Take(su.HunkStart - hunk.Start)).Length);

        spanStart = hunkText.IndexOf(su.SharedText, searchFrom, StringComparison.Ordinal);
        if (spanStart < 0 && searchFrom > 0)
            spanStart = hunkText.IndexOf(su.SharedText, StringComparison.Ordinal);
        if (spanStart < 0) return false;

        spanEnd = spanStart + su.SharedText.Length;
        return true;
    }

    internal static string ApplySharedUnlocksToHunk(
        string entryName,
        bool isAdded,
        MergeHunk hunk,
        string hunkText,
        IReadOnlyList<int> activeMods,
        IReadOnlyList<string> ancestorLines,
        IReadOnlyList<SharedUnlockHunk> candidates,
        Action<SharedUnlockHunk> onApplied)
    {
        List<(SharedUnlockHunk Unlock, int SpanStart, int SpanEnd)> matched = [];
        foreach (var su in candidates)
        {
            if (HunkMatchesSharedUnlock(su, entryName, isAdded, hunk, hunkText, activeMods, ancestorLines, out int spanStart, out int spanEnd))
                matched.Add((su, spanStart, spanEnd));
        }
        if (matched.Count == 0) return hunkText;

        var tokens = GmlReferences.Tokens(hunkText);
        if (tokens.Count == 0) return hunkText;

        Dictionary<int, (string Insertion, HashSet<int> AddedSlots)> editsByPos = [];

        foreach (var spanGroup in matched.GroupBy(m => (m.SpanStart, m.SpanEnd)))
        {
            int spanStart = spanGroup.Key.SpanStart;
            int spanEnd = spanGroup.Key.SpanEnd;
            var unlocksByOldSlot = spanGroup
                .Select(m => m.Unlock)
                .GroupBy(u => u.OldSlot)
                .ToDictionary(g => g.Key, g => g.OrderBy(u => u.NewSlot).ToArray());

            for (int i = 0; i + 3 < tokens.Count; i++)
            {
                if (tokens[i].Kind != 'i' || tokens[i].Value != "UnlockUpgrade") continue;
                int tokStart = tokens[i].Start;
                if (tokStart < spanStart || tokStart >= spanEnd) continue;

                foreach (var (oldSlot, unlocks) in unlocksByOldSlot)
                {
                    string oldLit = oldSlot.ToString(CultureInfo.InvariantCulture);
                    if (!IsUnlockUpgradeStatement(tokens, i, oldLit, out int stmtEndIdx)) continue;

                    int stmtEndPos = tokens[stmtEndIdx].Start + tokens[stmtEndIdx].Length;
                    if (stmtEndPos > spanEnd) continue;

                    var alreadyFollowing = CollectFollowingUnlockTargets(tokens, stmtEndIdx);
                    var (insertPos, standalone, indent) = ComputeInsertionPoint(hunkText, tokens[i], tokens[stmtEndIdx]);

                    foreach (var su in unlocks)
                    {
                        string newLit = su.NewSlot.ToString(CultureInfo.InvariantCulture);
                        if (alreadyFollowing.Contains(newLit)) continue;

                        if (!editsByPos.TryGetValue(insertPos, out var existing))
                        {
                            existing = ("", []);
                            editsByPos[insertPos] = existing;
                        }
                        if (!existing.AddedSlots.Add(su.NewSlot)) continue;

                        string stmt = FormatGuardedUnlock(newLit, standalone, indent);
                        editsByPos[insertPos] = (existing.Insertion + stmt, existing.AddedSlots);
                        onApplied(su);
                    }
                }
            }
        }

        if (editsByPos.Count == 0) return hunkText;

        StringBuilder sb = new(hunkText);
        foreach (var edit in editsByPos.OrderByDescending(e => e.Key))
            sb.Insert(edit.Key, edit.Value.Insertion);
        return sb.ToString();
    }

    private static bool IsUnlockUpgradeStatement(
        IReadOnlyList<GmlReferences.Token> tokens,
        int index,
        string slotLit,
        out int stmtEndIdx)
    {
        stmtEndIdx = -1;
        if (index + 3 >= tokens.Count) return false;
        if (tokens[index].Kind != 'i' || tokens[index].Value != "UnlockUpgrade") return false;
        if (index > 0 && tokens[index - 1].Value == ".") return false;
        if (tokens[index + 1].Value != "(" || tokens[index + 2].Kind != 'n'
            || tokens[index + 2].Value != slotLit || tokens[index + 3].Value != ")")
            return false;

        if (index > 0 && tokens[index - 1].Value is "=" or "(" or "," or "+" or "-" or "*" or "/" or "==" or "!=" or "&&" or "||")
            return false;

        if (index + 4 < tokens.Count && tokens[index + 4].Value == ";")
        {
            stmtEndIdx = index + 4;
            return true;
        }

        if (index + 4 >= tokens.Count || tokens[index + 4].Value == "}" || tokens[index + 4].Line > tokens[index + 3].Line)
        {
            stmtEndIdx = index + 3;
            return true;
        }

        return false;
    }

    private static HashSet<string> CollectFollowingUnlockTargets(
        IReadOnlyList<GmlReferences.Token> tokens,
        int stmtEndIdx)
    {
        HashSet<string> targets = new(StringComparer.Ordinal);
        int pos = stmtEndIdx + 1;
        while (pos < tokens.Count)
        {
            if (pos + 3 < tokens.Count
                && tokens[pos].Kind == 'i' && tokens[pos].Value == "UnlockUpgrade"
                && (pos == 0 || tokens[pos - 1].Value != ".")
                && tokens[pos + 1].Value == "("
                && tokens[pos + 2].Kind == 'n'
                && tokens[pos + 3].Value == ")")
            {
                targets.Add(tokens[pos + 2].Value);
                pos += (pos + 4 < tokens.Count && tokens[pos + 4].Value == ";") ? 5 : 4;
                continue;
            }

            if (pos + 10 < tokens.Count
                && tokens[pos].Kind == 'i' && tokens[pos].Value == "if"
                && tokens[pos + 1].Value == "("
                && tokens[pos + 2].Value == "!"
                && tokens[pos + 3].Kind == 'i' && tokens[pos + 3].Value == "UpgradeData"
                && tokens[pos + 4].Value == "."
                && tokens[pos + 5].Kind == 'i' && tokens[pos + 5].Value == "unlocks"
                && tokens[pos + 6].Value == "["
                && tokens[pos + 7].Kind == 'n'
                && tokens[pos + 8].Value == "]"
                && tokens[pos + 9].Value == ")")
            {
                int bodyPos = pos + 10;
                bool hasBrace = bodyPos < tokens.Count && tokens[bodyPos].Value == "{";
                if (hasBrace) bodyPos++;
                if (bodyPos + 3 < tokens.Count
                    && tokens[bodyPos].Kind == 'i' && tokens[bodyPos].Value == "UnlockUpgrade"
                    && tokens[bodyPos + 1].Value == "("
                    && tokens[bodyPos + 2].Kind == 'n'
                    && tokens[bodyPos + 3].Value == ")")
                {
                    int afterCall = bodyPos + ((bodyPos + 4 < tokens.Count && tokens[bodyPos + 4].Value == ";") ? 5 : 4);
                    if (!hasBrace || (afterCall < tokens.Count && tokens[afterCall].Value == "}"))
                    {
                        if (hasBrace) afterCall++;
                        targets.Add(tokens[pos + 7].Value);
                        targets.Add(tokens[bodyPos + 2].Value);
                        pos = afterCall;
                        continue;
                    }
                }
            }

            break;
        }
        return targets;
    }

    private static (int InsertPos, bool StandaloneLine, string Indent) ComputeInsertionPoint(
        string text,
        GmlReferences.Token callToken,
        GmlReferences.Token endToken)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, callToken.Start - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        bool onlyWhitespaceBefore = true;
        for (int i = lineStart; i < callToken.Start; i++)
        {
            if (text[i] is not (' ' or '\t'))
            {
                onlyWhitespaceBefore = false;
                break;
            }
        }

        int afterEnd = endToken.Start + endToken.Length;
        int lineEnd = text.IndexOf('\n', afterEnd);
        if (lineEnd < 0) lineEnd = text.Length;

        bool onlyTriviaAfter = true;
        for (int i = afterEnd; i < lineEnd; i++)
        {
            if (text[i] is ' ' or '\t') continue;
            if (text[i] == '/' && i + 1 < lineEnd && text[i + 1] == '/') break;
            onlyTriviaAfter = false;
            break;
        }

        if (onlyWhitespaceBefore && onlyTriviaAfter)
            return (lineEnd, true, text[lineStart..callToken.Start]);

        return (afterEnd, false, "");
    }

    private static string FormatGuardedUnlock(string newLit, bool standalone, string indent) =>
        standalone
            ? $"\n{indent}if (!UpgradeData.unlocks[{newLit}])\n{indent}{{\n{indent}    UnlockUpgrade({newLit});\n{indent}}}"
            : $" if (!UpgradeData.unlocks[{newLit}]) {{ UnlockUpgrade({newLit}); }}";

    private static bool RangesOverlap(int startA, int lengthA, int startB, int lengthB) =>
        lengthA == 0 && lengthB == 0 ? startA == startB
        : lengthA == 0 ? startA >= startB && startA <= startB + lengthB
        : lengthB == 0 ? startB >= startA && startB <= startA + lengthA
        : startA < startB + lengthB && startB < startA + lengthA;
}
