using System.Globalization;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

internal static class NavCompatibility
{
    internal const string Script = "gml_Script_nav_get_random_connected_node";
    internal const string Patrol = "gml_Object_Maize_Other_10";
    internal const string RoomStart = "gml_Object_NavBrain_Other_4";
    private const string Diagnostic = "[CGF-NAV-001]";

    internal static BuildPlan GuardPatrol(BuildPlan plan, IReadOnlyDictionary<string, CodeText> baseline)
    {
        string? script = Code(Script);
        string? patrol = Code(Patrol);
        if (script is null || patrol is null) return plan;

        string? guardedScript = ReplaceOnce(script, "var availableConnections = ds_list_create();", """
            if (nearestNode < 0 || !instance_exists(nearestNode))
            {
                if (previousNode >= 0 && instance_exists(previousNode)) return previousNode;
                return instance_nearest(x, y, NavNode);
            }
            var availableConnections = ds_list_create();
            """);
        guardedScript = ReplaceOnce(guardedScript, "var randomNode = -1;", """
            var randomNode = nearestNode;
            if (previousNode >= 0 && instance_exists(previousNode)) randomNode = previousNode;
            """);
        guardedScript = ReplaceOnce(guardedScript, "var edge = ds_list_find_value(nearestNode.edges, i);", """
            var edge = ds_list_find_value(nearestNode.edges, i);
                if (edge < 0 || !instance_exists(edge)) continue;
            """);
        foreach (string end in new[] { "endNode", "startNode" })
        {
            string check = $"edge.{end} != previousNode && edge.{end} != nearestNode";
            guardedScript = ReplaceOnce(guardedScript, check,
                $"{check} && edge.{end} >= 0 && instance_exists(edge.{end})");
        }

        string? guardedPatrol = ReplaceOnce(patrol, "target_x = target_node.x;", """
            if (target_node < 0 || !instance_exists(target_node))
            {
                target_node = instance_nearest(x, y, NavNode);
                if (target_node < 0 || !instance_exists(target_node)) exit;
            }
            target_x = target_node.x;
            """);
        guardedPatrol = ReplaceOnce(guardedPatrol, "if (target_node == lure_node)",
            "if (target_node == lure_node && previous_node >= 0 && instance_exists(previous_node))");
        if (guardedScript is null || guardedPatrol is null)
            return plan with { Decisions = [.. plan.Decisions,
                $"WARNING {Diagnostic} Could not add the Maize patrol guard because the selected navigation code has changed. Review {Script} and {Patrol} before rebuilding."] };

        List<PlannedAsset> assets = [.. plan.Assets];
        SetCode(Script, guardedScript);
        SetCode(Patrol, guardedPatrol);
        return plan with { Assets = assets, Decisions = [.. plan.Decisions,
            $"{Diagnostic} Guarded Maize patrol against dead ends and unavailable navigation instances."] };

        string? Code(string name)
        {
            var selected = plan.Assets.FirstOrDefault(a => a.Family == "code" && a.Name == name);
            return selected is null ? baseline.GetValueOrDefault(name)?.Text : selected.Gml;
        }

        void SetCode(string name, string text)
        {
            int index = assets.FindIndex(a => a.Family == "code" && a.Name == name);
            if (index >= 0)
            {
                assets[index] = assets[index] with { Gml = text };
                return;
            }
            var before = baseline[name];
            var snapshot = AssetFields.Json(new { });
            assets.Add(new("code", name, 0, new AssetPatch(name, snapshot, snapshot, [], before, before with { Text = text }),
                snapshot, text));
        }
    }

    internal static void RepairRoom(UndertaleData data, UndertaleData original, IReadOnlyDictionary<string, CodeText> baseline,
        Dictionary<string, string> recompile, Dictionary<string, string> generated, List<string> decisions)
    {
        const string room = "NewWorld";
        var before = original.Rooms.ByName(room);
        var merged = data.Rooms.ByName(room);
        if (before is null || merged is null || !merged.GameObjects.Any(i => i.ObjectDefinition?.Name.Content == "Maize"))
            return;
        var anchors = before.GameObjects.Where(i => i.ObjectDefinition?.Name.Content == "NavNode")
            .Select(i => (i.X, i.Y)).Distinct().ToArray();
        var brain = merged.GameObjects.Where(i => i.ObjectDefinition?.Name.Content == "NavBrain").ToArray();
        if (anchors.Length < 3 || brain.Length != 1
            || data.GameObjects.ByName("CaveRock") is null || data.GameObjects.ByName("NavEdge") is null)
            return;

        string? body = recompile.GetValueOrDefault(RoomStart) ?? baseline.GetValueOrDefault(RoomStart)?.Text;
        bool hasRoomStart = brain[0].ObjectDefinition!.Events[(int)EventType.Other].Any(e =>
            e.EventSubtype == (uint)EventSubtypeOther.RoomStart && e.Actions.Any(a => a.CodeId?.Name.Content == RoomStart));
        if (!hasRoomStart || body is null || !body.Contains("ds_list_add(edges,", StringComparison.Ordinal))
        {
            decisions.Add($"WARNING {Diagnostic} Could not repair the navigation graph in {room} because the selected NavBrain room-start code has changed.");
            return;
        }

        // The native Room Start event runs after all placed Create and instance creation code.
        // Reuse its collision rules against the final room, where combined terrain can cover a
        // mod's nodes even though that mod's original archive had a connected route.
        string coordinates = string.Join('\n', anchors.Select((p, index) => FormattableString.Invariant(
            $"    jamm_nav_x[{index}] = {p.X}; jamm_nav_y[{index}] = {p.Y};")));
        string repair = GraphRepair.Replace("__ANCHORS__", coordinates, StringComparison.Ordinal)
            .Replace("__COUNT__", anchors.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        string code = body + "\n" + repair;
        recompile[RoomStart] = generated[RoomStart] = code;
        decisions.Add($"{Diagnostic} Enabled room-start patrol recovery in {room}. If merged terrain strands Maize, use a connected route and recover clear nodes from the original room when needed.");
    }

    private const string GraphRepair = """
        if (room != NewWorld) exit;
        var jamm_nav_stranded = ds_list_create();
        with (Maize)
        {
            if (target_node >= 0 && instance_exists(target_node))
            {
                if (ds_list_size(target_node.edges) != 0) continue;
            }
            ds_list_add(jamm_nav_stranded, id);
        }
        if (ds_list_size(jamm_nav_stranded) == 0)
        {
            ds_list_destroy(jamm_nav_stranded);
            exit;
        }

        var jamm_nav_route = ds_list_create();
        // The native graph records up to two edges per pair. Three edges guarantee a choice of peers.
        with (NavNode)
            if (ds_list_size(edges) >= 3) ds_list_add(jamm_nav_route, id);

        if (ds_list_size(jamm_nav_route) == 0)
        {
            var jamm_nav_x, jamm_nav_y;
        __ANCHORS__
            var jamm_nav_recovered = ds_list_create();
            for (var jamm_i = 0; jamm_i < __COUNT__; jamm_i += 1)
            {
                var jamm_x = jamm_nav_x[jamm_i];
                var jamm_y = jamm_nav_y[jamm_i];
                if (collision_point(jamm_x, jamm_y, CaveRock, true, false)) continue;
                var jamm_at = instance_nearest(jamm_x, jamm_y, NavNode);
                if (jamm_at < 0)
                    jamm_at = instance_create(jamm_x, jamm_y, NavNode);
                else if (jamm_at.x != jamm_x || jamm_at.y != jamm_y)
                    jamm_at = instance_create(jamm_x, jamm_y, NavNode);
                else if (ds_list_size(jamm_at.edges) != 0)
                    continue;
                for (var jamm_j = 0; jamm_j < ds_list_size(jamm_nav_recovered); jamm_j += 1)
                {
                    var jamm_peer = ds_list_find_value(jamm_nav_recovered, jamm_j);
                    if (collision_line(jamm_x, jamm_y, jamm_peer.x, jamm_peer.y, CaveRock, true, false)) continue;
                    var jamm_edge = instance_create(jamm_x, jamm_y, NavEdge);
                    jamm_edge.startNode = jamm_at;
                    jamm_edge.endNode = jamm_peer;
                    ds_list_add(jamm_at.edges, jamm_edge);
                    ds_list_add(jamm_peer.edges, jamm_edge);
                }
                ds_list_add(jamm_nav_recovered, jamm_at);
            }
            for (var jamm_i = 0; jamm_i < ds_list_size(jamm_nav_recovered); jamm_i += 1)
            {
                var jamm_at = ds_list_find_value(jamm_nav_recovered, jamm_i);
                if (ds_list_size(jamm_at.edges) >= 2) ds_list_add(jamm_nav_route, jamm_at);
            }
            ds_list_destroy(jamm_nav_recovered);
        }

        for (var jamm_i = 0; jamm_i < ds_list_size(jamm_nav_stranded); jamm_i += 1)
        {
            var jamm_fish = ds_list_find_value(jamm_nav_stranded, jamm_i);
            with (jamm_fish)
            {
                var jamm_best = noone;
                var jamm_distance = 0;
                for (var jamm_j = 0; jamm_j < ds_list_size(jamm_nav_route); jamm_j += 1)
                {
                    var jamm_at = ds_list_find_value(jamm_nav_route, jamm_j);
                    var jamm_d = point_distance(x, y, jamm_at.x, jamm_at.y);
                    if (jamm_best < 0 || jamm_d < jamm_distance)
                    {
                        jamm_best = jamm_at;
                        jamm_distance = jamm_d;
                    }
                }
                if (jamm_best >= 0)
                {
                    target_node = jamm_best;
                    previous_node = noone;
                    if (collision_line(x, y, jamm_best.x, jamm_best.y, CaveRock, true, false))
                    {
                        // This placement cannot reach the recovered patrol through the merged terrain.
                        x = jamm_best.x;
                        y = jamm_best.y;
                    }
                }
            }
        }
        ds_list_destroy(jamm_nav_route);
        ds_list_destroy(jamm_nav_stranded);
        """;

    private static string? ReplaceOnce(string? gml, string anchor, string replacement)
    {
        if (gml is null) return null;
        int index = gml.IndexOf(anchor, StringComparison.Ordinal);
        return index >= 0 && gml.IndexOf(anchor, index + anchor.Length, StringComparison.Ordinal) < 0
            ? gml[..index] + replacement + gml[(index + anchor.Length)..]
            : null;
    }
}
