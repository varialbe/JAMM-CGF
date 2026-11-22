using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Tests generic build-time decoration eligibility analysis and DecorSleep runtime installation.</summary>
public sealed class DecorSleepTests
{
    private static UndertaleData CreateSyntheticArchive()
    {
        var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;

        var room = new UndertaleRoom { Name = data.Strings.MakeString("NewWorld"), Width = 100000, Height = 13000 };
        data.Rooms.Add(room);

        var camSpr = new UndertaleSprite { Name = data.Strings.MakeString("spr_cam"), Width = 32, Height = 32 };
        var rockSpr = new UndertaleSprite { Name = data.Strings.MakeString("spr_rock"), Width = 512, Height = 512 };
        var weedSpr = new UndertaleSprite { Name = data.Strings.MakeString("spr_weed"), Width = 200, Height = 300 };
        var lightSpr = new UndertaleSprite { Name = data.Strings.MakeString("spr_light"), Width = 500, Height = 500 };
        data.Sprites.Add(camSpr);
        data.Sprites.Add(rockSpr);
        data.Sprites.Add(weedSpr);
        data.Sprites.Add(lightSpr);

        var camera = ModObjects.Create(data, "Camera", persistent: false);
        camera.Sprite = camSpr;
        ModObjects.PlaceInstance(data, "NewWorld", camera, first: true);

        var lure = ModObjects.Create(data, "Lure", persistent: false);
        var lightbulb = ModObjects.Create(data, "Lightbulb", persistent: false);
        var sonar = ModObjects.Create(data, "SonarPanel", persistent: false);

        var compiler = new CompileGroup(data);
        compiler.QueueCodeReplace(camera.EventHandlerFor(EventType.Create, 0u, data), "x = 800; y = 1000;");
        compiler.QueueCodeReplace(lure.EventHandlerFor(EventType.Create, 0u, data), "x = 800; y = 1000;");
        compiler.QueueCodeReplace(lightbulb.EventHandlerFor(EventType.Create, 0u, data), "x = 800; y = 1000;");
        compiler.QueueCodeReplace(sonar.EventHandlerFor(EventType.Create, 0u, data), "active = false; shouldDraw = false;");

        // Decor roots + children
        var collidable = ModObjects.Create(data, "Collidable", persistent: false);
        var caveRock = ModObjects.Create(data, "CaveRock", persistent: false);
        caveRock.ParentId = collidable;
        compiler.QueueCodeReplace(caveRock.EventHandlerFor(EventType.Create, 0u, data), "image_speed = 0; checkTimer = 0;");
        compiler.QueueCodeReplace(caveRock.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), """
            checkTimer += 1;
            if (checkTimer == 5)
            {
                checkTimer = 0;
                if (point_distance(x, y, Camera.x, Camera.y) > 5000) { visible = 0; exit; }
                else { visible = 1; }
            }
            """);

        var caveRockBig = ModObjects.Create(data, "CaveRockBig", persistent: false);
        caveRockBig.ParentId = caveRock;
        caveRockBig.Sprite = rockSpr;
        ModObjects.PlaceInstance(data, "NewWorld", caveRockBig, first: false);

        var sceneItem = ModObjects.Create(data, "SceneItem", persistent: false);
        compiler.QueueCodeReplace(sceneItem.EventHandlerFor(EventType.Create, 0u, data), "image_speed = 0; home_x = x; home_y = y; para_scale = 1;");
        compiler.QueueCodeReplace(sceneItem.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), """
            scene_depth = depth + 18;
            if (scene_depth > 18) scene_depth -= 18;
            if (scene_depth > 0) scene_depth *= 0.1; else scene_depth *= 0.5;
            x = home_x + ((Camera.x - home_x) * scene_depth * para_scale);
            y = home_y + ((Camera.y - home_y) * scene_depth * 0.1 * para_scale);
            """);
        compiler.QueueCodeReplace(sceneItem.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.Draw, data), "if (view_current != 2) draw_self();");

        var weed = ModObjects.Create(data, "Weed", persistent: false);
        weed.ParentId = sceneItem;
        weed.Sprite = weedSpr;
        compiler.QueueCodeReplace(weed.EventHandlerFor(EventType.Create, 0u, data), "action_inherited(); jitter = 10;");
        compiler.QueueCodeReplace(weed.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), """
            action_inherited();
            image_angle = sin((current_time + jitter + x) / 1000) * 10;
            """);
        ModObjects.PlaceInstance(data, "NewWorld", weed, first: false);

        var moddedProp = ModObjects.Create(data, "ModdedCoralProp", persistent: false);
        moddedProp.ParentId = sceneItem;
        moddedProp.Sprite = weedSpr;
        ModObjects.PlaceInstance(data, "NewWorld", moddedProp, first: false);

        var unparentedProp = ModObjects.Create(data, "Lush_Tree", persistent: false);
        unparentedProp.Sprite = rockSpr;
        compiler.QueueCodeReplace(unparentedProp.EventHandlerFor(EventType.Create, 0u, data), "depth = 15;");
        ModObjects.PlaceInstance(data, "NewWorld", unparentedProp, first: false);

        var abstractLight = ModObjects.Create(data, "AbstractLight", persistent: false);
        compiler.QueueCodeReplace(abstractLight.EventHandlerFor(EventType.Create, 0u, data), "add_blend = c_yellow; sub_blend = c_white; image_alpha = 1;");
        compiler.QueueCodeReplace(abstractLight.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.Draw, data), "if (view_current == 0) { image_blend = add_blend; draw_self(); image_blend = sub_blend; }");

        var pulseLight = ModObjects.Create(data, "ModdedPulseLight", persistent: false);
        pulseLight.ParentId = abstractLight;
        pulseLight.Sprite = lightSpr;
        compiler.QueueCodeReplace(pulseLight.EventHandlerFor(EventType.Create, 0u, data), "action_inherited(); start_scale = 1; scale_amplitude = 0.1; pulse_speed = 0.002; base_alpha = 0.1; alpha_amplitude = 0.05; time_offset = 0;");
        compiler.QueueCodeReplace(pulseLight.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), """
            var t = (current_time * pulse_speed) + time_offset;
            image_xscale = start_scale + (sin(t) * scale_amplitude);
            image_yscale = image_xscale;
            image_alpha = base_alpha + (cos(t) * alpha_amplitude);
            image_angle += 0.3;
            """);
        ModObjects.PlaceInstance(data, "NewWorld", pulseLight, first: false);

        // Exempted objects:
        // 1. Gameplay Fish + Fish_Create_0 referencing Coral
        var fish = ModObjects.Create(data, "Fish", persistent: false);
        var coral = ModObjects.Create(data, "Coral", persistent: false);
        coral.ParentId = sceneItem;
        coral.Sprite = weedSpr;
        compiler.QueueCodeReplace(fish.EventHandlerFor(EventType.Create, 0u, data), "var nc = instance_nearest(x, y, Coral); bombed_x = nc.x;");

        // 2. BreakableRock (Alarm + SaveManager check)
        var breakable = ModObjects.Create(data, "BreakableRock", persistent: false);
        breakable.ParentId = caveRock;
        breakable.Sprite = rockSpr;
        compiler.QueueCodeReplace(breakable.EventHandlerFor(EventType.Alarm, 0u, data), "visible = 0; y = 1000000;");
        var saveMgr = ModObjects.Create(data, "SaveManager", persistent: false);
        compiler.QueueCodeReplace(saveMgr.EventHandlerFor(EventType.Other, 14u, data), "var gone = !instance_exists(BreakableRock);");

        // 3. MissileSilo under CaveRockBig (exempt gameplay object)
        var silo = ModObjects.Create(data, "MissileSilo", persistent: false);
        silo.ParentId = caveRockBig;
        silo.Sprite = rockSpr;
        compiler.QueueCodeReplace(silo.EventHandlerFor(EventType.Create, 0u, data), "image_speed = 0; canOpen = 1;");

        // 4. Lilypad (RNG + collision_rectangle)
        var lilypad = ModObjects.Create(data, "Lilypad", persistent: false);
        lilypad.Sprite = weedSpr;
        compiler.QueueCodeReplace(lilypad.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), """
            if (collision_rectangle(bbox_left, bbox_top, bbox_right, bbox_bottom, Lure, false, true) != noone)
                y += random_range(0.1, 0.5);
            """);
        ModObjects.PlaceInstance(data, "NewWorld", lilypad, first: false);

        // 5. CrystalfishLight (gameplay visible control)
        var cfLight = ModObjects.Create(data, "CrystalfishLight", persistent: false);
        cfLight.ParentId = abstractLight;
        cfLight.Sprite = lightSpr;
        compiler.QueueCodeReplace(cfLight.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), "visible = instance_exists(Lightbulb);");

        // 6. Read-only with (CaveRock) in Batfin & collision_point(..., Collidable)
        var batfin = ModObjects.Create(data, "Batfin", persistent: false);
        batfin.ParentId = fish;
        compiler.QueueCodeReplace(batfin.EventHandlerFor(EventType.Create, 0u, data), """
            var best = noone;
            with (CaveRock) { var d = point_distance(x, y, other.x, other.y); if (d < 500) best = id; }
            if (collision_point(x, y, Collidable, true, false)) x += 1;
            """);

        // 7. Endless Ocean chunk stream owning CaveRockBig
        var chunkLoad = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jmk_endless_ocean_load");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("jmk_endless_ocean_load"), Code = chunkLoad });
        compiler.QueueCodeReplace(chunkLoad, "var r = instance_create(argument0, 500, CaveRockBig); ds_list_add(argument1, r);");
        var chunkStream = UndertaleCode.CreateEmptyEntry(data, "gml_Script_jamm_chunk_stream");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("jamm_chunk_stream"), Code = chunkStream });
        compiler.QueueCodeReplace(chunkStream, "exit;");
        var eoCtrl = ModObjects.Create(data, "obj_jmk_endless_ocean", persistent: false);
        compiler.QueueCodeReplace(eoCtrl.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), "jamm_chunk_stream(0, 0, 1, jmk_endless_ocean_load, jmk_endless_ocean_load);");

        // 8. SonarPanel_Draw_0 with (SceneItem) + light_anchor + GlowPlant spawning child light
        compiler.QueueCodeReplace(sonar.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.Draw, data),
            "if (active && shouldDraw) { with (SceneItem) { if (point_distance(x, y, Camera.x, Camera.y) < 8000) draw_self(); } }");
        var lightAnchor = UndertaleCode.CreateEmptyEntry(data, "gml_Script_light_anchor");
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString("light_anchor"), Code = lightAnchor });
        compiler.QueueCodeReplace(lightAnchor, "with (light) { x = other.x; y = other.y; visible = other.visible; }");
        var glowPlant = ModObjects.Create(data, "GlowPlant", persistent: false);
        glowPlant.ParentId = sceneItem;
        glowPlant.Sprite = weedSpr;
        compiler.QueueCodeReplace(glowPlant.EventHandlerFor(EventType.Create, 0u, data),
            "action_inherited(); light = instance_create(x, y, ModdedPulseLight);");
        compiler.QueueCodeReplace(glowPlant.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.DrawBegin, data),
            "action_inherited(); with (light) { x = other.x; y = other.y; }");

        ModObjects.Compile(compiler);
        return data;
    }

    [Fact]
    public void CoversEligibleStaticAndModdedDecorWhileExemptingGameplayObjects()
    {
        using var data = CreateSyntheticArchive();
        var report = DecorSleepAnalysis.Analyze(data);
        var byName = report.Objects.ToDictionary(o => o.GameObject.Name.Content, StringComparer.Ordinal);

        // Covered decor objects (including modded + unparented + pulse catch-up)
        Assert.True(byName["CaveRock"].Covered);
        Assert.True(byName["CaveRockBig"].Covered);
        Assert.False(byName["CaveRockBig"].DeactivationSafe); // Collidable + Batfin + jamm_chunk_stream
        Assert.True(byName["SceneItem"].Covered);
        Assert.True(byName["Weed"].Covered);
        Assert.True(byName["Weed"].DeactivationSafe);
        Assert.Equal(DecorParallaxKind.SceneItem, byName["Weed"].Parallax);
        Assert.True(byName["GlowPlant"].Covered);
        Assert.False(byName["GlowPlant"].DeactivationSafe); // spawns child light in Create_0 + Draw_72
        Assert.NotNull(report.LightAnchorCode);
        Assert.True(byName["ModdedCoralProp"].Covered);
        Assert.Equal(DecorParallaxKind.SceneItem, byName["ModdedCoralProp"].Parallax);
        Assert.True(byName["Lush_Tree"].Covered);
        Assert.True(byName["Lush_Tree"].DeactivationSafe);
        Assert.True(byName["AbstractLight"].Covered);
        Assert.False(byName["AbstractLight"].DeactivationSafe);
        Assert.True(byName["ModdedPulseLight"].Covered);
        Assert.Contains(byName["ModdedPulseLight"].StepPatches, sp => sp.Kind == DecorStepKind.AngleAccumPulse && Math.Abs(sp.Rate - 0.3) < 1e-9);

        // Exempted gameplay objects
        Assert.False(byName["Fish"].Covered);
        Assert.False(byName["Batfin"].Covered);
        Assert.False(byName["Coral"].Covered);
        Assert.False(byName["BreakableRock"].Covered);
        Assert.False(byName["MissileSilo"].Covered);
        Assert.False(byName["Lilypad"].Covered);
        Assert.False(byName["CrystalfishLight"].Covered);
        Assert.False(byName["Camera"].Covered);
        Assert.False(byName["SaveManager"].Covered);
        Assert.False(byName["obj_jmk_endless_ocean"].Covered);
    }

    [Fact]
    public void ApplyInstallsControllerGuardsStepsAndInitializesSkippedChildren()
    {
        using var data = CreateSyntheticArchive();
        var edits = new List<UndertaleCode>();
        var decisions = DecorSleep.Apply(data, edits, enabledByDefault: true);

        Assert.Contains(decisions, d => d.StartsWith("[CGF-PERF-DECOR] enabled", StringComparison.Ordinal));
        var ctrl = data.GameObjects.ByName(DecorSleep.Controller);
        Assert.NotNull(ctrl);
        Assert.True(ctrl!.Persistent);
        Assert.Equal(ctrl, data.Rooms.ByName("NewWorld")!.GameObjects[0].ObjectDefinition);

        // Guard injected into CaveRock_Step_0, SceneItem_Step_2, Weed_Step_0, ModdedPulseLight_Step_0
        string caveStep = ModObjects.Decompile(data, data.Code.ByName("gml_Object_CaveRock_Step_0")!);
        Assert.Contains("jamm_ds_enabled", caveStep, StringComparison.Ordinal);
        string sceneStep = ModObjects.Decompile(data, data.Code.ByName("gml_Object_SceneItem_Step_2")!);
        Assert.Contains("jamm_ds_vis", sceneStep, StringComparison.Ordinal);

        // Skipped child MissileSilo gets jamm_ds_managed = false so CaveRock_Step_0 still runs for it
        string siloCreate = ModObjects.Decompile(data, data.Code.ByName("gml_Object_MissileSilo_Create_0")!);
        Assert.Contains("jamm_ds_managed = 0", siloCreate, StringComparison.Ordinal);

        // light_anchor script respects sleeping jamm_ds_vis
        string lightAnchor = ModObjects.Decompile(data, data.Code.ByName("gml_Script_light_anchor")!);
        Assert.Contains("jamm_ds_vis", lightAnchor, StringComparison.Ordinal);

        // Re-applying is a clean no-op
        var second = DecorSleep.Apply(data, edits);
        Assert.Single(second);
        Assert.Contains("already installed", second[0], StringComparison.Ordinal);
    }

    [Fact]
    public void SkipsCleanlyWhenRequiredLegacyLayoutIsMissing()
    {
        using var data = UndertaleData.CreateNew();
        data.GeneralInfo.Major = 1;
        data.GeneralInfo.BytecodeVersion = 16;
        int codeCount = data.Code.Count;

        var decisions = DecorSleep.Apply(data);
        Assert.Single(decisions);
        Assert.Contains("skipped", decisions[0], StringComparison.Ordinal);
        Assert.Equal(codeCount, data.Code.Count);
        Assert.Null(data.GameObjects.ByName(DecorSleep.Controller));
    }

    [Fact]
    public void CoversGrottoAlphaLerpNoDrawCodeLitAndNoInheritChild()
    {
        using var data = CreateSyntheticArchive();
        var compiler = new CompileGroup(data);

        // GrottoObject + GrottoRock1 (Create_0 without action_inherited)
        var grotto = ModObjects.Create(data, "GrottoObject", persistent: false);
        compiler.QueueCodeReplace(grotto.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data),
            "if (instance_exists(Camera)) { x = home_x + ((Camera.x - home_x) * para_x); y = home_y + ((Camera.y - home_y) * para_y); }");
        compiler.QueueCodeReplace(grotto.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.Draw, data),
            "if (view_current != 2) draw_self();");

        var grottoRock = ModObjects.Create(data, "GrottoRock1", persistent: false);
        grottoRock.ParentId = grotto;
        grottoRock.Sprite = data.Sprites[1];
        compiler.QueueCodeReplace(grottoRock.EventHandlerFor(EventType.Create, 0u, data),
            "image_speed = 0; home_x = x; home_y = y; para_x = 0.2; para_y = 0.02;");
        ModObjects.PlaceInstance(data, "NewWorld", grottoRock, first: false);

        // GrottoFrontgroundrock (AlphaLerpLure)
        var frontRock = ModObjects.Create(data, "GrottoFrontgroundrock", persistent: false);
        frontRock.ParentId = data.GameObjects.ByName("SceneItem");
        frontRock.Sprite = data.Sprites[1];
        compiler.QueueCodeReplace(frontRock.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), """
            var target_alpha = 1;
            if (instance_exists(Lure)) { if (Lure.y > 1295) target_alpha = 0.15; }
            image_alpha = lerp(image_alpha, target_alpha, 0.08);
            """);
        ModObjects.PlaceInstance(data, "NewWorld", frontRock, first: false);

        // Sea (NoDrawCodeLit child whose Step sets background_color -> Step kept active, Visible managed)
        var noDrawLit = ModObjects.Create(data, "NoDrawCodeLit", persistent: false);
        compiler.QueueCodeReplace(noDrawLit.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.Draw, data), "if (view_current != 3) draw_self();");
        var sea = ModObjects.Create(data, "Sea", persistent: false);
        sea.ParentId = noDrawLit;
        sea.Sprite = data.Sprites[1];
        compiler.QueueCodeReplace(sea.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.Step, data), "background_color = 0;");
        ModObjects.PlaceInstance(data, "NewWorld", sea, first: false);

        ModObjects.Compile(compiler);

        var report = DecorSleepAnalysis.Analyze(data);
        var byName = report.Objects.ToDictionary(o => o.GameObject.Name.Content, StringComparer.Ordinal);

        Assert.True(byName["GrottoRock1"].Covered);
        Assert.Equal(DecorParallaxKind.GrottoObject, byName["GrottoRock1"].Parallax);
        Assert.True(byName["GrottoFrontgroundrock"].Covered);
        Assert.Contains(byName["GrottoFrontgroundrock"].StepPatches, sp => sp.Kind == DecorStepKind.AlphaLerpLure);
        Assert.True(byName["Sea"].Covered);
        Assert.True(byName["Sea"].ManageVisible);
        Assert.Empty(byName["Sea"].StepPatches);

        var edits = new List<UndertaleCode>();
        var decisions = DecorSleep.Apply(data, edits, enabledByDefault: false);
        Assert.Contains(decisions, d => d.Contains("default off", StringComparison.Ordinal));
        string grottoCreate = ModObjects.Decompile(data, data.Code.ByName("gml_Object_GrottoRock1_Create_0")!);
        Assert.Contains("jamm_ds_vis", grottoCreate, StringComparison.Ordinal);
        string ctrlCreate = ModObjects.Decompile(data, data.Code.ByName($"gml_Object_{DecorSleep.Controller}_Create_0")!);
        Assert.Contains("""ini_read_real("Perf", "DecorSleep", 0)""", ctrlCreate, StringComparison.Ordinal);
    }

    [Fact]
    public void PerfSettingsDefaultsDecorSleepOnAndProfileLibraryPersistsToggle()
    {
        var defaults = new PerfSettings();
        Assert.True(defaults.DecorSleep);

        string temp = Path.Combine(Path.GetTempPath(), $"jamm-ds-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var store = new Jamm.Core.Management.ManagerStore(temp);
            var profiles = new Jamm.Core.Management.ProfileLibrary(store);
            profiles.CreateEmpty("p1");
            var updated = profiles.SetPerf("p1", decorSleep: false);
            Assert.False(updated.Perf.DecorSleep);
            Assert.False(profiles.Get("p1").Perf.DecorSleep);

            // Older profile JSON lacking "decorSleep" defaults DecorSleep to true.
            string p2Path = Path.Combine(temp, "profiles", "p2.json");
            File.WriteAllText(p2Path, """
                {
                  "name": "p2",
                  "mods": [],
                  "resolutions": {},
                  "perf": { "startEnabled": false, "fishGrid": false, "interval": 4 }
                }
                """);
            Assert.True(profiles.Get("p2").Perf.DecorSleep);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void ArchiveBuilderInstallsDecorSleepOnlyWhenPerfDecorSleepIsTrue()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"jamm-ds-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string basePath = Path.Combine(temp, "base.win");
            using (var baseData = CreateSyntheticArchive())
            {
                baseData.GeneralInfo.Minor = 0;
                baseData.GeneralInfo.Build = 1773;
                baseData.GeneralInfo.Timestamp = 1692207312;
                var compiler = new CompileGroup(baseData);
                var trawler = UndertaleCode.CreateEmptyEntry(baseData, "gml_Script_fish_check_trawler");
                baseData.Scripts.Add(new UndertaleScript { Name = baseData.Strings.MakeString("fish_check_trawler"), Code = trawler });
                compiler.QueueCodeReplace(trawler, "if (collision_line(Net.top.x, Net.top.y, Net.bot.x, Net.bot.y, id, true, false)) { x += 1; }");
                var avoid = UndertaleCode.CreateEmptyEntry(baseData, "gml_Script_fish_avoid_fish");
                baseData.Scripts.Add(new UndertaleScript { Name = baseData.Strings.MakeString("fish_avoid_fish"), Code = avoid });
                compiler.QueueCodeReplace(avoid, "x = (argument1 * argument1) + 1; y = (100000 * argument2) + 1; z = (100000 * argument2) + 2;");
                var fishStep = baseData.GameObjects.ByName("Fish")!.EventHandlerFor(EventType.Step, 2u, baseData);
                compiler.QueueCodeReplace(fishStep, "x += 1;");
                var magmer = ModObjects.Create(baseData, "Magmer", persistent: false);
                compiler.QueueCodeReplace(magmer.EventHandlerFor(EventType.Draw, 0u, baseData), "light.image_alpha = 1 - power(1 - heat_level, 2);");
                var volteen = ModObjects.Create(baseData, "Volteen", persistent: false);
                compiler.QueueCodeReplace(volteen.EventHandlerFor(EventType.Other, 13u, baseData), "light.add_blend = 4210752; light.sub_blend = 4210752;");
                compiler.QueueCodeReplace(volteen.EventHandlerFor(EventType.Other, 10u, baseData), "if (charge > 0) { light.add_blend = 0; light.sub_blend = 0; } else { light.add_blend = 4210752; light.sub_blend = 4210752; }");
                ModObjects.Compile(compiler);
                using var fs = File.Create(basePath);
                UndertaleIO.Write(fs, baseData);
            }

            var store = new Jamm.Core.Management.ManagerStore(Path.Combine(temp, "store"));
            store.Set("base", basePath);
            var perfMod = new Jamm.Core.Management.BuiltinMods(store).Add("perf").Mod;
            var library = new Jamm.Core.Management.ModLibrary(store);
            var input = new Jamm.Core.Building.BuildInput(library.Patch(perfMod), library.Archive(perfMod));

            // 1. Plain Perf build with no perfSettings (or DecorSleep: false): byte-identical, no DecorSleep.
            string outPlain = Path.Combine(temp, "plain.win");
            Jamm.Core.Building.ArchiveBuilder.Build(basePath, [input], outPlain, noRepack: true, perfSettings: null);
            Assert.Equal(Jamm.Core.Building.BuildFiles.Hash(basePath), Jamm.Core.Building.BuildFiles.Hash(outPlain));

            string outOff = Path.Combine(temp, "off.win");
            Jamm.Core.Building.ArchiveBuilder.Build(basePath, [input], outOff, noRepack: true, perfSettings: new PerfSettings(DecorSleep: false, LightCulling: false));
            Assert.Equal(Jamm.Core.Building.BuildFiles.Hash(basePath), Jamm.Core.Building.BuildFiles.Hash(outOff));

            // 2. Profile default PerfSettings (DecorSleep: true, StartEnabled: false, FishGrid: false): installs DecorSleep only.
            string outOn = Path.Combine(temp, "on.win");
            var res = Jamm.Core.Building.ArchiveBuilder.Build(basePath, [input], outOn, noRepack: true, perfSettings: new PerfSettings());
            Assert.Contains(res.Plan.Decisions, d => d.StartsWith("[CGF-PERF-DECOR] enabled", StringComparison.Ordinal));
            using var loaded = Jamm.Core.GameFileLoader.Load(outOn);
            Assert.NotNull(loaded.Data.GameObjects.ByName(DecorSleep.Controller));
            Assert.Null(loaded.Data.GameObjects.ByName(PerfMode.ControllerName));
            Assert.Null(loaded.Data.GameObjects.ByName(FishGrid.Controller));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }
}
