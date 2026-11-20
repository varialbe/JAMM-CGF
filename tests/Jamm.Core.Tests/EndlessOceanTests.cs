using Jamm.Core.ModKit;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Verifies the Endless Ocean recipe and its legacy archive integration.</summary>
public sealed class EndlessOceanTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("2147483646", 2147483646)]
    [InlineData("-1", null)]
    [InlineData("2147483647", null)]
    [InlineData("1.5", null)]
    [InlineData("\"123\"", null)]
    public void RecipeValidatesSeeds(string seed, int? expected)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, RecipePackage.ManifestFileName), $$"""
                { "id": "endless-ocean", "generator": "endless-ocean", "name": "Endless Ocean", "seed": {{seed}} }
                """);
            if (expected is int value)
                Assert.Equal(value, RecipePackage.Load(directory.FullName).Seed);
            else
                Assert.Contains("$.seed", Assert.Throws<InvalidDataException>(() => RecipePackage.Load(directory.FullName)).Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AppliesAfterTheRoomWidthAndTheEasterlyPlacedArtwork()
    {
        using var data = UndertaleData.CreateNew();
        var world = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld"), Width = 172000, Height = 17000, Speed = 60 };
        data.Rooms.Add(world);
        foreach (string name in new[]
        {
            "Camera", "Boat7", "SaveManager", "Rod", "Lure", "Collidable", "CaveRockBig",
            "Celad", "Inno", "Mustardfish", "Elder_Mustardfish", "Dijon", "Garm"
        })
            data.GameObjects.Add(new UndertaleGameObject { Name = data.Strings.MakeString(name) });

        var sprite = new UndertaleSprite { Name = data.Strings.MakeString("far_spr"), Width = 400, Height = 200, OriginX = 50 };
        var objectAtEdge = new UndertaleGameObject { Name = data.Strings.MakeString("far_content"), Sprite = sprite };
        data.Sprites.Add(sprite);
        data.GameObjects.Add(objectAtEdge);
        world.GameObjects.Add(new UndertaleRoom.GameObject { X = 173000, ScaleX = 2, ObjectDefinition = objectAtEdge });
        Assert.Equal(173700u, EndlessOceanMod.ProtectedEastEdge(world));

        var compiler = new CompileGroup(data);
        foreach (string name in new[] { "get_index_for_name", "fish_object_to_name" })
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
            compiler.QueueCodeReplace(code, "return -1;");
        }
        var manager = data.GameObjects.ByName("SaveManager")!;
        foreach (uint subtype in new uint[] { 14, 15 })
            compiler.QueueCodeReplace(manager.EventHandlerFor(EventType.Other, subtype, data), """
                if (state == 6) ini_write_real("Fishes", string(id), -100);
                else ini_write_real("Fishes", string(id), state);
                """);
        ModObjects.Compile(compiler);

        EndlessOceanMod.Apply(data, 1234);

        Assert.Equal(173700u + EndlessOceanMod.ExtensionWidth, world.Width);
        Assert.Equal(17000u, world.Height);
        Assert.Equal(60u, world.Speed);
        Assert.Equal(EndlessOceanMod.Controller, world.GameObjects[0].ObjectDefinition.Name.Content);
        Assert.Same(objectAtEdge, world.GameObjects[1].ObjectDefinition);
        Assert.Contains("\"seed\", 1234", ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{EndlessOceanMod.Controller}_Other_4")!));
        Assert.Contains("jmk_endless_ocean_fish", ModObjects.Decompile(data, data.Code.ByName("gml_Object_SaveManager_Other_14")!));
        Assert.Throws<InvalidDataException>(() => EndlessOceanMod.Apply(data));
    }

}
