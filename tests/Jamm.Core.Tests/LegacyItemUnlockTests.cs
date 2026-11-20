using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Tests;

/// <summary>Exercises legacy unlock evidence and opt-in repairs without game files.</summary>
public sealed class LegacyItemUnlockTests
{
    private const string BoatCreate = "gml_Object_BoatData_Create_0";
    private const string RodCreate = "gml_Object_RodData_Create_0";
    private const string BaseBoat = """
        object[0] = BoatBase;
        name[0] = "Starter";
        cost[0] = 100;
        unlocked[0] = 1;
        nItems = 1;
        """;
    private const string BaseRod = """
        name[0] = "Starter";
        cost[0] = 100;
        unlocked[0] = 1;
        nItems = 1;
        """;
    private const string ExtraBoat = """
        object[1] = BoatExtra;
        name[1] = "Skiff";
        nItems = 2;
        """;
    private const string ExtraRod = """
        name[1] = "Reef Rod";
        unlocked[1] = -2;
        cost[1] = 500;
        nItems = 2;
        """;

    [Theory]
    [InlineData("", "WARNING", "[CGF-UNLOCK-001]")]
    [InlineData("unlocked[1] = 0; cost[1] = 250;", "INFO", "$250")]
    [InlineData("unlocked[1] = true;", "INFO", "Starts owned")]
    public void BoatCheckRequiresUsableShopStateOrOwnership(string shopRow, string severity, string message)
    {
        var patch = Patch(Change(BoatCreate, BaseBoat, ExtraBoat + "\n" + shopRow));
        var report = ConflictAnalyzer.Analyze([patch], allowSingle: true);
        var item = Assert.Single(report.Findings, f => f.Kind == "unlock");
        Assert.Equal(severity, item.Severity);
        Assert.Contains(message, item.Message);
        Assert.Contains(BoatCreate, item.Sources!);
    }

    [Fact]
    public void ReportsQuestGrantAndIndependentSaveFlagAsEvidence()
    {
        var rod = Change(RodCreate, BaseRod, ExtraRod);
        var quest = Change("gml_Object_ReefQuest_Step_0", null, """
            if (complete) { with (RodData) { unlocked[1] = 1; } }
            """);
        var report = ConflictAnalyzer.Analyze([Patch(rod, quest)], allowSingle: true);
        Assert.Contains("gml_Object_ReefQuest_Step_0:1 grants ownership",
            Assert.Single(report.Findings, f => f.Kind == "unlock").Message);

        var save = Change(LegacyItemUnlocks.SaveLoad, null, """
            RodData.unlocked[1] = ini_read_real("Unlocks", "reef_reward", -1);
            """);
        var writer = Change("gml_Object_ReefAchievement_Step_0", null, """
            if (reward_earned) ini_write_real("Unlocks", "reef_reward", 1);
            """);
        report = ConflictAnalyzer.Analyze([Patch(rod, save, writer)], allowSingle: true);
        var finding = Assert.Single(report.Findings, f => f.Kind == "unlock");
        Assert.Contains("from a save flag", finding.Message);
        Assert.Contains(writer.Name, finding.Sources!);

        var mirror = writer with { ModCode = Code("ini_write_real(\"Unlocks\", \"reef_reward\", RodData.unlocked[1]);") };
        report = ConflictAnalyzer.Analyze([Patch(rod, save, mirror)], allowSingle: true);
        Assert.Contains("[CGF-UNLOCK-002]", Assert.Single(report.Findings, f => f.Kind == "unlock").Message);

        var legacyWriter = writer with { ModCode = Code("ini_write_real(\"Unlocks\", \"rod1\", true);") };
        report = ConflictAnalyzer.Analyze([Patch(rod, legacyWriter)], allowSingle: true);
        Assert.Contains("flag read by the legacy save loader",
            Assert.Single(report.Findings, f => f.Kind == "unlock").Message);
    }

    [Fact]
    public void CheckerReportsRelocatedSlotForDistinctBoats()
    {
        var first = Patch(Change(BoatCreate, BaseBoat, ExtraBoat + "\nunlocked[1] = -2;"));
        var second = Patch(Change(BoatCreate, BaseBoat,
            ExtraBoat.Replace("Skiff", "Cutter", StringComparison.Ordinal)
                .Replace("BoatExtra", "BoatCutter", StringComparison.Ordinal) + "\nunlocked[1] = -2;"))
            with { Mod = new("other.win", "2", 1, "1.4", 16, "Other") };
        var report = ConflictAnalyzer.Analyze([first, second]);
        Assert.Contains(report.Findings, f => f.Message.Contains("boat 'Skiff' (slot 1) has no unlock path"));
        Assert.Contains(report.Findings, f => f.Message.Contains("boat 'Cutter' (slot 2) has no unlock path"));
    }

    [Fact]
    public void AChangedDynamicUnlockPreventsAnUnprovenWarningOrRepair()
    {
        var rod = Change(RodCreate, BaseRod, ExtraRod);
        var grant = Change("gml_Object_RodData_Step_0", "for (i = 0; i < 1; i++) unlocked[i] = 1;",
            "for (i = 0; i < 2; i++) unlocked[i] = 1;");
        var patch = Patch(rod, grant);
        var item = Assert.Single(ConflictAnalyzer.Analyze([patch], allowSingle: true).Findings, f => f.Kind == "unlock");
        Assert.Equal("INFO", item.Severity);
        Assert.Contains("unverified", item.Message);
        Assert.DoesNotContain(BuildPlan.Create([patch], null, null, null, new HashSet<int> { 0 }).Decisions,
            decision => decision.Contains("made"));
    }

    [Fact]
    public void OptInBuildEditsOnlyTheSelectedModAndKeepsOldSaveOwnership()
    {
        var boat = Change(BoatCreate, BaseBoat, ExtraBoat);
        var rod = Change(RodCreate, BaseRod, ExtraRod);
        var baseline = new Dictionary<string, CodeText> { [LegacyItemUnlocks.SaveLoad] =
            Code("""with (BoatData) { for (i = 0; i < nItems; i++) { unlocked[i] = ini_read_real("Unlocks", "boat" + string(i), -1); } }""") };
        var patches = new[] { Patch(boat), Patch(rod) with { Mod = new("other.win", "2", 1, "1.4", 16, "Other") } };

        var off = BuildPlan.Create(patches, null, null, baseline);
        Assert.DoesNotContain(off.Decisions, d => d.Contains("shop unlock repair"));
        Assert.DoesNotContain("unlocked[1] = 0", off.Assets.Single(a => a.Name == BoatCreate).Gml);

        var on = BuildPlan.Create(patches, null, null, baseline, new HashSet<int> { 0 });
        Assert.Contains(on.Decisions, d => d.Contains("Legacy: boat 'Skiff' (slot 1) buyable for $100"));
        string? text = on.Assets.Single(a => a.Name == BoatCreate).Gml;
        Assert.Contains("unlocked[1] = 0;", text);
        Assert.Contains("cost[1] = 100;", text);
        Assert.Contains("if (i == 1 && unlocked[i] < 0) unlocked[i] = 0;",
            on.Assets.Single(a => a.Name == LegacyItemUnlocks.SaveLoad).Gml);
        Assert.DoesNotContain("unlocked[1] = 0;", on.Assets.Single(a => a.Name == RodCreate).Gml);
    }

    [Fact]
    public void SinglePatchCliWritesTheUnlockWarningToTheReport()
    {
        string directory = Directory.CreateTempSubdirectory("jamm-unlock-").FullName;
        try
        {
            string basePath = Path.Combine(directory, "base.win");
            File.WriteAllText(basePath, "synthetic patch base");
            var patch = Patch(Change(BoatCreate, BaseBoat, ExtraBoat));
            patch = patch with { Base = patch.Base with { Sha256 = BuildFiles.Hash(basePath) } };
            string patchPath = Path.Combine(directory, "legacy.cgfmm-patch.json");
            File.WriteAllText(patchPath, PatchJson.Serialize(patch));
            string json = Path.Combine(directory, "report.json");
            Assert.Equal(0, Jamm.Cli.CheckCommand.Run([patchPath, "--base", basePath, "--json", json]));
            using var report = JsonDocument.Parse(File.ReadAllText(json));
            var findings = report.RootElement.GetProperty("report").GetProperty("findings").EnumerateArray();
            Assert.Contains(findings, f => f.GetProperty("message").GetString()!.StartsWith("[CGF-UNLOCK-001] Legacy: boat"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ModPatch Patch(params AssetPatch[] code)
    {
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
        foreach (string family in CgfAssetTypes.IndexedFamilies.Concat(["texturePages", "strings", "variables", "functions"]))
            changes[family] = new([], [], []);
        changes["code"] = new(code.Where(c => c.Base is null).ToArray(), code.Where(c => c.Base is not null).ToArray(), []);
        return new(PatchJson.FormatVersion, "test", DateTimeOffset.UnixEpoch,
            new("legacy.win", "1", 1, "1.4", 16, "Legacy"), new("base.win", "base", 1, "1.4", 16), [],
            changes, new(StringComparer.Ordinal), []);
    }

    private static AssetPatch Change(string entry, string? before, string addition)
    {
        var snapshot = AssetFields.Json(new { });
        return new(entry, before is null ? null : snapshot, snapshot, [], before is null ? null : Code(before),
            Code(before is null ? addition : before + "\n" + addition));
    }

    private static CodeText Code(string text) => new(text, "gml", 0, 0, null, []);
}
