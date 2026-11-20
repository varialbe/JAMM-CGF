using Jamm.Core.Building;
using Jamm.Core.Merging;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies ModKit areas through package, patch, check, merge and archive reload.</summary>
public sealed class ModKitAreaTests
{
    [Theory]
    [InlineData("""{"id":"reef","bounds":{"x":0,"y":0,"w":0,"h":5}}""", "bounds.w")]
    [InlineData("""{"id":"reef","bounds":{"x":0,"y":0,"w":2,"h":5},"pieces":[{"object":"Rock","x":2,"y":1}]}""", "relative")]
    [InlineData("""{"id":"reef","bounds":{"x":0,"y":0,"w":2,"h":5},"removes":[{"object":"Rock","x":5,"y":1}]}""", "within")]
    [InlineData("""{"id":"reef","bounds":{"x":0,"y":0,"w":2,"h":5},"anchor":{"mode":"follow"}}""", "object")]
    [InlineData("""{"id":"reef","bounds":{"x":0,"y":0,"w":2,"h":5},"opens":{"object":"Gate","x":0,"y":0,"set":{"a.b":1}}}""", "variable name")]
    public void ReportsInvalidAreaFields(string area, string error)
    {
        using var temp = new AreaTestDirectory();
        string package = temp.Package($$"""{"id":"area-test","name":"Area test","areas":[{{area}}]}""");
        Assert.Contains(error, Assert.Throws<InvalidDataException>(() => ModKitPackage.Load(package)).Message);
    }

    [Theory]
    [InlineData("follow", 90, 1850, true, false)]
    [InlineData("follow", 90, 1850, true, true)]
    [InlineData("fixed", 30, 1600, false, false)]
    public void MergesAreaAgainstEditedRoomAndPreservesExistingCreationCode(string mode, int left, int top, bool follows, bool competingMove)
    {
        using var temp = new AreaTestDirectory();
        string basePath = temp.PathFor("base.win");
        string sourcePath = temp.PathFor("test.win");
        string editedPath = temp.PathFor("edited.win");
        string output = temp.PathFor("output.win");
        using (var original = NewWorld())
            Save(original, basePath);

        string anchor = mode == "follow" ? ""","object":"Gate","x":40,"y":1620""" : "";
        string opens = follows ? ""","opens":{"object":"Gate","x":40,"y":1620,"set":{"canOpen":true}}"""
            : ""","removes":[{"object":"Debris","x":54,"y":1695}]""";
        string package = temp.Package($$$"""
            {"id":"area-test","name":"Area test",
             "areas":[{"id":"reef","bounds":{"x":30,"y":1600,"w":80,"h":100},
                       "anchor":{"mode":"{{{mode}}}"{{{anchor}}}},
                       "pieces":[{"object":"Rock","x":13,"y":25,"scaleX":-1.5,"rotation":90},
                                 {"object":"jmk_area_test_object_effect","x":10,"y":20}],
                       "lights":[{"object":"Lamp","x":35,"y":48,"set":{"image_alpha":0.35}}]
                       {{{opens}}}}],
             "sprites":[{"key":"swimmer","width":16,"height":16,"compose":[{"file":"part.svg"}]}],
             "objects":[{"key":"effect","depth":-10,"hooks":{"create":"effect.gml","step":"effect.gml","draw":"effect.gml"}}],
             "fish":[{"key":"swimmer","name":"Swimmer","size":"small","sprite":"swimmer",
                      "spawn":{"area":"reef","count":10,"within":{"x":3,"y":5,"w":50,"h":70} } }]}
            """);
        File.WriteAllText(Path.Combine(package, "part.svg"), """
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"><ellipse cx="7" cy="7" rx="5" ry="2" fill="#335577"/></svg>
            """);
        File.WriteAllText(Path.Combine(package, "effect.gml"), "animation_frame = 1;");
        ModKitBuilder.Build(package, basePath, sourcePath);
        using var originalFile = GameFileLoader.Load(basePath);
        using var sourceFile = GameFileLoader.Load(sourcePath);
        using (var edited = GameFileLoader.Load(basePath))
        {
            var room = edited.Data.Rooms.ByName("NewWorld")!;
            var gate = room.GameObjects.Single(i => i.ObjectDefinition.Name.Content == "Gate");
            gate.X += 60;
            gate.Y += 250;
            room.GameObjects.Remove(room.GameObjects.Single(i => i.ObjectDefinition.Name.Content == "Debris" && i.X == 47));
            Add(room, edited.Data, "Rock", 115, 1870);
            Save(edited.Data, editedPath);
        }

        using var mod = GameFileLoader.Load(editedPath);
        var areaPatch = PatchJson.Deserialize(PatchJson.Serialize(PatchBuilder.Create(originalFile, sourceFile, "Area test")));
        var editPatch = PatchBuilder.Create(originalFile, mod, "Room edit");
        Assert.Empty(areaPatch.Changes["code"].Changed);
        Assert.Empty(areaPatch.Changes["objects"].Changed);
        Assert.Empty(areaPatch.Changes["rooms"].Changed);
        List<BuildInput> inputs = [new(editPatch, editedPath), new(areaPatch, sourcePath)];
        if (competingMove)
        {
            string earlierPath = temp.PathFor("earlier.win");
            using (var earlier = GameFileLoader.Load(basePath))
            {
                var gate = earlier.Data.Rooms.ByName("NewWorld")!.GameObjects.Single(i => i.ObjectDefinition.Name.Content == "Gate");
                gate.X -= 11;
                gate.Y += 71;
                Save(earlier.Data, earlierPath);
            }
            using var earlierFile = GameFileLoader.Load(earlierPath);
            inputs.Insert(0, new(PatchBuilder.Create(originalFile, earlierFile, "Earlier move"), earlierPath));
        }
        var report = ConflictAnalyzer.Analyze(inputs.Select(i => i.Patch).ToArray());
        Assert.Contains(report.Findings, f => f.Severity == "WARNING" && f.Message.Contains("[CGF-AREA-001]") && f.Mods.Contains("Room edit"));
        Assert.Equal(follows, report.Findings.Any(f => f.Severity == "INFO" && f.Message.Contains("follows Gate")));
        Assert.Equal(competingMove, report.Findings.Any(f => f.Kind == "area" && f.Message.Contains("discarded 1 earlier placement")));
        Assert.Equal(0, report.ExitCode);

        var build = ArchiveBuilder.Build(basePath, inputs, output);
        Assert.Contains(build.Plan.Decisions, d => d.Contains("[CGF-AREA-001]"));
        using var result = GameFileLoader.Load(output);
        var world = result.Data.Rooms.ByName("NewWorld")!;
        var rock = Assert.Single(world.GameObjects, i => i.X == left + 13 && i.Y == top + 25);
        Assert.Equal(-1.5f, rock.ScaleX);
        Assert.Equal(90, rock.Rotation);
        var lamp = Assert.Single(world.GameObjects, i => i.X == left + 35 && i.Y == top + 48);
        Assert.NotNull(lamp.CreationCode);
        var effect = Assert.Single(world.GameObjects, i => i.ObjectDefinition.Name.Content == "jmk_area_test_object_effect");
        Assert.Equal((left + 10, top + 20), (effect.X, effect.Y));
        Assert.Equal(-10, effect.ObjectDefinition.Depth);
        foreach (string type in new[] { "Create", "Step", "Draw" })
            Assert.NotNull(result.Data.Code.ByName($"gml_Object_jmk_area_test_object_effect_{type}_0"));

        var solids = world.GameObjects.Select(i => SolidAt.Create(i)).OfType<SolidAt>().ToArray();
        var swimmers = world.GameObjects.Where(i => i.ObjectDefinition.Name.Content == "jmk_area_test_swimmer").ToArray();
        Assert.Equal(10, swimmers.Length);
        Assert.All(swimmers, i =>
        {
            Assert.InRange(i.X, left + 3, left + 52);
            Assert.InRange(i.Y, top + 5, top + 74);
            Assert.DoesNotContain(solids, solid => solid.Contains(i.X, i.Y));
        });
        Assert.Equal(world.GameObjects.Select(i => i.InstanceID).Distinct().Count(), world.GameObjects.Count);
        Assert.Equal(follows, world.GameObjects.Any(i => i.ObjectDefinition.Name.Content == "Debris"));
        if (follows)
        {
            var gate = Assert.Single(world.GameObjects, i => i.ObjectDefinition.Name.Content == "Gate");
            string code = new CodeDecompiler(result.Data, "test", _ => { }).Read(gate.CreationCode).Text;
            Assert.Contains("was_here = 17", code);
            Assert.Contains("canOpen = 1", code);
            Assert.Equal(ContentFingerprints.Code(originalFile.Data.Code.ByName("gml_Object_Gate_Create_0")!),
                ContentFingerprints.Code(result.Data.Code.ByName("gml_Object_Gate_Create_0")!));
        }
    }

    [Fact]
    public void SpawnExcludesPreciseMaskPixelsWithRotationAndMirroring()
    {
        using var data = NewWorld();
        var room = data.Rooms.ByName("NewWorld")!;
        var rock = Add(room, data, "Rock", 100, 1640);
        rock.ScaleX = -2;
        rock.Rotation = 90;
        var solid = SolidAt.Create(rock)!;
        Assert.True(solid.Contains(102, 1644));
        Assert.False(solid.Contains(102, 1640));
        var bw = new ModKitAreaBounds(92, 1635, 20, 29);
        HashSet<(int, int)> occupied = [];
        var points = AreaPlacement.WaterPoints(bw, 51, [solid], occupied).ToArray();
        Assert.Equal(51, points.Length);
        Assert.Equal(points.Length, points.Distinct().Count());
        Assert.All(points, p =>
        {
            Assert.True(bw.Contains(p.X, p.Y));
            Assert.False(solid.Contains(p.X, p.Y));
        });
        Assert.Empty(AreaPlacement.WaterPoints(new(102, 1644, 1, 1), 1, [solid], []));
    }

    private static UndertaleData NewWorld()
    {
        var data = UndertaleData.CreateNew();
        var room = data.Rooms[0];
        room.Name = data.Strings.MakeString("NewWorld");
        room.Width = 2000;
        room.Height = 2400;
        var rockSprite = new UndertaleSprite
        {
            Name = data.Strings.MakeString("spr_rock"), Width = 10, Height = 8,
            MarginRight = 9, MarginBottom = 7, SepMasks = UndertaleSprite.SepMaskType.Precise
        };
        var collision = rockSprite.NewMaskEntry(data);
        for (int y = 0; y < 8; y++) collision.Data[y * 2] = 0x3E;
        rockSprite.CollisionMasks.Add(collision);
        data.Sprites.Add(rockSprite);
        foreach (string name in new[] { "Gate", "Rock", "Lamp", "FishSmall", "Debris" })
            data.GameObjects.Add(new() { Name = data.Strings.MakeString(name), Visible = true });
        var rock = data.GameObjects.ByName("Rock")!;
        rock.Sprite = rockSprite;
        rock.Solid = true;
        var gate = Add(room, data, "Gate", 40, 1620);
        Add(room, data, "Debris", 47, 1670);
        Add(room, data, "Debris", 54, 1695);
        gate.CreationCode = UndertaleCode.CreateEmptyEntry(data, "gml_RoomCC_test_gate");
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(gate.CreationCode, "was_here = 17;");
        compiler.QueueCodeReplace(gate.ObjectDefinition.EventHandlerFor(EventType.Create, 0u, data), "canOpen = 0;");
        ArchiveVerifier.Compile(compiler, "synthetic test world");
        return data;
    }

    private static UndertaleRoom.GameObject Add(UndertaleRoom room, UndertaleData data, string name, int x, int y)
    {
        var instance = new UndertaleRoom.GameObject
        {
            InstanceID = ++data.GeneralInfo.LastObj, ObjectDefinition = data.GameObjects.ByName(name), X = x, Y = y
        };
        room.GameObjects.Add(instance);
        return instance;
    }

    private static void Save(UndertaleData data, string path)
    {
        using var stream = File.Create(path);
        UndertaleIO.Write(stream, data);
    }

    internal sealed class AreaTestDirectory : IDisposable
    {
        private readonly string path = Directory.CreateTempSubdirectory("jamm-area-test-").FullName;
        internal string PathFor(string name) => Path.Combine(path, name);
        internal string Package(string json)
        {
            string dir = Directory.CreateDirectory(PathFor("package")).FullName;
            File.WriteAllText(Path.Combine(dir, "jamm-mod.json"), json);
            return dir;
        }
        public void Dispose() => Directory.Delete(path, true);
    }
}
