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

    [Fact]
    public void AllJammToastsSetExplicitFontAlignmentColorAndAlphaInDrawGui()
    {
        using var data = Generate(false);
        string smoothToast = Code(data, "Draw_64");
        Assert.Contains("draw_set_font(fnt_babySmall)", smoothToast);
        Assert.Contains("draw_set_halign(fa_left)", smoothToast);
        Assert.Contains("draw_set_valign(fa_top)", smoothToast);
        Assert.Contains("draw_set_color(c_black)", smoothToast);
        Assert.Contains("draw_set_color(c_white)", smoothToast);

        HdMod.Apply(data, null, false);
        string hdToast = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{HdMod.ObjectName}_Draw_64"));
        Assert.Contains("draw_set_font(fnt_babySmall)", hdToast);
        Assert.Contains("draw_set_halign(fa_left)", hdToast);
        Assert.Contains("draw_set_valign(fa_top)", hdToast);

        PerfMode.Queue(data, new CompileGroup(data), false);
        // Compile PerfMode controller onto a fresh synthetic model with fnt_babySmall
        var perfData = UndertaleData.CreateNew();
        perfData.GeneralInfo.Major = 1;
        perfData.GeneralInfo.BytecodeVersion = 16;
        perfData.Fonts.Add(new() { Name = perfData.Strings.MakeString("fnt_babySmall"), DisplayName = perfData.Strings.MakeString("Small") });
        foreach (string room in new[] { "NewWorld", "room11" })
            perfData.Rooms.Add(new() { Name = perfData.Strings.MakeString(room) });
        var perfCompiler = new CompileGroup(perfData);
        PerfMode.Queue(perfData, perfCompiler, false);
        ModObjects.Compile(perfCompiler);
        string perfToast = ModObjects.Decompile(perfData, perfData.Code.ByName("gml_Object_obj_jamm_perf_Draw_64"));
        Assert.Contains("draw_set_font(fnt_babySmall)", perfToast);
        Assert.Contains("draw_set_halign(fa_left)", perfToast);
        Assert.Contains("draw_set_valign(fa_top)", perfToast);
        Assert.Contains("string_width(jamm_perf_text)", perfToast);
        Assert.Contains("string_height(jamm_perf_text)", perfToast);
        Assert.Contains("draw_set_color(c_white)", perfToast);
    }

    [Fact]
    public void ConsoleGeneratorBuildsInGameConsoleWithHelpAndUnlockAllAndNoHotkeyCheat()
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babySmall"), DisplayName = data.Strings.MakeString("Small") });
        foreach (string name in new[] { "Girl", "Rod", "Handler", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons", "AmbianceController", "CatalogData", "ExperienceBar", "SaveManager" })
            ModObjects.Create(data, name, false);
        foreach (string name in new[] { "NewWorld", "room11" })
            data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });

        ConsoleMod.Apply(data);

        var obj = data.GameObjects.ByName(ConsoleMod.ObjectName);
        Assert.NotNull(obj);
        Assert.True(obj.Persistent);
        Assert.Contains(data.Rooms.ByName("NewWorld")!.GameObjects, i => i.ObjectDefinition == obj);
        Assert.Contains(data.Rooms.ByName("room11")!.GameObjects, i => i.ObjectDefinition == obj);

        string create = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Create_0"));
        string begin = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Step_1"));
        string end = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Step_2"));
        string gui = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Draw_64"));

        // Verify F12 / backtick toggle, keyboard_string input, Up/Down history, commands, and slot detection:
        Assert.Contains("jamm_console_history = ds_list_create()", create);
        Assert.Contains("jamm_console_slot_owner = -1", create);
        Assert.Contains("jamm_console_slot = -1", create);
        Assert.Contains("keyboard_check_pressed(vk_f12)", begin);
        Assert.Contains("keyboard_check_pressed(vk_up)", begin);
        Assert.Contains("keyboard_check_pressed(vk_down)", begin);
        Assert.Contains("keyboard_string", begin);
        Assert.Contains("\"help\"", begin);
        Assert.Contains("\"unlockall\"", begin);
        Assert.Contains("\"unlockgear\"", begin);
        Assert.Contains("\"maxlevel\"", begin);
        Assert.Contains("\"level\"", begin);
        Assert.Contains("\"catalog all\"", begin);
        Assert.Contains("\"unlockfish\"", begin);
        Assert.Contains("\"money\"", begin);
        Assert.Contains("UpgradeData.unlocks", begin);
        Assert.Contains("RodData.unlocked", begin);
        Assert.Contains("LureData.unlocked", begin);
        Assert.Contains("BoatData.unlocked", begin);
        Assert.Contains("ToolData.unlocked", begin);
        Assert.Contains("GearData.unlocked", begin);
        Assert.Contains("Girl.nightfallUnlocked = 1", begin);
        Assert.Contains("CatalogData.count", begin);
        Assert.Contains("CatalogData.life", begin);
        Assert.Contains("ExperienceBar.max_level", begin);
        Assert.Contains("SaveManager.saveflag", begin);
        Assert.Matches(@"jamm_console_unlocked_all = (false|0);", begin);
        Assert.Contains("Girl.dollars", begin);
        Assert.Contains("WARNING: The next autosave", begin);
        Assert.DoesNotContain("dollars +=", begin);
        Assert.Contains("LureData.unlocked[5] = 1", end);
        Assert.Contains("AmbianceController.lockDay = 0", end);
        Assert.Contains("SaveManager.saveflag", end);
        Assert.Contains("draw_set_font(fnt_babySmall)", gui);
        Assert.Contains("draw_set_halign(fa_left)", gui);
        Assert.Contains("draw_set_valign(fa_top)", gui);
    }

    [Fact]
    public void ConsoleGuardsGameplayInputEventsAndClearsPollingInputInBeginStep()
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babySmall"), DisplayName = data.Strings.MakeString("Small") });
        foreach (string name in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
            ModObjects.Create(data, name, false);
        foreach (string name in new[] { "NewWorld", "room11" })
            data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });

        var gameplay = ModObjects.Create(data, "GameplayInputFixture", false);
        var keyPress = gameplay.EventHandlerFor(EventType.KeyPress, 77u, data);
        var keyboard = gameplay.EventHandlerFor(EventType.Keyboard, 77u, data);
        var keyRelease = gameplay.EventHandlerFor(EventType.KeyRelease, 77u, data);
        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(keyPress, "gameplay_keypress_hits += 1;");
        compiler.QueueCodeReplace(keyboard, "gameplay_keyboard_hits += 1;");
        compiler.QueueCodeReplace(keyRelease, "gameplay_keyrelease_hits += 1;");
        ModObjects.Compile(compiler);

        ConsoleMod.Apply(data);

        foreach (string entry in new[]
        {
            "gml_Object_GameplayInputFixture_KeyPress_77",
            "gml_Object_GameplayInputFixture_Keyboard_77",
            "gml_Object_GameplayInputFixture_KeyRelease_77"
        })
        {
            string code = ModObjects.Decompile(data, data.Code.ByName(entry)!);
            Assert.Contains("variable_global_exists(\"jamm_console_open\")", code);
            Assert.Contains("global.jamm_console_open", code);
            Assert.Contains("keyboard_check_pressed(192)", code);
            Assert.Contains("keyboard_check_pressed(vk_f12)", code);
        }

        string guardedKeyPress = ModObjects.Decompile(data, data.Code.ByName("gml_Object_GameplayInputFixture_KeyPress_77")!);
        Assert.Contains("gameplay_keypress_hits += 1", guardedKeyPress);
        string create = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Create_0")!);
        string begin = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Step_1")!);
        string end = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Step_2")!);
        Assert.Matches(@"global\.jamm_console_open = (false|0);", create);
        Assert.Contains("global.jamm_console_open = !global.jamm_console_open", begin);
        Assert.Contains("keyboard_string = jamm_clean", begin);
        Assert.Contains("keyboard_clear(vk_enter)", begin);
        Assert.DoesNotContain("keyboard_clear(vk_enter)", end);
    }

    [Fact]
    public void ConsoleGeneratorCompilesWhenOptionalCatalogExperienceAndSaveObjectsAreAbsent()
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        foreach (string name in new[] { "Girl", "RodData", "LureData", "BoatData", "ToolData", "UpgradeData", "GearData", "Icons" })
            ModObjects.Create(data, name, false);
        foreach (string name in new[] { "NewWorld", "room11" })
            data.Rooms.Add(new() { Name = data.Strings.MakeString(name) });

        ConsoleMod.Apply(data);

        string begin = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Step_1"));
        string end = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{ConsoleMod.ObjectName}_Step_2"));

        Assert.Contains("\"unlockall\"", begin);
        Assert.Contains("Girl.level", begin);
        Assert.DoesNotContain("CatalogData", begin);
        Assert.DoesNotContain("ExperienceBar", begin);
        Assert.DoesNotContain("SaveManager", begin);
        Assert.DoesNotContain("SaveManager", end);
    }

    private static string Code(UndertaleData data, string suffix)
        => ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{SmoothMod.ObjectName}_{suffix}"));

    private static UndertaleData Generate(bool debug)
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        data.Fonts.Add(new() { Name = data.Strings.MakeString("fnt_babySmall"), DisplayName = data.Strings.MakeString("Small") });
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
