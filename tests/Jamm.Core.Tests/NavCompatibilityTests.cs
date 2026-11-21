using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Builds synthetic Maize patrols without using game files.</summary>
public sealed class NavCompatibilityTests
{
    [Fact]
    public void BuildRepairsAnIsolatedStartingNodeAndGuardsTheDeadEnd()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        string vanillaPath = temp.PathFor("vanilla.win"), mergedPath = temp.PathFor("merged.win");
        WriteFixture(basePath, isolated: false);
        WriteFixture(modPath, isolated: true);
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);

        var vanilla = ArchiveBuilder.Build(basePath, [], vanillaPath);
        Assert.Equal(File.ReadAllBytes(basePath), File.ReadAllBytes(vanillaPath));
        Assert.Empty(vanilla.Plan.Decisions);
        var build = ArchiveBuilder.Build(basePath, [new(PatchBuilder.Create(original, mod), modPath)], mergedPath);

        using var merged = GameFileLoader.Load(mergedPath);
        var node = Assert.Single(merged.Data.Rooms[0].GameObjects, i => i.ObjectDefinition?.Name.Content == "NavNode");
        Assert.Equal("gml_RoomCC_test", node.CreationCode?.Name.Content);
        string roomStart = PatchBuilder.ReadCode(merged, NavCompatibility.RoomStart).Text;
        Assert.Contains("jamm_nav_x[2] = 30;", roomStart);
        Assert.Contains("collision_line(", roomStart);
        Assert.Contains("target_node = jamm_best;", roomStart);
        string navigation = PatchBuilder.ReadCode(merged, NavCompatibility.Script).Text;
        Assert.Contains("var randomNode = nearestNode;", navigation);
        Assert.Contains("randomNode = previousNode;", navigation);
        Assert.DoesNotContain("randomNode = -1;", navigation);
        string patrol = PatchBuilder.ReadCode(merged, NavCompatibility.Patrol).Text;
        Assert.Contains("!instance_exists(target_node)", patrol);
        Assert.Contains("target_node = instance_nearest(x, y, ", patrol);
        Assert.Contains(build.Plan.Decisions, d => d.StartsWith("[CGF-NAV-001] Enabled room-start", StringComparison.Ordinal));
        Assert.Contains(build.Plan.Decisions, d => d.StartsWith("[CGF-NAV-001] Guarded Maize", StringComparison.Ordinal));
    }

    private static void WriteFixture(string path, bool isolated)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        var room = data.Rooms[0];
        room.Name = data.Strings.MakeString("NewWorld");
        var node = Object("NavNode");
        var brain = Object("NavBrain");
        var maize = Object("Maize");
        Object("NavEdge");
        Object("CaveRock");
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(node.EventHandlerFor(EventType.Create, 0u, data), "edges = ds_list_create();");
        compiler.QueueCodeReplace(brain.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.RoomStart, data), isolated
            ? "with (NavNode) { if (id == noone) ds_list_add(edges, 0); }"
            : "with (NavNode) { ds_list_add(edges, 0); }");
        compiler.QueueCodeReplace(maize.EventHandlerFor(EventType.Create, 0u, data),
            "target_node = instance_nearest(x, y, NavNode); previous_node = noone;");
        compiler.QueueCodeReplace(maize.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User0, data), """
            target_x = target_node.x;
            if (target_node == lure_node) target_node = previous_node;
            """);
        var script = UndertaleCode.CreateEmptyEntry(data, NavCompatibility.Script);
        data.Scripts.Add(new() { Name = data.Strings.MakeString("nav_get_random_connected_node"), Code = script });
        compiler.QueueCodeReplace(script, """
            var nearestNode = argument[0];
            var previousNode = argument[1];
            var availableConnections = ds_list_create();
            var i = 0;
            while (i < ds_list_size(nearestNode.edges))
            {
                var edge = ds_list_find_value(nearestNode.edges, i);
                if (edge.endNode != previousNode && edge.endNode != nearestNode) ds_list_add(availableConnections, edge.endNode);
                if (edge.startNode != previousNode && edge.startNode != nearestNode) ds_list_add(availableConnections, edge.startNode);
                i += 1;
            }
            var randomNode = -1;
            if (ds_list_size(availableConnections) > 0) randomNode = ds_list_find_value(availableConnections, 0);
            ds_list_destroy(availableConnections);
            return randomNode;
            """);
        var creation = UndertaleCode.CreateEmptyEntry(data, "gml_RoomCC_test");
        compiler.QueueCodeReplace(creation, "fixture_value = 3;");
        var compiled = compiler.Compile();
        Assert.True(compiled.Successful, compiled.PrintAllErrors(true));

        Place(node, 10, creation);
        if (!isolated)
        {
            Place(node, 20);
            Place(node, 30);
        }
        Place(brain, 5);
        Place(maize, 11);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);

        UndertaleGameObject Object(string name)
        {
            var obj = new UndertaleGameObject { Name = data.Strings.MakeString(name) };
            data.GameObjects.Add(obj);
            return obj;
        }

        void Place(UndertaleGameObject obj, int x, UndertaleCode? code = null) => room.GameObjects.Add(new()
        {
            ObjectDefinition = obj, X = x, InstanceID = ++data.GeneralInfo.LastObj, CreationCode = code
        });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("jamm-nav-test-");
        internal string PathFor(string name) => Path.Combine(directory.FullName, name);
        public void Dispose() => directory.Delete(recursive: true);
    }
}
