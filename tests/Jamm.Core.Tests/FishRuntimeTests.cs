using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies FishRuntime standalone jamm_api_init fallback, jamm_fish_register, and 22b coordination.</summary>
public sealed class FishRuntimeTests
{
    [Fact]
    public void FishOnlyInitializerDoesNotActivateItemSavePatches()
    {
        const string name = "gml_Script_jamm_init_demo";
        const string gml = "jamm_fish_register(\"demo:f\", DemoFish, 0, \"F\", \"Desc\", 0, 10, 1400, 1600, 1);\nreturn 0;";
        var snapshot = AssetFields.Json(new { });
        var source = new CodeText(gml, "gml", 0, 0, null, []);
        var plan = new BuildPlan([new("code", name, 0, new(name, null, snapshot, [], null, source), snapshot, gml)], []);
        var baseline = new Dictionary<string, CodeText>
        {
            ["gml_Object_SaveManager_Other_13"] = new("return 0;", "gml", 0, 0, null, [])
        };
        Assert.Same(plan, ItemsRuntime.PreparePlan(plan, baseline));
    }

    [Fact]
    public void FishRuntimeInjectsRegisterStandaloneApiInitAndNameLookup()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        string output = temp.PathFor("merged.win");

        WriteSyntheticGame(basePath, includeApiInit: false);
        WriteSyntheticGame(modPath, includeApiInit: false, d =>
        {
            var fishParent = d.GameObjects.ByName("FishSmall")!;
            d.GameObjects.Add(new() { Name = d.Strings.MakeString("jmk_demo_minnow"), ParentId = fishParent });
            var fishCreate = d.GameObjects.ByName("jmk_demo_minnow")!.EventHandlerFor(EventType.Create, 0u, d);

            var regCode = UndertaleCode.CreateEmptyEntry(d, "gml_Script_jamm_fish_register");
            d.Scripts.Add(new() { Name = d.Strings.MakeString("jamm_fish_register"), Code = regCode });

            var initCode = UndertaleCode.CreateEmptyEntry(d, "gml_Script_jamm_init_demo");
            d.Scripts.Add(new() { Name = d.Strings.MakeString("jamm_init_demo"), Code = initCode });

            var compiler = new CompileGroup(d);
            compiler.QueueCodeReplace(fishCreate, "instance_destroy();\n");
            compiler.QueueCodeReplace(regCode, "return -1;\n");
            compiler.QueueCodeReplace(initCode, "jamm_fish_register(\"demo:minnow\", jmk_demo_minnow, 0, \"Minnow\", \"Tiny fish.\", 1100, 1500, 1400, 1700, 3);\nreturn 0;\n");
            Assert.True(compiler.Compile().Successful);
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "DemoFishMod");

        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d => d.Contains("Fish API:", StringComparison.Ordinal));

        using var merged = GameFileLoader.Load(output);
        string reg = PatchBuilder.ReadCode(merged, FishRuntime.RegisterCode).Text;
        Assert.Contains("instance_number(", reg);
        Assert.Contains("instance_create(", reg);
        int createPos = reg.IndexOf("jamm_inst = instance_create(", StringComparison.Ordinal);
        Assert.True(createPos >= 0);
        int guardPos = reg.IndexOf("if (instance_exists(jamm_inst))", createPos, StringComparison.Ordinal);
        int metadataPos = reg.IndexOf("jamm_inst.start_x =", createPos, StringComparison.Ordinal);
        int catalogPos = reg.IndexOf("jamm_catalog_register(", StringComparison.Ordinal);
        Assert.True(guardPos > createPos && metadataPos > guardPos);
        Assert.True(catalogPos > metadataPos);
        Assert.Contains("TRUE_START_X", reg);
        Assert.Contains("jamm_fish_names", reg);
        Assert.Contains("CatalogData", reg);
        Assert.Contains("jamm_catalog_register(", reg);

        string apiInit = PatchBuilder.ReadCode(merged, FishRuntime.ApiInitCode).Text;
        Assert.Contains("jamm_init_demo()", apiInit);

        string saveStep = PatchBuilder.ReadCode(merged, FishRuntime.SaveManagerStep).Text;
        Assert.Contains("jamm_api_done", saveStep);
        Assert.Contains("jamm_api_init()", saveStep);
        Assert.True(saveStep.IndexOf("jamm_api_init()", StringComparison.Ordinal) < saveStep.IndexOf("if (!loaded)", StringComparison.Ordinal));

        string toName = PatchBuilder.ReadCode(merged, FishRuntime.FishObjectToName).Text;
        Assert.Contains("jamm_fish_names", toName);
    }

    [Fact]
    public void FishRuntimeCoordinatesWithExistingApiInitWithoutDuplicating()
    {
        var snapshot = JsonSerializer.SerializeToElement(new { });
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [CatalogMerge.Create] = new("object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\njamm_catalog_notice = 0;\njamm_catalog_legacy[0, 0] = 0;\n", "gml", 0, 0, null, []),
            [FishRuntime.SaveManagerStep] = new("// already wired by 22b\njamm_api_init();\nif (!loaded) { loaded = 1; }\n", "gml", 0, 0, null, []),
            [FishRuntime.ApiInitCode] = new("jamm_init_items();\njamm_init_fish();\n", "gml", 0, 0, null, []),
            [FishRuntime.FishObjectToName] = new("return object_get_name(argument0);\n", "gml", 0, 0, null, [])
        };

        var assets = new List<PlannedAsset>
        {
            new("code", "gml_Script_jamm_init_fish", 0, new("gml_Script_jamm_init_fish", null, snapshot, [], null, new("jamm_fish_register(\"a:b\", MyFish, 0, \"B\", \"d\", 0, 10, 0, 10, 1);", "gml", 0, 0, null, [])), snapshot, "jamm_fish_register(\"a:b\", MyFish, 0, \"B\", \"d\", 0, 10, 0, 10, 1);")
        };

        var plan = FishRuntime.PreparePlan(new BuildPlan(assets, []), baseline);
        Assert.Contains(plan.Assets, a => a.Name == FishRuntime.RegisterCode);
        Assert.Contains(plan.Assets, a => a.Name == FishRuntime.FishObjectToName);
        Assert.DoesNotContain(plan.Assets, a => a.Name == FishRuntime.ApiInitCode);
        Assert.DoesNotContain(plan.Assets, a => a.Name == FishRuntime.SaveManagerStep);
        Assert.Contains(plan.Decisions, d => d.Contains("existing jamm_api_init", StringComparison.Ordinal));
    }

    [Fact]
    public void FishRuntimeLeavesBuildsWithoutFishApiUntouched()
    {
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [FishRuntime.SaveManagerStep] = new("if (!loaded) { loaded = 1; }\n", "gml", 0, 0, null, [])
        };
        var input = new BuildPlan([], []);
        var result = FishRuntime.PreparePlan(input, baseline);
        Assert.Empty(result.Assets);
        Assert.Empty(result.Decisions);
    }

    [Fact]
    public void FishApiModAlsoActivatesCatalogRuntimeNameKeyedSavePersistence()
    {
        var snapshot = JsonSerializer.SerializeToElement(new { });
        var baseline = new Dictionary<string, CodeText>(StringComparer.Ordinal)
        {
            [CatalogMerge.Create] = new("object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\n", "gml", 0, 0, null, []),
            [FishRuntime.SaveManagerStep] = new("if (!loaded) { loaded = 1; }\n", "gml", 0, 0, null, []),
            [FishRuntime.FishObjectToName] = new("return object_get_name(argument0);\n", "gml", 0, 0, null, []),
            ["gml_Object_SaveManager_Other_13"] = new(
                "var i = 0, j = 0;\ncount[i, j] = ini_read_real(\"Catalog\", \"f\" + string(i) + string(j), 0);\nlife[i, j] = ini_read_real(\"Catalog\", \"li\" + string(i) + string(j), 3);\n",
                "gml", 0, 0, null, []),
            ["gml_Object_SaveManager_Other_14"] = new(
                "var i = 0, j = 0;\nini_write_real(\"Catalog\", \"f\" + string(i) + string(j), count[i, j]);\nini_write_real(\"Catalog\", \"li\" + string(i) + string(j), life[i, j]);\n",
                "gml", 0, 0, null, [])
        };

        var assets = new List<PlannedAsset>
        {
            new("code", "gml_Script_jamm_init_demo", 0, new("gml_Script_jamm_init_demo", null, snapshot, [], null, new("jamm_fish_register(\"demo:minnow\", jmk_demo_minnow, 0, \"Minnow\", \"Tiny fish.\", 1100, 1500, 1400, 1700, 3);", "gml", 0, 0, null, [])), snapshot, "jamm_fish_register(\"demo:minnow\", jmk_demo_minnow, 0, \"Minnow\", \"Tiny fish.\", 1100, 1500, 1400, 1700, 3);")
        };

        var plan = CatalogRuntime.PreparePlan(FishRuntime.PreparePlan(new BuildPlan(assets, []), baseline), baseline);
        string mergedRead = plan.Assets.Single(a => a.Name == "gml_Object_SaveManager_Other_13").Gml!;
        string mergedWrite = plan.Assets.Single(a => a.Name == "gml_Object_SaveManager_Other_14").Gml!;
        Assert.Contains("jamm_catalog_read(\"f\"", mergedRead);
        Assert.Contains("jamm_catalog_read(\"li\"", mergedRead);
        Assert.Contains("\"JAMM_Catalog\"", mergedWrite);
        Assert.Contains("\"f:\" + object_get_name(object[i, j])", mergedWrite);
        Assert.Contains("\"li:\" + object_get_name(object[i, j])", mergedWrite);
    }

    private static void WriteSyntheticGame(string path, bool includeApiInit, Action<UndertaleData>? edit = null)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.GeneralInfo.Build = 1773;

        foreach (string name in new[] { "Fish", "FishSmall", "BaseFish", "CatalogData", "SaveManager" })
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        data.GameObjects.ByName("FishSmall")!.ParentId = data.GameObjects.ByName("Fish");
        data.GameObjects.ByName("BaseFish")!.ParentId = data.GameObjects.ByName("FishSmall");

        var catalogCreate = data.GameObjects.ByName("CatalogData")!.EventHandlerFor(EventType.Create, 0u, data);
        var saveStep = data.GameObjects.ByName("SaveManager")!.EventHandlerFor(EventType.Step, 0u, data);
        var toNameCode = UndertaleCode.CreateEmptyEntry(data, FishRuntime.FishObjectToName);
        data.Scripts.Add(new() { Name = data.Strings.MakeString("fish_object_to_name"), Code = toNameCode });

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(catalogCreate,
            """
            object[0, 0] = BaseFish;
            name[0, 0] = "Base";
            map[0, 0] = 0;
            desc[0, 0] = "Base desc";
            """);
        compiler.QueueCodeReplace(saveStep,
            """
            if (!loaded)
            {
                loaded = 1;
            }
            """);
        compiler.QueueCodeReplace(toNameCode,
            """
            var n;
            n = string_replace_all(object_get_name(argument0), "_", " ");
            return n;
            """);
        if (includeApiInit)
        {
            var apiCode = UndertaleCode.CreateEmptyEntry(data, FishRuntime.ApiInitCode);
            data.Scripts.Add(new() { Name = data.Strings.MakeString(FishRuntime.ApiInitScript), Code = apiCode });
            compiler.QueueCodeReplace(apiCode, "return 0;\n");
        }
        Assert.True(compiler.Compile().Successful);

        edit?.Invoke(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgfmm-fish-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => System.IO.Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
