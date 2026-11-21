using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed partial class BuildingTests
{
    [Fact]
    public void FinalBuildCapsFishInEventsScriptsCatalogAndRegistration()
    {
        using var temp = new TemporaryDirectory();
        string original = temp.PathFor("base.win"), mod = temp.PathFor("mod.win"), output = temp.PathFor("merged.win");
        WriteModel(original, d => Setup(d, false));
        WriteModel(mod, d => Setup(d, true));
        var input = Input(original, mod);
        var build = ArchiveBuilder.Build(original, [input], output);
        Assert.Contains(build.Plan.Decisions, d => d.StartsWith("[CGF-VALUE-001]", StringComparison.Ordinal)
            && d.Contains("Capped fish sale values above 100,000,000: Cuddlefish 5,000,000,000 -> 100,000,000 (Demo)"));
        Assert.Contains(build.Plan.Decisions, d => d.Contains("Cuddlefish XP_VALUE") && d.Contains("5,000,000,000"));
        using (var merged = GameFileLoader.Load(output))
        {
            Assert.Contains("BASE_VALUE = 100000000;", Read(merged, "gml_Object_Cuddlefish_Create_0"));
            Assert.Contains("min(random(", Read(merged, "gml_Object_Cuddlefish_Step_0"));
            string script = Read(merged, "gml_Script_fish_bonus");
            Assert.Contains("BASE_VALUE = min(BASE_VALUE + ", script);
            Assert.Contains("jamm_guard_fish_sale(argument0, random(", Read(merged, "gml_Script_apply_dynamic_fish_value"));
            Assert.Contains("BASE_VALUE = 100000000;", Read(merged, "gml_Object_CatalogFish_Create_0"));
            Assert.Contains("BASE_VALUE = 5000000000;", Read(merged, "gml_Object_NotAFish_Create_0"));
            Assert.Contains("100000000)", Read(merged, "gml_Script_jamm_init_demo"));
            Assert.Contains("min(value, 100000000)", Read(merged, "gml_Script_fish_appraise"));
        }

        var intermediate = ArchiveBuilder.BuildLayer(original, [input], temp.PathFor("intermediate.win"), null,
            false, null, default, null, noRepack: true, intermediate: true);
        Assert.DoesNotContain(intermediate.Plan.Decisions, d => d.Contains("[CGF-VALUE-001]"));
        string off = temp.PathFor("off.win");
        ArchiveBuilder.Build(original, [input], off, valueGuard: new(0, false));
        using (var untouched = GameFileLoader.Load(off))
            Assert.Contains("BASE_VALUE = 5000000000;", Read(untouched, "gml_Object_Cuddlefish_Create_0"));

        string copy = temp.PathFor("vanilla.win");
        ArchiveBuilder.Build(original, [], copy, valueGuard: new(1));
        Assert.Equal(BuildFiles.Hash(original), BuildFiles.Hash(copy));

        static void Setup(UndertaleData d, bool modded)
        {
            var fish = GuardObject(d, "Fish");
            var cuddlefish = GuardObject(d, "Cuddlefish", fish);
            var catalogFish = GuardObject(d, "CatalogFish");
            var nonFish = GuardObject(d, "NotAFish");
            var catalog = GuardObject(d, "CatalogData");
            var compiler = new CompileGroup(d);
            string extreme = "5000000000";
            compiler.QueueCodeReplace(cuddlefish.EventHandlerFor(EventType.Create, d), modded
                ? $"BASE_VALUE = {extreme}; XP_VALUE = {extreme};" : "BASE_VALUE = 50; XP_VALUE = 50;");
            compiler.QueueCodeReplace(catalogFish.EventHandlerFor(EventType.Create, d), modded ? $"BASE_VALUE = {extreme};" : "BASE_VALUE = 50;");
            compiler.QueueCodeReplace(nonFish.EventHandlerFor(EventType.Create, d), modded ? $"BASE_VALUE = {extreme};" : "BASE_VALUE = 50;");
            compiler.QueueCodeReplace(catalog.EventHandlerFor(EventType.Create, d), "object[0, 0] = CatalogFish; name[0, 0] = \"Demo\"; map[0, 0] = 0;");
            compiler.QueueCodeReplace(GuardScript(d, "fish_appraise"), "value = BASE_VALUE * 2;");
            if (modded)
            {
                compiler.QueueCodeReplace(GuardScript(d, "fish_bonus"), "BASE_VALUE += random(1000000000);");
                compiler.QueueCodeReplace(GuardScript(d, "apply_dynamic_fish_value"), "argument0.BASE_VALUE = random(1000000000);");
                compiler.QueueCodeReplace(cuddlefish.EventHandlerFor(EventType.Step, 0u, d),
                    "if (active) BASE_VALUE = random(1000000000); fish_bonus();");
                compiler.QueueCodeReplace(GuardScript(d, "jamm_fish_register"), "return -1;");
                compiler.QueueCodeReplace(GuardScript(d, "jamm_init_demo"), """
                    jamm_fish_register("demo:cuddle", Cuddlefish, 0, "Cuddle", "Desc", 0, 10, 0, 10, 1, "", "", "", "", "", 0, 5000000000);
                    return 0;
                    """);
            }
            CheckCompile(compiler);
        }
    }

    [Fact]
    public void FairPlayGuardsDebugStatsAndPaidPricesWhilePreservingMoneyAndProgression()
    {
        using var temp = new TemporaryDirectory();
        string original = temp.PathFor("base.win"), mod = temp.PathFor("mod.win"), output = temp.PathFor("merged.win");
        WriteModel(original, d => Setup(d, false));
        WriteModel(mod, d => Setup(d, true));
        var res = ArchiveBuilder.Build(original, [Input(original, mod)], output);
        string[] decisions = res.Plan.Decisions.Where(d => d.Contains("[CGF-FAIR-001]")).ToArray();
        Assert.All(new[] { "rod.rod_power", "rod.cast_speed", "boat.move_speed", "lure.sink_speed", "upgrade.effect[1]", "price.RodData", "rod.sinker_distance" },
            stat => Assert.Contains(decisions, d => d.Contains(stat) && d.Contains("Demo")));
        Assert.Contains(res.Plan.Decisions, d => d.Contains("[CGF-FAIR-002]") && d.Contains("isolated value without a clear debug signature"));
        using var merged = GameFileLoader.Load(output);
        string rod = Read(merged, "gml_Object_RodData_Create_0");
        Assert.Contains("cost[0] = 100;", rod);
        Assert.Contains("cost[1] = 0;", rod);
        Assert.Contains("rod_power_flat[0] = 200;", rod);
        Assert.Contains("cast_speed[0] = 500;", rod);
        Assert.Contains("cast_speed[1] = 250;", rod);
        Assert.Contains("rod_power[0] = 30;", rod);
        Assert.Contains("jamm_guard_stat(random(1000000000)", rod);
        Assert.Contains("upgrades[1] += 20;", Read(merged, "gml_Object_Rod_Other_10"));
        Assert.Contains("dollars = 5000000000;", Read(merged, "gml_Object_Girl_Create_0"));

        static void Setup(UndertaleData data, bool modded)
        {
            var compiler = new CompileGroup(data);
            compiler.QueueCodeReplace(GuardObject(data, "RodData").EventHandlerFor(EventType.Create, data), modded ? """
                cost[0] = 0; cost[1] = 0;
                rod_power[0] = 9999999; rod_power_flat[0] = 200;
                cast_speed[0] = 500; cast_speed[1] = 250;
                sinker_distance[0] = random(1000000000);
                """ : """
                cost[0] = 100; cost[1] = 0;
                rod_power[0] = 3; rod_power_flat[0] = 2;
                cast_speed[0] = 10; cast_speed[1] = 12; sinker_distance[0] = 1000;
                """);
            compiler.QueueCodeReplace(GuardObject(data, "Boat").EventHandlerFor(EventType.Create, data),
                modded ? "move_speed = 5000000000;" : "move_speed = 10;");
            compiler.QueueCodeReplace(GuardObject(data, "Lure").EventHandlerFor(EventType.Create, data),
                modded ? "SINK_SPEED = 999999;" : "SINK_SPEED = 5;");
            compiler.QueueCodeReplace(GuardObject(data, "Rod").EventHandlerFor(EventType.Other, 10u, data),
                modded ? "upgrades[1] += 9999999;" : "upgrades[1] += 2;");
            compiler.QueueCodeReplace(GuardObject(data, "Girl").EventHandlerFor(EventType.Create, data),
                modded ? "dollars = 5000000000;" : "dollars = 0;");
            if (modded)
            {
                compiler.QueueCodeReplace(GuardScript(data, "jamm_rod_register"), "return -1;");
                compiler.QueueCodeReplace(GuardScript(data, "register_test_rod"),
                    """jamm_rod_register("demo:rod", "Rod", 250, 999999999, 2, 1, 7, 2000, -1);""");
            }
            CheckCompile(compiler);
        }
    }

    [Fact]
    public void MissingRodEffectSlotsReadAsZeroWithBothValueGuardsOff()
    {
        using var temp = new TemporaryDirectory();
        string original = temp.PathFor("base.win"), mod = temp.PathFor("mod.win"), output = temp.PathFor("merged.win");
        WriteModel(original, d => Setup(d, false));
        WriteModel(mod, d => Setup(d, true));
        var res = ArchiveBuilder.Build(original, [Input(original, mod)], output, valueGuard: new(0, false));
        Assert.Contains(res.Plan.Decisions, d => d.StartsWith("[CGF-CONSOLE-001]", StringComparison.Ordinal));
        using var merged = GameFileLoader.Load(output);
        Assert.Contains("jamm_rod_effect_at(id, 22)", Read(merged, "gml_Object_Rod_Step_0"));
        Assert.Contains("jamm_rod_effect_at(", Read(merged, "gml_Object_Camera_Step_0"));
        Assert.Contains("argument1 < array_length_1d(upgrades)", Read(merged, "gml_Script_jamm_rod_effect_at"));

        static void Setup(UndertaleData data, bool modded)
        {
            var compiler = new CompileGroup(data);
            var rod = GuardObject(data, "Rod");
            var camera = GuardObject(data, "Camera");
            compiler.QueueCodeReplace(rod.EventHandlerFor(EventType.Other, 10u, data),
                "upgrades[0] = 0; upgrades[1] = 0;");
            compiler.QueueCodeReplace(rod.EventHandlerFor(EventType.Step, 0u, data),
                modded ? "effect = upgrades[22];" : "effect = upgrades[1];");
            compiler.QueueCodeReplace(camera.EventHandlerFor(EventType.Step, 0u, data),
                modded ? "zoom = Rod.upgrades[22];" : "zoom = Rod.upgrades[1];");
            CheckCompile(compiler);
        }
    }

    private static UndertaleGameObject GuardObject(UndertaleData data, string name, UndertaleGameObject? parent = null)
    {
        var obj = new UndertaleGameObject { Name = data.Strings.MakeString(name), ParentId = parent, Visible = true };
        data.GameObjects.Add(obj);
        return obj;
    }

    private static UndertaleCode GuardScript(UndertaleData data, string name)
    {
        var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
        data.Scripts.Add(new() { Name = data.Strings.MakeString(name), Code = code });
        return code;
    }

    private static void CheckCompile(CompileGroup compiler)
    {
        var result = compiler.Compile();
        if (!result.Successful) Assert.Fail(result.PrintAllErrors(true));
    }

    private static BuildInput Input(string original, string mod)
    {
        using var a = GameFileLoader.Load(original);
        using var b = GameFileLoader.Load(mod);
        return new(PatchBuilder.Create(a, b, "Demo"), mod);
    }

    private static string Read(LoadedGameFile file, string entry) => PatchBuilder.ReadCode(file, entry).Text;
}
