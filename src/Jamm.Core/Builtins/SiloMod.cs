using System.Buffers.Binary;
using System.IO.Compression;
using Jamm.Core.ModKit;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// Optional isolated "Silo" mod: activates the existing <c>MissileSilo</c> hatch in <c>NewWorld</c>
/// with visible opening animation and lets the player descend into a pleasant underwater sanctuary
/// (<c>room_jamm_silo</c>) and exit safely back to the ocean.
/// </summary>
public static class SiloMod
{
    /// <summary>Stable mod identifier used in recipes and library provenance.</summary>
    public const string ModId = "jamm-silo";
    /// <summary>Persistent controller managing safe room transitions.</summary>
    public const string ControllerObjectName = "obj_jamm_silo_ctrl";
    /// <summary>Beacon light object placed at the silo hatch.</summary>
    public const string BeaconObjectName = "obj_jamm_silo_beacon";
    /// <summary>Sanctuary room controller and player explorer object.</summary>
    public const string AreaObjectName = "obj_jamm_silo_area";
    /// <summary>Dedicated underwater sanctuary room.</summary>
    public const string SanctuaryRoomName = "room_jamm_silo";

    /// <summary>Applies the isolated Silo mod onto an in-memory legacy <see cref="UndertaleData"/>.</summary>
    public static void Apply(UndertaleData data, string? packageDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var newWorld = data.Rooms.ByName("NewWorld")
            ?? throw new InvalidDataException("Required room 'NewWorld' was not found: build the Silo mod from the untouched legacy 2023 data.win.");
        var missileSilo = data.GameObjects.ByName("MissileSilo")
            ?? throw new InvalidDataException("Required object 'MissileSilo' was not found: build the Silo mod from the untouched legacy 2023 data.win.");
        if (data.Sprites.ByName("spr_silo") is null || data.Sprites.ByName("spr_silomask") is null)
            throw new InvalidDataException("Required sprites 'spr_silo' and 'spr_silomask' were not found: build the Silo mod from the untouched legacy 2023 data.win.");
        if (data.Rooms.ByName(SanctuaryRoomName) is not null || data.GameObjects.ByName(ControllerObjectName) is not null)
            throw new InvalidDataException("Silo mod assets already exist: build the Silo mod from the original data.win, not a modded one.");

        var sprites = ImportSprites(data, packageDirectory);

        bool hasCatSwim = data.GameObjects.ByName("CatSwim") is not null;
        bool hasLure = data.GameObjects.ByName("Lure") is not null;
        bool hasCamera = data.GameObjects.ByName("Camera") is not null;
        bool hasSaveManager = data.GameObjects.ByName("SaveManager") is not null;
        bool hasNavBrain = data.GameObjects.ByName("NavBrain") is not null;
        bool hasBreathBar = data.GameObjects.ByName("BreathBar") is not null;
        bool hasHandler = data.GameObjects.ByName("Handler") is not null;
        bool hasFont = data.Fonts.ByName("fnt_babySmall") is not null;
        var abstractLight = data.GameObjects.ByName("AbstractLight");

        var ctrl = ModObjects.Create(data, ControllerObjectName, persistent: true);
        ctrl.Visible = true;
        ctrl.Depth = -10000;

        var beacon = new UndertaleGameObject
        {
            Name = data.Strings.MakeString(BeaconObjectName),
            ParentId = abstractLight,
            Sprite = sprites["spr_jamm_silo_glow"],
            Visible = true,
            Persistent = false,
            Depth = 0
        };
        data.GameObjects.Add(beacon);

        var area = new UndertaleGameObject
        {
            Name = data.Strings.MakeString(AreaObjectName),
            Sprite = sprites["spr_jamm_silo_diver"],
            Visible = true,
            Persistent = false,
            Depth = 0
        };
        data.GameObjects.Add(area);

        var sanctuary = CreateSanctuaryRoom(data, area);

        var compiler = new CompileGroup(data);

        // Persistent transition controller
        Queue(compiler, data, ctrl, EventType.Create, 0, ControllerCreateGml());
        Queue(compiler, data, ctrl, EventType.Other, (uint)EventSubtypeOther.RoomStart,
            ControllerRoomStartGml(hasSaveManager, hasNavBrain, hasCatSwim, hasLure, hasCamera, hasBreathBar));
        Queue(compiler, data, ctrl, EventType.Step, (uint)EventSubtypeStep.BeginStep, ControllerBeginStepGml());
        Queue(compiler, data, ctrl, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, ControllerDrawGuiGml(hasFont));

        // Hatch beacon light in NewWorld
        Queue(compiler, data, beacon, EventType.Create, 0, BeaconCreateGml(abstractLight is not null));
        Queue(compiler, data, beacon, EventType.Step, (uint)EventSubtypeStep.Step, BeaconStepGml());

        // Existing MissileSilo door asset behavior + visible opening animation
        Queue(compiler, data, missileSilo, EventType.Create, 0, MissileSiloCreateGml());
        Queue(compiler, data, missileSilo, EventType.Step, (uint)EventSubtypeStep.Step,
            MissileSiloStepGml(hasCatSwim, hasLure, hasNavBrain, hasHandler));
        Queue(compiler, data, missileSilo, EventType.Draw, (uint)EventSubtypeDraw.Draw, MissileSiloDrawGml(hasFont));

        // Pleasant sanctuary area controller & player diver
        Queue(compiler, data, area, EventType.Create, 0, AreaCreateGml(hasSaveManager));
        Queue(compiler, data, area, EventType.Step, (uint)EventSubtypeStep.Step, AreaStepGml());
        Queue(compiler, data, area, EventType.Draw, (uint)EventSubtypeDraw.Draw, AreaDrawGml());
        Queue(compiler, data, area, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, AreaDrawGuiGml(hasFont));

        ModObjects.Compile(compiler);

        // Place controller and hatch beacon in NewWorld (read-only base coordinates near MissileSilo at 33984, 11570).
        var siloInst = newWorld.GameObjects.FirstOrDefault(g => g.ObjectDefinition == missileSilo);
        int beaconX = (siloInst?.X ?? 33984) - 24;
        int beaconY = (siloInst?.Y ?? 11570) + 30;
        ModObjects.PlaceInstance(data, "NewWorld", ctrl, first: true);
        newWorld.GameObjects.Add(new UndertaleRoom.GameObject
        {
            InstanceID = ++data.GeneralInfo.LastObj,
            ObjectDefinition = beacon,
            X = beaconX,
            Y = beaconY
        });

        // Place sanctuary diver/controller inside room_jamm_silo.
        sanctuary.GameObjects.Add(new UndertaleRoom.GameObject
        {
            InstanceID = ++data.GeneralInfo.LastObj,
            ObjectDefinition = area,
            X = 800,
            Y = 145
        });
    }

    private static Dictionary<string, UndertaleSprite> ImportSprites(UndertaleData data, string? packageDirectory)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "jamm-silo-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string glowPath = ResolveOrCreateArt(packageDirectory, tempDir, "spr_jamm_silo_glow.png", CreateGlowPng);
            string diverPath = ResolveOrCreateArt(packageDirectory, tempDir, "spr_jamm_silo_diver.png", CreateDiverStripPng);
            string fishPath = ResolveOrCreateArt(packageDirectory, tempDir, "spr_jamm_silo_fish.png", CreateSanctuaryFishStripPng);

            var specs = new List<SpriteImportSpec>
            {
                new("spr_jamm_silo_glow", glowPath, 1, [64, 64], CenterOrigin: true, GenerateMask: false),
                new("spr_jamm_silo_diver", diverPath, 2, [32, 24], CenterOrigin: true, GenerateMask: true),
                new("spr_jamm_silo_fish", fishPath, 2, [24, 16], CenterOrigin: true, GenerateMask: false)
            };
            return ModKitSpriteImporter.ImportAll(data, specs);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string ResolveOrCreateArt(string? packageDirectory, string tempDir, string fileName, Func<byte[]> generate)
    {
        if (!string.IsNullOrWhiteSpace(packageDirectory))
        {
            string candidate = Path.GetFullPath(Path.Combine(packageDirectory, "art", fileName));
            if (File.Exists(candidate))
                return candidate;
        }
        string fallback = Path.Combine(tempDir, fileName);
        File.WriteAllBytes(fallback, generate());
        return fallback;
    }

    private static UndertaleRoom CreateSanctuaryRoom(UndertaleData data, UndertaleGameObject area)
    {
        var room = new UndertaleRoom
        {
            Name = data.Strings.MakeString(SanctuaryRoomName),
            Caption = data.Strings.MakeString("Silo Sanctuary"),
            Width = 1600,
            Height = 1100,
            Speed = 30,
            Persistent = false,
            BackgroundColor = 0xFF3A2812,
            DrawBackgroundColor = true,
            Flags = UndertaleRoom.RoomEntryFlags.EnableViews | UndertaleRoom.RoomEntryFlags.ClearViewBackground
        };
        if (room.Views.Count > 0)
        {
            var view = room.Views[0];
            view.Enabled = true;
            view.ViewX = 400;
            view.ViewY = 0;
            view.ViewWidth = 800;
            view.ViewHeight = 480;
            view.PortX = 0;
            view.PortY = 0;
            view.PortWidth = 1200;
            view.PortHeight = 720;
            view.BorderX = 400;
            view.BorderY = 240;
            view.SpeedX = -1;
            view.SpeedY = -1;
            view.ObjectId = area;
        }
        data.Rooms.Add(room);
        data.GeneralInfo.RoomOrder.Add(new(room, data.Rooms.IndexOf(room)));
        return room;
    }

    private static void Queue(CompileGroup compiler, UndertaleData data, UndertaleGameObject obj, EventType type, uint subtype, string gml)
        => compiler.QueueCodeReplace(obj.EventHandlerFor(type, subtype, data), gml);

    private static string ControllerCreateGml() => """
        if (instance_number(object_index) > 1)
        {
            instance_destroy();
            exit;
        }
        persistent = true;
        silo_returning = 0;
        silo_cooldown = 0;
        silo_entry_mode = 0;
        silo_return_x = 33960;
        silo_return_y = 11470;
        silo_toast_until = 0;
        silo_toast_text = "";
        """;

    private static string ControllerRoomStartGml(bool hasSaveManager, bool hasNavBrain, bool hasCatSwim, bool hasLure, bool hasCamera, bool hasBreathBar)
    {
        string activateSave = hasSaveManager ? "instance_activate_object(SaveManager);" : "";
        string activateNav = hasNavBrain ? "instance_activate_object(NavBrain);" : "";
        string resetCat = hasCatSwim ? """
            if (instance_exists(CatSwim))
            {
                CatSwim.x = silo_return_x;
                CatSwim.y = silo_return_y;
                CatSwim.swim_x = 0;
                CatSwim.swim_y = 0;
                CatSwim.drift_x = 0;
                CatSwim.drift_y = 0;
                if (instance_exists(CatSwim.tracker))
                {
                    CatSwim.tracker.x = silo_return_x;
                    CatSwim.tracker.y = silo_return_y;
                }
                if (instance_exists(CatSwim.collider))
                {
                    CatSwim.collider.x = silo_return_x;
                    CatSwim.collider.y = silo_return_y;
                }
            }
            """ : "";
        string resetBreath = hasBreathBar ? """
            if (instance_exists(BreathBar))
            {
                BreathBar.breath = max(BreathBar.breath, BreathBar.MAX_BREATH * 0.75);
            }
            """ : "";
        string resetLure = hasLure ? """
            if (instance_exists(Lure) && Lure.state != 0)
            {
                Lure.x = silo_return_x;
                Lure.y = silo_return_y;
                Lure.speed = 0;
                Lure.hspeed = 0;
                Lure.vspeed = 0;
            }
            """ : "";
        string resetCam = hasCamera ? """
            if (instance_exists(Camera))
            {
                Camera.x = silo_return_x;
                Camera.y = silo_return_y;
            }
            """ : "";

        return $$"""
            if (room == NewWorld && silo_returning)
            {
                silo_returning = 0;
                silo_cooldown = 75;
                room_persistent = false;
                {{activateSave}}
                {{activateNav}}
                {{resetCat}}
                {{resetBreath}}
                {{resetLure}}
                {{resetCam}}
                silo_toast_text = "Returned safely from Silo Sanctuary";
                silo_toast_until = current_time + 3200;
            }
            """;
    }

    private static string ControllerBeginStepGml() => """
        if (silo_cooldown > 0)
        {
            silo_cooldown -= 1;
        }
        """;

    private static string ControllerDrawGuiGml(bool hasFont)
    {
        string fontLine = hasFont ? "draw_set_font(fnt_babySmall);" : "draw_set_font(-1);";
        return $$"""
            if (current_time < silo_toast_until && silo_toast_text != "")
            {
                {{fontLine}}
                draw_set_halign(fa_center);
                draw_set_valign(fa_middle);
                draw_set_alpha(0.78);
                draw_set_color(c_black);
                draw_rectangle(210, 14, 590, 42, false);
                draw_set_alpha(1);
                draw_set_color(make_color_rgb(165, 245, 230));
                draw_text(400, 28, silo_toast_text);
                draw_set_halign(fa_left);
                draw_set_valign(fa_top);
            }
            """;
    }

    private static string BeaconCreateGml(bool hasParent) => $$"""
        {{(hasParent ? "event_inherited();" : "")}}
        add_blend = make_color_rgb(90, 215, 200);
        sub_blend = make_color_rgb(210, 240, 235);
        image_scale = 3.2;
        image_xscale = image_scale;
        image_yscale = image_scale;
        image_alpha = 0.9;
        silo_pulse = 0;
        """;

    private static string BeaconStepGml() => """
        silo_pulse += 0.07;
        image_scale = 3.0 + (0.35 * sin(silo_pulse));
        image_xscale = image_scale;
        image_yscale = image_scale;
        """;

    private static string MissileSiloCreateGml() => """
        image_speed = 0;
        image_index = 0;
        mask_index = spr_silomask;
        canOpen = 1;
        silo_state = 0;
        silo_anim = 0;
        silo_lid_lift = 0;
        silo_near = 0;
        silo_near_mode = 0;
        silo_tick = 0;
        """;

    private static string MissileSiloStepGml(bool hasCatSwim, bool hasLure, bool hasNavBrain, bool hasHandler)
    {
        string checkCat = hasCatSwim ? """
            if (instance_exists(CatSwim))
            {
                d_cat = point_distance(CatSwim.x, CatSwim.y, hx, hy);
                if (d_cat < 420)
                {
                    near_cat = 1;
                    silo_near_mode = 1;
                }
                if (silo_state == 2 && cooldown <= 0)
                {
                    if (point_distance(CatSwim.x, CatSwim.y, hx, hy + 24) < 110 && CatSwim.y > (y - 6))
                    {
                        enter_now = 1;
                        enter_mode = 1;
                    }
                }
            }
            """ : "";
        string checkLure = hasLure ? """
            if (instance_exists(Lure) && Lure.state != 0)
            {
                d_lure = point_distance(Lure.x, Lure.y, hx, hy);
                if (d_lure < 420)
                {
                    near_lure = 1;
                    if (silo_near_mode == 0) silo_near_mode = 2;
                }
            }
            """ : "";
        string handlerGuard = hasHandler ? "!instance_exists(Handler) && " : "";
        string deactivateNav = hasNavBrain ? "if (instance_exists(NavBrain)) instance_deactivate_object(NavBrain);" : "";

        string farCheck = FarCheckGml(hasCatSwim, hasLure);

        return $$"""
            var hx, hy, near_cat, near_lure, d_cat, d_lure, cooldown, enter_now, enter_mode;
            silo_tick += 1;
            hx = x - 24;
            hy = y + 30;
            near_cat = 0;
            near_lure = 0;
            silo_near_mode = 0;
            enter_now = 0;
            enter_mode = 0;
            cooldown = 0;
            if (instance_exists({{ControllerObjectName}}))
            {
                cooldown = {{ControllerObjectName}}.silo_cooldown;
            }
            {{checkCat}}
            {{checkLure}}
            silo_near = (near_cat || near_lure);
            if (silo_near)
            {
                if (silo_state == 0 || silo_state == 3)
                {
                    silo_state = 1;
                }
                if (silo_state == 2 && cooldown <= 0)
                {
                    if ({{handlerGuard}}(keyboard_check_pressed(ord("W")) || keyboard_check_pressed(ord("E")) || keyboard_check_pressed(vk_enter)))
                    {
                        enter_now = 1;
                        enter_mode = silo_near_mode;
                    }
                }
            }
            else
            {
                if (silo_state == 1 || silo_state == 2)
                {
                    {{farCheck}}
                }
            }
            if (silo_state == 0)
            {
                silo_anim = 0;
                silo_lid_lift = 0;
                sprite_index = spr_silo;
                mask_index = spr_silomask;
                image_index = 0;
            }
            else if (silo_state == 1)
            {
                silo_anim = min(4, silo_anim + 0.09);
                silo_lid_lift = clamp(silo_anim / 4, 0, 1);
                sprite_index = spr_silomask;
                mask_index = spr_silomask;
                image_index = floor(silo_anim);
                if (silo_anim >= 4)
                {
                    silo_state = 2;
                }
            }
            else if (silo_state == 2)
            {
                silo_anim = 4;
                silo_lid_lift = 1;
                sprite_index = spr_silomask;
                mask_index = spr_silomask;
                image_index = 4;
            }
            else if (silo_state == 3)
            {
                silo_anim = max(0, silo_anim - 0.09);
                silo_lid_lift = clamp(silo_anim / 4, 0, 1);
                sprite_index = spr_silomask;
                mask_index = spr_silomask;
                image_index = floor(silo_anim);
                if (silo_anim <= 0)
                {
                    silo_state = 0;
                    sprite_index = spr_silo;
                    image_index = 0;
                }
            }
            if (enter_now)
            {
                if (instance_exists({{ControllerObjectName}}))
                {
                    {{ControllerObjectName}}.silo_entry_mode = enter_mode;
                    {{ControllerObjectName}}.silo_return_x = hx;
                    {{ControllerObjectName}}.silo_return_y = y - 80;
                    {{ControllerObjectName}}.silo_returning = 0;
                }
                room_persistent = true;
                {{deactivateNav}}
                room_goto({{SanctuaryRoomName}});
            }
            """;
    }

    private static string FarCheckGml(bool hasCatSwim, bool hasLure)
    {
        var checks = new List<string>();
        if (hasCatSwim) checks.Add("if (instance_exists(CatSwim) && point_distance(CatSwim.x, CatSwim.y, hx, hy) < 620) keep_open = 1;");
        if (hasLure) checks.Add("if (instance_exists(Lure) && Lure.state != 0 && point_distance(Lure.x, Lure.y, hx, hy) < 620) keep_open = 1;");
        return $$"""
            var keep_open;
            keep_open = 0;
            {{string.Join("\n            ", checks)}}
            if (!keep_open)
            {
                silo_state = 3;
            }
            """;
    }

    private static string MissileSiloDrawGml(bool hasFont)
    {
        string fontLine = hasFont ? "draw_set_font(fnt_babySmall);" : "draw_set_font(-1);";
        return $$"""
            var hx, hy, i, by, bx, glow_col;
            hx = x - 24;
            hy = y + 30;
            if (silo_state == 0)
            {
                draw_sprite_ext(spr_silo, 0, x, y, image_xscale, image_yscale, image_angle, image_blend, image_alpha);
            }
            else
            {
                glow_col = make_color_rgb(90, 230, 210);
                draw_set_blend_mode(bm_add);
                draw_set_alpha(0.35 * silo_lid_lift);
                draw_circle_colour(hx, hy + 16, 65 + (25 * silo_lid_lift), glow_col, c_black, false);
                draw_set_alpha(0.22 * silo_lid_lift);
                draw_rectangle_colour(hx - 54, hy - 35, hx + 54, hy + 55, glow_col, glow_col, c_black, c_black, false);
                draw_set_blend_mode(bm_normal);
                draw_set_alpha(1);

                draw_sprite_ext(spr_silomask, floor(silo_anim), x, y, image_xscale, image_yscale, image_angle, image_blend, image_alpha);
                if (silo_lid_lift < 0.99)
                {
                    draw_sprite_ext(spr_silo, 0, x - (56 * silo_lid_lift), y - (44 * silo_lid_lift), image_xscale, image_yscale, 12 * silo_lid_lift, image_blend, 1 - silo_lid_lift);
                }

                draw_set_alpha(0.6 * silo_lid_lift);
                draw_set_color(make_color_rgb(175, 250, 240));
                for (i = 0; i < 6; i += 1)
                {
                    by = hy + 30 - ((silo_tick * 1.4 + i * 19) mod 95);
                    bx = hx - 36 + (i * 14) + (6 * sin((silo_tick * 0.1) + i));
                    draw_circle(bx, by, 2 + (i mod 2), true);
                }
                draw_set_alpha(1);
            }

            if (silo_near)
            {
                var label;
                if (silo_state == 2)
                {
                    label = "Silo Open - Swim in or press W / E to descend";
                }
                else
                {
                    label = "Silo Hatch Opening...";
                }
                {{fontLine}}
                draw_set_halign(fa_center);
                draw_set_valign(fa_middle);
                draw_set_alpha(0.82);
                draw_set_color(c_black);
                draw_rectangle(hx - 175, hy - 102, hx + 175, hy - 74, false);
                draw_set_alpha(1);
                draw_set_color(make_color_rgb(175, 250, 235));
                draw_text(hx, hy - 88, label);
                draw_set_halign(fa_left);
                draw_set_valign(fa_top);
            }
            """;
    }

    private static string AreaCreateGml(bool hasSaveManager)
    {
        string deactivateSave = hasSaveManager ? "if (instance_exists(SaveManager)) instance_deactivate_object(SaveManager);" : "";
        return $$"""
            {{deactivateSave}}
            display_set_gui_size(800, 480);
            image_speed = 0;
            image_index = 0;
            vx = 0;
            vy = 1.6;
            facing = 1;
            silo_ticks = 0;
            exit_armed = 0;
            exit_triggered = 0;
            view_xview[0] = clamp(x - 400, 0, room_width - view_wview[0]);
            view_yview[0] = clamp(y - 240, 0, room_height - view_hview[0]);

            var i;
            fish_count = 12;
            for (i = 0; i < fish_count; i += 1)
            {
                fish_x[i] = 220 + ((i * 107) mod 1160);
                fish_y[i] = 270 + ((i * 97) mod 620);
                fish_dir[i] = ((i mod 2) * 2) - 1;
                fish_spd[i] = 0.7 + ((i mod 4) * 0.28);
                fish_phase[i] = i * 0.85;
                if ((i mod 3) == 0) fish_col[i] = make_color_rgb(255, 210, 120);
                else if ((i mod 3) == 1) fish_col[i] = make_color_rgb(140, 240, 255);
                else fish_col[i] = make_color_rgb(255, 165, 200);
            }

            kelp_count = 24;
            for (i = 0; i < kelp_count; i += 1)
            {
                kelp_x[i] = 135 + (i * 58);
                kelp_h[i] = 70 + ((i * 37) mod 95);
                kelp_phase[i] = i * 0.6;
            }

            mote_count = 28;
            for (i = 0; i < mote_count; i += 1)
            {
                mote_x[i] = 120 + ((i * 131) mod 1360);
                mote_y[i] = 140 + ((i * 89) mod 840);
                mote_spd[i] = 0.25 + ((i mod 5) * 0.12);
                mote_phase[i] = i * 0.5;
            }
            """;
    }

    private static string AreaStepGml() => $$"""
        var ax, ay, mx, my, md, i, fx, fy;
        silo_ticks += 1;
        ax = 0;
        ay = 0;
        if (keyboard_check(vk_left) || keyboard_check(ord("A"))) ax -= 1;
        if (keyboard_check(vk_right) || keyboard_check(ord("D"))) ax += 1;
        if (keyboard_check(vk_up) || keyboard_check(ord("W"))) ay -= 1;
        if (keyboard_check(vk_down) || keyboard_check(ord("S"))) ay += 1;
        if (mouse_check_button(mb_left))
        {
            mx = mouse_x;
            my = mouse_y;
            md = point_distance(x, y, mx, my);
            if (md > 18)
            {
                ax += (mx - x) / md;
                ay += (my - y) / md;
            }
        }
        vx += ax * 0.42;
        vy += ay * 0.42;
        vx *= 0.90;
        vy *= 0.90;
        if (point_distance(0, 0, vx, vy) > 5.5)
        {
            var sc;
            sc = 5.5 / point_distance(0, 0, vx, vy);
            vx *= sc;
            vy *= sc;
        }
        x += vx;
        y += vy;
        x = clamp(x, 110, 1490);
        y = clamp(y, 78, 965);
        if (abs(vx) > 0.15)
        {
            facing = sign(vx);
        }
        image_xscale = facing;
        image_angle = clamp(-vy * 3.5 * facing, -18, 18);
        image_index = floor(silo_ticks * 0.14) mod 2;

        if (y > 195 || silo_ticks > 45)
        {
            exit_armed = 1;
        }

        // Update peaceful sanctuary fish
        for (i = 0; i < fish_count; i += 1)
        {
            fish_x[i] += fish_dir[i] * fish_spd[i];
            fish_y[i] += 0.35 * sin((silo_ticks * 0.05) + fish_phase[i]);
            if (fish_x[i] < 180) { fish_x[i] = 180; fish_dir[i] = 1; }
            if (fish_x[i] > 1420) { fish_x[i] = 1420; fish_dir[i] = -1; }
            fx = fish_x[i];
            fy = fish_y[i];
            if (point_distance(x, y, fx, fy) < 110)
            {
                fish_dir[i] = (fx >= x) ? 1 : -1;
            }
        }

        // Update floating light motes
        for (i = 0; i < mote_count; i += 1)
        {
            mote_y[i] -= mote_spd[i];
            mote_x[i] += 0.25 * sin((silo_ticks * 0.03) + mote_phase[i]);
            if (mote_y[i] < 95)
            {
                mote_y[i] = 980;
            }
        }

        // Smooth camera tracking clamped to room_jamm_silo
        var tx, ty;
        tx = clamp(x - (view_wview[0] * 0.5), 0, room_width - view_wview[0]);
        ty = clamp(y - (view_hview[0] * 0.5), 0, room_height - view_hview[0]);
        view_xview[0] += (tx - view_xview[0]) * 0.14;
        view_yview[0] += (ty - view_yview[0]) * 0.14;

        // Safe exit back to NewWorld
        if (!exit_triggered && exit_armed)
        {
            var at_hatch;
            at_hatch = (abs(x - 800) < 90 && y < 120);
            if (at_hatch || keyboard_check_pressed(vk_escape) || keyboard_check_pressed(ord("Q")) || keyboard_check_pressed(vk_backspace) || (abs(x - 800) < 140 && y < 180 && keyboard_check_pressed(ord("E"))))
            {
                exit_triggered = 1;
                if (instance_exists({{ControllerObjectName}}))
                {
                    {{ControllerObjectName}}.silo_returning = 1;
                }
                room_goto(NewWorld);
            }
        }
        """;

    private static string AreaDrawGml() => """
        var top_c, mid_c, bot_c, wall_c, ledge_c, i, kx, ky, kh, sway, mx, my;
        top_c = make_color_rgb(16, 48, 68);
        mid_c = make_color_rgb(24, 82, 98);
        bot_c = make_color_rgb(18, 52, 62);
        wall_c = make_color_rgb(18, 30, 38);
        ledge_c = make_color_rgb(32, 64, 68);

        // Background water gradient
        draw_rectangle_colour(0, 0, room_width, 550, top_c, top_c, mid_c, mid_c, false);
        draw_rectangle_colour(0, 550, room_width, room_height, mid_c, mid_c, bot_c, bot_c, false);

        // Soft light shafts descending from the entry hatch (800, 0)
        draw_set_blend_mode(bm_add);
        draw_set_alpha(0.11);
        var beam_c;
        beam_c = make_color_rgb(130, 245, 225);
        draw_triangle_colour(710, 0, 890, 0, 1080, 990, beam_c, beam_c, c_black, false);
        draw_triangle_colour(710, 0, 520, 990, 1080, 990, beam_c, c_black, c_black, false);
        draw_set_blend_mode(bm_normal);
        draw_set_alpha(1);

        // Background sanctuary arches / observation pillars
        draw_set_color(make_color_rgb(22, 62, 74));
        for (i = 0; i < 4; i += 1)
        {
            var px;
            px = 260 + (i * 360);
            draw_rectangle(px - 22, 220, px + 22, 1010, false);
            draw_circle(px, 230, 62, true);
        }

        // Cavern walls, stepped ledges, and floor
        draw_set_color(wall_c);
        draw_rectangle(0, 0, 84, room_height, false);
        draw_rectangle(room_width - 84, 0, room_width, room_height, false);
        draw_rectangle(0, 0, 705, 56, false);
        draw_rectangle(895, 0, room_width, 56, false);
        draw_rectangle(0, 990, room_width, room_height, false);

        draw_set_color(ledge_c);
        draw_rectangle(84, 982, room_width - 84, 996, false);
        draw_rectangle(140, 690, 420, 725, false);
        draw_rectangle(1180, 660, 1460, 695, false);
        draw_rectangle(640, 830, 960, 862, false);

        // Swaying procedural seagrass fronds
        for (i = 0; i < kelp_count; i += 1)
        {
            kx = kelp_x[i];
            ky = 986;
            kh = kelp_h[i];
            sway = 14 * sin((silo_ticks * 0.04) + kelp_phase[i]);
            draw_line_width_colour(kx, ky, kx + sway, ky - kh, 5, make_color_rgb(35, 130, 105), make_color_rgb(95, 220, 170));
        }

        // Glowing crystal clusters along ledges
        draw_set_blend_mode(bm_add);
        for (i = 0; i < 6; i += 1)
        {
            var cx, cy, cr, cc;
            if (i == 0) { cx = 220; cy = 686; cc = make_color_rgb(90, 235, 220); }
            else if (i == 1) { cx = 360; cy = 686; cc = make_color_rgb(255, 200, 110); }
            else if (i == 2) { cx = 740; cy = 826; cc = make_color_rgb(130, 240, 255); }
            else if (i == 3) { cx = 860; cy = 826; cc = make_color_rgb(255, 210, 120); }
            else if (i == 4) { cx = 1240; cy = 656; cc = make_color_rgb(110, 245, 200); }
            else { cx = 1380; cy = 656; cc = make_color_rgb(255, 185, 130); }
            cr = 38 + (6 * sin((silo_ticks * 0.06) + i));
            draw_set_alpha(0.32);
            draw_circle_colour(cx, cy, cr, cc, c_black, false);
            draw_set_alpha(0.85);
            draw_triangle_colour(cx - 10, cy, cx + 10, cy, cx, cy - 28, cc, cc, c_white, false);
        }
        draw_set_blend_mode(bm_normal);
        draw_set_alpha(1);

        // Floating light motes
        draw_set_alpha(0.65);
        for (i = 0; i < mote_count; i += 1)
        {
            mx = mote_x[i];
            my = mote_y[i];
            draw_set_color(make_color_rgb(190, 250, 235));
            draw_circle(mx, my, 2 + (i mod 2), false);
        }
        draw_set_alpha(1);

        // Friendly sanctuary fish
        for (i = 0; i < fish_count; i += 1)
        {
            draw_sprite_ext(spr_jamm_silo_fish, floor((silo_ticks * 0.12) + i) mod 2, fish_x[i], fish_y[i], -fish_dir[i], 1, 0, fish_col[i], 0.95);
        }

        // Top Exit Hatch beacon
        draw_set_blend_mode(bm_add);
        draw_set_alpha(0.35 + (0.15 * sin(silo_ticks * 0.08)));
        draw_circle_colour(800, 68, 72, make_color_rgb(140, 255, 220), c_black, false);
        draw_set_blend_mode(bm_normal);
        draw_set_alpha(1);
        draw_set_color(make_color_rgb(175, 250, 235));
        draw_rectangle(715, 28, 885, 62, true);

        // Diver player sprite
        draw_self();
        """;

    private static string AreaDrawGuiGml(bool hasFont)
    {
        string fontLine = hasFont ? "draw_set_font(fnt_babySmall);" : "draw_set_font(-1);";
        return $$"""
            {{fontLine}}
            draw_set_alpha(0.72);
            draw_set_color(c_black);
            draw_rectangle(14, 12, 786, 40, false);
            draw_set_alpha(1);
            draw_set_color(make_color_rgb(185, 250, 235));
            draw_set_halign(fa_center);
            draw_set_valign(fa_middle);
            draw_text(400, 26, "Silo Sanctuary  |  WASD / Arrows / Mouse: Swim  |  Top Hatch or Esc / Q: Exit Safely");
            draw_set_halign(fa_left);
            draw_set_valign(fa_top);
            """;
    }

    private static byte[] CreateGlowPng()
    {
        const int size = 128;
        byte[] rgba = new byte[size * size * 4];
        double half = size * 0.5;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double d = Math.Sqrt(((x - half) * (x - half)) + ((y - half) * (y - half))) / half;
                double falloff = Math.Clamp(1.0 - d, 0.0, 1.0);
                byte a = (byte)Math.Round(falloff * falloff * 255.0);
                int idx = (y * size + x) * 4;
                rgba[idx] = 255;
                rgba[idx + 1] = 255;
                rgba[idx + 2] = 255;
                rgba[idx + 3] = a;
            }
        return EncodePng(size, size, rgba);
    }

    private static byte[] CreateDiverStripPng()
    {
        const int fw = 64, h = 48, frames = 2;
        const int w = fw * frames;
        byte[] rgba = new byte[w * h * 4];
        for (int f = 0; f < frames; f++)
        {
            int ox = f * fw;
            int flipperOffset = f == 0 ? -2 : 2;
            // Cozy teal suit body + amber diving helmet + flippers
            FillEllipse(rgba, w, h, ox + 28, 26, 14, 10, 45, 165, 175, 255);
            FillEllipse(rgba, w, h, ox + 40, 22, 11, 11, 240, 185, 70, 255);
            FillEllipse(rgba, w, h, ox + 42, 22, 7, 7, 155, 235, 255, 255);
            // Cat ears inside helmet silhouette
            FillTriangle(rgba, w, h, ox + 36, 14, ox + 39, 8, ox + 42, 14, 240, 185, 70, 255);
            FillTriangle(rgba, w, h, ox + 43, 14, ox + 46, 8, ox + 49, 14, 240, 185, 70, 255);
            // Flippers
            FillEllipse(rgba, w, h, ox + 12, 24 + flipperOffset, 7, 4, 245, 140, 55, 255);
            FillEllipse(rgba, w, h, ox + 13, 30 - flipperOffset, 7, 4, 245, 140, 55, 255);
        }
        return EncodePng(w, h, rgba);
    }

    private static byte[] CreateSanctuaryFishStripPng()
    {
        const int fw = 48, h = 32, frames = 2;
        const int w = fw * frames;
        byte[] rgba = new byte[w * h * 4];
        for (int f = 0; f < frames; f++)
        {
            int ox = f * fw;
            int tailShift = f == 0 ? 0 : 2;
            FillEllipse(rgba, w, h, ox + 21, 16, 13, 8, 255, 255, 255, 255);
            FillTriangle(rgba, w, h, ox + 32, 16, ox + 44, 8 + tailShift, ox + 44, 24 - tailShift, 225, 245, 255, 255);
            FillEllipse(rgba, w, h, ox + 13, 14, 2, 2, 25, 35, 50, 255);
        }
        return EncodePng(w, h, rgba);
    }

    private static void FillEllipse(byte[] rgba, int w, int h, double cx, double cy, double rx, double ry, byte r, byte g, byte b, byte a)
    {
        for (int y = Math.Max(0, (int)(cy - ry - 1)); y <= Math.Min(h - 1, (int)(cy + ry + 1)); y++)
            for (int x = Math.Max(0, (int)(cx - rx - 1)); x <= Math.Min(w - 1, (int)(cx + rx + 1)); x++)
            {
                double dx = (x - cx) / rx, dy = (y - cy) / ry;
                if ((dx * dx) + (dy * dy) <= 1.0)
                {
                    int idx = (y * w + x) * 4;
                    rgba[idx] = r; rgba[idx + 1] = g; rgba[idx + 2] = b; rgba[idx + 3] = a;
                }
            }
    }

    private static void FillTriangle(byte[] rgba, int w, int h, int x1, int y1, int x2, int y2, int x3, int y3, byte r, byte g, byte b, byte a)
    {
        int minX = Math.Max(0, Math.Min(x1, Math.Min(x2, x3)));
        int maxX = Math.Min(w - 1, Math.Max(x1, Math.Max(x2, x3)));
        int minY = Math.Max(0, Math.Min(y1, Math.Min(y2, y3)));
        int maxY = Math.Min(h - 1, Math.Max(y1, Math.Max(y2, y3)));
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                int c1 = (x2 - x1) * (y - y1) - (y2 - y1) * (x - x1);
                int c2 = (x3 - x2) * (y - y2) - (y3 - y2) * (x - x2);
                int c3 = (x1 - x3) * (y - y3) - (y1 - y3) * (x - x3);
                if ((c1 >= 0 && c2 >= 0 && c3 >= 0) || (c1 <= 0 && c2 <= 0 && c3 <= 0))
                {
                    int idx = (y * w + x) * 4;
                    rgba[idx] = r; rgba[idx + 1] = g; rgba[idx + 2] = b; rgba[idx + 3] = a;
                }
            }
    }

    private static byte[] EncodePng(int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(ms, "IHDR"u8, w =>
        {
            Span<byte> buf = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(buf[0..4], width);
            BinaryPrimitives.WriteInt32BigEndian(buf[4..8], height);
            buf[8] = 8;
            buf[9] = 6;
            w.Write(buf);
        });
        byte[] raw = new byte[height * (1 + width * 4)];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (1 + width * 4) + 1, width * 4);
        byte[] compressed;
        using (var zms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(zms, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            compressed = zms.ToArray();
        }
        WriteChunk(ms, "IDAT"u8, w => w.Write(compressed));
        WriteChunk(ms, "IEND"u8, _ => { });
        return ms.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, Action<Stream> writeData)
    {
        using var body = new MemoryStream();
        body.Write(type);
        writeData(body);
        byte[] bytes = body.ToArray();
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, bytes.Length - 4);
        output.Write(len);
        output.Write(bytes);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(bytes));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
