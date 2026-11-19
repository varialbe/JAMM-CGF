using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Tests;

/// <summary>Legacy rod slot, upgrade row, and consumer relocation using synthetic patches.</summary>
public sealed class LegacyRodRelocationTests
{
    private const string RodCreate = LegacyRodRelocation.RodCreate;
    private const string UpgradeCreate = LegacyRodRelocation.UpgradeCreate;
    private const string Load = "gml_Object_SaveManager_Other_13";
    private const string Save = "gml_Object_SaveManager_Other_14";
    private const string AutoSave = "gml_Object_SaveManager_Other_15";
    private const string Rocket = "gml_Object_Rocket_Create_0";
    private const string Quest = "gml_Script_start_quest_dist";
    private const string RodCard = "gml_Object_RodCard_Other_10";
    private const string Hook = "gml_Script_rod_hook";

    private const string BaseRod = """
        nItems = 6;
        unlocked[5] = -1;
        unlocked[6] = -2;
        unlocked[7] = -2;
        name[5] = "Old Rod";
        cost[5] = 300;
        """;

    private const string MatrixLoop = """
        for (ix = 0; ix < 6; ix++)
        {
            for (iy = 0; iy < 8; iy++)
            {
                object[ix, iy] = -1;
            }
        }
        """;

    [Fact]
    public void DistinctRodsKeepBothEntriesAndRaiseAllConsumerBounds()
    {
        string baseUpgrade = "cost_base[5] = RodData.cost[5];\nupg[5, 7] = -1;\n" + MatrixLoop;
        string firstRow = string.Concat(Enumerable.Range(0, 9).Select(i => $"upg[6, {i}] = -1;\n"));
        string firstUpgrade = baseUpgrade.Replace(MatrixLoop,
            firstRow + MatrixLoop.Replace("ix < 6", "ix < 7").Replace("iy < 8", "iy < 9"));
        var first = RodPatch("First", "Prototype", (UpgradeCreate, baseUpgrade, firstUpgrade));
        var later = RodPatch("Later", "Mystic Rod",
            (RodCard, "if (item_id == 6) { y = 1; }\n", "if (item_id == 6) { y = 1; }\nrod_id = 6;\nitem_id = 6;\n"),
            (Hook, "", "with (RodData) { unlocked[6] = 1; }\nRodData.cost[6] += 1;\nrod_was_unlocked(6);\nrod_was_purchased(6);\nlengthdir_x(6, 0);\nproduct = 6;\n"));
        var baseCode = new Dictionary<string, CodeText>
        {
            [Load] = Code(MatrixLoop),
            [Save] = Code(MatrixLoop),
            [AutoSave] = Code(MatrixLoop.Replace("ix < 6", "ix <= 5").Replace("iy < 8", "iy <= 7")),
            [Rocket] = Code("target_distance[5] = 100;\n"),
            [Quest] = Code("dist[5] = 10;\ndpth[5] = 9;\n")
        };

        var plan = BuildPlan.Create([first, later], null, null, baseCode);
        string rods = Gml(plan, RodCreate);
        string upgrades = Gml(plan, UpgradeCreate);
        Assert.Contains("name[6] = \"Prototype\";", rods);
        Assert.Contains("name[7] = \"Mystic Rod\";", rods);
        Assert.Contains("nItems = 8;", rods);
        Assert.Contains("unlocked[7] = 0;", rods);
        Assert.Contains("unlocked[6] = -2;", rods);
        Assert.Contains(firstRow, upgrades);
        for (int i = 0; i < 9; i++)
            Assert.Contains($"upg[7, {i}] = -1;", upgrades);
        Assert.Contains("cost_base[7] = RodData.cost[7];", upgrades);
        foreach (string entry in new[] { UpgradeCreate, Load, Save })
        {
            Assert.Contains("ix < 8", Gml(plan, entry));
            Assert.Contains("iy < 9", Gml(plan, entry));
        }
        Assert.Contains("ix <= 7", Gml(plan, AutoSave));
        Assert.Contains("iy <= 8", Gml(plan, AutoSave));
        Assert.Contains("target_distance[7] = target_distance[5];", Gml(plan, Rocket));
        Assert.Contains("dist[7] = dist[5];", Gml(plan, Quest));
        Assert.Contains("dpth[7] = dpth[5];", Gml(plan, Quest));
        Assert.Contains("if (item_id == 6)", Gml(plan, RodCard));
        Assert.Contains("item_id = 7;\n", Gml(plan, RodCard));
        Assert.Contains("rod_id = 7;\n", Gml(plan, RodCard));
        string hook = Gml(plan, Hook);
        Assert.Contains("with (RodData) { unlocked[7] = 1; }", hook);
        Assert.Contains("RodData.cost[7] += 1;", hook);
        Assert.Contains("rod_was_unlocked(7);", hook);
        Assert.Contains("rod_was_purchased(7);", hook);
        Assert.Contains("lengthdir_x(6, 0);", hook);
        Assert.Contains("product = 6;", hook);
        Assert.Equal(new[] { new RodFrame(0, 6, 6), new RodFrame(1, 6, 7) }, plan.RodFrames);
        Assert.Contains("[CGF-RELOCATE-006] Rod \"Mystic Rod\" (Later) moved from slot 6 to 7; rewrote 9 typed references; spr_rod frame 6->7", plan.Decisions);
    }

    [Fact]
    public void ExplicitUpgradeRowsMoveWithTheirRod()
    {
        const string baseUpgrade = "upg[5, 0] = -1;\n";
        var first = RodPatch("First", "Prototype", (UpgradeCreate, baseUpgrade, baseUpgrade + "upg[6, 0] = 1;\n"));
        var later = RodPatch("Later", "Mystic Rod", (UpgradeCreate, baseUpgrade,
            baseUpgrade + "upg[6, 0] = 2;\nobject[6, 0] = obj_custom;\n"));
        string upgrade = Gml(BuildPlan.Create([first, later]), UpgradeCreate);
        Assert.Contains("upg[6, 0] = 1;", upgrade);
        Assert.Contains("upg[7, 0] = 2;", upgrade);
        Assert.Contains("object[7, 0] = obj_custom;", upgrade);
    }

    [Fact]
    public void SameIdentityIsNotRelocated()
    {
        var plan = BuildPlan.Create([RodPatch("First", "Shared"), RodPatch("Later", "Shared")]);
        Assert.Empty(plan.RodFrames);
        Assert.DoesNotContain(plan.Decisions, d => d.Contains("CGF-RELOCATE-006"));
    }

    [Fact]
    public void UnclassifiedRodLiteralCancelsRelocation()
    {
        var first = RodPatch("First", "Prototype");
        var later = RodPatch("Later", "Mystic Rod", (Hook, "", "set_rod_variant(\n    6\n);\n"));
        var plan = BuildPlan.Create([first, later]);
        Assert.Empty(plan.RodFrames);
        Assert.Contains(plan.Decisions, d => d.StartsWith("WARNING: rod slot 6", StringComparison.Ordinal));
        Assert.DoesNotContain("name[7]", Gml(plan, RodCreate));
        Assert.Contains(ConflictAnalyzer.Analyze([first, later]).Findings,
            f => f.Severity == "WARNING" && f.Asset == "rod-slot");
    }

    private static string Gml(BuildPlan plan, string entry) => plan.Assets.Single(a => a.Name == entry).Gml!;

    private static CodeText Code(string text) => new(text, "gml", 0, 0, null, []);

    private static ModPatch RodPatch(string mod, string rod, params (string Entry, string Before, string After)[] extras)
    {
        string after = BaseRod.Replace("nItems = 6", "nItems = 7")
            .Replace("unlocked[6] = -2", mod == "Later" ? "unlocked[6] = 0" : "unlocked[6] = -2")
            + $"\nname[6] = \"{rod}\";\ncost[6] = 400;\n";
        var snapshot = JsonSerializer.SerializeToElement(new { });
        var code = extras.Prepend((Entry: RodCreate, Before: BaseRod, After: after))
            .Select(e => new AssetPatch(e.Entry, e.Before.Length == 0 ? null : snapshot, snapshot, [],
                e.Before.Length == 0 ? null : Code(e.Before), Code(e.After))).ToArray();
        var baseSprite = JsonSerializer.SerializeToElement(new { width = 10, height = 5, frameCount = 6 });
        var modSprite = JsonSerializer.SerializeToElement(new { width = 10, height = 5, frameCount = 7, mod });
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal)
        {
            ["code"] = new(code.Where(a => a.BaseCode is null).ToArray(), code.Where(a => a.BaseCode is not null).ToArray(), []),
            ["sprites"] = new([], [new("spr_rod", baseSprite, modSprite, [])], [])
        };
        return new(2, "test", DateTimeOffset.UnixEpoch, new(mod + ".win", mod, 1, "1.4", 16, mod),
            new("base.win", "base", 1, "1.4", 16), [], changes);
    }
}
