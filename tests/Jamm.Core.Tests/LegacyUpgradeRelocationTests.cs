using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Jamm.Core.Tests;

/// <summary>Legacy upgrade slot relocation, lineage selection, typed sink rewriting, count bounds, and icon atlas merging.</summary>
public sealed class LegacyUpgradeRelocationTests
{
    private const string Create = "gml_Object_UpgradeData_Create_0";
    private const string BaseCreate = """
        for (i = 0; i <= 17; i++)
        {
            unlocks[i] = 0;
        }
        name[0] = "Scouting";
        name[17] = "Flick";
        desc[17] = "Desc 17";
        cost[17] = 0.5;
        upg[0, 0] = -1;
        object[0, 0] = -1;
        """;

    [Fact]
    public void SharedIdenticalHunkIsNotRewrittenAndLineageOriginKeepsSlotEvenWhenLater()
    {
        // ModA (earlier) has a shared hunk in Rod_Step_0 PLUS a unique hunk in Camera_Step_0.
        // ModB (later) has ONLY the shared hunk in Rod_Step_0 -> ModB is the lineage origin, so ModA relocates 18 -> 19!
        const string rodStepBase = "if (state == 0) { angle = 0; }\n";
        const string sharedRodHunk = "if (state == 0) { angle = 0; }\nif (UnlockCondition) { UnlockUpgrade(18); }\n";
        const string cameraBase = "zoom = 1;\n";
        const string uniqueCameraHunk = "zoom = 1;\nif (UpgradeData.upg[Rod.rod_id, 0] == 18) { zoom = 2; }\n";

        var modA = Patch("ModA",
            ("gml_Object_UpgradeData_Create_0", BaseCreate,
                BaseCreate.Replace("i <= 17", "i <= 18", StringComparison.Ordinal) + "\nname[18] = \"Derived\";\ncost[18] = 0.7;\n"),
            ("gml_Object_Rod_Step_0", rodStepBase, sharedRodHunk),
            ("gml_Object_Camera_Step_0", cameraBase, uniqueCameraHunk));

        var modB = Patch("ModB",
            ("gml_Object_UpgradeData_Create_0", BaseCreate,
                BaseCreate.Replace("i <= 17", "i <= 18", StringComparison.Ordinal) + "\nname[18] = \"Origin\";\ncost[18] = 0.6;\n"),
            ("gml_Object_Rod_Step_0", rodStepBase, sharedRodHunk));

        var plan = BuildPlan.Create([modA, modB]);
        string create = plan.Assets.Single(a => a.Name == Create).Gml!;
        string rodStep = plan.Assets.Single(a => a.Name == "gml_Object_Rod_Step_0").Gml!;
        string camera = plan.Assets.Single(a => a.Name == "gml_Object_Camera_Step_0").Gml!;

        Assert.Contains("name[18] = \"Origin\";", create);
        Assert.Contains("name[19] = \"Derived\";", create);
        Assert.Contains("cost[19] = 0.7;", create);
        Assert.Contains("for (i = 0; i <= 19; i++)", create);
        Assert.Contains("UnlockUpgrade(18);", rodStep);
        Assert.DoesNotContain("UnlockUpgrade(19);", rodStep);
        Assert.Contains("UpgradeData.upg[Rod.rod_id, 0] == 19", camera);
        Assert.Contains(plan.Decisions, d =>
            d.Contains("[CGF-RELOCATE-003] Upgrade \"Derived\" (ModA) moved from slot 18 to 19", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Assets, a => a.Gml?.Contains("<<<<<<<", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void TypedSinksRewriteWhileRodRowIndicesNonUpgradeSwitchesAndUnrelatedLiteralsStayUntouched()
    {
        const string rodOtherBase = "switch (UpgradeData.upg[rod_id, i])\n{\n    case 0:\n        upgrades[2] = 1;\n        break;\n}\n";
        const string rodStepBase = "switch (state)\n{\n    case 0:\n        break;\n}\n";
        const string cardOtherBase = "switch (card_id)\n{\n    case 0:\n        break;\n}\nif (allowPurchase)\n{\n    UpgradeData.upg[Rod.rod_id, Rod.slot_id] = card_id;\n}\n";
        const string calcCostBase = "var cost = UpgradeData.cost[argument0];\nreturn cost;\n";
        const string extraBase = "x = 0;\n";

        var alpha = Patch("Alpha",
            (Create, BaseCreate, BaseCreate.Replace("i <= 17", "i <= 18", StringComparison.Ordinal) + "\nname[18] = \"Alpha\";\ncost[18] = 0.4;\n"),
            ("gml_Object_Girl_Step_0", extraBase, "x = 0;\nif (UpgradeData.unlocks[18]) { UnlockUpgrade(18); }\n"));

        var beta = Patch("Beta",
            (Create, BaseCreate, BaseCreate.Replace("i <= 17", "i <= 18", StringComparison.Ordinal)
                + "\nname[18] = \"Beta\";\ndesc[18] = \"Desc B\";\ncost[18] = 0.8;\nunlocks[18] = 1;\n"
                + "for (iy = 0; iy < 9; iy++) { upg[18, iy] = -1; object[18, iy] = -1; }\n"),
            ("gml_Object_Rod_Other_10", rodOtherBase,
                "switch (UpgradeData.upg[rod_id, i])\n{\n    case 0:\n        upgrades[2] = 1;\n        break;\n    case 18:\n        upgrades[18] = 1;\n        break;\n}\n"),
            ("gml_Object_Rod_Step_0", rodStepBase,
                "switch (state)\n{\n    case 0:\n        break;\n    case 18:\n        upgrades[18] = 1;\n        break;\n}\n"),
            ("gml_Object_UpgradeCard_Other_10", cardOtherBase,
                "switch (card_id)\n{\n    case 0:\n        break;\n    case 18:\n        if (Rod.upgrades[18]) { allowPurchase = 0; }\n        break;\n}\n"
                + "if (allowPurchase)\n{\n    UpgradeData.upg[Rod.rod_id, Rod.slot_id] = card_id;\n    if (card_id == 18) { upgrade_was_purchased(18); }\n}\n"),
            ("gml_Script_calculate_upgrade_cost", calcCostBase,
                "if (argument0 != 18) { return UpgradeData.cost[argument0]; }\nreturn UpgradeData.cost[16];\n"),
            ("gml_Object_Helper_Step_0", extraBase,
                "x = lengthdir_x(18, 0);\nif (UpgradeData.upg[0, 1] == 18) { UpgradeData.upg[0, 2] = 18; }\n"));

        var plan = BuildPlan.Create([alpha, beta]);
        string create = plan.Assets.Single(a => a.Name == Create).Gml!;
        string rodOther = plan.Assets.Single(a => a.Name == "gml_Object_Rod_Other_10").Gml!;
        string rodStep = plan.Assets.Single(a => a.Name == "gml_Object_Rod_Step_0").Gml!;
        string cardOther = plan.Assets.Single(a => a.Name == "gml_Object_UpgradeCard_Other_10").Gml!;
        string calcCost = plan.Assets.Single(a => a.Name == "gml_Script_calculate_upgrade_cost").Gml!;
        string helper = plan.Assets.Single(a => a.Name == "gml_Object_Helper_Step_0").Gml!;

        // Relocated Beta definitions and rod-row exclusion
        Assert.Contains("name[19] = \"Beta\";", create);
        Assert.Contains("desc[19] = \"Desc B\";", create);
        Assert.Contains("cost[19] = 0.8;", create);
        Assert.Contains("unlocks[19] = 1;", create);
        Assert.Contains("upg[18, iy] = -1;", create);
        Assert.Contains("object[18, iy] = -1;", create);

        // Rod_Other_10: the upgrade-id case label moves, the effect slot upgrades[18] does NOT
        // (Rod.upgrades[] is an effect-slot array, not indexed by upgrade id).
        Assert.Contains("case 19:", rodOther);
        Assert.Contains("upgrades[18] = 1;", rodOther);
        Assert.DoesNotContain("upgrades[19]", rodOther);

        // Rod_Step_0: switch (state) keeps case 18:, and effect slots stay untouched
        Assert.Contains("case 18:", rodStep);
        Assert.Contains("upgrades[18] = 1;", rodStep);

        // UpgradeCard_Other_10: switch (card_id) case, card_id ==, upgrade_was_purchased(); Rod.upgrades[] effect slot untouched
        Assert.Contains("case 19:", cardOther);
        Assert.Contains("Rod.upgrades[18]", cardOther);
        Assert.Contains("card_id == 19", cardOther);
        Assert.Contains("upgrade_was_purchased(19)", cardOther);

        // calculate_upgrade_cost: argument0 != 19
        Assert.Contains("argument0 != 19", calcCost);

        // Helper: UpgradeData.upg comparisons/assignments rewritten, lengthdir_x(18, 0) untouched
        Assert.Contains("lengthdir_x(18, 0)", helper);
        Assert.Contains("UpgradeData.upg[0, 1] == 19", helper);
        Assert.Contains("UpgradeData.upg[0, 2] = 19", helper);

        Assert.Contains(plan.Decisions, d =>
            d.Contains("[CGF-RELOCATE-003] Upgrade \"Beta\" (Beta) moved from slot 18 to 19", StringComparison.Ordinal));
    }

    [Fact]
    public void CountBoundsMergeToMaxBothViaNormalizationAndSlotUnion()
    {
        const string girlBase = "for (i = 0; i <= 17; i++) { gear_upgrade_scalar[i] = 1; }\n";
        const string menuAlarmBase = "iCard++;\nif (iCard <= 17) { alarm[0] = 2; }\n";
        const string saveBase = "with (UpgradeData)\n{\n    for (i = 0; i <= 17; i++) { unlocks[i] = ini_read_real(\"Upgrades\", \"unlocks\" + string(i), unlocks[i]); }\n}\n";

        var alpha = Patch("Alpha",
            (Create, BaseCreate, BaseCreate.Replace("i <= 17", "i <= 18", StringComparison.Ordinal) + "\nname[18] = \"Alpha\";\n"),
            ("gml_Object_Girl_Other_10", girlBase, girlBase.Replace("17", "18", StringComparison.Ordinal)),
            ("gml_Object_UpgradeMenu_Alarm_0", menuAlarmBase, menuAlarmBase.Replace("17", "18", StringComparison.Ordinal)),
            ("gml_Object_SaveManager_Other_13", saveBase, saveBase.Replace("17", "18", StringComparison.Ordinal)));

        var beta = Patch("Beta",
            (Create, BaseCreate, BaseCreate.Replace("i <= 17", "i <= 18", StringComparison.Ordinal) + "\nname[18] = \"Beta\";\n"),
            ("gml_Object_Girl_Other_10", girlBase, girlBase.Replace("17", "18", StringComparison.Ordinal)),
            ("gml_Object_UpgradeMenu_Alarm_0", menuAlarmBase, menuAlarmBase.Replace("17", "18", StringComparison.Ordinal)));

        var plan = BuildPlan.Create([alpha, beta]);
        Assert.Contains("i <= 19", plan.Assets.Single(a => a.Name == Create).Gml!);
        Assert.Contains("i <= 19", plan.Assets.Single(a => a.Name == "gml_Object_Girl_Other_10").Gml!);
        Assert.Contains("iCard <= 19", plan.Assets.Single(a => a.Name == "gml_Object_UpgradeMenu_Alarm_0").Gml!);
        Assert.Contains("i <= 19", plan.Assets.Single(a => a.Name == "gml_Object_SaveManager_Other_13").Gml!);

        // Non-relocated disjoint count bounds (<= 18 vs <= 20) also resolve to max via SlotUnion.
        var nonRelocA = Patch("NonRelocA",
            (Create, BaseCreate, BaseCreate.Replace("i <= 17", "i <= 18", StringComparison.Ordinal) + "\nname[18] = \"Eighteen\";\n"),
            ("gml_Object_UpgradeMenu_Alarm_0", menuAlarmBase, menuAlarmBase.Replace("17", "18", StringComparison.Ordinal)));
        var nonRelocB = Patch("NonRelocB",
            (Create, BaseCreate, BaseCreate.Replace("i <= 17", "i <= 20", StringComparison.Ordinal) + "\nname[20] = \"Twenty\";\n"),
            ("gml_Object_UpgradeMenu_Alarm_0", menuAlarmBase, menuAlarmBase.Replace("17", "20", StringComparison.Ordinal)));

        var nonRelocReport = ConflictAnalyzer.Analyze([nonRelocA, nonRelocB]);
        Assert.DoesNotContain(nonRelocReport.Findings, f => f.Severity == "CONFLICT" && f.Asset == "gml_Object_UpgradeMenu_Alarm_0");
        var nonRelocPlan = BuildPlan.Create([nonRelocA, nonRelocB]);
        Assert.Contains("iCard <= 20", nonRelocPlan.Assets.Single(a => a.Name == "gml_Object_UpgradeMenu_Alarm_0").Gml!);
        Assert.Contains("i <= 20", nonRelocPlan.Assets.Single(a => a.Name == Create).Gml!);
    }

    [Theory]
    [InlineData("var upgrade_variant = 18;\n")]
    [InlineData("var uid = 18;\nUnlockUpgrade(uid);\n")]
    [InlineData("s = ini_read_real(\"Upgrades\", \"unlocks\" + string(18), 0);\n")]
    public void UnclassifiedUpgradeTypedLiteralFailsClosedWithWarningAndKeepsGroupChoice(string suspiciousSnippet)
    {
        var alpha = Patch("Alpha", (Create, BaseCreate, BaseCreate + "\nname[18] = \"Alpha\";\n"));
        var beta = Patch("Beta",
            (Create, BaseCreate, BaseCreate + "\nname[18] = \"Beta\";\n"),
            ("gml_Object_Helper_Step_0", "x = 0;\n", suspiciousSnippet));

        var report = ConflictAnalyzer.Analyze([alpha, beta]);
        Assert.Contains(report.Findings, f => f.Severity == "WARNING" && f.Kind == "relocation" && f.Asset == "upgrade-slot");
        Assert.Contains(report.Findings, f => f.Severity == "CONFLICT" && f.Kind == "upgrade-group");

        var plan = BuildPlan.Create([alpha, beta], new Dictionary<string, string> { ["code/upgrade-slot[18]"] = "Alpha" });
        Assert.Contains(plan.Decisions, d => d.StartsWith("WARNING: upgrade slot 18", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Decisions, d => d.Contains("[CGF-RELOCATE-003]", StringComparison.Ordinal));
        string create = plan.Assets.Single(a => a.Name == Create).Gml!;
        Assert.Contains("name[18] = \"Alpha\"", create);
        Assert.DoesNotContain("name[19]", create);
    }

    [Fact]
    public void SameNameAndVanillaSlotEditDoNotRelocate()
    {
        var sameA = Patch("A", (Create, BaseCreate, BaseCreate + "\nname[18] = \"Shared\";\ncost[18] = 0.5;\n"));
        var sameB = Patch("B", (Create, BaseCreate, BaseCreate + "\nname[18] = \"Shared\";\ncost[18] = 0.6;\n"));
        Assert.DoesNotContain(BuildPlan.Create([sameA, sameB]).Decisions, d => d.Contains("[CGF-RELOCATE-003]", StringComparison.Ordinal));

        var editA = Patch("EditA", (Create, BaseCreate, BaseCreate.Replace("cost[17] = 0.5;", "cost[17] = 0.9;", StringComparison.Ordinal)));
        var editB = Patch("EditB", (Create, BaseCreate, BaseCreate.Replace("cost[17] = 0.5;", "cost[17] = 1.2;", StringComparison.Ordinal)));
        Assert.DoesNotContain(BuildPlan.Create([editA, editB]).Decisions, d => d.Contains("[CGF-RELOCATE-003]", StringComparison.Ordinal));
    }

    [Fact]
    public void SyntheticArchivesMergeRelocatedUpgradeIconFramesAndFallbackSafely()
    {
        using var temp = new TemporaryDirectory();
        string basePath = temp.PathFor("base.win");
        string alphaPath = temp.PathFor("alpha.win");
        string betaPath = temp.PathFor("beta.win");
        string badIconPath = temp.PathFor("badicon.win");

        WriteSyntheticArchive(basePath, iconFrameCount: 19, frame19Colour: null, extraUpgradeCode: null);
        WriteSyntheticArchive(alphaPath, iconFrameCount: 20, frame19Colour: 111, extraUpgradeCode: ("AlphaUpg", "AlphaHook", "if (!UpgradeData.unlocks[18]) { UnlockUpgrade(18); }"));
        WriteSyntheticArchive(betaPath, iconFrameCount: 20, frame19Colour: 222, extraUpgradeCode: ("BetaUpg", "BetaHook", "if (!UpgradeData.unlocks[18]) { UnlockUpgrade(18); }"));
        WriteSyntheticArchive(badIconPath, iconFrameCount: 19, frame19Colour: null, extraUpgradeCode: ("NoIconUpg", "NoIconHook", "if (!UpgradeData.unlocks[18]) { UnlockUpgrade(18); }"));

        using var original = GameFileLoader.Load(basePath);
        using var alphaMod = GameFileLoader.Load(alphaPath);
        using var betaMod = GameFileLoader.Load(betaPath);
        using var badIconMod = GameFileLoader.Load(badIconPath);

        var alphaPatch = PatchBuilder.Create(original, alphaMod, "Alpha");
        var betaPatch = PatchBuilder.Create(original, betaMod, "Beta");
        var badIconPatch = PatchBuilder.Create(original, badIconMod, "NoIcon");

        // 1. Compatible spr_upgradeicons: both frame 19 (Alpha) and frame 20 (Beta) survive with distinct pixel data.
        string mergedPath = temp.PathFor("merged.win");
        var build = ArchiveBuilder.Build(basePath, [new(alphaPatch, alphaPath), new(betaPatch, betaPath)], mergedPath);
        Assert.NotNull(build.Verification);
        Assert.Contains(build.Plan.Decisions, d =>
            d.Contains("[CGF-RELOCATE-003] Upgrade \"BetaUpg\" (Beta) moved from slot 18 to 19", StringComparison.Ordinal)
            && d.Contains("icon frame 19->20", StringComparison.Ordinal));

        using (var merged = GameFileLoader.Load(mergedPath))
        {
            var icons = merged.Data.Sprites.ByName("spr_upgradeicons")!;
            Assert.Equal(21, icons.Textures.Count);
            Assert.NotSame(icons.Textures[19].Texture, icons.Textures[20].Texture);

            string create = PatchBuilder.ReadCode(merged, Create).Text;
            Assert.Contains("name[18] = \"AlphaUpg\"", create);
            Assert.Contains("name[19] = \"BetaUpg\"", create);

            Assert.Contains("UnlockUpgrade(18)", PatchBuilder.ReadCode(merged, "gml_Object_AlphaHook_Step_0").Text);
            Assert.Contains("UnlockUpgrade(19)", PatchBuilder.ReadCode(merged, "gml_Object_BetaHook_Step_0").Text);
        }

        // 2. Missing frame 19 in relocated mod's spr_upgradeicons: keeps item at slot 19 with duplicated frame 0 + WARNING.
        string fallbackPath = temp.PathFor("fallback.win");
        var fallbackBuild = ArchiveBuilder.Build(basePath, [new(alphaPatch, alphaPath), new(badIconPatch, badIconPath)], fallbackPath);
        Assert.NotNull(fallbackBuild.Verification);
        Assert.Contains(fallbackBuild.Plan.Decisions, d => d.StartsWith("WARNING:", StringComparison.Ordinal));
        Assert.Contains(fallbackBuild.Plan.Decisions, d =>
            d.Contains("[CGF-RELOCATE-003] Upgrade \"NoIconUpg\" (NoIcon) moved from slot 18 to 19", StringComparison.Ordinal)
            && d.Contains("icon frame fallback", StringComparison.Ordinal));

        using (var fallback = GameFileLoader.Load(fallbackPath))
        {
            var icons = fallback.Data.Sprites.ByName("spr_upgradeicons")!;
            Assert.Equal(21, icons.Textures.Count);
            Assert.Same(icons.Textures[0].Texture, icons.Textures[20].Texture);
            Assert.Contains("name[19] = \"NoIconUpg\"", PatchBuilder.ReadCode(fallback, Create).Text);
        }
    }

    private static void WriteSyntheticArchive(string path, int iconFrameCount, byte? frame19Colour, (string Name, string HookObject, string HelperCode)? extraUpgradeCode)
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.Build = 1773;
        data.GeneralInfo.BytecodeVersion = 16;

        data.GameObjects.Add(new() { Name = data.Strings.MakeString("UpgradeData") });
        if (extraUpgradeCode is { } hook)
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(hook.HookObject) });

        var spr = new UndertaleSprite
        {
            Name = data.Strings.MakeString("spr_upgradeicons"),
            Width = 4,
            Height = 4,
            MarginRight = 3,
            MarginBottom = 3
        };
        for (int i = 0; i < iconFrameCount; i++)
        {
            byte colour = i == 19 && frame19Colour is { } c ? c : (byte)(10 + i);
            var image = new GMImage(4, 4);
            image.GetRawImageData()[0] = colour;
            var page = new UndertaleEmbeddedTexture { Name = data.Strings.MakeString($"Page_{i}"), TextureData = new() { Image = image.ConvertToPng() } };
            var item = new UndertaleTexturePageItem
            {
                Name = data.Strings.MakeString($"Item_{i}"),
                TexturePage = page,
                SourceWidth = 4,
                SourceHeight = 4,
                TargetWidth = 4,
                TargetHeight = 4,
                BoundingWidth = 4,
                BoundingHeight = 4
            };
            data.EmbeddedTextures.Add(page);
            data.TexturePageItems.Add(item);
            spr.Textures.Add(new() { Texture = item });
        }
        data.Sprites.Add(spr);

        var unlockCode = UndertaleCode.CreateEmptyEntry(data, "gml_Script_UnlockUpgrade");
        data.Scripts.Add(new() { Name = data.Strings.MakeString("UnlockUpgrade"), Code = unlockCode });

        int maxBound = extraUpgradeCode is null ? 17 : 18;
        string createGml = $$"""
            for (i = 0; i <= {{maxBound}}; i++)
            {
                unlocks[i] = 0;
            }
            name[0] = "Scouting";
            name[17] = "Flick";
            """;
        if (extraUpgradeCode is { } extra)
            createGml += $"\nname[18] = \"{extra.Name}\";\ncost[18] = 0.5;\n";

        var createCode = data.GameObjects.ByName("UpgradeData")!.EventHandlerFor(EventType.Create, 0u, data);

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(unlockCode, "UpgradeData.unlocks[argument0] = 1;");
        compiler.QueueCodeReplace(createCode, createGml);
        if (extraUpgradeCode is { } extraHook)
        {
            var helperCode = data.GameObjects.ByName(extraHook.HookObject)!.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data);
            compiler.QueueCodeReplace(helperCode, extraHook.HelperCode);
        }
        var result = compiler.Compile();
        Assert.True(result.Successful, result.PrintAllErrors(true));

        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    private static ModPatch Patch(string name, params (string Entry, string BaseText, string ModText)[] entries)
    {
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
        foreach (string family in new[] { "code", "scripts", "objects", "rooms", "sprites", "texturePages", "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions" })
            changes[family] = new([], [], []);
        var snapshot = JsonSerializer.SerializeToElement(new { });
        var changed = entries.Select(e => new AssetPatch(e.Entry, snapshot, snapshot, [],
            new(e.BaseText, "gml", 0, 0, null, []), new(e.ModText, "gml", 0, 0, null, []))).ToArray();
        changes["code"] = new([], changed, []);
        return new(2, "test", DateTimeOffset.UnixEpoch,
            new(name + ".win", name, 1, "1.4", 16, name), new("base.win", "base", 1, "1.4", 16), [], changes, new(), []);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cgfmm-upg-reloc-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
