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

    [Fact]
    public void WrapsCompoundAssignmentsAndPrefixIncrements()
    {
        List<InstanceReference> found = [];
        string text = """
            Bo.flags |= 1;
            Bo.flags &= 2;
            Bo.flags ^= 3;
            Bo.x %= 4;
            Bo.x <<= 1;
            Bo.x >>= 1;
            ++Bo.x;
            --Bo.y;
            near = instance_nearest(x, y, Bo);
            near.flags |= 4;
            ++near.x;
            """;
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.flags |= 1; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.flags &= 2; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.flags ^= 3; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.x %= 4; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.x <<= 1; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.x >>= 1; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ ++Bo.x; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ --Bo.y; }}", result);
        Assert.Contains("if (near >= 0 && instance_exists(near)) { near.flags |= 4; }", result);
        Assert.Contains("if (near >= 0 && instance_exists(near)) { ++near.x; }", result);
        Assert.All(found, f => Assert.True(f.Guarded));
    }

    [Fact]
    public void GuardsMultilineStatementsAndMultipleStatementsPerLineWithoutBreakingForHeaders()
    {
        List<InstanceReference> found = [];
        string text = """
            Bo.x = clamp(
                a,
                0,
                10
            );
            Bo
                .y = 2;
            d = Bo
                .z;
            a = 1; Bo.x = 2; Worm.y = 3;
            if (b) Bo.x = 4; else y = 1;
            for (
                Bo.i = 0;
                Bo.i < 10;
                Bo.i++
            )
            {
                z = 1;
            }
            """;
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.x = clamp(\n    a,\n    0,\n    10\n); }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo\n    .y = 2; }}", result);
        Assert.Contains($"d = jamm_iv_z({S("Bo")});", result);
        Assert.Contains($"a = 1; {{ if (instance_exists({S("Bo")})) {{ Bo.x = 2; }} }} {{ if (instance_exists({S("Worm")})) {{ Worm.y = 3; }} }}", result);
        Assert.Contains($"if (b) {{ if (instance_exists({S("Bo")})) {{ Bo.x = 4; }} }} else y = 1;", result);
        Assert.Contains("for (\n    Bo.i = 0;\n", result);
        Assert.DoesNotContain("{ Bo.i = 0; }", result);
        Assert.Contains($"jamm_iv_i({S("Bo")}) < 10;", result);
    }

    [Fact]
    public void DetectsShadowedVariablesInMixedVarFunctionArgsAndCompoundOps()
    {
        List<InstanceReference> found = [];
        string text = """
            function move_fish(a, Bo, Worm = 0)
            {
                return Bo.x + Worm.y;
            }
            var x = foo(1, 2), Sizzer;
            a = Sizzer.x;
            Baby_Sizzer += 1;
            b = Baby_Sizzer.y;
            other . Lure = 1;
            c = Lure.object_index == Fish.object_index;
            """;
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out var accessors);
        Assert.Contains("return Bo.x + Worm.y;", result);
        Assert.Contains("a = Sizzer.x;", result);
        Assert.Contains("b = Baby_Sizzer.y;", result);
        Assert.Contains($"c = {S("Lure")} == Fish.object_index;", result);
        Assert.Empty(accessors);
        Assert.Equal(new[] { "Lure", "Fish" }, found.Select(f => f.Object));
    }

    [Fact]
    public void GuardsLookupVariablesEvenWhenNamedLikeObjectsAndRespectsIfScope()
    {
        List<InstanceReference> found = [];
        string text = """
            var Bo = instance_nearest(x, y, Fish);
            d = Bo.x;
            near = instance_nearest(x, y, Worm);
            if (near != noone)
            {
                a = near.x;
            }
            b = near.y;
            if (near != noone || fallback)
                c = near.z;
            if (near == noone)
                show_debug_message("missing");
            e = near.speed;
            near = near.parent;
            safe = instance_nearest(x, y, Worm);
            if (safe == noone) exit;
            f = safe.x;
            scoped = instance_nearest(x, y, Worm);
            if (cond)
            {
                if (scoped == noone) exit;
                g = scoped.x;
            }
            h = scoped.y;
            """;
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        Assert.Contains("d = jamm_iv_x(Bo);", result);
        Assert.Contains("a = near.x;", result);
        Assert.Contains("b = jamm_iv_y(near);", result);
        Assert.Contains("c = jamm_iv_z(near);", result);
        Assert.Contains("e = jamm_iv_speed(near);", result);
        Assert.Contains("near = jamm_iv_parent(near);", result);
        Assert.Contains("f = safe.x;", result);
        Assert.Contains("g = scoped.x;", result);
        Assert.Contains("h = jamm_iv_y(scoped);", result);
    }

    [Fact]
    public void ExistenceGuardHandlesBooleanOperatorsUnbracedBlocksAndNoSemicolons()
    {
        List<InstanceReference> found = [];
        string text = """
            if (a or b && instance_exists(Bo))
                Bo.x = 1;
            if (a ^^ b && instance_exists(Bo))
                Bo.y = 2;
            if (instance_exists(Bo))
                if (c) {}
            Bo.z = 3;
            if (instance_exists(Bo))
                exit
            Bo.w = 4;
            if ((a || b) && instance_exists(Bo))
                Bo.k = 5;
            if (!instance_exists(Bo)) exit;
            Bo.m = 6;
            """;
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.x = 1; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.y = 2; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.z = 3; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.w = 4; }}", result);
        Assert.Contains("Bo.k = 5;\nif (!instance_exists(Bo)) exit;\nBo.m = 6;", result);
    }

    [Fact]
    public void PinsNestedParensQualifiedNamesChainingMaskingSwitchAndElse()
    {
        List<InstanceReference> found = [];
        string text = """
            if ((a && instance_exists(Bo)) || b)
                Bo.x = 1;
            if (!(a && instance_exists(Bo)))
                Bo.y = 2;
            other.Bo.x = 1;
            self.Bo.y = 2;
            global.Bo.z = 3;
            global.near = instance_nearest(x, y, Bo);
            d = other.near.x;
            near = instance_nearest(x, y, Bo);
            other.near = 1;
            e = near.x;
            f = Bo.target.x;
            Bo.target.x = 1;
            s1 = "escaped \"Bo.x\"";
            s2 = 'escaped \'Bo.y\'';
            s3 = "slash \\"; Bo.z = 4;
            /* Bo.x */ /* " */ g = Bo.w;
            // /*
            h = Bo.v;
            Bo.name = "a; \"b\"";
            if (cond)
                y = 1;
            else
                Bo.u = 5;
            switch (Bo.state)
            {
                case 0: Bo.p = 1; break;
                case Bo.object_index: break;
                default: return Bo.q;
            }
            """;
        string result = InstanceGuard.Rewrite("x", text, Facts(), found, out _);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.x = 1; }}", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.y = 2; }}", result);
        Assert.Contains("other.Bo.x = 1;", result);
        Assert.Contains("self.Bo.y = 2;", result);
        Assert.Contains("global.Bo.z = 3;", result);
        Assert.Contains("d = other.near.x;", result);
        Assert.Contains("e = jamm_iv_x(near);", result);
        Assert.Contains("f = Bo.target.x;", result);
        Assert.Contains("Bo.target.x = 1;", result);
        Assert.Contains("s1 = \"escaped \\\"Bo.x\\\"\";", result);
        Assert.Contains("s2 = 'escaped \\'Bo.y\\'';", result);
        Assert.Contains($"s3 = \"slash \\\\\"; {{ if (instance_exists({S("Bo")})) {{ Bo.z = 4; }} }}", result);
        Assert.Contains($"/* Bo.x */ /* \" */ g = jamm_iv_w({S("Bo")});", result);
        Assert.Contains($"h = jamm_iv_v({S("Bo")});", result);
        Assert.Contains($"if (instance_exists({S("Bo")})) {{ Bo.name = \"a; \\\"b\\\"\"; }}", result);
        Assert.Contains($"else\n    {{ if (instance_exists({S("Bo")})) {{ Bo.u = 5; }} }}", result);
        Assert.Contains($"switch (jamm_iv_state({S("Bo")}))", result);
        Assert.Contains($"case 0: {{ if (instance_exists({S("Bo")})) {{ Bo.p = 1; }} }} break;", result);
        Assert.Contains("case Bo.object_index: break;", result);
        Assert.Contains($"default: return jamm_iv_q({S("Bo")});", result);
    }

    [Fact]
    public void PinsLegacyInstanceSitesAndIdempotency()
    {
        var facts = Facts();
        foreach (var (code, input, expected) in new[]
        {
            ("gml_Object_Chook_Other_10", "if (point_distance(x, y, Lure.x, Lure.y) <= 51)\n{\n    alarm[0] = 1;\n}", $"instance_exists({S("Ancient_Kingfish")})"),
            ("gml_Object_Thermo_Other_10", "var dist = point_distance(ThermoSchool.x, ThermoSchool.y, nearby_magmer.x, nearby_magmer.y);", "if (instance_exists(nearby_magmer))"),
            ("gml_Object_SaveManager_Other_13", "ind = Bulbfish.object_index;", $"var jamm_lamp_object = {S("Bulbfish")};"),
            ("gml_Object_LightingSystem_Draw_0", "draw_sprite_ext(Torby.sprite_index, 0, Girl.x + ox, Girl.y + oy, -1, 1, 0, c_white, 0.3);", $"var jamm_ghost_sprite = object_get_sprite({S("Torby")});"),
            ("gml_Object_Snobfish_Other_10", "if (bro.object_index != Nightfish)\n{\n    a = 1;\n}", $"bro.object_index == {S("Nightfish")}"),
            ("gml_Object_Flash_Other_10", "target_x = target_node.x;\ntarget_y = target_node.y;", $"instance_nearest(x, y, {S("NavNode")})")
        })
        {
            List<InstanceReference> found = [];
            string rewritten = InstanceGuard.Rewrite(code, input, facts, found, out _);
            Assert.Contains(expected, rewritten);
            Assert.Contains(found, f => f.Kind is "instance-repair" or "object-index" && f.Guarded);
            Assert.Equal(rewritten, InstanceGuard.Rewrite(code, rewritten, facts, [], out _));
        }
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
