using System.Text.Json;
using Jamm.Core;
using Jamm.Core.Building;
using Jamm.Core.Management;
using Jamm.Core.Patching;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Validates viewport light culling compilation, opt-in Perf wiring, and byte-identical plain Perf builds.</summary>
public sealed class LightCullingTests
{
    [Fact]
    public void LightCullingPatchesLightingSystemAndAddsController()
    {
        using var data = CreateLightingTestData(useTgeOffsetSurface: false);
        var edits = new List<UndertaleCode>();

        var decisions = LightCulling.Apply(data, edits, enabledByDefault: false);

        var ctrl = data.GameObjects.ByName(LightCulling.Controller);
        Assert.NotNull(ctrl);
        Assert.True(ctrl.Persistent);
        Assert.True(ctrl.Visible);
        Assert.Equal(-999998, ctrl.Depth);

        string decision = Assert.Single(decisions);
        Assert.Contains("4 light objects", decision, StringComparison.Ordinal);
        Assert.Contains("2 LightingSystem draw loops patched", decision, StringComparison.Ordinal);
        Assert.Contains("default=off", decision, StringComparison.Ordinal);

        string create = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{LightCulling.Controller}_Create_0")!);
        Assert.Contains("""ini_read_real("Perf", "LightCulling", 0)""", create, StringComparison.Ordinal);
        Assert.Contains("global.jamm_lc_mode[", create, StringComparison.Ordinal);

        string drawBegin = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{LightCulling.Controller}_Draw_72")!);
        Assert.Contains("global.jamm_lc_v0", drawBegin, StringComparison.Ordinal);
        Assert.Contains("global.jamm_lc_v1", drawBegin, StringComparison.Ordinal);
        Assert.Contains("global.jamm_lc_hid", drawBegin, StringComparison.Ordinal);
        Assert.Contains("visible = false", drawBegin, StringComparison.Ordinal);

        string postDraw = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{LightCulling.Controller}_Draw_77")!);
        Assert.Contains("global.jamm_lc_ready = 0", postDraw, StringComparison.Ordinal);
        Assert.Contains("visible = true", postDraw, StringComparison.Ordinal);

        string guiBegin = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{LightCulling.Controller}_Draw_74")!);
        Assert.Contains("visible = true", guiBegin, StringComparison.Ordinal);

        var lightingDraw = data.Code.ByName("gml_Object_LightingSystem_Draw_0")!;
        string patchedDraw = ModObjects.Decompile(data, lightingDraw);
        Assert.Contains("global.jamm_lc_v0", patchedDraw, StringComparison.Ordinal);
        Assert.Contains("global.jamm_lc_v1", patchedDraw, StringComparison.Ordinal);
        Assert.Contains("global.jamm_lc_ready", patchedDraw, StringComparison.Ordinal);
        Assert.Contains(lightingDraw, edits);
        Assert.Equal(8, edits.Count);

        // Controller placed in NewWorld without overwriting existing room instances.
        var room = data.Rooms.ByName("NewWorld")!;
        Assert.Contains(room.GameObjects, go => ReferenceEquals(go.ObjectDefinition, ctrl));

        // Idempotent second call.
        var second = LightCulling.Apply(data, edits);
        Assert.Equal(["LightCulling already present; skipped."], second);
        Assert.Equal(8, edits.Count);
    }

    [Fact]
    public void LightCullingPreservesTgeSurfaceOffsetAndScaleModes()
    {
        using var data = CreateLightingTestData(useTgeOffsetSurface: true);
        var edits = new List<UndertaleCode>();

        var decisions = LightCulling.Apply(data, edits, enabledByDefault: true);

        Assert.Contains("default=on", Assert.Single(decisions), StringComparison.Ordinal);
        string patchedDraw = ModObjects.Decompile(data, data.Code.ByName("gml_Object_LightingSystem_Draw_0")!);
        Assert.Contains("view_xview[0]) + 2", patchedDraw, StringComparison.Ordinal);
        Assert.Contains("global.jamm_lc_v0_n", patchedDraw, StringComparison.Ordinal);
        Assert.Contains("global.jamm_lc_v1_n", patchedDraw, StringComparison.Ordinal);

        int absId = data.GameObjects.IndexOf(data.GameObjects.ByName("AbstractLight")!);
        int lightId = data.GameObjects.IndexOf(data.GameObjects.ByName("Light")!);
        int crystalId = data.GameObjects.IndexOf(data.GameObjects.ByName("CrystalLight1")!);
        int customId = data.GameObjects.IndexOf(data.GameObjects.ByName("CustomNoInheritLight")!);
        string create = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{LightCulling.Controller}_Create_0")!);

        Assert.Contains($"global.jamm_lc_mode[{absId}] = 1", create, StringComparison.Ordinal);
        Assert.Contains($"global.jamm_lc_mode[{lightId}] = 2", create, StringComparison.Ordinal);
        Assert.Contains($"global.jamm_lc_mode[{crystalId}] = 1", create, StringComparison.Ordinal);
        Assert.Contains($"global.jamm_lc_mode[{customId}] = 3", create, StringComparison.Ordinal);
    }

    [Fact]
    public void LightCullingSkipsGracefullyWhenLayoutIsMissing()
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;

        var decisions = LightCulling.Apply(data);

        Assert.Equal(["LightCulling skipped (missing LightingSystem/AbstractLight/room layout)."], decisions);
        Assert.Null(data.GameObjects.ByName(LightCulling.Controller));
    }

    [Fact]
    public void PerfSettingsDefaultsLightCullingOnAndProfileLibraryPersistsToggle()
    {
        var defaults = new PerfSettings();
        Assert.True(defaults.LightCulling);

        string tempDir = Path.Combine(Path.GetTempPath(), $"jamm-lc-prof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new ManagerStore(tempDir);
            var profiles = new ProfileLibrary(store);
            profiles.CreateEmpty("p1");
            Assert.True(profiles.Get("p1").Perf.LightCulling);

            var disabled = profiles.SetPerf("p1", lightCulling: false);
            Assert.False(disabled.Perf.LightCulling);
            Assert.False(profiles.Get("p1").Perf.LightCulling);

            var enabled = profiles.SetPerf("p1", lightCulling: true);
            Assert.True(enabled.Perf.LightCulling);
            Assert.True(profiles.Get("p1").Perf.LightCulling);

            // Older profile JSON lacking "lightCulling" defaults LightCulling to true.
            string p1Json = File.ReadAllText(store.Path("profiles", "p1.json"));
            var p2Node = System.Text.Json.Nodes.JsonNode.Parse(p1Json)!.AsObject();
            p2Node["id"] = "p2";
            p2Node["name"] = "p2";
            p2Node["perf"]!.AsObject().Remove("lightCulling");
            File.WriteAllText(store.Path("profiles", "p2.json"), p2Node.ToJsonString());
            Assert.True(profiles.Get("p2").Perf.LightCulling);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ArchiveBuilderPlainPerfRemainsByteIdenticalAndOptInInstallsLightCulling()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"jamm-lc-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string basePath = Path.Combine(tempDir, "base.win");
            using (var baseData = CreateLightingTestData(useTgeOffsetSurface: false))
            {
                AddPerfBuiltinRequiredSymbols(baseData);
                using var fs = File.Create(basePath);
                UndertaleIO.Write(fs, baseData);
            }

            string perfPatchPath = Path.Combine(tempDir, "perf-only.patch.win");
            using (var patchData = CreateLightingTestData(useTgeOffsetSurface: false))
            {
                AddPerfBuiltinRequiredSymbols(patchData);
                PerfMod.Apply(patchData, applyFishGrid: false, applyScheduler: false, applyCoreOptimizations: false, applyDecorSleep: false, applyLightCulling: false);
                using var fs = File.Create(perfPatchPath);
                UndertaleIO.Write(fs, patchData);
            }

            byte[] baseBytes = File.ReadAllBytes(basePath);
            using var baseLoaded = GameFileLoader.Load(basePath);
            using var patchLoaded = GameFileLoader.Load(perfPatchPath);
            var patch = PatchBuilder.Create(baseLoaded, patchLoaded, "Perf");
            var input = new BuildInput(patch, perfPatchPath);

            // 1. Plain Perf build with null perfSettings or both DecorSleep/LightCulling false: byte-identical to vanilla.
            string outNull = Path.Combine(tempDir, "out-null.win");
            ArchiveBuilder.Build(basePath, [input], outNull, noRepack: true, perfSettings: null);
            Assert.Equal(baseBytes, File.ReadAllBytes(outNull));

            string outOff = Path.Combine(tempDir, "out-off.win");
            ArchiveBuilder.Build(basePath, [input], outOff, noRepack: true, perfSettings: new PerfSettings(DecorSleep: false, LightCulling: false));
            Assert.Equal(baseBytes, File.ReadAllBytes(outOff));

            // 2. Opt-in LightCulling only: installs obj_jamm_light_cull without obj_jamm_decor_sleep.
            string outLightOnly = Path.Combine(tempDir, "out-light.win");
            var res = ArchiveBuilder.Build(basePath, [input], outLightOnly, noRepack: true, perfSettings: new PerfSettings(DecorSleep: false, LightCulling: true));
            Assert.Contains(res.Plan.Decisions, d => d.Contains("LightCulling enabled", StringComparison.Ordinal));

            using (var loaded = GameFileLoader.Load(outLightOnly))
            {
                Assert.NotNull(loaded.Data.GameObjects.ByName(LightCulling.Controller));
                Assert.Null(loaded.Data.GameObjects.ByName(DecorSleep.Controller));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PerfModDefaultsLeaveLightCullingOffAndBenchmarkFlagInstallsIt()
    {
        using var data = CreateLightingTestData(useTgeOffsetSurface: false);
        AddPerfBuiltinRequiredSymbols(data);

        PerfMod.Apply(data, applyFishGrid: false, applyScheduler: false, applyCoreOptimizations: false);
        Assert.Null(data.GameObjects.ByName(LightCulling.Controller));

        var decisions = new List<string>();
        BenchmarkMod.Apply(data, new BenchmarkOptions(LightCulling: true), decisions.Add);
        Assert.NotNull(data.GameObjects.ByName(LightCulling.Controller));
        Assert.Contains(decisions, d => d.Contains("LightCulling enabled", StringComparison.Ordinal));
        string guiEnd = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{BenchmarkMod.ObjectName}_Draw_75")!);
        Assert.Contains("# light_culling=", guiEnd, StringComparison.Ordinal);
    }

    private static UndertaleData CreateLightingTestData(bool useTgeOffsetSurface)
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;

        AddFunction(data, "surface_exists", 1);
        AddFunction(data, "surface_create", 2);
        AddFunction(data, "surface_free", 1);
        AddFunction(data, "surface_get_width", 1);
        AddFunction(data, "surface_get_height", 1);
        AddFunction(data, "surface_set_target", 1);
        AddFunction(data, "surface_reset_target", 0);
        AddFunction(data, "draw_clear", 1);
        AddFunction(data, "draw_rectangle_colour", 9);
        AddFunction(data, "draw_set_blend_mode", 1);
        AddFunction(data, "draw_self", 0);
        AddFunction(data, "draw_set_alpha", 1);
        AddFunction(data, "draw_set_color", 1);
        AddFunction(data, "draw_rectangle", 5);
        AddFunction(data, "draw_surface", 3);
        AddFunction(data, "draw_surface_stretched", 5);
        AddFunction(data, "make_color_rgb", 3);
        AddFunction(data, "ini_open", 1);
        AddFunction(data, "ini_read_real", 3);
        AddFunction(data, "ini_key_exists", 2);
        AddFunction(data, "ini_write_real", 3);
        AddFunction(data, "ini_close", 0);
        AddFunction(data, "instance_number", 1);
        AddFunction(data, "instance_destroy", 0);
        AddFunction(data, "variable_global_exists", 1);
        AddFunction(data, "variable_instance_exists", 2);
        AddFunction(data, "sprite_exists", 1);
        AddFunction(data, "sprite_get_width", 1);
        AddFunction(data, "sprite_get_height", 1);
        AddFunction(data, "sprite_get_xoffset", 1);
        AddFunction(data, "sprite_get_yoffset", 1);
        AddFunction(data, "action_inherited", 0);
        AddFunction(data, "room_get_name", 1);
        AddFunction(data, "file_text_open_write", 1);
        AddFunction(data, "file_text_write_string", 2);
        AddFunction(data, "file_text_writeln", 1);
        AddFunction(data, "file_text_close", 1);
        AddFunction(data, "get_timer", 0);
        AddFunction(data, "random_set_seed", 1);
        AddFunction(data, "instance_exists", 1);
        AddFunction(data, "game_end", 0);

        AddScript(data, "color_from_depth", "return 0;");

        var radSprite = new UndertaleSprite
        {
            Name = data.Strings.MakeString("spr_light_radgrad"),
            Width = 500,
            Height = 500,
            OriginX = 250,
            OriginY = 250
        };
        data.Sprites.Add(radSprite);

        var camera = ModObjects.Create(data, "Camera", persistent: false);
        var girl = ModObjects.Create(data, "Girl", persistent: false);
        var lure = ModObjects.Create(data, "Lure", persistent: false);
        var rod = ModObjects.Create(data, "Rod", persistent: false);
        var torby = ModObjects.Create(data, "Torby", persistent: false);
        var fish = ModObjects.Create(data, "Fish", persistent: false);
        var catSwim = ModObjects.Create(data, "CatSwim", persistent: false);
        var boat = ModObjects.Create(data, "Boat", persistent: false);
        var net = ModObjects.Create(data, "Net", persistent: false);

        var absLight = ModObjects.Create(data, "AbstractLight", persistent: false);
        absLight.Depth = -20;
        var light = ModObjects.Create(data, "Light", persistent: false);
        light.ParentId = absLight;
        light.Sprite = radSprite;
        light.Depth = -20;
        var crystal = ModObjects.Create(data, "CrystalLight1", persistent: false);
        crystal.ParentId = absLight;
        crystal.Depth = 0;
        var custom = ModObjects.Create(data, "CustomNoInheritLight", persistent: false);
        custom.ParentId = absLight;
        custom.Sprite = radSprite;
        custom.Depth = -20;

        var lightingSystem = ModObjects.Create(data, "LightingSystem", persistent: false);

        var world = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld"), Width = 10000, Height = 10000 };
        data.Rooms.Add(world);

        var compiler = new UndertaleModLib.Compiler.CompileGroup(data);
        compiler.QueueCodeReplace(absLight.EventHandlerFor(EventType.Create, 0u, data), """
            add_blend = make_color_rgb(80, 80, 30);
            sub_blend = make_color_rgb(150, 150, 150);
            image_alpha = 1;
            image_scale = 2;
            image_xscale = image_scale;
            image_yscale = image_scale;
            """);
        compiler.QueueCodeReplace(light.EventHandlerFor(EventType.Create, 0u, data), """
            action_inherited();
            special_scale = 0.15;
            """);
        compiler.QueueCodeReplace(crystal.EventHandlerFor(EventType.Create, 0u, data), """
            action_inherited();
            sprite_index = spr_light_radgrad;
            """);
        compiler.QueueCodeReplace(custom.EventHandlerFor(EventType.Create, 0u, data), """
            image_xscale = 1;
            image_yscale = 1;
            """);

        string draw0 = useTgeOffsetSurface
            ? """
              var _vw = ceil(view_wview[0]);
              var _vh = ceil(view_hview[0]);
              var _surf_w = _vw + 4;
              var _surf_h = _vh + 4;
              if (!surface_exists(light_surface)) light_surface = surface_create(_surf_w, _surf_h);
              if (!surface_exists(mini_surface)) mini_surface = surface_create(100, 100);
              if (view_current == 0)
              {
                  surface_set_target(light_surface);
                  draw_clear(0);
                  top_color = color_from_depth(Camera.y - (_vh * 0.5));
                  bot_color = color_from_depth(Camera.y + (_vh * 0.5));
                  draw_rectangle_colour(0, 0, _surf_w, _surf_h, top_color, top_color, bot_color, bot_color, 0);
                  draw_set_blend_mode(3);
                  with (AbstractLight)
                  {
                      if (visible)
                      {
                          x_original = x;
                          y_original = y;
                          x = (x_original - view_xview[0]) + 2;
                          y = (y_original - view_yview[0]) + 2;
                          draw_self();
                          x = x_original;
                          y = y_original;
                      }
                  }
                  draw_set_blend_mode(0);
                  surface_reset_target();
              }
              else if (view_current == 1)
              {
                  surface_set_target(mini_surface);
                  draw_clear(0);
                  draw_set_blend_mode(3);
                  with (AbstractLight)
                  {
                      if (visible)
                      {
                          x_original = x;
                          y_original = y;
                          x = (x_original - view_xview[1]) * 0.1666;
                          y = (y_original - view_yview[1]) * 0.1666;
                          image_xscale *= 0.1666;
                          image_yscale *= 0.1666;
                          draw_self();
                          x = x_original;
                          y = y_original;
                          image_xscale /= 0.1666;
                          image_yscale /= 0.1666;
                      }
                  }
                  draw_set_blend_mode(0);
                  surface_reset_target();
              }
              draw_set_alpha(1);
              """
            : """
              if (!surface_exists(light_surface)) light_surface = surface_create(width, height);
              if (!surface_exists(mini_surface)) mini_surface = surface_create(100, 100);
              if (view_current == 0)
              {
                  surface_set_target(light_surface);
                  draw_clear(0);
                  top_color = color_from_depth(Camera.y - (height * 0.5));
                  bot_color = color_from_depth(Camera.y + (height * 0.5));
                  draw_rectangle_colour(0, 0, width, height, top_color, top_color, bot_color, bot_color, 0);
                  draw_set_blend_mode(3);
                  with (AbstractLight)
                  {
                      if (visible)
                      {
                          x_original = x;
                          y_original = y;
                          x = x_original - view_xview[0];
                          y = y_original - view_yview[0];
                          draw_self();
                          x = x_original;
                          y = y_original;
                      }
                  }
                  draw_set_blend_mode(0);
                  surface_reset_target();
              }
              else if (view_current == 1)
              {
                  surface_set_target(mini_surface);
                  draw_clear(0);
                  draw_set_blend_mode(3);
                  with (AbstractLight)
                  {
                      if (visible)
                      {
                          x_original = x;
                          y_original = y;
                          x = (x_original - view_xview[1]) * 0.1666;
                          y = (y_original - view_yview[1]) * 0.1666;
                          image_xscale *= 0.1666;
                          image_yscale *= 0.1666;
                          draw_self();
                          x = x_original;
                          y = y_original;
                          image_xscale /= 0.1666;
                          image_yscale /= 0.1666;
                      }
                  }
                  draw_set_blend_mode(0);
                  surface_reset_target();
              }
              draw_set_alpha(1);
              """;

        compiler.QueueCodeReplace(lightingSystem.EventHandlerFor(EventType.Draw, 0u, data), draw0);
        ModObjects.Compile(compiler);
        _ = (camera, girl, lure, rod, torby, fish, catSwim, boat, net);
        return data;
    }

    private static void AddPerfBuiltinRequiredSymbols(UndertaleData data)
    {
        AddFunction(data, "keyboard_check_pressed", 1);
        AddFunction(data, "keyboard_check", 1);
        AddFunction(data, "point_distance", 4);
        AddFunction(data, "instance_activate_all", 0);
        AddFunction(data, "instance_deactivate_object", 1);
        AddFunction(data, "is_undefined", 1);
        AddFunction(data, "collision_line", 7);
        AddFunction(data, "power", 2);

        foreach (string name in new[]
        {
            "SaveManager", "GameModeMenu", "GM_Classic", "GM_Realism",
            "Girl", "Rod", "Lure", "Boat", "BoatData",
            "ToolData", "SonarPanel", "SonarPanelAdvanced", "RadarButton",
            "Camera", "AmbianceController", "QuestEngine",
            "Fish", "SceneItem", "Magmer", "Volteen"
        })
        {
            if (data.GameObjects.ByName(name) is null)
                ModObjects.Create(data, name, persistent: false);
        }

        AddScript(data, "fish_avoid_fish", "if (d < (argument1 * argument1)) { h += (100000 * argument2) / d; v += (100000 * argument2) / d; }");
        AddScript(data, "fish_check_trawler", "if (collision_line(Net.top.x, Net.top.y, Net.bot.x, Net.bot.y, id, true, false)) { state = 3; }");

        var compiler = new UndertaleModLib.Compiler.CompileGroup(data);
        compiler.QueueCodeReplace(data.GameObjects.ByName("QuestEngine")!.EventHandlerFor(EventType.Step, 1u, data), "quest_counter = 0;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Girl")!.EventHandlerFor(EventType.Step, 0u, data), "a = delta_time; b = delta_time; c = delta_time;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Fish")!.EventHandlerFor(EventType.Step, 2u, data), "if (y > 5000 && !deepfish)\n{\n    var r = 1;\n}");
        compiler.QueueCodeReplace(data.GameObjects.ByName("SceneItem")!.EventHandlerFor(EventType.Step, 2u, data), "scene_depth = depth + 18;\nx = home_x;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Magmer")!.EventHandlerFor(EventType.Draw, 0u, data), "light.image_alpha = 1 - power(1 - heat_level, 2);");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Volteen")!.EventHandlerFor(EventType.Other, 13u, data), "light.add_blend = 4210752;\nlight.sub_blend = 4210752;");
        compiler.QueueCodeReplace(data.GameObjects.ByName("Volteen")!.EventHandlerFor(EventType.Other, 10u, data), "if (charge > 0)\n{\n    light.add_blend = 0;\n    light.sub_blend = 0;\n}\nelse\n{\n    light.add_blend = 4210752;\n    light.sub_blend = 4210752;\n}");
        ModObjects.Compile(compiler);
    }

    private static void AddFunction(UndertaleData data, string name, int argCount)
    {
        if (data.Functions.ByName(name) is not null) return;
        data.Functions.Add(new UndertaleFunction
        {
            Name = data.Strings.MakeString(name),
            NameStringID = data.Strings.Count - 1
        });
        _ = argCount;
    }

    private static void AddScript(UndertaleData data, string name, string gml)
    {
        if (data.Scripts.ByName(name) is not null) return;
        var code = new UndertaleCode { Name = data.Strings.MakeString("gml_Script_" + name) };
        data.Code.Add(code);
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
        var compiler = new UndertaleModLib.Compiler.CompileGroup(data);
        compiler.QueueCodeReplace(code, gml);
        ModObjects.Compile(compiler);
    }
}
