using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class InstanceGuardTests
{
    private static InstanceGuard.ObjectFacts Facts()
    {
        var facts = new InstanceGuard.ObjectFacts();
        foreach (string name in new[] { "Lure", "Fish", "Bo", "Sizzer", "Baby_Sizzer", "Worm", "Girl" }) facts.Names.Add(name);
        facts.WithChildren.Add("Fish");
        facts.WithChildren.Add("Sizzer");
        foreach (string name in new[] { "Fish", "Bo", "Sizzer", "Baby_Sizzer", "Worm" }) facts.Risky.Add(name);
        return facts;
    }

    [Fact]
    public void ReplacesObjectIndexOfLeafObjectsWithTheConstant()
    {
        List<InstanceReference> found = [];
        string text = "if (Lure.fish.object_index == Bo.object_index)\n{\n    delirius = 1;\n}";
        string result = InstanceGuard.Rewrite("gml_Object_Fish_Other_17", text, Facts(), found, out var accessors);
        Assert.Contains("Lure.fish.object_index == Bo)", result);
        Assert.Empty(accessors);
        Assert.Equal("object-index", Assert.Single(found).Kind);
    }

    [Fact]
    public void ParentObjectIndexUsesTheAccessorBecauseAChildMayBeFirst()
    {
        List<InstanceReference> found = [];
        string result = InstanceGuard.Rewrite("x", "if (inhale.object_index == Sizzer.object_index) a = 1;", Facts(), found, out var accessors);
        Assert.Contains("== jamm_iv_object_index(Sizzer)", result);
        Assert.Contains("object_index", accessors);
        Assert.Contains("return noone;", InstanceGuard.AccessorGml("object_index"));
        Assert.Contains("return 0;", InstanceGuard.AccessorGml("state"));
    }

    [Fact]
    public void GuardsFishReadsButNotStringsCommentsOrSafeObjects()
    {
        List<InstanceReference> found = [];
        string text = "if (Worm.state == 3 || Girl.level > 2) s = \"Worm.state\"; // Bo.x\nd = point_distance(x, y, Bo.x, Bo.y);";
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out var accessors);
        Assert.Contains("jamm_iv_state(Worm) == 3", result);
        Assert.Contains("Girl.level", result);
        Assert.Contains("\"Worm.state\"", result);
        Assert.Contains("// Bo.x", result);
        Assert.Contains("point_distance(x, y, jamm_iv_x(Bo), jamm_iv_y(Bo))", result);
        Assert.Equal(new[] { "state", "x", "y" }, accessors.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WrapsSimpleWritesUnlessAnElseFollows()
    {
        List<InstanceReference> found = [];
        string text = "if (a)\n{\n    Worm.state = Bo.size + 1;\n}\nBo.life--;\nif (b)\n    Bo.x = 4;\nelse\n    y = 1;\nBo.arr[1] = 2;";
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        Assert.Contains("if (instance_exists(Worm)) { Worm.state = jamm_iv_size(Bo) + 1; }", result);
        Assert.Contains("if (instance_exists(Bo)) { Bo.life--; }", result);
        Assert.Contains("    Bo.x = 4;\nelse", result);
        Assert.Contains(found, f => f.Kind == "fish-write" && !f.Guarded && f.Snippet == "Bo.x = 4;");
        Assert.Contains(found, f => f.Kind == "fish-array" && !f.Guarded);
    }

    [Fact]
    public void ReportsUncheckedLookupsAndSkipsCheckedOnes()
    {
        List<InstanceReference> found = [];
        string text = "near = instance_nearest(x, y, Bo);\ndirection = point_direction(x, y, near.x, near.y);\n"
            + "safe = instance_nearest(x, y, Bo);\nif (safe != noone) a = safe.x;";
        InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        var lookup = Assert.Single(found, f => f.Kind == "unchecked-lookup");
        Assert.Equal("near", lookup.Object);
    }
}

public sealed partial class BuildingTests
{
    [Fact]
    public void InstanceGuardRewritesObjectIndexInModdedBuildsAndKeepsVanillaBytes()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win"), modPath = temp.PathFor("mod.win");
        WriteModel(basePath, d => Setup(d, false));
        WriteModel(modPath, d => Setup(d, true));
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "Weather-like mod");
        string output = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.StartsWith("[CGF-INSTANCE-004]", StringComparison.Ordinal));
        Assert.Contains(build.Plan.Decisions, d => d.StartsWith("[CGF-INSTANCE-005]", StringComparison.Ordinal));
        using (var merged = GameFileLoader.Load(output))
        {
            string bite = PatchBuilder.ReadCode(merged, "gml_Object_Fish_Other_17").Text;
            Assert.Contains("object_index == Bo &&", bite);
            Assert.DoesNotContain("Bo.object_index", bite);
            Assert.Contains("jamm_iv_size(Bo)", bite);
            Assert.NotNull(merged.Data.Scripts.ByName("jamm_iv_size"));
        }
        string vanilla = temp.PathFor("vanilla.win");
        ArchiveBuilder.Build(basePath, [], vanilla);
        Assert.Equal(File.ReadAllBytes(basePath), File.ReadAllBytes(vanilla));

        static void Setup(UndertaleData data, bool modded)
        {
            foreach (string name in new[] { "Lure", "Fish", "Bo", "Marker" })
                data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
            data.GameObjects.ByName("Bo").ParentId = data.GameObjects.ByName("Fish");
            var compiler = new CompileGroup(data);
            compiler.QueueCodeReplace(data.GameObjects.ByName("Fish").EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User7, data),
                "if (Lure.fish.object_index == Bo.object_index && Bo.size > 1) { delirius = 1; }");
            compiler.QueueCodeReplace(data.GameObjects.ByName("Marker").EventHandlerFor(EventType.Create, data), modded ? "value = 2;" : "value = 1;");
            Assert.True(compiler.Compile().Successful);
        }
    }
}
