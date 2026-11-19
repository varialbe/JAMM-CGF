using System.Text.Json;
using Jamm.Core.Building;
using Jamm.Core.Patching;
using Jamm.Core.Diffing;

namespace Jamm.Core.Tests;

public sealed class AssetNameRelocationTests
{
    [RealFilesFact]
    public void RealDollfinRoomInstancesFollowTheRenamedObject()
    {
        string originalPath = Environment.GetEnvironmentVariable("JAMM_ORIGINAL")!;
        string modsPath = Environment.GetEnvironmentVariable("JAMM_MODS_DIR")!;
        string[] candidates = [Path.Combine(modsPath, "pack-inputs", "Antiquity.win"), Path.Combine(modsPath, "Antiquity.win"), Path.Combine(Path.GetDirectoryName(modsPath)!, "mods", "pack-inputs", "Antiquity.win")];
        string antiquityPath = candidates.First(File.Exists);
        string cavePath = Path.Combine(modsPath, "cave-expansion.win");
        using var original = GameFileLoader.Load(originalPath);
        using var antiquity = GameFileLoader.Load(antiquityPath);
        using var cave = GameFileLoader.Load(cavePath);
        var patches = new[] { PatchBuilder.Create(original, antiquity, "Antiquity"), PatchBuilder.Create(original, cave, "cave-expansion") };
        var prepared = AssetNameRelocation.Prepare(patches);
        var room = prepared.Patches[1].Changes["rooms"].Added.Concat(prepared.Patches[1].Changes["rooms"].Changed)
            .Single(a => a.Name == "NewWorld").Mod;
        Assert.Contains(room.GetProperty("instances").EnumerateObject(), entry => entry.Name.Contains("Dollfin_jamm_cave_expansion", StringComparison.Ordinal)
            && entry.Value.EnumerateArray().Any(i => i.GetProperty("object").GetString() == "Dollfin_jamm_cave_expansion"));
        string output = Path.Combine(Path.GetTempPath(), $"jamm-dollfin-{Guid.NewGuid():N}.win");
        try
        {
            var build = ArchiveBuilder.Build(originalPath,
                [new(patches[0], antiquityPath), new(patches[1], cavePath)], output);
            using var merged = GameFileLoader.Load(output);
            int expected = cave.Data.Rooms.Single(r => r.Name.Content == "NewWorld").GameObjects.Count(i => i.ObjectDefinition?.Name.Content == "Dollfin");
            int actual = merged.Data.Rooms.Single(r => r.Name.Content == "NewWorld").GameObjects.Count(i => i.ObjectDefinition?.Name.Content == "Dollfin_jamm_cave_expansion");
            Assert.Equal(expected, actual);
            Assert.Contains(build.Plan.Decisions, d => d.Contains("[CGF-RELOCATE-002] Object \"Dollfin\"", StringComparison.Ordinal));
            Assert.Contains(build.Acceptance!.Mods.Single(m => m.Mod == "cave-expansion").Items,
                item => item.Kind == "object" && item.Identity == "Dollfin_jamm_cave_expansion" && item.Status == "Relocated");
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void DifferentObjectAdditionsRenameLaterClaimAndRewriteCodeAndStructuredReferences()
    {
        var first = Patch("First", Object("ParentA", "spr_a"), "var x = Foo;\n", "ParentA", "spr_a");
        var later = Patch("Later", Object("ParentB", "spr_b"), "instance_create(0, 0, Foo);\n", "ParentB", "spr_b");
        var plan = BuildPlan.Create([first, later]);

        Assert.Contains(plan.Assets, a => a.Family == "objects" && a.Name == "Foo");
        Assert.Contains(plan.Assets, a => a.Family == "objects" && a.Name == "Foo_jamm_later");
        Assert.Contains(plan.Assets, a => a.Gml?.Contains("Foo_jamm_later", StringComparison.Ordinal) == true);
        Assert.Contains(plan.Decisions, d => d.StartsWith("[CGF-RELOCATE-002] Object \"Foo\" (Later)", StringComparison.Ordinal));
    }

    [Fact]
    public void IdenticalAdditionIsSharedAndModifiedCopyGetsInfoDecision()
    {
        var sameA = Patch("A", Object("Parent", "spr"), "return 1;\n", "Parent", "spr");
        var sameB = Patch("B", Object("Parent", "spr"), "return 1;\n", "Parent", "spr");
        Assert.DoesNotContain(BuildPlan.Create([sameA, sameB]).Decisions, d => d.Contains("CGF-RELOCATE-002", StringComparison.Ordinal));

        string common = string.Join('\n', Enumerable.Range(0, 10).Select(i => $"x{i} = {i};"));
        var editA = Patch("A", Object("Parent", "spr"), common, "Parent", "spr");
        var editB = Patch("B", Object("Parent", "spr"), common + "\nextra = 1;", "Parent", "spr");
        var plan = BuildPlan.Create([editA, editB]);
        Assert.DoesNotContain(plan.Assets, a => a.Name == "Foo_jamm_b");
        Assert.Contains(plan.Decisions, d => d.StartsWith("INFO [CGF-RELOCATE-002]", StringComparison.Ordinal));
    }

    [Fact]
    public void DynamicStringLookupFailsClosedAndSpriteCollisionRelocates()
    {
        var one = Patch("One", Object("A", "spr"), "return 1;", "A", "spr");
        var unsafePatch = Patch("Two", Object("B", "spr"), "x = asset_get_index(\"Foo\");", "B", "spr");
        var warning = BuildPlan.Create([one, unsafePatch]);
        Assert.Contains(warning.Decisions, d => d.StartsWith("WARNING [CGF-RELOCATE-002]", StringComparison.Ordinal));
        Assert.DoesNotContain(warning.Assets, a => a.Name == "Foo_jamm_two");

        var spriteA = Patch("One", Object("A", "spr"), "", "A", "spr", spriteSnapshot: Sprite(16, 16, 1));
        var spriteB = Patch("Two", Object("B", "spr"), "", "B", "spr", spriteSnapshot: Sprite(24, 16, 2));
        Assert.Contains(BuildPlan.Create([spriteA, spriteB]).Assets, a => a.Family == "sprites" && a.Name == "spr_jamm_two");
    }

    [Fact]
    public void RelocationRewritesRoomInstanceKeysAndPayloadReferences()
    {
        JsonElement room = JsonSerializer.SerializeToElement(new
        {
            instances = new Dictionary<string, object[]> { ["[\"Foo\",1,2]"] = [new { @object = "Foo", x = 1, y = 2 }] }
        });
        var first = Patch("One", Object("A", "spr"), "return 1;", "A", "spr", roomSnapshot: room);
        var later = Patch("Two", Object("B", "spr"), "return 2;", "B", "spr", roomSnapshot: room);
        var prepared = AssetNameRelocation.Prepare([first, later]);
        var rewritten = prepared.Patches[1].Changes["rooms"].Added.Single().Mod;
        Assert.True(rewritten.GetProperty("instances").TryGetProperty("[\"Foo_jamm_two\",1,2]", out var instance));
        Assert.Equal("Foo_jamm_two", instance[0].GetProperty("object").GetString());
    }

    [Fact]
    public void ObjectWithFreelyNamedEventCodeIsNotLineageOfCodelessNamesake()
    {
        // The first mod's object names its event code "FooCreate" (not gml_Object_Foo_*); the later mod's namesake has no events.
        var withEvents = JsonSerializer.SerializeToElement(new
        {
            parent = "Boat", sprite = "spr",
            events = new Dictionary<string, object> { ["0:0"] = new { otherObject = (string?)null, code = new[] { "FooCreate" } } }
        });
        var first = Patch("First", withEvents, "", "Boat", "spr");
        var code = first.Changes["code"];
        first.Changes["code"] = code with { Added = [new AssetPatch("FooCreate", null, JsonSerializer.SerializeToElement(new { }), [], null,
            new("speed = 2;\n", "gml", 0, 0, null, []), 0)] };
        var codeless = Patch("Later", JsonSerializer.SerializeToElement(new { parent = "Boat", sprite = "spr", events = new { } }), "", "Boat", "spr");
        codeless.Changes["code"] = codeless.Changes["code"] with { Added = [] };

        var prepared = AssetNameRelocation.Prepare([first, codeless]);
        Assert.Contains(prepared.Decisions, d => d.StartsWith("[CGF-RELOCATE-002] Object \"Foo\" (Later) renamed", StringComparison.Ordinal));
        Assert.DoesNotContain(prepared.Decisions, d => d.Contains("shares lineage", StringComparison.Ordinal));
    }

    private static ModPatch Patch(string mod, JsonElement obj, string code, string parent, string sprite, JsonElement? spriteSnapshot = null, JsonElement? roomSnapshot = null)
    {
        var changes = new SortedDictionary<string, AssetChanges>(StringComparer.Ordinal);
        foreach (string family in new[] { "code", "scripts", "objects", "rooms", "sprites", "texturePages", "sounds", "fonts", "backgrounds", "paths", "timelines", "strings", "variables", "functions" })
            changes[family] = new([], [], []);
        var empty = JsonSerializer.SerializeToElement(new { });
        var objectAsset = new AssetPatch("Foo", null, obj, [], null, null, 0);
        changes["objects"] = new([objectAsset], [], []);
        string entry = "gml_Object_Foo_Create_0";
        var codeAsset = new AssetPatch(entry, null, empty, [], null, new(code, "gml", 0, 0, null, []), 0);
        changes["code"] = new([codeAsset], [], []);
        if (spriteSnapshot is { } spriteJson)
            changes["sprites"] = new([new AssetPatch("spr", null, spriteJson, [], null, null, 0)], [], []);
        if (roomSnapshot is { } roomJson)
            changes["rooms"] = new([new AssetPatch("rm", null, roomJson, [], null, null, 0)], [], []);
        // Parent and sprite values are included to make the object classification explicit.
        _ = parent; _ = sprite;
        return new(2, "test", DateTimeOffset.UnixEpoch,
            new(mod + ".win", mod, 1, "1.4", 16, mod), new("base.win", "base", 1, "1.4", 16), [], changes, new(), []);
    }

    private static JsonElement Object(string parent, string sprite) => JsonSerializer.SerializeToElement(new { parent, sprite, events = new { } });
    private static JsonElement Sprite(int width, int height, int frames) => JsonSerializer.SerializeToElement(new { width, height, frameCount = frames });
}
