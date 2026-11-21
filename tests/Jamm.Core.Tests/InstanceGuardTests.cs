using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class InstanceGuardTests
{
    private static string S(string obj) => InstanceGuard.Symbol(obj);

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
        Assert.Contains($"Lure.fish.object_index == {S("Bo")})", result);
        Assert.Empty(accessors);
        Assert.Equal("object-index", Assert.Single(found).Kind);
    }

    [Fact]
    public void ParentAndLookupObjectIndexStayTypedAndAreOnlyReported()
    {
        List<InstanceReference> found = [];
        string text = "if (inhale.object_index == Sizzer.object_index) a = 1;\nbro = instance_nearest(x, y, Bo);\nif (bro.object_index != Bo) b = bro.x;";
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out var accessors);
        Assert.Contains("== Sizzer.object_index", result);
        Assert.Contains("if (bro.object_index != Bo)", result);
        Assert.Contains("b = jamm_iv_x(bro)", result);
        Assert.DoesNotContain("object_index", accessors);
        Assert.Contains(found, f => f.Object == "Sizzer" && !f.Guarded);
        Assert.Contains("return noone;", InstanceGuard.AccessorGml("object_index"));
        Assert.Contains("return 0;", InstanceGuard.AccessorGml("state"));
    }

    [Fact]
    public void GuardsFishReadsButNotStringsCommentsOrSafeObjects()
    {
        List<InstanceReference> found = [];
        string text = "if (Worm.state == 3 || Girl.level > 2) s = \"Worm.state\"; // Bo.x\nd = point_distance(x, y, Bo.x, Bo.y);";
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out var accessors);
        Assert.Contains($"jamm_iv_state({S("Worm")}) == 3", result);
        Assert.Contains("Girl.level", result);
        Assert.Contains("\"Worm.state\"", result);
        Assert.Contains("// Bo.x", result);
        Assert.Contains($"point_distance(x, y, jamm_iv_x({S("Bo")}), jamm_iv_y({S("Bo")}))", result);
        Assert.Equal(new[] { "state", "x", "y" }, accessors.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WrapsSimpleWritesAndPreservesElseBinding()
    {
        List<InstanceReference> found = [];
        string text = "if (a)\n{\n    Worm.state = Bo.size + 1;\n}\nBo.life--;\nif (b)\n    Bo.x = 4;\nelse\n    y = 1;\nBo.arr[1] = 2;";
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        Assert.Contains($"if (instance_exists({S("Worm")})) {{ Worm.state = jamm_iv_size({S("Bo")}) + 1; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.life--; }}", result);
        Assert.Contains($"{{ if (instance_exists({S("Bo")})) {{ Bo.x = 4; }} }}\nelse", result);
        Assert.Contains(found, f => f.Kind == "fish-write" && f.Guarded && f.Snippet == "Bo.x = 4;");
        Assert.Contains(found, f => f.Kind == "fish-array" && !f.Guarded);
    }

    [Fact]
    public void GuardsUncheckedLookupReadsAndSkipsCheckedOnes()
    {
        List<InstanceReference> found = [];
        string text = "near = instance_nearest(x, y, Bo);\ndirection = point_direction(x, y, near.x, near.y);\nnear.speed = 2;\n"
            + "safe = instance_nearest(x, y, Bo);\nif (safe != noone) a = safe.x;\n"
            + "other_one = instance_nearest(x, y, Bo);\nif (other_one) b = other_one.y;";
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out var accessors);
        Assert.Contains("point_direction(x, y, jamm_iv_x(near), jamm_iv_y(near))", result);
        Assert.Contains("if (near >= 0 && instance_exists(near)) { near.speed = 2; }", result);
        Assert.Contains("a = safe.x", result);
        Assert.Contains("b = other_one.y", result);
        Assert.Equal(3, found.Count(f => f.Kind == "unchecked-lookup" && f.Guarded));
    }

    [Fact]
    public void DistinguishesTypedObjectSymbolsFromShadowedNamesAndChainedReads()
    {
        List<InstanceReference> found = [];
        string text = $"""
            var Bo = instance_nearest(x, y, Fish);
            if (Bo.object_index == Fish) exit;
            if (fish.object_index ==     {S("Bo")}.object_index) exit;
            {S("Bo")}.x     += 1;
            who = {S("Bo")}.target.x;
            with ({S("Bo")}) break;
            """;
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        Assert.Contains("if (Bo.object_index == Fish)", result);
        Assert.Contains($"if (fish.object_index ==     {S("Bo")})", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ {S("Bo")}.x", result);
        Assert.Contains(found, f => f.Kind == "fish-read" && !f.Guarded && f.Object == "Bo");
        Assert.Contains(found, f => f.Kind == "with-object" && f.Object == "Bo");
        Assert.Equal(result, InstanceGuard.Rewrite("x", result, Facts(), [], out _));
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
