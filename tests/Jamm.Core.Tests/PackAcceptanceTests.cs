using System.Text.Json;
using Jamm.Cli;
using Jamm.Core.Acceptance;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Per-mod pack acceptance reporting, runtime risk checks, and CLI output/JSON serialization.</summary>
public sealed class PackAcceptanceTests
{
    [Fact]
    public void AddedAssetsAndSharedConflictReportPresentAndModifiedByOther()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modAPath = temp.PathFor("modA.win");
        string modBPath = temp.PathFor("modB.win");

        WriteSyntheticArchive(basePath);
        WriteSyntheticArchive(modAPath, d =>
        {
            AddObject(d, "obj_alpha");
            AddSprite(d, "spr_alpha");
            AddSound(d, "snd_alpha");
            AddRoom(d, "rm_alpha");
            AddScript(d, "scr_alpha", "return 1;");
            AddObject(d, "obj_shared", depth: 10);
            AddScript(d, "scr_shared", "return 10;");
        });
        WriteSyntheticArchive(modBPath, d =>
        {
            AddObject(d, "obj_shared", depth: 99);
            AddScript(d, "scr_shared", "return 99;");
        });

        using var original = GameFileLoader.Load(basePath);
        using var modA = GameFileLoader.Load(modAPath);
        using var modB = GameFileLoader.Load(modBPath);
        var pa = PatchBuilder.Create(original, modA, "ModA");
        var pb = PatchBuilder.Create(original, modB, "ModB");

        string output = temp.PathFor("merged.win");
        var result = ArchiveBuilder.Build(basePath, [new(pa, modAPath), new(pb, modBPath)], output);
        Assert.NotNull(result.Acceptance);

        var repA = result.Acceptance.Mods.Single(m => m.Mod == "ModA");
        var repB = result.Acceptance.Mods.Single(m => m.Mod == "ModB");

        Assert.Equal(AcceptanceStatus.Present, repA.Items.Single(i => i.Kind == AcceptanceKind.Object && i.Identity == "obj_alpha").Status);
        Assert.Equal(AcceptanceStatus.Present, repA.Items.Single(i => i.Kind == AcceptanceKind.Sprite && i.Identity == "spr_alpha").Status);
        Assert.Equal(AcceptanceStatus.Present, repA.Items.Single(i => i.Kind == AcceptanceKind.Sound && i.Identity == "snd_alpha").Status);
        Assert.Equal(AcceptanceStatus.Present, repA.Items.Single(i => i.Kind == AcceptanceKind.Room && i.Identity == "rm_alpha").Status);
        Assert.Equal(AcceptanceStatus.Present, repA.Items.Single(i => i.Kind == AcceptanceKind.Script && i.Identity == "scr_alpha").Status);

        Assert.Equal(AcceptanceStatus.ModifiedByOther, repA.Items.Single(i => i.Kind == AcceptanceKind.Object && i.Identity == "obj_shared").Status);
        Assert.Equal(AcceptanceStatus.ModifiedByOther, repA.Items.Single(i => i.Kind == AcceptanceKind.Script && i.Identity == "scr_shared").Status);
        Assert.Equal(AcceptanceStatus.Present, repB.Items.Single(i => i.Kind == AcceptanceKind.Object && i.Identity == "obj_shared").Status);
        Assert.Equal(AcceptanceStatus.Present, repB.Items.Single(i => i.Kind == AcceptanceKind.Script && i.Identity == "scr_shared").Status);
    }

    [Fact]
    public void DistinctBoatsOnOneSlotBothArriveAfterRelocation()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modAPath = temp.PathFor("modA.win");
        string modBPath = temp.PathFor("modB.win");

        WriteSyntheticArchive(basePath, d =>
        {
            AddObject(d, "BoatData");
            AddObject(d, "Boat1");
            CompileObjectCreate(d, "BoatData", """
                unlocked[0] = 1;
                name[0] = "No Boat";
                object[0] = -1;
                unlocked[1] = 0;
                name[1] = "Basic";
                object[1] = Boat1;
                nItems = 2;
                """);
        });
        WriteSyntheticArchive(modAPath, d =>
        {
            AddObject(d, "BoatData");
            AddObject(d, "Boat1");
            AddObject(d, "BoatAlpha");
            CompileObjectCreate(d, "BoatData", """
                unlocked[0] = 1;
                name[0] = "No Boat";
                object[0] = -1;
                unlocked[1] = 0;
                name[1] = "Basic";
                object[1] = Boat1;
                unlocked[2] = 0;
                name[2] = "Alpha Boat";
                object[2] = BoatAlpha;
                nItems = 3;
                """);
        });
        WriteSyntheticArchive(modBPath, d =>
        {
            AddObject(d, "BoatData");
            AddObject(d, "Boat1");
            AddObject(d, "BoatBeta");
            CompileObjectCreate(d, "BoatData", """
                unlocked[0] = 1;
                name[0] = "No Boat";
                object[0] = -1;
                unlocked[1] = 0;
                name[1] = "Basic";
                object[1] = Boat1;
                unlocked[2] = 0;
                name[2] = "Beta Boat";
                object[2] = BoatBeta;
                nItems = 3;
                """);
        });

        using var original = GameFileLoader.Load(basePath);
        using var modA = GameFileLoader.Load(modAPath);
        using var modB = GameFileLoader.Load(modBPath);
        var pa = PatchBuilder.Create(original, modA, "ModA");
        var pb = PatchBuilder.Create(original, modB, "ModB");

        string output = temp.PathFor("merged.win");
        var result = ArchiveBuilder.Build(basePath, [new(pa, modAPath), new(pb, modBPath)], output);
        Assert.NotNull(result.Acceptance);

        var repA = result.Acceptance.Mods.Single(m => m.Mod == "ModA");
        var repB = result.Acceptance.Mods.Single(m => m.Mod == "ModB");

        // Distinct boats on the same slot are relocated (CGF-RELOCATE-001): both survive and no hunk is lost.
        var boatA = Assert.Single(repA.Items, i => i.Kind == AcceptanceKind.Boat && i.Identity == "BoatAlpha");
        Assert.Equal(AcceptanceStatus.Present, boatA.Status);
        Assert.DoesNotContain(repA.Items, i => i.Kind == AcceptanceKind.Hunk);

        var boatB = Assert.Single(repB.Items, i => i.Kind == AcceptanceKind.Boat && i.Identity == "BoatBeta");
        Assert.Equal(AcceptanceStatus.Relocated, boatB.Status);
        Assert.DoesNotContain(repB.Items, i => i.Kind == AcceptanceKind.Hunk);
        Assert.Contains(result.Plan.Decisions, d => d.StartsWith("[CGF-RELOCATE-001]", StringComparison.Ordinal));
    }

    [Fact]
    public void FishRelocatedInCatalogStillCountsAsArrived()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modAPath = temp.PathFor("modA.win");
        string modBPath = temp.PathFor("modB.win");

        const string createName = "gml_Object_CatalogData_Create_0";
        WriteSyntheticArchive(basePath, d =>
        {
            AddObject(d, "CatalogData");
            AddObject(d, "BaseFish");
            CompileCode(d, createName, "object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"Base\";");
        });
        WriteSyntheticArchive(modAPath, d =>
        {
            AddObject(d, "CatalogData");
            AddObject(d, "BaseFish");
            AddObject(d, "AlphaFish");
            CompileCode(d, createName, "object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"Base\";\nobject[0, 1] = AlphaFish;\nname[0, 1] = \"Alpha\";\nmap[0, 1] = 1;\ndesc[0, 1] = \"Alpha\";");
        });
        WriteSyntheticArchive(modBPath, d =>
        {
            AddObject(d, "CatalogData");
            AddObject(d, "BaseFish");
            AddObject(d, "BetaFish");
            CompileCode(d, createName, "object[0, 0] = BaseFish;\nname[0, 0] = \"Base\";\nmap[0, 0] = 0;\ndesc[0, 0] = \"Base\";\nobject[0, 1] = BetaFish;\nname[0, 1] = \"Beta\";\nmap[0, 1] = 1;\ndesc[0, 1] = \"Beta\";");
        });

        using var original = GameFileLoader.Load(basePath);
        using var modA = GameFileLoader.Load(modAPath);
        using var modB = GameFileLoader.Load(modBPath);
        var pa = PatchBuilder.Create(original, modA, "Alpha");
        var pb = PatchBuilder.Create(original, modB, "Beta");

        string output = temp.PathFor("merged.win");
        var result = ArchiveBuilder.Build(basePath, [new(pa, modAPath), new(pb, modBPath)], output);
        Assert.NotNull(result.Acceptance);

        var repA = result.Acceptance.Mods.Single(m => m.Mod == "Alpha");
        var repB = result.Acceptance.Mods.Single(m => m.Mod == "Beta");

        var fishA = Assert.Single(repA.Items, i => i.Kind == AcceptanceKind.Fish && i.Identity == "AlphaFish");
        Assert.Equal(AcceptanceStatus.Present, fishA.Status);

        var fishB = Assert.Single(repB.Items, i => i.Kind == AcceptanceKind.Fish && i.Identity == "BetaFish");
        Assert.Equal(AcceptanceStatus.Relocated, fishB.Status);
        string debugItems = string.Join("; ", repB.Items.Select(i => $"{i.Kind}:{i.Identity}={i.Status}({i.Detail})"));
        Assert.True(repB.TotalCount == repB.ArrivedCount, debugItems);
        Assert.Equal(0, repB.LostCount);

        string summary = TextReport.AcceptanceSummary(result.Acceptance);
        Assert.Contains($"Beta {repB.TotalCount}/{repB.TotalCount} present", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RoomPlacementCountsDetectPresentAndLostPlacements()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modAPath = temp.PathFor("modA.win");
        string modBPath = temp.PathFor("modB.win");

        WriteSyntheticArchive(basePath);
        WriteSyntheticArchive(modAPath, d =>
        {
            var fish = d.GameObjects.ByName("fish")!;
            var extra = AddObject(d, "obj_reef_fish");
            d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = fish, X = 100, Y = 100 });
            d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = extra, X = 200, Y = 100 });
            d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = extra, X = 300, Y = 100 });
        });
        WriteSyntheticArchive(modBPath, d =>
        {
            var extra = AddObject(d, "obj_cave_fish");
            d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = extra, X = 400, Y = 100 });
        });

        using var original = GameFileLoader.Load(basePath);
        using var modA = GameFileLoader.Load(modAPath);
        using var modB = GameFileLoader.Load(modBPath);
        var pa = PatchBuilder.Create(original, modA, "ModA");
        var pb = PatchBuilder.Create(original, modB, "ModB");

        string output = temp.PathFor("merged.win");
        var result = ArchiveBuilder.Build(basePath, [new(pa, modAPath), new(pb, modBPath)], output);
        Assert.NotNull(result.Acceptance);

        var repA = result.Acceptance.Mods.Single(m => m.Mod == "ModA");
        Assert.Equal(AcceptanceStatus.Present, repA.Items.Single(i => i.Kind == AcceptanceKind.Placement && i.Identity == "room0/fish").Status);
        Assert.Equal(AcceptanceStatus.Present, repA.Items.Single(i => i.Kind == AcceptanceKind.Placement && i.Identity == "room0/obj_reef_fish").Status);

        // Simulate a merged archive where ModA's room placement for obj_reef_fish was dropped.
        using var merged = GameFileLoader.Load(output);
        foreach (var inst in merged.Data.Rooms[0].GameObjects.Where(i => i.ObjectDefinition?.Name.Content == "obj_reef_fish").ToArray())
            merged.Data.Rooms[0].GameObjects.Remove(inst);
        var simulated = PackAcceptance.Analyze(
            original.Data,
            [new("ModA", "modA.win", modA.Data, pa), new("ModB", "modB.win", modB.Data, pb)],
            result.Plan,
            merged.Data);
        var simRepA = simulated.Mods.Single(m => m.Mod == "ModA");
        Assert.Equal(AcceptanceStatus.Lost, simRepA.Items.Single(i => i.Kind == AcceptanceKind.Placement && i.Identity == "room0/obj_reef_fish").Status);
    }

    [Fact]
    public void TableLengthAndLightOwnerAndInstanceRisksAreReported()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");

        WriteSyntheticArchive(basePath, d =>
        {
            AddObject(d, "BoatData");
            AddObject(d, "Boat1");
            CompileObjectCreate(d, "BoatData", """
                unlocked[0] = 1;
                name[0] = "No Boat";
                object[0] = -1;
                unlocked[1] = 0;
                name[1] = "Basic";
                object[1] = Boat1;
                nItems = 2;
                """);
        });
        WriteSyntheticArchive(modPath, d =>
        {
            AddObject(d, "BoatData");
            AddObject(d, "Boat1");
            AddObject(d, "Boat2");
            // Writes slot 2 (length 3) while leaving nItems = 2 -> TABLE-LENGTH risk.
            CompileObjectCreate(d, "BoatData", """
                unlocked[0] = 1;
                name[0] = "No Boat";
                object[0] = -1;
                unlocked[1] = 0;
                name[1] = "Basic";
                object[1] = Boat1;
                unlocked[2] = 0;
                name[2] = "Broken Boat";
                object[2] = Boat2;
                nItems = 2;
                """);

            AddObject(d, "obj_light");
            var glowHeavy = AddObject(d, "obj_glow_heavy");
            CompileObjectCreate(d, "obj_glow_heavy", "light = instance_create(x, y, obj_light);");
            for (int i = 0; i < 6; i++)
                d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = glowHeavy, X = 50 + i * 10, Y = 50 });

            var glowSafe = AddObject(d, "obj_glow_safe");
            CompileObjectCreate(d, "obj_glow_safe", "light = instance_create(x, y, obj_light);");
            for (int i = 0; i < 5; i++)
                d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = glowSafe, X = 200 + i * 10, Y = 50 });
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "RiskMod");

        string output = temp.PathFor("merged.win");
        var result = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);
        Assert.NotNull(result.Acceptance);

        Assert.Contains(result.Acceptance.Risks, r => r.Code == "TABLE-LENGTH" && r.Target == "BoatData");
        Assert.Contains(result.Acceptance.Risks, r => r.Code == "LIGHT-CULL-RISK" && r.Target == "room0/obj_glow_heavy");
        Assert.DoesNotContain(result.Acceptance.Risks, r => r.Code == "LIGHT-CULL-RISK" && r.Target == "room0/obj_glow_safe");

        // CGF-INSTANCE-002 decision surfaced as a risk.
        var planWithInstanceRisk = result.Plan with
        {
            Decisions = [.. result.Plan.Decisions, "WARNING [CGF-INSTANCE-002] gml_Object_Test_Step_0, merged GML line 4 (selected source RiskMod): fish.x may require a live instance."]
        };
        using var merged = GameFileLoader.Load(output);
        var withInstanceRisk = PackAcceptance.Analyze(
            original.Data,
            [new("RiskMod", "mod.win", mod.Data, patch)],
            planWithInstanceRisk,
            merged.Data);
        Assert.Contains(withInstanceRisk.Risks, r => r.Code == "CGF-INSTANCE-002" && r.Target == "gml_Object_Test_Step_0");
    }

    [Fact]
    public void BuildCliSupportsAcceptanceJsonAndVerboseAndPreservesOutputHash()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("mod.win");
        WriteSyntheticArchive(basePath);
        WriteSyntheticArchive(modPath, d =>
        {
            AddObject(d, "obj_cli_test");
            d.Rooms[0].GameObjects.Add(new() { ObjectDefinition = d.GameObjects.ByName("obj_cli_test")!, X = 40, Y = 40 });
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "CliMod");

        string directOut = temp.PathFor("direct.win");
        var directBuild = ArchiveBuilder.Build(basePath, [new(patch, modPath)], directOut);
        string dryOut = temp.PathFor("dry.win");
        var dryBuild = ArchiveBuilder.Build(basePath, [new(patch, modPath)], dryOut, dryRun: true);

        Assert.NotNull(directBuild.Acceptance);
        Assert.NotNull(dryBuild.Acceptance);
        Assert.Equal(directBuild.Acceptance.Mods[0].Items, dryBuild.Acceptance.Mods[0].Items);

        string cliOut = temp.PathFor("cli.win");
        string jsonOut = temp.PathFor("acceptance.json");
        var originalOut = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            int exit = BuildCommand.Run("build", ["--base", basePath, "--order", modPath, "--out", cliOut, "--acceptance-json", jsonOut], verbose: true);
            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(BuildFiles.Hash(directOut), BuildFiles.Hash(cliOut));
        string stdout = captured.ToString();
        Assert.Contains("Pack acceptance: 1 mod", stdout, StringComparison.Ordinal);
        Assert.Contains("[Present] object obj_cli_test", stdout, StringComparison.Ordinal);
        Assert.True(File.Exists(jsonOut));

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonOut));
        Assert.Equal(1, doc.RootElement.GetProperty("mods").GetArrayLength());
    }

    [Fact]
    public void JammApiRegistrationsAreCountedAndReportedPresent()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string modPath = temp.PathFor("api-mod.win");

        WriteSyntheticArchive(basePath, d =>
        {
            foreach (string name in new[] { "BoatData", "CatalogData", "SaveManager", "Girl", "Boat", "Analytics", "GameModeMenu" })
                AddObject(d, name);
            CompileCode(d, "gml_Object_BoatData_Create_0", "unlocked[0] = 1; name[0] = \"No Boat\"; object[0] = -1; disp[0] = -1; desc[0] = \"\"; nItems = 1;");
            CompileCode(d, "gml_Object_CatalogData_Create_0", "object[0, 0] = fish; name[0, 0] = \"Fish\"; map[0, 0] = 0; desc[0, 0] = \"Base\";");
            CompileCode(d, "gml_Object_SaveManager_Step_0", "if (!loaded) { loaded = 1; }");
            CompileCode(d, "gml_Object_SaveManager_Other_13", "var sf = 1;\nvar lf = 1;\nif (sf > 0 || lf > 0)\n{\n    with (BoatData)\n    {\n        for (i = 0; i < nItems; i++)\n        {\n            unlocked[i] = ini_read_real(\"Unlocks\", \"boat\" + string(i), -1);\n        }\n    }\n    var bi = ini_read_real(\"Main\", \"boat\", -1);\n    if (bi > 0 && BoatData.object[bi] > 0) { instance_create(0, 0, BoatData.object[bi]); }\n}\nelse\n{\n    with (Analytics) { event_perform(ev_other, ev_user0); }\n}");
            CompileCode(d, "gml_Object_SaveManager_Other_14", "if (!saving_locked)\n{\n    ini_open(\"s.ini\");\n    with (BoatData)\n    {\n        for (i = 0; i < nItems; i++)\n        {\n            ini_write_real(\"Unlocks\", \"boat\" + string(i), unlocked[i]);\n        }\n    }\n    if (instance_exists(Boat)) { ini_write_real(\"Main\", \"boat\", Boat.boat_index); }\n    ini_close();\n    ini_open(\"meta.ini\");\n}");
            CompileCode(d, "gml_Object_SaveManager_Other_15", "if (!saving_locked)\n{\n    ini_open(\"b.ini\");\n    with (BoatData)\n    {\n        for (i = 0; i < nItems; i++)\n        {\n            ini_write_real(\"Unlocks\", \"boat\" + string(i), unlocked[i]);\n        }\n    }\n    ini_close();\n    ini_open(\"meta.ini\");\n}");
        });

        WriteSyntheticArchive(modPath, d =>
        {
            foreach (string name in new[] { "BoatData", "CatalogData", "SaveManager", "Girl", "Boat", "Analytics", "GameModeMenu" })
                AddObject(d, name);
            AddObject(d, "obj_api_boat");
            AddObject(d, "obj_api_fish");
            AddScript(d, "jamm_boat_register", "return 0;");
            AddScript(d, "jamm_fish_register", "return 0;");
            AddScript(d, "jamm_init_api_demo", """
                jamm_boat_register("demo:boat", "API Boat", 1000, -1, obj_api_boat, "Boat");
                jamm_fish_register("demo:fish", obj_api_fish, 0, "API Fish", "Fish", 0, 100, 0, 100, 2);
                """);
            CompileCode(d, "gml_Object_BoatData_Create_0", "unlocked[0] = 1; name[0] = \"No Boat\"; object[0] = -1; disp[0] = -1; desc[0] = \"\"; nItems = 1;");
            CompileCode(d, "gml_Object_CatalogData_Create_0", "object[0, 0] = fish; name[0, 0] = \"Fish\"; map[0, 0] = 0; desc[0, 0] = \"Base\";");
            CompileCode(d, "gml_Object_SaveManager_Step_0", "if (!loaded) { loaded = 1; }");
            CompileCode(d, "gml_Object_SaveManager_Other_13", "var sf = 1;\nvar lf = 1;\nif (sf > 0 || lf > 0)\n{\n    with (BoatData)\n    {\n        for (i = 0; i < nItems; i++)\n        {\n            unlocked[i] = ini_read_real(\"Unlocks\", \"boat\" + string(i), -1);\n        }\n    }\n    var bi = ini_read_real(\"Main\", \"boat\", -1);\n    if (bi > 0 && BoatData.object[bi] > 0) { instance_create(0, 0, BoatData.object[bi]); }\n}\nelse\n{\n    with (Analytics) { event_perform(ev_other, ev_user0); }\n}");
            CompileCode(d, "gml_Object_SaveManager_Other_14", "if (!saving_locked)\n{\n    ini_open(\"s.ini\");\n    with (BoatData)\n    {\n        for (i = 0; i < nItems; i++)\n        {\n            ini_write_real(\"Unlocks\", \"boat\" + string(i), unlocked[i]);\n        }\n    }\n    if (instance_exists(Boat)) { ini_write_real(\"Main\", \"boat\", Boat.boat_index); }\n    ini_close();\n    ini_open(\"meta.ini\");\n}");
            CompileCode(d, "gml_Object_SaveManager_Other_15", "if (!saving_locked)\n{\n    ini_open(\"b.ini\");\n    with (BoatData)\n    {\n        for (i = 0; i < nItems; i++)\n        {\n            ini_write_real(\"Unlocks\", \"boat\" + string(i), unlocked[i]);\n        }\n    }\n    ini_close();\n    ini_open(\"meta.ini\");\n}");
        });

        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "ApiDemo");
        string output = temp.PathFor("merged.win");
        var result = ArchiveBuilder.Build(basePath, [new(patch, modPath)], output);

        Assert.NotNull(result.Acceptance);
        var rep = Assert.Single(result.Acceptance.Mods);
        Assert.Equal(2, rep.ApiRegistrationCount);
        Assert.NotNull(result.Acceptance.ApiRegistrations);
        Assert.Equal(1, result.Acceptance.ApiRegistrations["jamm_boat_register"]);
        Assert.Equal(1, result.Acceptance.ApiRegistrations["jamm_fish_register"]);
        string debugItems = string.Join("; ", rep.Items.Select(i => $"{i.Kind}:{i.Identity}={i.Status}({i.Detail})"));
        var boatItem = rep.Items.FirstOrDefault(i => i.Kind == AcceptanceKind.Boat && i.Identity == "obj_api_boat");
        Assert.True(boatItem is not null, debugItems);
        Assert.Equal(AcceptanceStatus.Present, boatItem.Status);
        var fishItem = rep.Items.FirstOrDefault(i => i.Kind == AcceptanceKind.Fish && i.Identity == "obj_api_fish");
        Assert.True(fishItem is not null, debugItems);
        Assert.Equal(AcceptanceStatus.Present, fishItem.Status);
    }

    [RealFilesFact]
    public void RealBaseAbyssAndVerdantReportItemsAndRisksWithoutError()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string modsDir = Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!;
        string abyssPath = Path.Combine(modsDir, "the-abyss-beta.win");
        string cavePath = Path.Combine(modsDir, "cave-expansion.win");
        if (!File.Exists(abyssPath) || !File.Exists(cavePath)) return;

        using var temp = new TemporaryDirectory();
        using var original = GameFileLoader.Load(basePath);
        using var abyss = GameFileLoader.Load(abyssPath);
        using var cave = GameFileLoader.Load(cavePath);
        var inputs = new BuildInput[]
        {
            new(PatchBuilder.Create(original, abyss, "Abyss"), abyssPath),
            new(PatchBuilder.Create(original, cave, "Cave"), cavePath)
        };

        string outPath = temp.PathFor("real-merged.win");
        var result = ArchiveBuilder.Build(basePath, inputs, outPath);
        Assert.NotNull(result.Acceptance);
        Assert.Equal(2, result.Acceptance.Mods.Count);
        Assert.All(result.Acceptance.Mods, m => Assert.True(m.TotalCount > 0));
    }

    private static void WriteSyntheticArchive(string path, Action<UndertaleData>? edit = null)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;
        var code = UndertaleCode.CreateEmptyEntry(data, "test_code");
        CompileCode(data, "test_code", "x = 1;\ny = 2;\n");
        var fish = AddObject(data, "fish");
        data.Rooms[0].GameObjects.Add(new() { ObjectDefinition = fish, X = 10, Y = 20, InstanceID = 42 });
        edit?.Invoke(data);
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static UndertaleGameObject AddObject(UndertaleData d, string name, int depth = 0)
    {
        var existing = d.GameObjects.ByName(name);
        if (existing is not null)
        {
            existing.Depth = depth;
            return existing;
        }
        var obj = new UndertaleGameObject { Name = d.Strings.MakeString(name), Depth = depth };
        d.GameObjects.Add(obj);
        return obj;
    }

    private static void AddSprite(UndertaleData d, string name)
    {
        if (d.Sprites.ByName(name) is not null) return;
        d.Sprites.Add(new UndertaleSprite { Name = d.Strings.MakeString(name), Width = 4, Height = 4 });
    }

    private static void AddSound(UndertaleData d, string name)
    {
        if (d.Sounds.ByName(name) is not null) return;
        d.Sounds.Add(new UndertaleSound { Name = d.Strings.MakeString(name), AudioID = -1, GroupID = 0 });
    }

    private static void AddRoom(UndertaleData d, string name)
    {
        if (d.Rooms.ByName(name) is not null) return;
        d.Rooms.Add(new UndertaleRoom { Name = d.Strings.MakeString(name), Width = 800, Height = 480 });
    }

    private static void AddScript(UndertaleData d, string name, string gml)
    {
        string codeName = "gml_Script_" + name;
        var code = d.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(d, codeName);
        var existing = d.Scripts.ByName(name);
        if (existing is null)
            d.Scripts.Add(new UndertaleScript { Name = d.Strings.MakeString(name), Code = code });
        else
            existing.Code = code;
        CompileCode(d, codeName, gml);
    }

    private static void CompileObjectCreate(UndertaleData d, string objName, string gml)
    {
        var obj = d.GameObjects.ByName(objName)!;
        var code = obj.EventHandlerFor(EventType.Create, 0u, d);
        var compiler = new CompileGroup(d);
        compiler.QueueCodeReplace(code, gml);
        var result = compiler.Compile();
        Assert.True(result.Successful, result.PrintAllErrors(true));
    }

    private static void CompileCode(UndertaleData d, string codeName, string gml)
    {
        var code = d.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(d, codeName);
        var compiler = new CompileGroup(d);
        compiler.QueueCodeReplace(code, gml);
        var result = compiler.Compile();
        Assert.True(result.Successful, result.PrintAllErrors(true));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cgfmm-acceptance-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
