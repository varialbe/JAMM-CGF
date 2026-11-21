using Jamm.Core.Building;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

public sealed class InstanceCompatibilityTests
{
    private const string Body = "if (Lure.fish > 0 && Lure.fish.object_index == Flash.object_index)\n{\n    fish_lunge();\n}\n";
    private const string FishThinnerEvent = "gml_Object_FishThinner_Other_10";
    private const string FishThinnerBody = "while (n_culled < number_to_cull && attempts < max_attempts)\n{\n    if (instance_number(object_index) > 5 && point_distance(x, y, Lure.x, Lure.y) > 2000)\n    {\n        deactivated = 1;\n        if (light > 0)\n        {\n            instance_destroy(light);\n        }\n        instance_deactivate_object(id);\n        other.n_culled++;\n    }\n    other.attempts++;\n}\n";
    private static readonly PatchFile Source = new("mod.win", "test", 0, "1", 16, "Test mod");
    private static readonly ModPatch Patch = new(2, "test", DateTimeOffset.UnixEpoch, Source, Source, [], []);

    [Theory]
    [InlineData("gml_Object_Electron_Other_17", "Flash")]
    [InlineData("gml_Object_Shados_Other_17", "Lumos")]
    public void RepairsKnownFishLungeTypeChecksAndPreservesDifferentBodies(string entry, string target)
    {
        string body = Body.Replace("Flash", target, StringComparison.Ordinal);
        string differentBody = body + $"{target}.speed = 0;";
        var known = Plan(body, entry);
        var other = Plan(differentBody, entry);

        var repaired = InstanceCompatibility.Repair(known, [Patch], Baseline());

        Assert.Contains($"== {target})", repaired.Assets.Single(a => a.Name == entry).Gml);
        Assert.Contains(repaired.Decisions, decision => decision.Contains("CGF-INSTANCE-001", StringComparison.Ordinal));
        var repairedOther = InstanceCompatibility.Repair(other, [Patch], Baseline());
        Assert.Equal(differentBody, repairedOther.Assets.Single(a => a.Name == entry).Gml);
    }

    [Fact]
    public void ProtectsFishLightsInVanillaFishThinnerAndIsIdempotent()
    {
        var plan = Plan(FishThinnerBody, FishThinnerEvent);

        var repaired = InstanceCompatibility.Repair(plan, [Patch], new Dictionary<string, CodeText>
        {
            [FishThinnerEvent] = new(FishThinnerBody, "gml", 0, 0, null, [])
        });

        string text = Assert.Single(repaired.Assets).Gml!;
        Assert.Contains("&& !(light > 0)", text);
        Assert.Contains("other.attempts++;", text);
        Assert.Contains("CGF-INSTANCE-003", Assert.Single(repaired.Decisions));
        Assert.Same(repaired, InstanceCompatibility.Repair(repaired, [Patch], Baseline(FishThinnerBody)));
    }

    [Fact]
    public void LeavesUnknownFishThinnerTextUnchangedAndWarns()
    {
        const string unknown = "if (instance_number(object_index) > 3) { instance_destroy(id); }";
        var plan = Plan(unknown, FishThinnerEvent);

        var result = InstanceCompatibility.Repair(plan, [Patch], Baseline(unknown));

        Assert.Equal(unknown, Assert.Single(result.Assets).Gml);
        Assert.Contains("WARNING [CGF-INSTANCE-003]", Assert.Single(result.Decisions));
    }

    [Fact]
    public void AttemptsIncrementRemainsOutsideTheLightFishCullGuard()
    {
        var result = InstanceCompatibility.Repair(Plan(FishThinnerBody, FishThinnerEvent), [Patch], Baseline(FishThinnerBody));
        string text = Assert.Single(result.Assets).Gml!;
        int condition = text.IndexOf("&& !(light > 0)", StringComparison.Ordinal);
        int attempts = text.IndexOf("other.attempts++;", StringComparison.Ordinal);

        Assert.True(condition >= 0 && attempts > condition);
        Assert.Equal(text[..attempts].Count(c => c == '}') + 1, text[..attempts].Count(c => c == '{'));
    }

    [RealFilesFact]
    public void RealSingleModBuildProtectsFishThinnerLights()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string modsPath = Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!;
        string modPath = Directory.GetFiles(modsPath, "*.win").Order(StringComparer.Ordinal).First();
        using var temp = new TemporaryDirectory();
        using var original = GameFileLoader.Load(basePath);
        using var mod = GameFileLoader.Load(modPath);
        var patch = PatchBuilder.Create(original, mod, "FishThinnerGuardTest");

        var build = ArchiveBuilder.Build(basePath, [new(patch, modPath)], temp.PathFor("merged.win"));
        using var merged = GameFileLoader.Load(temp.PathFor("merged.win"));

        Assert.Contains("&& !(light > 0)", PatchBuilder.ReadCode(merged, FishThinnerEvent).Text);
        Assert.Contains(build.Plan.Decisions, decision => decision.StartsWith("[CGF-INSTANCE-003]", StringComparison.Ordinal));
    }

    [Fact]
    public void WarnsAboutCodeAccessWhenTheLastPlacedInstanceDisappears()
    {
        var original = WithRoom(placed: true);
        var merged = WithRoom(placed: false);
        var plan = Plan("// Flash.y\ntext = \"Flash.sprite_index\";\nx = Flash.x;\ny = holder.Flash.y;");

        string warning = Assert.Single(InstanceCompatibility.Diagnose(plan, [Patch], original, merged).Decisions);

        Assert.Contains("CGF-INSTANCE-002", warning);
        Assert.Contains("line 3", warning);
        Assert.Contains("Flash.x", warning);
        Assert.Contains("NewWorld", warning);
        Assert.Empty(InstanceCompatibility.Diagnose(plan, [Patch], original, original).Decisions);
    }

    private static BuildPlan Plan(string gml, string name = "gml_Object_Electron_Other_17") => new([new("code", name, 0, null, null, gml)], []);

    private static IReadOnlyDictionary<string, CodeText> Baseline(string text = FishThinnerBody) => new Dictionary<string, CodeText>
    {
        [FishThinnerEvent] = new(text, "gml", 0, 0, null, [])
    };

    private static UndertaleData WithRoom(bool placed)
    {
        var data = UndertaleData.CreateNew();
        var flash = new UndertaleGameObject { Name = data.Strings.MakeString("Flash") };
        var room = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld") };
        data.GameObjects.Add(flash);
        data.Rooms.Add(room);
        if (placed) room.GameObjects.Add(new() { ObjectDefinition = flash });
        return data;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cgfmm-instance-compat-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(directory);
        internal string PathFor(string name) => Path.Combine(directory, name);
        public void Dispose() => Directory.Delete(directory, true);
    }
}
