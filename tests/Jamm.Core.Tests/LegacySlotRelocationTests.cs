using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Tests;

/// <summary>Legacy boat slot relocation with portable synthetic code patches.</summary>
public sealed class LegacySlotRelocationTests
{
    private const string Create = "gml_Object_BoatData_Create_0";
    private const string BaseCode = "object[0] = boat_starter;\nname[0] = \"Starter\";\nobject[7] = boat_old;\nname[7] = \"Old\";\n";

    [Fact]
    public void DistinctBoatClaimsRelocateTypedReferencesAndUnionTableInsertions()
    {
        var alpha = Patch("Alpha", "Echo", "boat_echo", "BoatData.unlocked[8] = -1;\nboat_index = 8;\nlengthdir_x(8, 0);\n");
        var beta = Patch("Beta", "Dark", "boat_dark", "BoatData.unlocked[8] = -1;\nboat_index = 8;\nlabel = \"boat_8\";\n");

        var plan = BuildPlan.Create([alpha, beta]);
        string create = plan.Assets.Single(a => a.Name == Create).Gml!;
        string step = plan.Assets.Single(a => a.Name == "gml_Object_BoatData_Step_0").Gml!;

        Assert.Contains("object[8] = boat_echo;", create);
        Assert.Contains("object[9] = boat_dark;", create);
        Assert.Contains("name[9] = \"Dark\";", create);
        Assert.Contains("BoatData.unlocked[9] = -1;", step);
        Assert.Contains("boat_index = 8;", step);
        Assert.Contains("boat_index = 9;", step);
        Assert.Contains("lengthdir_x(8, 0);", step);
        Assert.Contains("\"boat_8\"", step);
        Assert.Contains(plan.Decisions, d => d.Contains("[CGF-RELOCATE-001] Boat", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets, a => a.Gml?.Contains("<<<<<<<", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SameIdentityAndVanillaFieldEditDoNotRelocate()
    {
        var sameA = Patch("A", "Shared", "boat_shared", "BoatData.unlocked[8] = -1;\n");
        var sameB = Patch("B", "Shared", "boat_shared", "BoatData.unlocked[8] = -1;\n");
        Assert.DoesNotContain(BuildPlan.Create([sameA, sameB]).Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));

        var edit = Patch("Edit", "Starter", "boat_starter", "BoatData.cost[0] = 123;\n");
        Assert.DoesNotContain(BuildPlan.Create([edit]).Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));
    }

    [Fact]
    public void UnclassifiedBoatTypedLiteralKeepsLegacyConflictAndAddsWarning()
    {
        var alpha = Patch("Alpha", "Echo", "boat_echo", "BoatData.unlocked[8] = -1;\n");
        var beta = Patch("Beta", "Dark", "boat_dark", "BoatData.unlocked[8] = -1;\nvar boat_variant = 8;\n");

        var plan = BuildPlan.Create([alpha, beta]);

        Assert.Contains(plan.Decisions, d => d.StartsWith("WARNING: boat slot 8", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets.Single(a => a.Name == Create).Gml!, "object[9] = boat_dark;");
        Assert.Contains(ConflictAnalyzer.Analyze([alpha, beta]).Findings, f => f.Severity == "WARNING" && f.Kind == "relocation");
    }

    private static ModPatch Patch(string name, string boatName, string boatObject, string stepTail)
    {
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
        foreach (string family in new[] { "code", "scripts", "objects", "rooms", "sprites", "texturePages", "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions" })
            changes[family] = new([], [], []);
        var snapshot = JsonSerializer.SerializeToElement(new { });
        string create = BaseCode + $"object[8] = {boatObject};\nname[8] = \"{boatName}\";\ncost[8] = 1000000;\n";
        var createAsset = new AssetPatch(Create, snapshot, snapshot, [], new(BaseCode, "gml", 0, 0, null, []), new(create, "gml", 0, 0, null, []));
        string stepBase = "BoatData.unlocked[7] = -1;\n";
        var stepAsset = new AssetPatch("gml_Object_BoatData_Step_0", snapshot, snapshot, [], new(stepBase, "gml", 0, 0, null, []), new(stepBase + stepTail, "gml", 0, 0, null, []));
        changes["code"] = new([], [createAsset, stepAsset], []);
        return new(2, "test", DateTimeOffset.UnixEpoch,
            new(name + ".win", name, 1, "1.4", 16, name), new("base.win", "base", 1, "1.4", 16), [], changes, new(), []);
    }
}
