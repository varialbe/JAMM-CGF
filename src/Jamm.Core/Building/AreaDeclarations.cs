using System.Text.Json;
using Jamm.Core.Merging;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;

namespace Jamm.Core.Building;

// A generated legacy GML script carries the declaration through .win, patch caches and ZIP import.
internal sealed record AreaDeclaration(string Package, ModKitArea Area, IReadOnlyList<AreaFish> Fish);
internal sealed record AreaFish(string Object, int Count, ModKitAreaBounds? Within);
internal sealed record AreaAnchorSelection(ModKitAreaTarget Target, int Source, IReadOnlyList<ModKitAreaTarget> Superseded);

internal static class AreaDeclarations
{
    internal const string Prefix = "jamm_area_";
    private const string CodePrefix = "gml_Script_" + Prefix;
    private const string Signature = "CGF_AREA_1:";

    internal static string Encode(AreaDeclaration declaration) =>
        $"return \"{Signature}{Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(declaration))}\";";

    internal static AreaDeclaration Read(string name, string? gml)
    {
        var token = GmlReferences.Tokens(gml ?? "").FirstOrDefault(t => t.Kind == 's' && t.Value.StartsWith(Signature, StringComparison.Ordinal));
        if (token?.Value is { } value)
        {
            try
            {
                if (JsonSerializer.Deserialize<AreaDeclaration>(Convert.FromBase64String(value[Signature.Length..])) is { } declaration)
                    return declaration;
            }
            catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException)
            {
                throw Invalid(name, ex);
            }
        }
        throw Invalid(name);

        static InvalidDataException Invalid(string name, Exception? inner = null) =>
            new($"Area declaration '{name}' is unreadable. Rebuild its ModKit source package and regenerate the patch.", inner);
    }

    internal static bool IsDeclaration(string codeName) => codeName.StartsWith(CodePrefix, StringComparison.Ordinal);

    internal static IReadOnlyList<ConflictItem> Diagnose(IReadOnlyList<ModPatch> patches, IReadOnlyList<string> names,
        IReadOnlyDictionary<string, JsonElement> finalRooms)
    {
        var definitions = patches.SelectMany((p, i) => p.Changes.TryGetValue("code", out var code)
            ? code.Added.Concat(code.Changed).Where(a => IsDeclaration(a.Name))
                .Select(a => (Mod: i, Declaration: Read(a.Name, a.ModCode?.Text)))
            : []).ToArray();
        List<ConflictItem> findings = [];
        var resolved = new List<(int Mod, string Room, string Area, ModKitAreaBounds Bounds)>();

        foreach (var (owner, declaration) in definitions)
        {
            var area = declaration.Area;
            string label = $"{declaration.Package}:{area.Id}";
            var edits = RoomEdits(patches, area.Room).Where(r => r.Mod != owner).ToArray();
            var bounds = area.Bounds;
            if (area.Anchor.Target is { } anchor && finalRooms.TryGetValue(area.Room, out var snapshot))
            {
                var selection = ResolveAnchor(Instances(snapshot, anchor.Object), anchor, edits);
                if (selection is null)
                    Add("WARNING", -1, $"follow anchor {Describe(anchor)} is missing or ambiguous in {area.Room}.");
                else if (selection.Target != anchor)
                {
                    var target = selection.Target;
                    bounds = bounds.Translate(checked(target.X - anchor.X), checked(target.Y - anchor.Y));
                    string alternatives = selection.Superseded.Count == 0 ? "" :
                        $" Used the latest selected move in load order and discarded {selection.Superseded.Count} earlier placement(s) of the same anchor.";
                    Add("INFO", selection.Source, $"follows {anchor.Object} from ({anchor.X}, {anchor.Y}) to ({target.X}, {target.Y}) in {area.Room}.{alternatives}");
                }
            }
            foreach (var edit in edits)
            {
                var before = Map(edit.Room.Base);
                var after = Map(edit.Room.Mod);
                var changed = (before?.EnumerateObject().Select(e => e.Name) ?? [])
                    .Concat(after?.EnumerateObject().Select(e => e.Name) ?? []).Distinct(StringComparer.Ordinal)
                    .Where(key => !string.Equals(Value(before, key), Value(after, key), StringComparison.Ordinal))
                    .Select(KeyTarget).Where(t => t is not null && (bounds.Contains(t.X, t.Y) || area.Bounds.Contains(t.X, t.Y)))
                    .Cast<ModKitAreaTarget>().ToArray();
                if (changed.Length == 0) continue;
                var examples = string.Join(", ", changed.Take(4).Select(Describe));
                Add("WARNING", edit.Mod, $"{changed.Length} changed instance position(s) intersect {area.Room} bounds ({examples}). Review the placement in game.");
            }
            foreach (var other in resolved.Where(r => r.Room == area.Room && bounds.Intersects(r.Bounds)))
                Add("WARNING", other.Mod, $"bounds overlap area '{other.Area}' in {area.Room}. Review both placements in game.");
            resolved.Add((owner, area.Room, label, bounds));

            void Add(string severity, int source, string message) => findings.Add(new(severity, "area", "rooms", label,
                source < 0 || source == owner ? [names[owner]] : [names[owner], names[source]], $"[CGF-AREA-001] Area '{label}' {message}"));
        }
        return findings;
    }

    internal static IEnumerable<(int Mod, AssetPatch Room)> RoomEdits(IReadOnlyList<ModPatch> patches, string room) =>
        patches.SelectMany((patch, i) => patch.Changes.TryGetValue("rooms", out var changes)
            ? changes.Added.Concat(changes.Changed).Where(a => a.Name == room).Select(a => (i, a))
            : []);

    internal static AreaAnchorSelection? ResolveAnchor(IEnumerable<ModKitAreaTarget> instances, ModKitAreaTarget reference,
        IEnumerable<(int Mod, AssetPatch Room)> edits)
    {
        var matches = instances.Where(i => i.Object == reference.Object).ToArray();
        if (matches.Count(i => i == reference) == 1) return new(reference, -1, []);
        if (matches.Length == 0 || matches.Distinct().Count() != matches.Length) return null;

        var moves = edits.Select(edit => (edit.Mod, Target: SingleMove(edit.Room, reference)))
            .Where(move => move.Target is not null && matches.Contains(move.Target)).ToArray();
        if (matches.Length == 1)
            return new(matches[0], moves.Length > 0 ? moves[^1].Mod : -1, []);
        if (matches.Any(match => moves.All(move => move.Target != match)))
            return null;

        // Room patches key instances by object and coordinates. Rival moves of a singleton
        // can appear as distinct additions, so retain the latest move that survived the merge.
        var winner = moves[^1];
        return new(winner.Target!, winner.Mod, matches.Where(match => match != winner.Target).ToArray());
    }

    internal static string Describe(ModKitAreaTarget target) => $"{target.Object} ({target.X}, {target.Y})";

    private static ModKitAreaTarget? SingleMove(AssetPatch patch, ModKitAreaTarget reference)
    {
        var before = Instances(patch.Base, reference.Object).ToArray();
        if (before.Length != 1 || before[0] != reference) return null;
        var after = Instances(patch.Mod, reference.Object).ToArray();
        return after.Length == 1 && after[0] != reference ? after[0] : null;
    }

    private static IEnumerable<ModKitAreaTarget> Instances(JsonElement? snapshot, string name)
    {
        if (Map(snapshot) is not { } instances) yield break;
        foreach (var entry in instances.EnumerateObject())
            if (KeyTarget(entry.Name) is { } target && target.Object == name)
                for (int i = 0; i < entry.Value.GetArrayLength(); i++) yield return target;
    }

    private static JsonElement? Map(JsonElement? snapshot) =>
        snapshot is { ValueKind: JsonValueKind.Object } room && room.TryGetProperty("instances", out var value) ? value : null;

    private static string? Value(JsonElement? map, string key) =>
        map is { } entries && entries.TryGetProperty(key, out var value) ? value.GetRawText() : null;

    private static ModKitAreaTarget? KeyTarget(string key)
    {
        using var document = JsonDocument.Parse(key);
        var tuple = document.RootElement;
        return tuple[0].GetString() is { } name ? new(name, tuple[1].GetInt32(), tuple[2].GetInt32()) : null;
    }
}
