using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Checks that the gameplay showcase instrumentation compiles against small synthetic archives.</summary>
public sealed class ShowcaseSceneTests
{
    [Fact]
    public void HarborShowcaseCompilesAndUsesNormalMenuInputs()
    {
        using var data = CreateSyntheticGame(
            ["SaveManager", "GameModeMenu", "Girl", "Camera", "jmk_harbor_town_object_harbor"],
            ["jmk_harbor_town_script_command"]);
        using var output = RunScene(data, "showcase-harbor");

        string step = ModObjects.Decompile(output, output.Code.ByName("gml_Object_obj_jamm_showcase_harbor_Step_1")!);
        Assert.Contains("money 500000000", step);
        Assert.Contains("keyboard_key_press(ord(\"E\"))", step);
        Assert.Contains("keyboard_key_press(vk_space)", step);
        Assert.Contains("jamm_showcase_harbor.csv", ModObjects.Decompile(output, output.Code.ByName("gml_Object_obj_jamm_showcase_harbor_Create_0")!));
        Assert.Contains("labels[0] = \"lighthouse\"", ModObjects.Decompile(output, output.Code.ByName("gml_Object_obj_jamm_showcase_harbor_Create_0")!));
        Assert.Contains("levels[current_level]", step);
        Assert.Contains(output.Rooms.ByName("NewWorld")!.GameObjects,
            instance => instance.ObjectDefinition?.Name.Content == "obj_jamm_showcase_harbor");
    }

    [Fact]
    public void SubmarineShowcaseCompilesShopDiveCatchAndAscentFlow()
    {
        using var data = CreateSyntheticGame(
            ["SaveManager", "Girl", "Handler", "Message", "Boat", "Net", "Lure", "BoatData", "icon_Boats",
                "BoatMenu", "BoatCard", "obj_jamm_console", "jmk_submarine_submarine"],
            ["jamm_item_find", "line_snap", "line_reset"]);

        using var output = RunScene(data, "showcase-submarine");

        string step = ModObjects.Decompile(output, output.Code.ByName("gml_Object_obj_jamm_showcase_submarine_Step_1")!);
        Assert.Contains("shop_card", step);
        Assert.Contains("keyboard_key_press(vk_right)", step);
        Assert.Contains("keyboard_key_press(ord(\"V\"))", step);
        Assert.Contains("keyboard_key_press(ord(\"L\"))", step);
        Assert.Contains("\"catch\"", step);
        Assert.Contains("jamm_showcase_submarine.csv", ModObjects.Decompile(output, output.Code.ByName("gml_Object_obj_jamm_showcase_submarine_Create_0")!));
        Assert.Contains("surfaced", step);
        Assert.Contains(output.Rooms.ByName("NewWorld")!.GameObjects,
            instance => instance.ObjectDefinition?.Name.Content == "obj_jamm_showcase_submarine");
    }

    private static UndertaleData RunScene(UndertaleData input, string scene)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "jamm-" + scene + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string inputPath = Path.Combine(tempDir, "input.win");
            string outputPath = Path.Combine(tempDir, "output.win");
            using (var stream = File.Create(inputPath)) UndertaleIO.Write(stream, input);
            Assert.Equal(0, Jamm.ModMaker.Program.Main(["bench", inputPath, outputPath, "--scene", scene]));
            using var resultStream = File.OpenRead(outputPath);
            return UndertaleIO.Read(resultStream);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static UndertaleData CreateSyntheticGame(string[] objects, string[] scripts)
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        var room = new UndertaleRoom
        {
            Name = data.Strings.MakeString("NewWorld"),
            Width = 100000,
            Height = 13000,
            Speed = 30
        };
        data.Rooms.Add(room);
        foreach (string name in objects)
            ModObjects.Create(data, name, persistent: false);

        foreach (string name in scripts)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
        }
        return data;
    }
}
