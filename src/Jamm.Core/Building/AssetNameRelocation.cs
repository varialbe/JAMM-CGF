using System.Text;
using System.Text.Json;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

/// <summary>Separates unrelated additions that claim the same GameMaker asset name.</summary>
internal static class AssetNameRelocation
{
    internal sealed record Result(ModPatch[] Patches, IReadOnlyList<string> Decisions,
        IReadOnlyDictionary<(int Mod, string Family, string Original), string> Renames);

    private static readonly string[] Families = ["objects", "sprites", "sounds", "scripts", "backgrounds", "paths", "fonts", "timelines"];

    internal static Result Prepare(IReadOnlyList<ModPatch> source)
    {
        ModPatch[] patches = source.ToArray();
        List<string> decisions = [];
        Dictionary<(int, string, string), string> renames = [];
        var groups = patches.SelectMany((patch, mod) => Families.SelectMany(family =>
                patch.Changes.GetValueOrDefault(family)?.Added.Select(asset => (Mod: mod, Family: family, Asset: asset)) ?? []))
            .GroupBy(x => (x.Family, x.Asset.Name)).Where(g => g.Select(x => x.Mod).Distinct().Count() > 1);

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Key.Name)) continue;
            var claimants = group.OrderBy(x => x.Mod).ToArray();
            var primary = claimants[0];
            if (group.Key.Family == "scripts" && group.Key.Name.StartsWith("jamm_", StringComparison.Ordinal))
            {
                decisions.Add($"INFO [CGF-RELOCATE-002] Script \"{group.Key.Name}\" is reserved for JAMM runtime/API handling; retained its existing merge behavior.");
                continue;
            }
            foreach (var later in claimants.Skip(1))
            {
                if (Identical(primary.Asset, later.Asset, patches[primary.Mod], patches[later.Mod], group.Key.Family)) continue;
                string? reason = ModificationReason(group.Key.Family, primary.Asset, later.Asset, patches, primary.Mod, later.Mod);
                if (reason is not null)
                {
                    decisions.Add($"INFO [CGF-RELOCATE-002] {Kind(group.Key.Family)} \"{group.Key.Name}\" ({Name(patches[later.Mod])}) shares lineage with {Name(patches[primary.Mod])} ({reason}); merged as a modification.");
                    continue;
                }
                string target = UniqueName(group.Key.Name, Name(patches[later.Mod]), group.Key.Family,
                    patches.SelectMany(p => p.Changes[group.Key.Family].Added.Concat(p.Changes[group.Key.Family].Changed)).Select(a => a.Name)
                        .Concat(patches.SelectMany(p => p.BaseAssets?.GetValueOrDefault(group.Key.Family) ?? [])));
                if (!TryRewrite(patches[later.Mod], group.Key.Family, group.Key.Name, target, out var rewritten, out int refs, out int code, out int rooms, out string? warning))
                {
                    decisions.Add($"WARNING [CGF-RELOCATE-002] {Kind(group.Key.Family)} \"{group.Key.Name}\" ({Name(patches[later.Mod])}) was not renamed: {warning}");
                    continue;
                }
                patches[later.Mod] = rewritten;
                renames[(later.Mod, group.Key.Family, group.Key.Name)] = target;
                decisions.Add($"[CGF-RELOCATE-002] {Kind(group.Key.Family)} \"{group.Key.Name}\" ({Name(patches[later.Mod])}) renamed to \"{target}\": different asset than {Name(patches[primary.Mod])} ({DifferenceReason(group.Key.Family)}); rewrote {refs} references ({code} code entries, {rooms} room instances).");
            }
        }
        return new(patches, decisions, renames);
    }

    private static bool Identical(AssetPatch a, AssetPatch b, ModPatch first, ModPatch second, string family) => a.Mod.GetRawText() == b.Mod.GetRawText()
        && a.ModCode?.Text == b.ModCode?.Text && EventTexts(first, a.Name, family, a).SequenceEqual(EventTexts(second, b.Name, family, b), StringComparer.Ordinal);

    private static string? ModificationReason(string family, AssetPatch first, AssetPatch next, ModPatch[] patches, int firstMod, int nextMod)
    {
        if (family == "objects" && first.Mod.TryGetProperty("parent", out var p1) && next.Mod.TryGetProperty("parent", out var p2)
            && p1.GetRawText() == p2.GetRawText()
            && first.Mod.TryGetProperty("sprite", out var s1) && next.Mod.TryGetProperty("sprite", out var s2) && s1.GetRawText() == s2.GetRawText())
        {
            string[] a = EventTexts(patches[firstMod], first.Name, family, first), b = EventTexts(patches[nextMod], next.Name, family, next);
            if (a.Length == 0 && b.Length == 0 && EventKeys(first) == EventKeys(next)) return "same parent/sprite with no owned event code";
            if (a.Length > 0 && b.Length > 0 && Similarity(a, b) >= 0.6) return "same parent/sprite and at least 60% shared event code";
        }
        if (family == "scripts")
        {
            string[] a = EventTexts(patches[firstMod], first.Name, family), b = EventTexts(patches[nextMod], next.Name, family);
            if (a.Length > 0 && b.Length > 0 && Similarity(a, b) >= 0.6) return "at least 60% shared script code";
        }
        if (family == "sprites" && first.Mod.TryGetProperty("width", out var w1) && next.Mod.TryGetProperty("width", out var w2)
            && w1.GetRawText() == w2.GetRawText() && first.Mod.GetProperty("height").GetRawText() == next.Mod.GetProperty("height").GetRawText()
            && next.Mod.GetProperty("frameCount").GetInt32() > first.Mod.GetProperty("frameCount").GetInt32()) return "same dimensions with added frames";
        return null;
    }

    private static string[] EventTexts(ModPatch patch, string name, string family, AssetPatch? asset = null)
    {
        var code = (patch.Changes.GetValueOrDefault("code")?.Added ?? []).Concat(patch.Changes.GetValueOrDefault("code")?.Changed ?? []).ToArray();
        // Objects may name their event code freely (e.g. "Boat8create"); the snapshot's event table is authoritative.
        var owned = family == "objects" ? EventCodeNames(asset) : [];
        return code.Where(a => family == "objects"
                ? owned.Contains(a.Name) || a.Name.StartsWith($"gml_Object_{name}_", StringComparison.Ordinal)
                : family == "scripts" && a.Name == $"gml_Script_{name}")
            .Select(a => a.ModCode?.Text ?? "").ToArray();
    }

    private static HashSet<string> EventCodeNames(AssetPatch? asset)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        if (asset is null || !asset.Mod.TryGetProperty("events", out var events) || events.ValueKind != System.Text.Json.JsonValueKind.Object) return names;
        foreach (var ev in events.EnumerateObject())
            if (ev.Value.TryGetProperty("code", out var list) && list.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String) names.Add(item.GetString()!);
        return names;
    }

    private static string EventKeys(AssetPatch asset) => asset.Mod.TryGetProperty("events", out var events) && events.ValueKind == System.Text.Json.JsonValueKind.Object
        ? string.Join(",", events.EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal)) : "";

    private static double Similarity(string[] a, string[] b)
    {
        var left = a.SelectMany(Lines).ToHashSet(StringComparer.Ordinal);
        var right = b.SelectMany(Lines).ToHashSet(StringComparer.Ordinal);
        return left.Count == 0 || right.Count == 0 ? 0 : (double)left.Intersect(right, StringComparer.Ordinal).Count() / Math.Max(left.Count, right.Count);
        static IEnumerable<string> Lines(string text) => text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0);
    }

    private static bool TryRewrite(ModPatch patch, string family, string oldName, string newName, out ModPatch result,
        out int references, out int codeEntries, out int roomInstances, out string? warning)
    {
        references = codeEntries = roomInstances = 0; warning = null; result = patch;
        int referenceCount = 0, codeCount = 0, roomCount = 0;
        string? unsafeWarning = null;
        var changes = new SortedDictionary<string, AssetChanges>(patch.Changes, StringComparer.Ordinal);
        foreach (string currentFamily in changes.Keys.ToArray())
        {
            AssetPatch RewriteAsset(AssetPatch asset)
            {
                string newAssetName = currentFamily == family && asset.Name == oldName ? newName
                    : family == "objects" && currentFamily == "code" && asset.Name.StartsWith($"gml_Object_{oldName}_", StringComparison.Ordinal)
                        ? asset.Name.Replace($"gml_Object_{oldName}_", $"gml_Object_{newName}_", StringComparison.Ordinal)
                        : family == "scripts" && currentFamily == "code" && asset.Name == $"gml_Script_{oldName}" ? $"gml_Script_{newName}" : asset.Name;
                bool codeEntryChanged = currentFamily == "code" && newAssetName != asset.Name;
                var snapshot = RewriteJson(asset.Mod, oldName, newName, family, currentFamily, out int structuredRefs, out int structuredRooms);
                referenceCount += structuredRefs; roomCount += structuredRooms;
                CodeText? modCode = asset.ModCode;
                if (modCode is { Format: "gml" })
                {
                    if (HasUnsafeStringLookup(modCode.Text, oldName)) { unsafeWarning ??= $"code/{asset.Name} contains a dynamic string lookup of '{oldName}'"; return asset; }
                    string changed = RewriteIdentifiers(modCode.Text, oldName, newName, out int count);
                    if (count > 0) referenceCount += count;
                    string? parent = OwnedCodeName(modCode.Parent, family, oldName, newName);
                    string[] children = modCode.Children.Select(child => OwnedCodeName(child, family, oldName, newName) ?? child).ToArray();
                    if (count > 0 || parent is not null || !children.SequenceEqual(modCode.Children, StringComparer.Ordinal))
                    {
                        codeEntryChanged = true;
                        modCode = modCode with { Text = changed, Parent = parent ?? modCode.Parent, Children = children };
                    }
                }
                if (codeEntryChanged) codeCount++;
                return asset with { Name = newAssetName, Mod = snapshot,
                    Fields = asset.Base is { } baseline ? FieldDiff.Compare(baseline, snapshot) : asset.Fields,
                    ModCode = modCode };
            }
            changes[currentFamily] = changes[currentFamily] with
            {
                Added = changes[currentFamily].Added.Select(RewriteAsset).ToArray(),
                Changed = changes[currentFamily].Changed.Select(RewriteAsset).ToArray(),
                Removed = changes[currentFamily].Removed.Select(n => n == oldName && currentFamily == family ? newName : n).ToArray()
            };
        }
        references = referenceCount; codeEntries = codeCount; roomInstances = roomCount; warning = unsafeWarning;
        if (warning is not null) return false;
        var referencesByAsset = patch.References?.Select(reference =>
        {
            string sourceName = reference.Family == family && reference.Asset == oldName ? newName : reference.Asset;
            string targetName = reference.TargetFamily == family && reference.Target == oldName ? newName : reference.Target;
            if (sourceName != reference.Asset || targetName != reference.Target) referenceCount++;
            return reference with { Asset = sourceName, Target = targetName };
        }).ToArray();
        references = referenceCount; codeEntries = codeCount; roomInstances = roomCount;
        result = patch with { Changes = changes, References = referencesByAsset };
        return true;
    }

    private static JsonElement RewriteJson(JsonElement json, string oldName, string newName, string targetFamily, string snapshotFamily, out int refs, out int rooms)
    {
        int referenceCount = 0, roomCount = 0;
        object? Walk(JsonElement value, string property = "") => value.ValueKind switch
        {
            JsonValueKind.Object => ObjectValues(value, property),
            JsonValueKind.Array => value.EnumerateArray().Select(v => Walk(v, property)).ToArray(),
            JsonValueKind.String when value.GetString() == oldName && IsReferenceField(property, targetFamily, snapshotFamily) => CountAndReturn(property),
            JsonValueKind.String when OwnedCodeName(value.GetString(), targetFamily, oldName, newName) is { } codeName => CountCodeAndReturn(codeName),
            _ => JsonSerializer.Deserialize<object>(value.GetRawText())
        };
        Dictionary<string, object?> ObjectValues(JsonElement value, string property)
        {
            Dictionary<string, object?> result = new(StringComparer.Ordinal);
            foreach (var item in value.EnumerateObject())
            {
                string key = item.Name;
                if (property == "instances" && targetFamily == "objects")
                {
                    try
                    {
                        using var keyDocument = JsonDocument.Parse(key);
                        if (keyDocument.RootElement.ValueKind == JsonValueKind.Array && keyDocument.RootElement.GetArrayLength() > 0
                            && keyDocument.RootElement[0].ValueKind == JsonValueKind.String && keyDocument.RootElement[0].GetString() == oldName)
                        {
                            var values = keyDocument.RootElement.EnumerateArray().ToArray();
                            values[0] = JsonSerializer.SerializeToElement(newName);
                            key = JsonSerializer.Serialize(values.Select(v => JsonSerializer.Deserialize<object>(v.GetRawText())).ToArray());
                            referenceCount++; roomCount++;
                        }
                    }
                    catch (JsonException) { }
                }
                if (property == "events" && targetFamily == "objects" && key.StartsWith($"4:{oldName}", StringComparison.Ordinal))
                {
                    key = $"4:{newName}" + key[($"4:{oldName}").Length..];
                    referenceCount++;
                }
                result[key] = Walk(item.Value, item.Name);
            }
            return result;
        }
        object CountAndReturn(string property) { referenceCount++; if (property == "object") roomCount++; return newName; }
        object CountCodeAndReturn(string codeName) { referenceCount++; return codeName; }
        JsonElement result = JsonSerializer.SerializeToElement(Walk(json), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        refs = referenceCount; rooms = roomCount;
        return result;
    }

    private static bool IsReferenceField(string property, string targetFamily, string snapshotFamily) => property.ToLowerInvariant() is "parent" or "sprite" or "mask" or "object" or "resource" or "code" or "background" or "creationcode"
        || snapshotFamily == "code";

    private static string? OwnedCodeName(string? value, string family, string oldName, string newName)
    {
        if (value is null) return null;
        string prefix = family switch { "objects" => $"gml_Object_{oldName}_", "scripts" => $"gml_Script_{oldName}", _ => "" };
        bool owned = family == "scripts" ? value == prefix : prefix.Length > 0 && value.StartsWith(prefix, StringComparison.Ordinal);
        return owned
            ? prefix.Replace(oldName, newName, StringComparison.Ordinal) + value[prefix.Length..]
            : null;
    }

    private static string RewriteIdentifiers(string text, string oldName, string newName, out int count)
    {
        var edits = GmlReferences.Tokens(text).Where(t => t.Kind == 'i' && t.Value == oldName)
            .Select(t => (t.Start, t.Length)).ToArray();
        var result = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(x => x.Start)) result.Remove(edit.Start, edit.Length).Insert(edit.Start, newName);
        count = edits.Length;
        return result.ToString();
    }

    private static bool HasUnsafeStringLookup(string text, string name)
    {
        var tokens = GmlReferences.Tokens(text);
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Kind != 'i' || tokens[i].Value is not ("asset_get_index" or "object_get_name") || tokens[i + 1].Value != "(") continue;
            for (int j = i + 2; j < tokens.Count && tokens[j].Value != ")"; j++)
                if (tokens[j].Kind == 's' && tokens[j].Value == name) return true;
        }
        return false;
    }

    private static string UniqueName(string original, string mod, string family, IEnumerable<string> existing)
    {
        string slug = new string(mod.ToLowerInvariant().Select(c => IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (slug.Length == 0) slug = "mod";
        if (!char.IsLetter(slug[0]) && slug[0] != '_') slug = "m_" + slug;
        string stem = $"{SafeIdentifier(original)}_jamm_{slug}";
        string candidate = stem;
        var used = existing.ToHashSet(StringComparer.Ordinal);
        int suffix = 2;
        while (used.Contains(candidate)) candidate = $"{stem}_{suffix++}";
        return candidate;
    }

    private static string SafeIdentifier(string name)
    {
        string value = new(name.Select(c => IsAsciiLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (value.Length == 0) return "asset";
        return IsAsciiLetter(value[0]) || value[0] == '_' ? value : "_" + value;
    }

    private static bool IsAsciiLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    private static bool IsAsciiLetterOrDigit(char value) => IsAsciiLetter(value) || value is >= '0' and <= '9';

    private static string Name(ModPatch patch) => patch.Mod.Name ?? Path.GetFileNameWithoutExtension(patch.Mod.FileName);
    private static string Kind(string family) => family.TrimEnd('s') switch { "object" => "Object", "sprite" => "Sprite", "sound" => "Sound", "script" => "Script", _ => family };
    private static string DifferenceReason(string family) => family switch
    {
        "objects" => "parent/size/code differ",
        "sprites" => "dimensions/frame content differ",
        _ => "asset content differs"
    };
}
