using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Compiles Smooth against a synthetic legacy game without copyrighted game data.</summary>
public class SmoothGeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputWakeReplaysOnlyFrozenBeginStepsAndPreservesRenderReset(bool debug)
    {
        using var data = Generate(debug);
        string begin = Code(data, "Step_1");
        string alarm = Code(data, "Alarm_0");
        Assert.Contains("ds_map_add(jamm_input_begin, jamm_woken, 1)", begin);
        Assert.Contains("instance_activate_object(jamm_woken)", begin);
        Assert.Contains("alarm[0] = 1", begin);
        Assert.DoesNotContain("event_perform", begin);
        Assert.Contains("global.jamm_tick_delta = jamm_since", begin);
        Assert.Contains("with (all)", alarm);
        Assert.Contains("id != other.id && ds_map_exists(other.jamm_input_begin, id)", alarm);
        Assert.Contains("event_perform(ev_step, ev_step_begin)", alarm);
        Assert.Contains("ds_map_clear(jamm_input_begin)", alarm);
        Assert.DoesNotContain("instance_activate", alarm);
        Assert.Contains("QuestEngine.quest_counter = 0", Code(data, "Draw_76"));
        Assert.Contains("alarm[0] = -1", Code(data, "Other_4"));
    }

    [Fact]
    public void SonarVisibilityAndCastingPoseAreProtectedInCompiledDraw()
    {
        using var data = Generate(false);
        string draw = Code(data, "Draw_76");
        Assert.Contains("SonarPanel.active && SonarPanel.shouldDraw", draw);
        Assert.Contains("visible && !other.jamm_sonar", draw);
        Assert.Contains("jamm_sonar = view_visible[3]", draw);
        Assert.Contains("ds_list_add(other.jamm_fish_on, id)", draw);
        Assert.Contains("id == Lure.fish", draw);
        Assert.Contains("!jamm_casting", draw);
        foreach (string name in new[] { "Girl", "Rod", "Lure", "Boat" })
            Assert.Contains($"object_is_ancestor(object_index, {name})", draw);
        Assert.Contains("jamm_s_flip = frames_since_last_flip", draw);
        Assert.Contains("frames_since_last_flip = jamm_s_flip", Code(data, "Draw_75"));
    }

    private static string Code(UndertaleData data, string suffix)
        => ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{SmoothMod.ObjectName}_{suffix}"));

    private static UndertaleData Generate(bool debug)
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        foreach (string name in new[] { "Girl", "Rod", "Lure", "Boat", "SonarPanel", "Fish", "QuestEngine" })
            ModObjects.Create(data, name, false);
        foreach (string name in new[] { "NewWorld", "room11" })
            data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(data.GameObjects.ByName("QuestEngine").EventHandlerFor(EventType.Step, 1, data), "quest_counter = 0;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Girl").EventHandlerFor(EventType.Step, 0u, data), "a = delta_time; b = delta_time; c = delta_time;");
        ModObjects.Compile(compiler);
        SmoothMod.Apply(data, debug);
        return data;
    }
}
