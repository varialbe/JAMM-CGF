using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using Jamm.Core.Merging;

namespace Jamm.Core.Tests;

/// <summary>Catalog collision policy, lexical reference relocation and deterministic identities.</summary>
public sealed class CatalogApiTests
{
    private const string Create = "gml_Object_CatalogData_Create_0";
    private const string Base = "object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"base description\";\n";

    [Fact]
    public void CollisionsKeepBaseAndFirstModThenAppendEveryFishOnce()
    {
        var a = Patch("A", "Alpha"); var b = Patch("B", "Beta");
        var report = ConflictAnalyzer.Analyze([a, b]);
        Assert.DoesNotContain(report.Findings, f => f.Asset == Create && f.Severity == "CONFLICT");
        Assert.Contains(report.Findings, f => f.Kind == "catalog" && f.Severity == "AUTO");
        var plan = BuildPlan.Create([a, b]);
        string gml = plan.Assets.Single(a => a.Name == Create).Gml!;
        Assert.StartsWith(Base, gml);
        Assert.Contains("object[0, 1] = Alpha;", gml);
        Assert.Contains("object[0, 2] = Beta;", gml);
        Assert.Contains("map[0, 2] = 2;", gml);
        Assert.Contains("max_pop[0, 2] = instance_number(Beta);", gml);
        Assert.Contains("desc_seen[0, 2] = \"Beta desc\";", gml);
        Assert.Contains("jamm_catalog_legacy[0, 0] = 0;", gml);
        Assert.Contains("jamm_catalog_legacy[0, 2] = -1;", gml);
        Assert.Equal(2, plan.Catalog.Count);
        Assert.Equal(1, plan.Catalog.Single(c => c.Object == "Alpha").Slot);
        Assert.Equal(2, plan.Catalog.Single(c => c.Object == "Beta").Slot);
        Assert.Equal(gml, BuildPlan.Create([a, b]).Assets.Single(a => a.Name == Create).Gml);
        Assert.Equal(gml, BuildPlan.Create([a, b], new Dictionary<string, string> { [Create] = "A" }).Assets.Single(a => a.Name == Create).Gml);
        var reverse = BuildPlan.Create([b, a]);
        Assert.Equal(1, reverse.Catalog.Single(c => c.Object == "Beta").Slot);
        Assert.Equal(2, reverse.Catalog.Single(c => c.Object == "Alpha").Slot);
    }

    [Fact]
    public void SharedObjectIdentityIsDeduplicatedAndReplacementFishGetNewSlots()
    {
        var plan = BuildPlan.Create([Patch("A", "Alpha"), Patch("B", "Alpha")]);
        Assert.All(plan.Catalog, c => Assert.Equal(1, c.Slot));
        string gml = plan.Assets.Single(a => a.Name == Create).Gml!;
        Assert.Equal(1, gml.Split("object[0, 1] = Alpha;").Length - 1);
        var broken = Patch("Bad", "Alpha");
        var code = broken.Changes["code"].Changed.Single();
        broken.Changes["code"] = new([], [code with { ModCode = code.ModCode! with { Text = code.ModCode.Text.Replace("BaseFish", "OtherFish") } }], []);
        var replacement = BuildPlan.Create([broken]);
        Assert.Contains("object[0, 0] = BaseFish;", replacement.Assets.Single(a => a.Name == Create).Gml);
        Assert.Contains(replacement.Catalog, c => c.Object == "OtherFish" && c.Slot == 1);
    }

    [Fact]
    public void RewritesReadsWritesAndRevealCallsButLeavesStringsAndOtherScopesAlone()
    {
        var map = new Dictionary<(int Area, int Slot), int> { [(2, 58)] = 72 };
        string text = "CatalogData.count[2,58] += 1; Other.count[2,58] = 2; count[2,58] = 3; unhide_entry_for_values(2,58); // count[2,58]\nlabel = \"CatalogData.count[2,58]\";";
        string rewritten = CatalogMerge.RewriteReferences(text, "gml_Object_Quest_Step_0", map);
        Assert.Contains("CatalogData.count[2,72]", rewritten);
        Assert.Contains("Other.count[2,58]", rewritten);
        Assert.Contains("count[2,58] = 3", rewritten);
        Assert.Contains("unhide_entry_for_values(2,72)", rewritten);
        Assert.Contains("// count[2,58]", rewritten);
        Assert.Contains("\"CatalogData.count[2,58]\"", rewritten);
        Assert.Equal("with(CatalogData) { count[2,72] = 1; } count[2,58] = 2;", CatalogMerge.RewriteReferences("with(CatalogData) { count[2,58] = 1; } count[2,58] = 2;", "gml_Script_mixed", map));
        Assert.Equal("CatalogData.map[2,72] = 72;", CatalogMerge.RewriteReferences("CatalogData.map[2,58] = 58;", "gml_Script_map", map));
        Assert.Equal("hide[2,72] = count[2,72] <= 0;", CatalogMerge.RewriteReferences("hide[2,58] = count[2,58] <= 0;", "gml_Object_CatalogData_Step_1", map));
    }

    [Fact]
    public void ReferenceRelocationIsAppliedToTheOwningModsCodeBeforeMerging()
    {
        var a = Patch("A", "Alpha"); var b = Patch("B", "Beta");
        var code = b.Changes["code"];
        var snapshot = JsonSerializer.SerializeToElement(new { });
        b.Changes["code"] = code with { Added = [new("gml_Script_reveal_beta", null, snapshot, [], null,
            new("unhide_entry_for_values(0, 1);", "gml", 0, 0, null, [])),
            new("gml_Object_Beta_Create_0", null, snapshot, [], null, new("catalog_index = 1;", "gml", 0, 0, null, []))] };
        var plan = BuildPlan.Create([a, b]);
        Assert.Equal("unhide_entry_for_values(0, 2);", plan.Assets.Single(a => a.Name == "gml_Script_reveal_beta").Gml);
        Assert.Equal("catalog_index = 2;", plan.Assets.Single(a => a.Name == "gml_Object_Beta_Create_0").Gml);
    }

    [Fact]
    public void CatalogStepInsertionsKeepBothRelocatedHideRules()
    {
        var a = Patch("A", "Alpha"); var b = Patch("B", "Beta");
        foreach (var patch in new[] { a, b })
        {
            var changes = patch.Changes["code"];
            var snapshot = JsonSerializer.SerializeToElement(new { });
            var before = new CodeText("hide[0, 0] = count[0, 0] <= 0;\n", "gml", 0, 0, null, []);
            var after = before with { Text = before.Text + "hide[0, 1] = count[0, 1] <= 0;\n" };
            patch.Changes["code"] = changes with { Changed = [.. changes.Changed,
                new("gml_Object_CatalogData_Step_1", snapshot, snapshot, [], before, after)] };
        }
        string step = BuildPlan.Create([a, b]).Assets.Single(a => a.Name == "gml_Object_CatalogData_Step_1").Gml!;
        Assert.Contains("hide[0, 1] = count[0, 1] <= 0;", step);
        Assert.Contains("hide[0, 2] = count[0, 2] <= 0;", step);
    }

    [Fact]
    public void RecoversSelfOverwrittenNamedFishAndRejectsSemanticCorruption()
    {
        var patch = Patch("A", "Alpha");
        var asset = patch.Changes["code"].Changed.Single();
        patch.Changes["code"] = new([], [asset with { ModCode = asset.ModCode! with
            { Text = asset.ModCode.Text + "object[0, 1] = Beta;\nname[0, 1] = \"Beta\";\n" } }], []);
        var plan = BuildPlan.Create([patch]);
        string text = plan.Assets.Single(a => a.Name == Create).Gml!;
        Assert.Contains(plan.Catalog, m => m.Object == "Alpha" && m.Slot == 2);
        Assert.Equal(3, CatalogMerge.Validate(text, text));
        Assert.Throws<InvalidDataException>(() => CatalogMerge.Validate(text, text.Replace("object[0, 2] = Alpha", "object[0, 2] = Beta")));
        Assert.Throws<InvalidDataException>(() => CatalogMerge.Validate(text, text.Replace("map[0, 2] = 2", "map[0, 2] = 1")));
        Assert.Throws<InvalidDataException>(() => CatalogMerge.Validate(text, text.Replace("name[0, 2] = \"Alpha\"", "name[0, 2] = \"Wrong\"")));
    }

    private static ModPatch Patch(string name, string fish)
    {
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
        foreach (string family in new[] { "code", "scripts", "objects", "rooms", "sprites", "texturePages", "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions" }) changes[family] = new([], [], []);
        var snapshot = JsonSerializer.SerializeToElement(new { });
        string text = Base + $"object[0, 1] = {fish};\nname[0, 1] = \"{fish}\";\nmap[0, 1] = 1;\ndesc[0, 1] = \"{fish} desc\";\n";
        changes["code"] = new([], [new(Create, snapshot, snapshot, [], new(Base, "gml", 0, 0, null, []), new(text, "gml", 0, 0, null, []))], []);
        return new(2, "test", DateTimeOffset.UnixEpoch, new(name + ".win", name, 1, "1.4", 16, name), new("base.win", "base", 1, "1.4", 16), [], changes, new(), []);
    }
}
