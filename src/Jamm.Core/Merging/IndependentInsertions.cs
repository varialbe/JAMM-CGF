namespace Jamm.Core.Merging;

/// <summary>
/// Decides whether competing pure insertions at one position can safely be kept together: every alternative must only add
/// statements, the variables each alternative assigns must be disjoint, and no alternative may read a variable another one assigns.
/// Typical case: two mods append independent particle/initialization blocks to the same Create event.
/// </summary>
internal static class IndependentInsertions
{
    /// <summary>True when the conflicting list hunk can be unioned without changing either mod's meaning.</summary>
    internal static bool CanUnion(MergeHunk hunk)
    {
        if (hunk.Status != "CONFLICT" || hunk.Kind != "list" || hunk.KeepBoth is null || hunk.Versions.Count < 2) return false;
        var assigned = new List<HashSet<string>>();
        var read = new List<HashSet<string>>();
        foreach (var version in hunk.Versions)
        {
            var tokens = GmlReferences.Tokens(version.Text);
            if (tokens.Count == 0) return false;
            var writes = new HashSet<string>(StringComparer.Ordinal);
            var reads = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != 'i') continue;
                if (i > 0 && tokens[i - 1].Value == ".") continue;
                int start = i;
                string name = tokens[i].Value;
                // Treat "global.x" / "obj.x" as one qualified name so different globals do not collide.
                if (i + 2 < tokens.Count && tokens[i + 1].Value == "." && tokens[i + 2].Kind == 'i') { name += "." + tokens[i + 2].Value; i += 2; }
                bool statementStart = start == 0 || tokens[start - 1].Value is ";" or "{" or "}" || tokens[start - 1].Line < tokens[start].Line;
                bool assignment = i + 1 < tokens.Count && tokens[i + 1].Value is "=" or "+=" or "-=" or "*=" or "/=";
                if (statementStart && assignment) writes.Add(name); else reads.Add(name);
            }
            if (writes.Count == 0) return false;
            assigned.Add(writes); read.Add(reads);
        }
        for (int a = 0; a < assigned.Count; a++)
            for (int b = 0; b < assigned.Count; b++)
            {
                if (a == b) continue;
                if (assigned[a].Overlaps(assigned[b]) || read[a].Overlaps(assigned[b])) return false;
            }
        return true;
    }
}
