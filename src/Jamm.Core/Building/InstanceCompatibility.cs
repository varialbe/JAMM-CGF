using System.Text.RegularExpressions;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;

namespace Jamm.Core.Building;

internal static class InstanceCompatibility
{
    internal const int Revision = 2;
    private const string ElectronEvent = "gml_Object_Electron_Other_17";
    private const string FishThinnerEvent = "gml_Object_FishThinner_Other_10";
    private const string OriginalCondition = "if (Lure.fish > 0 && Lure.fish.object_index == Flash.object_index)";
    private const string FixedCondition = "if (Lure.fish > 0 && Lure.fish.object_index == Flash)";
    private const string FishCullCondition = "if (instance_number(object_index) > 5 && point_distance(x, y, Lure.x, Lure.y) > 2000)";
    private const string FishCullGuardedCondition = "if (instance_number(object_index) > 5 && point_distance(x, y, Lure.x, Lure.y) > 2000 && !(light > 0))";
    private const string FishCullDecision = "[CGF-INSTANCE-003] Kept fish lights alive: FishThinner no longer culls fish that own a light (prevents \"Unable to find any instance\" crashes in Magmer/Volteen draw code when merged rooms place more than 5 of them).";
    private static readonly Regex FishCullConditionPattern = new(
        @"if\s*\(\s*instance_number\s*\(\s*object_index\s*\)\s*>\s*5\s*&&\s*point_distance\s*\(\s*x\s*,\s*y\s*,\s*Lure\.x\s*,\s*Lure\.y\s*\)\s*>\s*2000\s*\)",
        RegexOptions.CultureInvariant);
    private static readonly Regex FishCullGuardedPattern = new(
        @"if\s*\(\s*instance_number\s*\(\s*object_index\s*\)\s*>\s*5\s*&&\s*point_distance\s*\(\s*x\s*,\s*y\s*,\s*Lure\.x\s*,\s*Lure\.y\s*\)\s*>\s*2000\s*&&\s*!\s*\(\s*light\s*>\s*0\s*\)\s*\)",
        RegexOptions.CultureInvariant);

    internal static BuildPlan Repair(BuildPlan plan, IReadOnlyList<ModPatch> patches, IReadOnlyDictionary<string, CodeText> baseline)
    {
        if (patches.Count > 0)
            plan = RepairFishThinner(plan, baseline);

        var electron = plan.Assets.FirstOrDefault(a => a.Family == "code" && a.Name == ElectronEvent);
        if (electron?.Gml is not { } gml) return plan;

        // Match the known event body so a future mod that reuses this event name keeps its own logic.
        string body = gml.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (body != OriginalCondition + "\n{\n    fish_lunge();\n}") return plan;

        var repaired = electron with { Gml = gml.Replace(OriginalCondition, FixedCondition, StringComparison.Ordinal) };
        return plan with
        {
            Assets = plan.Assets.Select(a => a == electron ? repaired : a).ToArray(),
            Decisions = [.. plan.Decisions,
                $"[CGF-INSTANCE-001] Fixed {ModName(patches[electron.Source])} in {ElectronEvent}: compare the hooked fish with the Flash object type without requiring a live Flash."]
        };
    }

    private static BuildPlan RepairFishThinner(BuildPlan plan, IReadOnlyDictionary<string, CodeText> baseline)
    {
        int index = plan.Assets.ToList().FindIndex(a => a.Family == "code" && a.Name == FishThinnerEvent);
        var selected = index >= 0 ? plan.Assets[index] : null;
        string? text = selected?.Gml ?? baseline.GetValueOrDefault(FishThinnerEvent)?.Text;
        if (text is null)
        {
            if (selected is null) return plan;
            return AddFishThinnerWarning(plan, "the event is missing from the selected build; keep the FishThinner event from the base archive and rebuild.");
        }

        if (FishCullGuardedPattern.Matches(text).Count == 1)
            return AddFishThinnerDecision(plan);

        MatchCollection matches = FishCullConditionPattern.Matches(text);
        if (matches.Count != 1)
            return AddFishThinnerWarning(plan, "the event no longer matches the known culling condition; review gml_Object_FishThinner_Other_10 before rebuilding.");

        Match match = matches[0];
        string repairedText = text[..match.Index] + FishCullGuardedCondition + text[(match.Index + match.Length)..];
        List<PlannedAsset> assets = [.. plan.Assets];
        if (selected is not null)
        {
            assets[index] = selected with { Gml = repairedText };
        }
        else
        {
            var before = baseline[FishThinnerEvent];
            var snapshot = AssetFields.Json(new { });
            var after = before with { Text = repairedText };
            assets.Add(new("code", FishThinnerEvent, 0,
                new AssetPatch(FishThinnerEvent, snapshot, snapshot, [], before, after), snapshot, repairedText));
        }
        return AddFishThinnerDecision(plan with { Assets = assets });
    }

    private static BuildPlan AddFishThinnerDecision(BuildPlan plan) => plan.Decisions.Contains(FishCullDecision, StringComparer.Ordinal)
        ? plan
        : plan with { Decisions = [.. plan.Decisions, FishCullDecision] };

    private static BuildPlan AddFishThinnerWarning(BuildPlan plan, string detail)
    {
        string warning = $"WARNING [CGF-INSTANCE-003] Could not protect fish lights in {FishThinnerEvent}: {detail}";
        return plan.Decisions.Contains(warning, StringComparer.Ordinal)
            ? plan
            : plan with { Decisions = [.. plan.Decisions, warning] };
    }

    internal static BuildPlan Diagnose(BuildPlan plan, IReadOnlyList<ModPatch> patches, UndertaleData original, UndertaleData merged)
    {
        var lostPlacements = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var originalRooms = original.Rooms.ToDictionary(r => r.Name.Content, StringComparer.Ordinal);
        foreach (var room in merged.Rooms)
        {
            if (!originalRooms.TryGetValue(room.Name.Content, out var originalRoom)) continue;
            var remaining = room.GameObjects.Select(i => i.ObjectDefinition?.Name.Content).ToHashSet(StringComparer.Ordinal);
            foreach (string? name in originalRoom.GameObjects.Select(i => i.ObjectDefinition?.Name.Content).Distinct(StringComparer.Ordinal))
            {
                if (name is null || remaining.Contains(name)) continue;
                if (!lostPlacements.TryGetValue(name, out var rooms)) lostPlacements[name] = rooms = [];
                rooms.Add(room.Name.Content);
            }
        }
        if (lostPlacements.Count == 0) return plan;

        var objectNames = merged.GameObjects.Select(o => o.Name.Content).ToHashSet(StringComparer.Ordinal);
        List<string> decisions = [.. plan.Decisions];
        foreach (var asset in plan.Assets.Where(a => a.Family == "code" && a.Source >= 0 && a.Gml is not null))
        {
            var tokens = GmlReferences.Tokens(asset.Gml!);
            var accesses = new HashSet<(string Object, string Member)>();
            for (int i = 0; i + 2 < tokens.Count; i++)
            {
                var target = tokens[i];
                var member = tokens[i + 2];
                if (target.Kind != 'i' || tokens[i + 1] is not { Kind: 'p', Value: "." } || member.Kind != 'i'
                    || (i > 0 && tokens[i - 1] is { Kind: 'p', Value: "." })
                    || !objectNames.Contains(target.Value) || !lostPlacements.TryGetValue(target.Value, out var rooms)
                    || !accesses.Add((target.Value, member.Value))) continue;

                decisions.Add($"WARNING [CGF-INSTANCE-002] {asset.Name}, merged GML line {target.Line} (selected source {ModName(patches[asset.Source])}): "
                    + $"{target.Value}.{member.Value} may require a live instance. The merged room has no placed {target.Value} in {string.Join(", ", rooms)}. "
                    + "If the game crashes here, report this code and the active profile. Check the instance at runtime before reading its fields.");
            }
        }
        return plan with { Decisions = decisions };
    }

    private static string ModName(ModPatch patch) => patch.Mod.Name ?? patch.Mod.FileName;
}
