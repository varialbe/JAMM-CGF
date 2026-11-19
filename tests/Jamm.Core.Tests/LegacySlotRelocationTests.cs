using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Tests;

/// <summary>Legacy boat, lure, and tool slot relocation with portable synthetic code patches.</summary>
public sealed class LegacySlotRelocationTests
{
    private const string BoatCreate = "gml_Object_BoatData_Create_0";
    private const string BoatBaseCode = "object[0] = boat_starter;\nname[0] = \"Starter\";\nobject[7] = boat_old;\nname[7] = \"Old\";\n";

    private const string LureCreate = "gml_Object_LureData_Create_0";
    private const string LureBaseCode =
        "unlocked[4] = -1;\nunlocked[5] = -1;\n" +
        "cost[4] = 50000;\ncost[5] = 100000;\n" +
        "name[4] = \"Mega Hook\";\nitem[4] = BigHook2;\ndisp[4] = spr_mega;\ndesc[4] = \"Mega\";\n" +
        "name[5] = \"Trawler\";\nitem[5] = Net;\ndisp[5] = spr_net;\ndesc[5] = \"Net\";\n" +
        "nItems = array_length_1d(unlocked);\n";

    private const string ToolCreate = "gml_Object_ToolData_Create_0";
    private const string ToolBaseCode =
        "nItems = 3;\n" +
        "unlocked[0] = 0;\nunlocked[1] = 0;\nunlocked[2] = -1;\nunlocked[3] = -1;\nunlocked[4] = -1;\n" +
        "cost[0] = 5000;\ncost[1] = 125000;\ncost[2] = 1200000;\n" +
        "quests[0] = 10;\nquests[1] = 20;\nquests[2] = 30;\n" +
        "name[2] = \"Deep Sonar\";\nitem[2] = SonarPanelAdvanced;\nmessage[2] = Msg_Sonar;\ndesc[2] = \"Deep\";\n";

    [Fact]
    public void DistinctBoatClaimsRelocateTypedReferencesAndUnionTableInsertions()
    {
        var alpha = BoatPatch("Alpha", "Echo", "boat_echo", "BoatData.unlocked[8] = -1;\nboat_index = 8;\nlengthdir_x(8, 0);\n");
        var beta = BoatPatch("Beta", "Dark", "boat_dark", "BoatData.unlocked[8] = -1;\nboat_index = 8;\nlabel = \"boat_8\";\n",
            ("gml_Script_scr_boat_hook", "", "BoatData.unlocked[8] = 1;\nboat_was_unlocked(8);\n"));

        var plan = BuildPlan.Create([alpha, beta]);
        string create = plan.Assets.Single(a => a.Name == BoatCreate).Gml!;
        string step = plan.Assets.Single(a => a.Name == "gml_Object_BoatData_Step_0").Gml!;
        string hook = plan.Assets.Single(a => a.Name == "gml_Script_scr_boat_hook").Gml!;

        Assert.Contains("object[8] = boat_echo;", create);
        Assert.Contains("object[9] = boat_dark;", create);
        Assert.Contains("name[9] = \"Dark\";", create);
        Assert.Contains("BoatData.unlocked[9] = -1;", step);
        Assert.Contains("boat_index = 8;", step);
        Assert.Contains("boat_index = 9;", step);
        Assert.Contains("lengthdir_x(8, 0);", step);
        Assert.Contains("\"boat_8\"", step);
        Assert.Contains("BoatData.unlocked[9] = 1;", hook);
        Assert.Contains("boat_was_unlocked(9);", hook);
        Assert.Contains(plan.Decisions, d => d.Contains("[CGF-RELOCATE-001] Boat", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets, a => a.Gml?.Contains("<<<<<<<", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SameIdentityAndVanillaFieldEditDoNotRelocate()
    {
        var sameA = BoatPatch("A", "Shared", "boat_shared", "BoatData.unlocked[8] = -1;\n");
        var sameB = BoatPatch("B", "Shared", "boat_shared", "BoatData.unlocked[8] = -1;\n");
        Assert.DoesNotContain(BuildPlan.Create([sameA, sameB]).Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));

        var edit = BoatPatch("Edit", "Starter", "boat_starter", "BoatData.cost[0] = 123;\n");
        Assert.DoesNotContain(BuildPlan.Create([edit]).Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));
    }

    [Fact]
    public void UnclassifiedBoatTypedLiteralKeepsLegacyConflictAndAddsWarning()
    {
        var alpha = BoatPatch("Alpha", "Echo", "boat_echo", "BoatData.unlocked[8] = -1;\n");
        var beta = BoatPatch("Beta", "Dark", "boat_dark", "BoatData.unlocked[8] = -1;\nvar boat_variant = 8;\n");

        var plan = BuildPlan.Create([alpha, beta]);

        Assert.Contains(plan.Decisions, d => d.StartsWith("WARNING: boat slot 8", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets.Single(a => a.Name == BoatCreate).Gml!, "object[9] = boat_dark;");
        Assert.Contains(ConflictAnalyzer.Analyze([alpha, beta]).Findings, f => f.Severity == "WARNING" && f.Kind == "relocation" && f.Asset == "boat-slot");
    }

    [Fact]
    public void DistinctLureClaimsRelocateTypedReferencesAndUnionTableInsertions()
    {
        var alpha = LurePatch("Alpha", "Lantern", "LanternObj", "unlocked[6] = 1;\n");
        var beta = LurePatch("Beta", "Giga Hook", "BigHook2", "unlocked[6] = -1;\n",
            ("gml_Object_Girl_Other_10", "gear_lure_scalar[5] = 1;\n", "gear_lure_scalar[5] = 1;\ngear_lure_scalar[6] = 1;\n"),
            ("gml_Script_scr_lure_hook", "", "LureData.unlocked[6] = 1;\nbuy_lure(6);\nlure_was_purchased(6);\nlure_index = 6;\nactive_lure = 6;\nlengthdir_x(6, 0);\nlabel = \"lure_6\";\n"));

        var plan = BuildPlan.Create([alpha, beta]);
        string create = plan.Assets.Single(a => a.Name == LureCreate).Gml!;
        string step = plan.Assets.Single(a => a.Name == "gml_Object_LureData_Step_0").Gml!;
        string girl = plan.Assets.Single(a => a.Name == "gml_Object_Girl_Other_10").Gml!;
        string hook = plan.Assets.Single(a => a.Name == "gml_Script_scr_lure_hook").Gml!;

        Assert.Contains("name[6] = \"Lantern\";", create);
        Assert.Contains("name[7] = \"Giga Hook\";", create);
        Assert.Contains("item[7] = BigHook2;", create);
        Assert.Contains("unlocked[7] = -1;", create);
        Assert.Contains("unlocked[7] = -1;", step);
        Assert.Contains("gear_lure_scalar[7] = 1;", girl);
        Assert.Contains("LureData.unlocked[7] = 1;", hook);
        Assert.Contains("buy_lure(7);", hook);
        Assert.Contains("lure_was_purchased(7);", hook);
        Assert.Contains("lure_index = 7;", hook);
        Assert.Contains("active_lure = 7;", hook);
        Assert.Contains("lengthdir_x(6, 0);", hook);
        Assert.Contains("\"lure_6\"", hook);
        Assert.Contains(plan.Decisions, d => d.StartsWith("[CGF-RELOCATE-001] Lure \"Giga Hook\" (Beta) moved from slot 6 to 7;", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets, a => a.Gml?.Contains("<<<<<<<", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SameLureIdentityAndUnclassifiedLureUseBehaveSafely()
    {
        var sameA = LurePatch("A", "Lantern", "LanternObj", "unlocked[6] = 1;\n");
        var sameB = LurePatch("B", "Lantern", "LanternObj", "unlocked[6] = 1;\n");
        Assert.DoesNotContain(BuildPlan.Create([sameA, sameB]).Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));

        var alpha = LurePatch("Alpha", "Lantern", "LanternObj", "unlocked[6] = 1;\n");
        var beta = LurePatch("Beta", "Giga Hook", "BigHook2", "unlocked[6] = 1;\nvar lure_variant = 6;\n");
        var plan = BuildPlan.Create([alpha, beta]);

        Assert.Contains(plan.Decisions, d => d.StartsWith("WARNING: lure slot 6", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets.Single(a => a.Name == LureCreate).Gml!, "name[7] = \"Giga Hook\";");
        Assert.Contains(ConflictAnalyzer.Analyze([alpha, beta]).Findings, f => f.Severity == "WARNING" && f.Kind == "relocation" && f.Asset == "lure-slot");
    }

    [Fact]
    public void DistinctToolClaimsRelocateTypedReferencesUpdateCountAndPreservePlaceholders()
    {
        var alpha = ToolPatch("Alpha", "Scanner", "obj_scanner", "0", "unlocked[3] = 1;\n");
        var beta = ToolPatch("Beta", "Dredger", "obj_dredger", "1", "unlocked[3] = 1;\n",
            ("gml_Object_ToolCard_Other_10", "if (item_id == 0) { x = 1; }\n", "if (item_id == 0) { x = 1; }\nif (item_id == 3) { x = 2; }\n"),
            ("gml_Script_scr_tool_hook", "", "ToolData.unlocked[3] = 1;\ntool_was_purchased(3);\ntool_index = 3;\nactive_tool = 3;\nlengthdir_x(3, 0);\nlabel = \"tool_3\";\n"));

        var plan = BuildPlan.Create([alpha, beta]);
        string create = plan.Assets.Single(a => a.Name == ToolCreate).Gml!;
        string step = plan.Assets.Single(a => a.Name == "gml_Object_ToolData_Step_0").Gml!;
        string card = plan.Assets.Single(a => a.Name == "gml_Object_ToolCard_Other_10").Gml!;
        string hook = plan.Assets.Single(a => a.Name == "gml_Script_scr_tool_hook").Gml!;

        Assert.Contains("nItems = 5;", create);
        Assert.Contains("unlocked[3] = 0;", create);
        Assert.Contains("unlocked[4] = 1;", create);
        Assert.DoesNotContain("unlocked[4] = -1;", create);
        Assert.Contains("name[3] = \"Scanner\";", create);
        Assert.Contains("name[4] = \"Dredger\";", create);
        Assert.Contains("item[4] = obj_dredger;", create);
        Assert.Contains("unlocked[4] = 1;", step);
        Assert.Contains("if (item_id == 4) { x = 2; }", card);
        Assert.Contains("ToolData.unlocked[4] = 1;", hook);
        Assert.Contains("tool_was_purchased(4);", hook);
        Assert.Contains("tool_index = 4;", hook);
        Assert.Contains("active_tool = 4;", hook);
        Assert.Contains("lengthdir_x(3, 0);", hook);
        Assert.Contains("\"tool_3\"", hook);
        Assert.Contains(plan.Decisions, d => d.StartsWith("[CGF-RELOCATE-001] Tool \"Dredger\" (Beta) moved from slot 3 to 4;", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets, a => a.Gml?.Contains("<<<<<<<", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SameToolIdentityAndUnclassifiedToolUseBehaveSafely()
    {
        var sameA = ToolPatch("A", "Scanner", "obj_scanner", "0", "unlocked[3] = 1;\n");
        var sameB = ToolPatch("B", "Scanner", "obj_scanner", "0", "unlocked[3] = 1;\n");
        Assert.DoesNotContain(BuildPlan.Create([sameA, sameB]).Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));

        var alpha = ToolPatch("Alpha", "Scanner", "obj_scanner", "0", "unlocked[3] = 1;\n");
        var beta = ToolPatch("Beta", "Dredger", "obj_dredger", "1", "unlocked[3] = 1;\nvar tool_variant = 3;\n");
        var plan = BuildPlan.Create([alpha, beta]);

        Assert.Contains(plan.Decisions, d => d.StartsWith("WARNING: tool slot 3", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Decisions, d => d.Contains("[CGF-RELOCATE-001]", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets.Single(a => a.Name == ToolCreate).Gml!, "name[4] = \"Dredger\";");
        Assert.Contains(ConflictAnalyzer.Analyze([alpha, beta]).Findings, f => f.Severity == "WARNING" && f.Kind == "relocation" && f.Asset == "tool-slot");
    }

    private static ModPatch BoatPatch(string name, string boatName, string boatObject, string stepTail,
        params (string Name, string BaseText, string ModText)[] extras)
    {
        string create = BoatBaseCode + $"object[8] = {boatObject};\nname[8] = \"{boatName}\";\ncost[8] = 1000000;\n";
        return BuildCodePatch(name, BoatCreate, BoatBaseCode, create,
            "gml_Object_BoatData_Step_0", "BoatData.unlocked[7] = -1;\n", stepTail, extras);
    }

    private static ModPatch LurePatch(string name, string lureName, string lureItem, string stepTail,
        params (string Name, string BaseText, string ModText)[] extras)
    {
        string create =
            "unlocked[4] = -1;\nunlocked[5] = -1;\nunlocked[6] = -1;\n" +
            "cost[4] = 50000;\ncost[5] = 100000;\ncost[6] = 125000;\n" +
            "name[4] = \"Mega Hook\";\nitem[4] = BigHook2;\ndisp[4] = spr_mega;\ndesc[4] = \"Mega\";\n" +
            "name[5] = \"Trawler\";\nitem[5] = Net;\ndisp[5] = spr_net;\ndesc[5] = \"Net\";\n" +
            $"name[6] = \"{lureName}\";\nitem[6] = {lureItem};\ndisp[6] = spr_custom;\ndesc[6] = \"{lureName}\";\n" +
            "nItems = array_length_1d(unlocked);\n";
        return BuildCodePatch(name, LureCreate, LureBaseCode, create,
            "gml_Object_LureData_Step_0", "unlocked[5] = 1;\n", stepTail, extras);
    }

    private static ModPatch ToolPatch(string name, string toolName, string toolItem, string unlockedValue, string stepTail,
        params (string Name, string BaseText, string ModText)[] extras)
    {
        string create =
            "nItems = 4;\n" +
            $"unlocked[0] = 0;\nunlocked[1] = 0;\nunlocked[2] = -1;\nunlocked[3] = {unlockedValue};\nunlocked[4] = -1;\n" +
            "cost[0] = 5000;\ncost[1] = 125000;\ncost[2] = 1200000;\ncost[3] = 50000;\n" +
            "quests[0] = 10;\nquests[1] = 20;\nquests[2] = 30;\nquests[3] = 35;\n" +
            "name[2] = \"Deep Sonar\";\nitem[2] = SonarPanelAdvanced;\nmessage[2] = Msg_Sonar;\ndesc[2] = \"Deep\";\n" +
            $"name[3] = \"{toolName}\";\nitem[3] = {toolItem};\nmessage[3] = Msg_{name};\ndesc[3] = \"{toolName}\";\n";
        return BuildCodePatch(name, ToolCreate, ToolBaseCode, create,
            "gml_Object_ToolData_Step_0", "unlocked[2] = 0;\n", stepTail, extras);
    }

    private static ModPatch BuildCodePatch(
        string modName,
        string createName, string createBase, string createMod,
        string stepName, string stepBase, string stepTail,
        (string Name, string BaseText, string ModText)[] extras)
    {
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
        foreach (string family in new[] { "code", "scripts", "objects", "rooms", "sprites", "texturePages", "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions" })
            changes[family] = new([], [], []);
        var snapshot = JsonSerializer.SerializeToElement(new { });
        List<AssetPatch> added = [];
        List<AssetPatch> changed =
        [
            new(createName, snapshot, snapshot, [], new(createBase, "gml", 0, 0, null, []), new(createMod, "gml", 0, 0, null, [])),
            new(stepName, snapshot, snapshot, [], new(stepBase, "gml", 0, 0, null, []), new(stepBase + stepTail, "gml", 0, 0, null, []))
        ];
        foreach (var (extraName, extraBase, extraMod) in extras)
        {
            if (string.IsNullOrEmpty(extraBase))
                added.Add(new(extraName, null, snapshot, [], null, new(extraMod, "gml", 0, 0, null, [])));
            else
                changed.Add(new(extraName, snapshot, snapshot, [], new(extraBase, "gml", 0, 0, null, []), new(extraMod, "gml", 0, 0, null, [])));
        }
        changes["code"] = new(added.ToArray(), changed.ToArray(), []);
        return new(2, "test", DateTimeOffset.UnixEpoch,
            new(modName + ".win", modName, 1, "1.4", 16, modName), new("base.win", "base", 1, "1.4", 16), [], changes, new(), []);
    }
}
