using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Diffing;
using Jamm.Core.Merging;
using Jamm.Core.Patching;

namespace Jamm.Core.Tests;

/// <summary>Ancestor merges, semantic overlaps and indirect problems using portable synthetic patches.</summary>
public class MergingTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static AssetPatch Asset(string name, string? before, string after) => new(name,
        before is null ? null : Json(before), Json(after), FieldDiff.Compare(before is null ? null : Json(before), Json(after)));
    private static CodeText Code(string text, string format = "gml") => new(text, format, 0, 0, null, []);
    private static AssetPatch CodeAsset(string name, string? before, string after, string format = "gml") =>
        Asset(name, before is null ? null : "{}", "{}") with { BaseCode = before is null ? null : Code(before), ModCode = Code(after, format) };
    private static ModPatch Patch(string name, params (string Family, AssetPatch? Asset, string? Removed)[] entries)
    {
        var families = new[] { "code", "scripts", "objects", "rooms", "sprites", "texturePages", "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions" };
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
        foreach (string family in families)
        {
            var group = entries.Where(e => e.Family == family).ToArray();
            changes[family] = new(group.Where(e => e.Asset is { Base: null }).Select(e => e.Asset!).ToArray(),
                group.Where(e => e.Asset?.Base is not null).Select(e => e.Asset!).ToArray(), group.Where(e => e.Removed is not null).Select(e => e.Removed!).ToArray());
        }
        return new(PatchJson.FormatVersion, "test", DateTimeOffset.UnixEpoch,
            new(name + ".win", name, 1, "1.4", 16, name), new("base.win", "base", 1, "1.4", 16), [], changes,
            new(StringComparer.Ordinal) { ["objects"] = ["fish"], ["sprites"] = ["spr_fish"] }, []);
    }

    [Fact]
    public void NonOverlappingEditsMergeAndNormalizeLineEndings()
    {
        var result = ThreeWayMerge.Merge("one\r\ntwo\r\nthree\r\n", "ONE\ntwo\nthree\n", "one\ntwo\nTHREE\n");
        Assert.False(result.HasConflicts);
        Assert.Equal("ONE\ntwo\nTHREE\n", result.Text);
        Assert.All(result.Hunks, h => Assert.Equal("AUTO", h.Status));
    }

    [Fact]
    public void AdjacentLoopInsertionAndNestedLoopEditDoNotCrossAssignStatements()
    {
        const string ancestor = "function f() {\n    upg[0, 0] = 1;\n    upg[1, 0] = 1;\n    for (ix = 0; ix < 6; ix++) {\n        for (iy = 0; iy < 8; iy++) {\n            object[ix, iy] = -1;\n        }\n    }\n    done();\n}\n";
        const string a = "function f() {\n    upg[0, 0] = 1;\n    upg[1, 0] = 1;\n    upg[6, 0] = 1;\n    for (ix = 0; ix < 6; ix++) {\n        for (iy = 0; iy < 8; iy++) {\n            object[ix, iy] = -1;\n        }\n    }\n    done();\n}\n";
        const string b = "function f() {\n    upg[0, 0] = 1;\n    upg[1, 0] = 1;\n    upg[18, 0] = 1;\n    for (iy = 0; iy < 9; iy++) {\n        upg[18, iy] = -1;\n    }\n    for (ix = 0; ix < 7; ix++) {\n        for (iy = 0; iy < 9; iy++) {\n            object[ix, iy] = -1;\n        }\n    }\n    done();\n}\n";
        foreach (var versions in new[]
        {
            new[] { new MergeVersion("A", a), new MergeVersion("B", b) },
            new[] { new MergeVersion("B", b), new MergeVersion("A", a) }
        })
        {
            var result = ThreeWayMerge.Merge(ancestor, versions);
            Assert.False(result.HasConflicts);
            Assert.Contains("upg[6, 0] = 1;", result.Text);
            Assert.Contains("for (iy = 0; iy < 9; iy++) {\n        upg[18, iy] = -1;", result.Text);
            Assert.Contains("for (ix = 0; ix < 7; ix++) {\n        for (iy = 0; iy < 9; iy++)", result.Text);
            Assert.DoesNotContain("for (iy = 0; iy < 9; iy++) {\n        object[ix, iy]", result.Text);
        }
    }

    [Fact]
    public void NearAdjacentIndependentLoopsAndOutsideEditsRemainMergeable()
    {
        const string ancestor = "function f() {\n    for (ix = 0; ix < 6; ix++) {\n        left_work();\n    }\n    for (iy = 0; iy < 8; iy++) {\n        right_work();\n    }\n    tail();\n}\n";
        var result = ThreeWayMerge.Merge(ancestor,
            "function f() {\n    for (ix = 0; ix < 7; ix++) {\n        left_work();\n    }\n    for (iy = 0; iy < 8; iy++) {\n        right_work();\n    }\n    tail();\n}\n",
            "function f() {\n    for (ix = 0; ix < 6; ix++) {\n        left_work();\n    }\n    for (iy = 0; iy < 9; iy++) {\n        right_work();\n    }\n    TAIL();\n}\n");
        Assert.False(result.HasConflicts);
        Assert.Contains("ix < 7", result.Text);
        Assert.Contains("iy < 9", result.Text);
        Assert.Contains("TAIL();", result.Text);
    }

    [Fact]
    public void ConflictingLoopScopeDoesNotSwallowUnrelatedTailEdit()
    {
        const string ancestor = "function f() {\n    for (ix = 0; ix < 6; ix++) {\n        work();\n    }\n    tail();\n}\n";
        var result = ThreeWayMerge.Merge(ancestor,
            "function f() {\n    for (ix = 0; ix < 7; ix++) {\n        work();\n    }\n    tail();\n}\n",
            "function f() {\n    for (ix = 0; ix < 8; ix++) {\n        work();\n    }\n    TAIL();\n}\n");
        Assert.True(result.HasConflicts);
        Assert.Contains("<<<<<<< A", result.Text);
        Assert.Contains("TAIL();", result.Text);
        Assert.Equal(1, result.Hunks.Count(h => h.Status == "CONFLICT"));
        Assert.Contains(result.Hunks, h => h.Status == "AUTO");
    }

    [Fact]
    public void EditCrossingLoopScopeExpandsToItsFullRangeAndKeepsOutsideEdit()
    {
        const string ancestor = "function f() {\n    before();\n    for (ix = 0; ix < 6; ix++) {\n        work(\"}\"); // {\n    }\n    after();\n    tail();\n}\n";
        const string a = "function f() {\n    before();\n    for (ix = 0; ix < 7; ix++) {\n        work(\"}\"); // {\n    }\n    after();\n    TAIL();\n}\n";
        const string b = "function f() {\n    BEFORE();\n    for (ix = 0; ix < 6; ix++) { // B\n        WORK();\n    } // B\n    AFTER();\n    tail();\n}\n";

        var result = ThreeWayMerge.Merge(ancestor, a, b);

        Assert.True(result.HasConflicts);
        var conflict = Assert.Single(result.Hunks, h => h.Status == "CONFLICT");
        Assert.Contains("before();", conflict.Base);
        Assert.Contains("after();", conflict.Base);
        Assert.Contains("TAIL();", result.Text);
        Assert.Contains("BEFORE();", result.Text);
        Assert.Contains("AFTER();", result.Text);
    }

    [Fact]
    public void DisjointOrdinaryEditsStillMergeInsideLoopContainingCode()
    {
        const string ancestor = "function f() {\n    for (ix = 0; ix < 6; ix++) {\n        work();\n    }\n    left();\n    right();\n}\n";
        var result = ThreeWayMerge.Merge(ancestor,
            "function f() {\n    for (ix = 0; ix < 6; ix++) {\n        work();\n    }\n    LEFT();\n    right();\n}\n",
            "function f() {\n    for (ix = 0; ix < 6; ix++) {\n        work();\n    }\n    left();\n    RIGHT();\n}\n");
        Assert.False(result.HasConflicts);
        Assert.Contains("LEFT();", result.Text);
        Assert.Contains("RIGHT();", result.Text);
    }

    [Theory]
    [InlineData("a\nb\nc\n", "a\nA\nc\n", "a\nB\nc\n")]
    [InlineData("a\nb\nc\n", "a\nc\n", "a\nB\nc\n")]
    public void OverlappingReplacementsAndDeleteEditKeepBothVersions(string ancestor, string a, string b)
    {
        var result = ThreeWayMerge.Merge(ancestor, a, b);
        Assert.True(result.HasConflicts);
        Assert.Contains("<<<<<<< A", result.Text); Assert.Contains("======= B", result.Text);
        Assert.Null(Assert.Single(result.Hunks).Resolution);
    }

    [Fact]
    public void IdenticalEditsAndIdenticalDeletionsAreHarmless()
    {
        var result = ThreeWayMerge.Merge("a\nb\nc\n", "a\nc\n", "a\nc\n");
        Assert.False(result.HasConflicts); Assert.Equal("a\nc\n", result.Text);
        Assert.Equal("INFO", Assert.Single(result.Hunks).Status);
    }

    [Fact]
    public void SharedInsertionHasSelectableKeepBothInLoadOrder()
    {
        var result = ThreeWayMerge.Merge("start\nend\n", "start\nfish_A\nend\n", "start\nfish_B\nend\n");
        var hunk = Assert.Single(result.Hunks);
        Assert.Equal("list", hunk.Kind); Assert.Contains("keepBoth", hunk.Choices);
        Assert.Equal("fish_A\nfish_B\n", hunk.KeepBoth);
        var reverse = ThreeWayMerge.Merge("start\nend\n", "start\nfish_B\nend\n", "start\nfish_A\nend\n");
        Assert.Equal("fish_B\nfish_A\n", Assert.Single(reverse.Hunks).KeepBoth);
    }

    [Fact]
    public void ThreeModsNeverUseConflictMarkersAsAnAncestor()
    {
        var result = ThreeWayMerge.Merge("a\nb\nc\nd\n", [new("A", "A\nb\nc\nd\n"), new("B", "B\nb\nc\nd\n"), new("C", "a\nb\nc\nD\n")]);
        Assert.True(result.HasConflicts); Assert.EndsWith("b\nc\nD\n", result.Text);
        Assert.Equal(1, result.Hunks.Count(h => h.Status == "CONFLICT"));
    }

    [Fact]
    public void EmptyAndMissingFinalNewlineMergesAreLossless()
    {
        Assert.Equal("a", ThreeWayMerge.Merge("", "a", "").Text);
        Assert.Equal("a\n", ThreeWayMerge.Merge("a", "a\n", "a").Text);
        Assert.Equal("", ThreeWayMerge.Merge("a\n", "", "a\n").Text);
    }

    [Theory]
    [InlineData("a\n", "a", "a\nb\n")]
    [InlineData("// comment\n", "// comment", "// comment\nrun();\n")]
    public void RemovingFinalNewlineConflictsWithAppendingCode(string ancestor, string a, string b)
    {
        foreach (var versions in new[] { new[] { new MergeVersion("A", a), new MergeVersion("B", b) },
            new[] { new MergeVersion("B", b), new MergeVersion("A", a) } })
        {
            var merge = ThreeWayMerge.Merge(ancestor, versions);
            Assert.True(merge.HasConflicts);
            var hunk = Assert.Single(merge.Hunks);
            Assert.Equal(ancestor, hunk.Base);
            Assert.Equal(new[] { a, b }.Order(), hunk.Versions.Select(v => v.Text).Order());
        }
    }

    [Fact]
    public void AssetRemovalVersusEditRetainsTextAndFieldAlternativesForDetails()
    {
        var a = Patch("A", ("code", null, "entry"), ("objects", null, "fish"));
        var b = Patch("B", ("code", CodeAsset("entry", "original\n", "changed\n"), null),
            ("objects", Asset("fish", "{\"depth\":0}", "{\"depth\":5}"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Equal(3, report.ExitCode);
        Assert.Contains("<<<<<<< A", report.CodeMerges["entry"].Text);
        Assert.Contains("changed", report.CodeMerges["entry"].Text);
        Assert.Equal("removal", Assert.Single(report.CodeMerges["entry"].Hunks).Kind);
        var detail = Assert.Single(Assert.Single(report.Findings, f => f.Family == "objects" && f.Kind == "removal").Fields!);
        Assert.Null(detail.Versions[0].Value);
        Assert.Equal(5, detail.Versions[1].Value!.Value.GetProperty("depth").GetInt32());
    }

    [Fact]
    public void SameNameCodeAdditionsUseTwoWayConflictOrIdenticalInfo()
    {
        var a = Patch("A", ("code", CodeAsset("new", null, "a\n"), null));
        var b = Patch("B", ("code", CodeAsset("new", null, "b\n"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Equal(3, report.ExitCode); Assert.Equal("addition", Assert.Single(report.Findings).Kind);
        Assert.DoesNotContain("keepBoth", report.CodeMerges["new"].Hunks[0].Choices);
        Assert.Equal(0, ConflictAnalyzer.Analyze([a, a with { Mod = b.Mod }]).ExitCode);
    }

    [Fact]
    public void FieldMergeCombinesDifferentFieldsAndDetectsDifferentValues()
    {
        var a = Patch("A", ("objects", Asset("fish", "{\"depth\":0,\"visible\":true}", "{\"depth\":10,\"visible\":true}"), null));
        var b = Patch("B", ("objects", Asset("fish", "{\"depth\":0,\"visible\":true}", "{\"depth\":0,\"visible\":false}"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Equal(0, report.ExitCode); Assert.Contains(report.Findings, f => f.Severity == "AUTO");
        var merged = Assert.Single(report.FieldMerges).Snapshot;
        Assert.Equal(10, merged.GetProperty("depth").GetInt32()); Assert.False(merged.GetProperty("visible").GetBoolean());
        b = Patch("B", ("objects", Asset("fish", "{\"depth\":0,\"visible\":true}", "{\"depth\":20,\"visible\":true}"), null));
        Assert.Equal(3, ConflictAnalyzer.Analyze([a, b]).ExitCode);
    }

    [Fact]
    public void ConflictingFieldsStillReportIndependentFieldsAsAutomatic()
    {
        const string ancestor = "{\"depth\":0,\"visible\":true,\"solid\":false}";
        var a = Patch("A", ("objects", Asset("fish", ancestor, "{\"depth\":10,\"visible\":false,\"solid\":false}"), null));
        var b = Patch("B", ("objects", Asset("fish", ancestor, "{\"depth\":20,\"visible\":true,\"solid\":true}"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Contains(report.Findings, f => f.Severity == "CONFLICT");
        Assert.Contains(report.Findings, f => f.Severity == "AUTO");
        var snapshot = Assert.Single(report.FieldMerges).Snapshot;
        Assert.False(snapshot.GetProperty("visible").GetBoolean());
        Assert.True(snapshot.GetProperty("solid").GetBoolean());
    }

    [Fact]
    public void RoomsUnionDistinctInstancesAndDeduplicateIdenticalInstances()
    {
        var a = Patch("A", ("rooms", Asset("sea", "{\"instances\":{}}", "{\"instances\":{\"fish,1,2\":[{\"rotation\":0}]}}"), null));
        var b = Patch("B", ("rooms", Asset("sea", "{\"instances\":{}}", "{\"instances\":{\"fish,3,4\":[{\"rotation\":0}]}}"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Equal(0, report.ExitCode); Assert.Equal(2, Assert.Single(report.FieldMerges).Snapshot.GetProperty("instances").EnumerateObject().Count());
        report = ConflictAnalyzer.Analyze([a, a with { Mod = b.Mod }]);
        Assert.Equal(0, report.ExitCode); Assert.Contains(report.Findings, f => f.Severity == "INFO");
        Assert.Single(Assert.Single(report.FieldMerges).Snapshot.GetProperty("instances").EnumerateObject());
    }

    [Fact]
    public void RemovingAnInstanceConflictsWithChangingItsTransform()
    {
        string ancestor = "{\"instances\":{\"fish,1,2\":[{\"rotation\":0}]}}";
        var a = Patch("A", ("rooms", Asset("sea", ancestor, "{\"instances\":{}}"), null));
        var b = Patch("B", ("rooms", Asset("sea", ancestor, "{\"instances\":{\"fish,1,2\":[{\"rotation\":90}]}}"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Equal(3, report.ExitCode); Assert.Contains(report.Findings, f => f.Kind == "field");
        Assert.True(Assert.Single(report.FieldMerges).RequiresDecision);
    }

    [Fact]
    public void RemovedUsedAssetsAndCrossEntryLiteralSlotsAreReported()
    {
        var a = Patch("A", ("objects", null, "fish"), ("code", CodeAsset("first", "", "list[42] = 1;\n"), null));
        var b = Patch("B", ("code", CodeAsset("second", "", "instance_create(0,0,fish);\nlist[42] = 2;\n"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Contains(report.Findings, f => f.Kind == "reference" && f.Asset == "fish");
        var index = Assert.Single(report.Findings, f => f.Kind == "index");
        Assert.Contains("list[42]", index.Asset); Assert.Contains(index.Sources!, s => s.Contains("second", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingUpgradeSlotReplacementIsReportedWithoutAnAddedIdGroup()
    {
        const string ancestor = "name[18] = \"existing\";\ncost[18] = 1;\ndesc[18] = \"base description\";\n";
        var a = Patch("A", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[18] = \"A upgrade\";\ncost[18] = 2;\ndesc[18] = \"A description\";\n"), null));
        var b = Patch("B", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[18] = \"B upgrade\";\ncost[18] = 3;\ndesc[18] = \"B description\";\n"), null));

        var report = ConflictAnalyzer.Analyze([a, b]);
        var grouped = Assert.Single(report.Findings, f => f.Kind == "upgrade-slot");
        Assert.Equal("INFO", grouped.Severity);
        Assert.Equal(new[] { "A", "B" }, grouped.Mods);
        Assert.Contains("No slot renumbering", grouped.Message, StringComparison.Ordinal);
        Assert.Contains(report.Findings, f => f.Kind == "index" && f.Severity == "CONFLICT");
        Assert.Contains(report.Findings, f => (f.Kind is "code" or "addition") && f.Severity == "CONFLICT");

        Assert.DoesNotContain(report.Findings, f => f.Kind == "upgrade-group");

        var plan = BuildPlan.Create([a, b], new Dictionary<string, string>
        {
            ["code/gml_Object_UpgradeData_Create_0"] = "A"
        });
        var selected = Assert.Single(plan.Assets, asset => asset.Family == "code" && asset.Name == "gml_Object_UpgradeData_Create_0");
        Assert.Contains("A upgrade", selected.Gml, StringComparison.Ordinal);
        Assert.DoesNotContain("B upgrade", selected.Gml, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyUpgradeGroupResolvesSameAnchorLiteralRowInsertionsWithoutMenuWrites()
    {
        const string ancestor = "name[17] = \"Existing\";\n// appended rows follow\n";
        const string alphaText = "name[17] = \"Existing\";\nname[18] = \"Alpha\";\ndesc[18] = \"Alpha description\";\ndesc2[18] = \"Alpha detail\";\ncost[18] = 100;\n// appended rows follow\n";
        const string betaText = "name[17] = \"Existing\";\nname[18] = \"Beta\";\ndesc[18] = \"Beta description\";\ndesc2[18] = \"Beta detail\";\ncost[18] = 200;\n// appended rows follow\n";
        var alpha = Patch("Alpha", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor, alphaText), null));
        var beta = Patch("Beta", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor, betaText), null));

        var report = ConflictAnalyzer.Analyze([alpha, beta]);
        var merged = Assert.Single(report.CodeMerges["gml_Object_UpgradeData_Create_0"].Hunks, h => h.Status == "CONFLICT");
        Assert.Equal("list", merged.Kind);
        var group = Assert.Single(report.Findings, f => f.Kind == "upgrade-group");
        Assert.Equal(new[] { "Alpha", "Beta" }, group.Mods);
        Assert.Equal(new[] { $"code/gml_Object_UpgradeData_Create_0#{merged.Start}" }, group.Sources);

        var plan = BuildPlan.Create([alpha, beta], new Dictionary<string, string>
        {
            ["code/upgrade-slot[18]"] = "Alpha"
        });
        Assert.Contains("upgrade-group code/upgrade-slot[18]: Alpha", plan.Decisions);
        string selected = Assert.Single(plan.Assets, a => a.Name == "gml_Object_UpgradeData_Create_0").Gml!;
        Assert.Contains("name[18] = \"Alpha\"", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("name[18] = \"Beta\"", selected, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyUpgradeGroupIncludesSharedIconAtlasAsAWholeAssetChoice()
    {
        const string ancestor = "// appended upgrade rows\n";
        var alpha = UpgradePatch("Alpha", "name[18] = \"Alpha\";\ndesc[18] = \"Alpha\";\ncost[18] = 100;\n// appended upgrade rows\n",
            "{\"frame0\":\"alpha\",\"frame1\":\"base\"}");
        var beta = UpgradePatch("Beta", "name[18] = \"Beta\";\ndesc[18] = \"Beta\";\ncost[18] = 200;\n// appended upgrade rows\n",
            "{\"frame0\":\"base\",\"frame1\":\"beta\"}");

        var report = ConflictAnalyzer.Analyze([alpha, beta]);
        var group = Assert.Single(report.Findings, f => f.Kind == "upgrade-group");
        Assert.Contains("sprites/spr_upgradeicons", group.Sources!);
        Assert.Contains("complete spr_upgradeicons sprite asset", group.Message, StringComparison.Ordinal);

        var otherAtlasMod = UpgradePatch("Other Atlas Mod", ancestor, "{\"frame0\":\"other\",\"frame1\":\"base\"}");
        var threeModReport = ConflictAnalyzer.Analyze([alpha, beta, otherAtlasMod]);
        var threeModGroup = Assert.Single(threeModReport.Findings, f => f.Kind == "upgrade-group");
        Assert.DoesNotContain("sprites/spr_upgradeicons", threeModGroup.Sources!);

        var plan = BuildPlan.Create([alpha, beta], new Dictionary<string, string>
        {
            ["code/upgrade-slot[18]"] = "Alpha"
        });
        var atlas = Assert.Single(plan.Assets, a => a.Family == "sprites" && a.Name == "spr_upgradeicons");
        Assert.Equal("alpha", atlas.Snapshot!.Value.GetProperty("frame0").GetString());
        Assert.Equal("base", atlas.Snapshot.Value.GetProperty("frame1").GetString());

        ModPatch UpgradePatch(string name, string code, string sprite)
        {
            var patch = Patch(name,
                ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor, code), null),
                ("sprites", Asset("spr_upgradeicons", "{\"frame0\":\"base\",\"frame1\":\"base\"}", sprite), null));
            return patch with { BaseAssets = new SortedDictionary<string, string[]>(patch.BaseAssets!, StringComparer.Ordinal)
            {
                ["sprites"] = ["spr_fish", "spr_upgradeicons"]
            } };
        }
    }

    [Fact]
    public void LegacyUpgradeGroupLeavesUnrelatedHunksSelectableAndRejectsConflictingHunkChoice()
    {
        const string ancestor = "name[17] = \"Existing\";\nmode = 0;\nmarker_a = 0;\nmarker_b = 0;\n// appended rows\n";
        var alpha = Patch("Alpha", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[17] = \"Existing\";\nmode = 0;\nmarker_a = 0;\nmarker_b = 0;\nname[18] = \"Alpha\";\ncost[18] = 100;\n// appended rows\n"), null));
        var beta = Patch("Beta", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[17] = \"Existing\";\nmode = 0;\nmarker_a = 0;\nmarker_b = 0;\nname[18] = \"Beta\";\ncost[18] = 200;\n// appended rows\n"), null));
        var third = Patch("Third", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[17] = \"Existing\";\nmode = 1;\nmarker_a = 0;\nmarker_b = 0;\n// appended rows\n"), null));
        var fourth = Patch("Fourth", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[17] = \"Existing\";\nmode = 2;\nmarker_a = 0;\nmarker_b = 0;\n// appended rows\n"), null));
        var patches = new[] { alpha, beta, third, fourth };

        var report = ConflictAnalyzer.Analyze(patches);
        var group = Assert.Single(report.Findings, f => f.Kind == "upgrade-group");
        string upgradeHunk = group.Sources!.Single(s => s.StartsWith("code/", StringComparison.Ordinal));
        var unrelatedHunk = Assert.Single(report.CodeMerges["gml_Object_UpgradeData_Create_0"].Hunks,
            h => h.Status == "CONFLICT" && !upgradeHunk.EndsWith($"#{h.Start}", StringComparison.Ordinal));

        var plan = BuildPlan.Create(patches, new Dictionary<string, string>
        {
            ["code/upgrade-slot[18]"] = "Alpha",
            [$"code/gml_Object_UpgradeData_Create_0#{unrelatedHunk.Start}"] = "Third"
        });
        string merged = Assert.Single(plan.Assets, a => a.Name == "gml_Object_UpgradeData_Create_0").Gml!;
        Assert.Contains("name[18] = \"Alpha\"", merged, StringComparison.Ordinal);
        Assert.Contains("mode = 1", merged, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => BuildPlan.Create(patches, new Dictionary<string, string>
        {
            ["code/upgrade-slot[18]"] = "Alpha",
            [upgradeHunk] = "Beta"
        }));
    }

    [Fact]
    public void LegacyUpgradeGroupRequiresDistinctLiteralNames()
    {
        const string ancestor = "// appended rows\n";
        var alpha = Patch("Alpha", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[18] = \"Shared\";\ncost[18] = 100;\n// appended rows\n"), null));
        var beta = Patch("Beta", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[18] = \"Shared\";\ncost[18] = 200;\n// appended rows\n"), null));
        var report = ConflictAnalyzer.Analyze([alpha, beta]);
        Assert.DoesNotContain(report.Findings, f => f.Kind == "upgrade-group");

        var distinctAlpha = Patch("Distinct Alpha", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[18] = \"Alpha\";\ncost[18] = 100;\n// appended rows\n"), null));
        var distinctBeta = Patch("Distinct Beta", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[18] = \"Beta\";\ncost[18] = 200;\n// appended rows\n"), null));
        var anotherSlot = Patch("Another Slot", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", ancestor,
            "name[19] = \"Gamma\";\ncost[19] = 300;\n// appended rows\n"), null));
        report = ConflictAnalyzer.Analyze([distinctAlpha, distinctBeta, anotherSlot]);
        Assert.DoesNotContain(report.Findings, f => f.Kind == "upgrade-group");

        var menuA = Patch("MenuA", ("code", CodeAsset("gml_Object_UpgradeMenu_Create_0", "// menu\n",
            "spawn_order[18] = 18;\n// menu\n"), null));
        var menuB = Patch("MenuB", ("code", CodeAsset("gml_Object_UpgradeMenu_Create_0", "// menu\n",
            "spawn_order[18] = 28;\n// menu\n"), null));
        report = ConflictAnalyzer.Analyze([menuA, menuB]);
        Assert.DoesNotContain(report.Findings, f => f.Kind == "upgrade-group");
    }

    [Fact]
    public void LegacyUpgradeGroupChoiceSpansCodeAssetsAndKeepsDisjointHunks()
    {
        const string dataBase = "name[17] = \"base\";\ncost[17] = 1;\nalpha_flag = 0;\nbeta_flag = 0;\n";
        const string menuBase = "spawn_order[18] = 18;\nalpha_menu_flag = 0;\nbeta_menu_flag = 0;\n";
        var alpha = Patch("Alpha", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", dataBase,
                "name[17] = \"base\";\ncost[17] = 1;\nname[18] = \"Alpha\";\ncost[18] = 2;\nalpha_flag = 1;\nbeta_flag = 0;\n"), null),
            ("code", CodeAsset("gml_Object_UpgradeMenu_Create_0", menuBase,
                "spawn_order[18] = 28;\nalpha_menu_flag = 1;\nbeta_menu_flag = 0;\n"), null));
        var beta = Patch("Beta", ("code", CodeAsset("gml_Object_UpgradeData_Create_0", dataBase,
                "name[17] = \"base\";\ncost[17] = 1;\nname[18] = \"Beta\";\ncost[18] = 3;\nalpha_flag = 0;\nbeta_flag = 1;\n"), null),
            ("code", CodeAsset("gml_Object_UpgradeMenu_Create_0", menuBase,
                "spawn_order[18] = 38;\nalpha_menu_flag = 0;\nbeta_menu_flag = 1;\n"), null));

        var report = ConflictAnalyzer.Analyze([alpha, beta]);
        var group = Assert.Single(report.Findings, f => f.Kind == "upgrade-group");
        Assert.Single(group.Sources!);

        var plan = BuildPlan.Create([alpha, beta], new Dictionary<string, string>
        {
            ["code/upgrade-slot[18]"] = "Alpha"
        });
        string data = Assert.Single(plan.Assets, a => a.Name == "gml_Object_UpgradeData_Create_0").Gml!;
        string menu = Assert.Single(plan.Assets, a => a.Name == "gml_Object_UpgradeMenu_Create_0").Gml!;
        Assert.Contains("Alpha", data, StringComparison.Ordinal);
        Assert.DoesNotContain("Beta\"", data, StringComparison.Ordinal);
        Assert.Contains("alpha_flag", data, StringComparison.Ordinal);
        Assert.Contains("beta_flag", data, StringComparison.Ordinal);
        Assert.Contains("spawn_order[18] = 38", menu, StringComparison.Ordinal);
        Assert.Contains("alpha_menu_flag", menu, StringComparison.Ordinal);
        Assert.Contains("beta_menu_flag", menu, StringComparison.Ordinal);
    }

    [Fact]
    public void UpgradeSlotClassifierRequiresKnownEventsAndOnlyAdvisesResolvableCodeConflicts()
    {
        const string ancestor = "name[18] = \"base\";\n";
        var a = Patch("A", ("code", CodeAsset("gml_Object_MyUpgradeDataFake_Create_0", ancestor, "name[18] = \"A\";\n"), null));
        var b = Patch("B", ("code", CodeAsset("gml_Object_MyUpgradeDataFake_Create_0", ancestor, "name[18] = \"B\";\n"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.DoesNotContain(report.Findings, f => f.Kind == "upgrade-slot");
        Assert.Contains(report.Findings, f => f.Kind == "index" && f.Severity == "CONFLICT");
    }

    [Fact]
    public void UnchangedSlotWritesDoNotBecomeIndexCollisions()
    {
        string ancestor = "list[42] = 1;\na = 0;\nb = 0;\n";
        var a = Patch("A", ("code", CodeAsset("first", ancestor, ancestor.Replace("a = 0", "a = 1")), null));
        var b = Patch("B", ("code", CodeAsset("second", ancestor, ancestor.Replace("b = 0", "b = 1")), null));
        Assert.DoesNotContain(ConflictAnalyzer.Analyze([a, b]).Findings, f => f.Kind == "index");
    }

    [Theory]
    [InlineData("catalog[42] =\n    make(1,\n        VALUE);\n")]
    [InlineData("catalog[|42] =\n    VALUE;\n")]
    [InlineData("ds_list_set(catalog, 42,\n    make(1,\n        VALUE));\n")]
    [InlineData("ds_map_add(catalog, \"key\",\n    make(1,\n        VALUE));\n")]
    [InlineData("catalog[?\"key\"] =\n    @\"first\nVALUE\";\n")]
    [InlineData("catalog[42] =\n    1\n    + VALUE;\n")]
    public void MultilineValueEditsBecomeIndexCollisions(string template)
    {
        var ancestor = template.Replace("VALUE", "old");
        var a = Patch("A", ("code", CodeAsset("first", ancestor, template.Replace("VALUE", "newA")), null));
        var b = Patch("B", ("code", CodeAsset("second", ancestor, template.Replace("VALUE", "newB")), null));
        Assert.Single(ConflictAnalyzer.Analyze([a, b]).Findings, f => f.Kind == "index");
    }

    [Fact]
    public void DeletingPartOfAMultilineValueIsStillAChangedWrite()
    {
        const string ancestor = "catalog[42] = make(\n    1,\n    2\n);\n";
        var a = Patch("A", ("code", CodeAsset("first", ancestor, ancestor.Replace("    1,\n", "")), null));
        var b = Patch("B", ("code", CodeAsset("second", ancestor, ancestor.Replace("    2\n", "    3\n")), null));
        Assert.Single(ConflictAnalyzer.Analyze([a, b]).Findings, f => f.Kind == "index");
    }

    [Theory]
    [InlineData("catalog[42] =\n    make(1, 2);\nnext = 0;\n")]
    [InlineData("catalog[42] =\n    make(1, 2)\nnext = 0\n")]
    [InlineData("ds_list_set(catalog, 42,\n    make(1, 2));\nnext = 0;\n")]
    public void ChangesAfterAMultilineWriteDoNotCountAsSlotEdits(string ancestor)
    {
        var a = Patch("A", ("code", CodeAsset("first", ancestor, ancestor.Replace("next = 0", "next = 1")), null));
        var b = Patch("B", ("code", CodeAsset("second", ancestor, ancestor.Replace("next = 0", "next = 2")), null));
        Assert.DoesNotContain(ConflictAnalyzer.Analyze([a, b]).Findings, f => f.Kind == "index");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependenciesUseTheMergedStructuredSnapshotInEitherLoadOrder(bool reverse)
    {
        const string ancestor = "{\"sprite\":\"old\",\"depth\":0}";
        var a = Patch("A", ("sprites", null, "old"),
            ("objects", Asset("fish", ancestor, "{\"sprite\":\"new\",\"depth\":0}"), null));
        var b = Patch("B", ("objects", Asset("fish", ancestor, "{\"sprite\":\"old\",\"depth\":5}"), null)) with
        { References = [new("objects", "fish", "sprites", "old")] };
        var report = ConflictAnalyzer.Analyze(reverse ? [b, a] : [a, b]);
        Assert.Equal(0, report.ExitCode);
        Assert.DoesNotContain(report.Findings, f => f.Kind == "reference");
        Assert.Equal("new", Assert.Single(report.FieldMerges).Snapshot.GetProperty("sprite").GetString());
    }

    [Fact]
    public void RemovedSourcesDoNotKeepTheirDependenciesAlive()
    {
        var a = Patch("A", ("sprites", null, "old"), ("objects", null, "fish"), ("code", null, "handler"));
        var b = Patch("B", ("code", CodeAsset("handler", "x = 0;", "use(old);"), null)) with
        { References = [new("objects", "fish", "sprites", "old")] };
        // The handler survives only if its removal/edit conflict is resolved that way.
        var conditional = ConflictAnalyzer.Analyze([a, b]);
        var reference = Assert.Single(conditional.Findings, f => f.Kind == "reference");
        Assert.Equal(new[] { "code/handler" }, reference.Sources);
        Assert.Contains("depending on unresolved choices", reference.Message);
        b = Patch("B") with { References = [new("objects", "fish", "sprites", "old")] };
        Assert.DoesNotContain(ConflictAnalyzer.Analyze([a, b]).Findings, f => f.Kind == "reference");
    }

    [Fact]
    public void DependenciesInUnresolvedFieldAlternativesAreRetained()
    {
        const string ancestor = "{\"sprite\":\"base\",\"mask\":\"discarded\",\"depth\":0}";
        var remove = Patch("Remove", ("sprites", null, "old"), ("sprites", null, "discarded"));
        var a = Patch("A", ("objects", Asset("fish", ancestor, "{\"sprite\":\"old\",\"mask\":null,\"depth\":0}"), null));
        var b = Patch("B", ("objects", Asset("fish", ancestor, "{\"sprite\":\"new\",\"mask\":\"discarded\",\"depth\":5}"), null));
        foreach (var patches in new[] { new[] { remove, a, b }, new[] { remove, b, a } })
        {
            var reference = Assert.Single(ConflictAnalyzer.Analyze(patches).Findings.Where(f => f.Kind == "reference").DistinctBy(f => f.Asset));
            Assert.Equal("old", reference.Asset);
            Assert.Contains("depending on unresolved choices", reference.Message);
        }
    }

    [Fact]
    public void ReferencesIgnoreStringsAndCommentsButRetainIdentifiers()
    {
        var ids = GmlReferences.Identifiers("fish(); // fake\n/* hidden */ x = \"spr_fish\"; y = 'sound'; room = sea;");
        Assert.Contains("fish", ids); Assert.Contains("sea", ids);
        Assert.DoesNotContain("fake", ids); Assert.DoesNotContain("hidden", ids); Assert.DoesNotContain("spr_fish", ids); Assert.DoesNotContain("sound", ids);
        var refs = GmlReferences.Structured("objects", "fish", Json("{\"parent\":\"parent_fish\",\"sprite\":\"spr_fish\",\"events\":{\"4:other\":{\"otherObject\":\"other\",\"code\":[\"collision\"]}}}"));
        Assert.Contains(refs, r => r.TargetFamily == "objects" && r.Target == "other");
        Assert.Contains(refs, r => r.TargetFamily == "code" && r.Target == "collision");
    }

    [Fact]
    public void LiteralExtractionHandlesMapsListsArraysAndSkipsCommentsComparisonsAndStrings()
    {
        var writes = GmlReferences.IndexWrites("a[42] = fish; ds_list_set(a, 42, fish); ds_map_add(m, \"key\", fish);\n"
            + "global.a[$2a] += 1; a[2] == 0; a[x] = 3; // a[99] = 0\n s = 'a[9] = 1'; /* ds_map_add(m,\"fake\",1) */");
        Assert.Equal(4, writes.Count);
        Assert.Contains(writes, w => w.Kind == "map" && w.Index == "string:key");
        Assert.Single(writes, w => w.Container == "global.a" && w.Index == "42");
    }

    [Fact]
    public void DisassemblyAlwaysRequiresASeparateDecisionAndIsNeverLineMerged()
    {
        var a = Patch("A", ("code", CodeAsset("broken", "ancestor", "push.i 42", "disassembly"), null));
        var b = Patch("B");
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Equal(3, report.ExitCode); Assert.Contains(report.Findings, f => f.Kind == "disassembly");
        Assert.Empty(report.CodeMerges);
    }

    [Fact]
    public void DifferentBasesAreRejectedWithBothModNamesAndIdentities()
    {
        var a = Patch("A"); var b = Patch("B") with { Base = new("other.win", "other", 2, "1.4", 16) };
        string message = Assert.Throws<InvalidDataException>(() => ConflictAnalyzer.Analyze([a, b])).Message;
        Assert.Contains("A targets", message); Assert.Contains("B targets", message); Assert.Contains("other", message);
    }

    [Fact]
    public void AnalysisIsDeterministicAndRecordsLoadOrder()
    {
        var a = Patch("A", ("sprites", Asset("same_fish", null, "{\"hash\":\"a\"}"), null));
        var b = Patch("B", ("sprites", Asset("same_fish", null, "{\"hash\":\"b\"}"), null));
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.Equal(JsonSerializer.Serialize(report), JsonSerializer.Serialize(ConflictAnalyzer.Analyze([a, b])));
        Assert.Equal(new[] { "A", "B" }, report.LoadOrder.Select(m => m.Name));
        Assert.Contains(report.Findings, f => f.Kind == "addition" && f.Severity == "CONFLICT");
    }

    [RealFilesFact]
    public void RealCheckUsesAllThreeModsAndReusesPortablePatches()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"jamm-check-{Guid.NewGuid():N}");
        try
        {
            string original = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
            string mods = Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!;
            string[] paths = new[] { "the-abyss-beta.win", "cave-expansion.win", "colour-expansion.win" }.Select(n => Path.Combine(mods, n)).ToArray();
            string output = Path.Combine(directory, "report.json");
            Assert.Equal(3, Jamm.Cli.CheckCommand.Run([.. paths, "--base", original, "--json", output]));
            var report = JsonDocument.Parse(File.ReadAllText(output)).RootElement.GetProperty("report");
            Assert.Equal(3, report.GetProperty("loadOrder").GetArrayLength());
            Assert.True(report.GetProperty("counts").GetProperty("CONFLICT").GetInt32() > 0);
            string[] patches = paths.Select(p => Path.Combine(directory, "patches", Path.GetFileNameWithoutExtension(p) + ".cgfmm-patch.json")).Reverse().ToArray();
            Assert.Equal(3, Jamm.Cli.CheckCommand.Run([.. patches, "--base", original, "--json", output]));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
