using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>
/// "Smooth" mod: renders at 60/120 FPS while game logic keeps its original 30 (Tab: 60) ticks per second.
/// Between logic ticks every active instance is deactivated (so no step/alarm/motion/animation runs); for the
/// extra frames everything is woken just for the draw events, and positions (of instances near a
/// camera), angles and cameras are interpolated between the last two ticks. Ticks are scheduled by real time,
/// so the game speed does not depend on the reached frame rate.
/// </summary>
public static class SmoothMod
{
    public const string ObjectName = "obj_jamm_smooth";
    public const string ProbeName = "obj_jamm_smooth_probe";

    public static void Apply(UndertaleData data, bool debug = false)
    {
        var compiler = new CompileGroup(data);
        var controller = ModObjects.Create(data, ObjectName, persistent: true);
        Queue(compiler, data, controller, EventType.Create, 0, Create(debug));
        Queue(compiler, data, controller, EventType.Other, (uint)EventSubtypeOther.RoomStart, RoomStart);
        Queue(compiler, data, controller, EventType.Step, (uint)EventSubtypeStep.BeginStep, BeginStep);
        Queue(compiler, data, controller, EventType.Step, (uint)EventSubtypeStep.EndStep, EndStep);
        Queue(compiler, data, controller, EventType.Draw, (uint)EventSubtypeDraw.PreDraw, PreDraw);
        Queue(compiler, data, controller, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, Toast);
        Queue(compiler, data, controller, EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, GuiEnd(debug));
        if (debug)
        {
            var probe = ModObjects.Create(data, ProbeName, persistent: true);
            Queue(compiler, data, probe, EventType.Step, (uint)EventSubtypeStep.BeginStep, "global.jamm_probe_begin += 1;");
            Queue(compiler, data, probe, EventType.Step, (uint)EventSubtypeStep.Step, "global.jamm_probe_step += 1;");
            Queue(compiler, data, probe, EventType.Step, (uint)EventSubtypeStep.EndStep, "global.jamm_probe_end += 1;");
            Queue(compiler, data, probe, EventType.Draw, (uint)EventSubtypeDraw.Draw, "global.jamm_probe_draw += 1; global.jamm_c_draw = instance_count;");
        }

        // Girl measures elapsed time with delta_time (day/night speed, cooldowns). With extra render
        // frames delta_time only covers one frame, so feed it the time since the previous logic tick.
        var girl = data.Code.ByName("gml_Object_Girl_Step_0") ?? throw new InvalidDataException("gml_Object_Girl_Step_0 not found: unsupported game version.");
        string girlCode = ModObjects.Decompile(data, girl);
        int count = girlCode.Split("delta_time").Length - 1;
        if (count != 3) throw new InvalidDataException($"Expected 3 uses of delta_time in Girl's step event, found {count}: unsupported game version.");
        compiler.QueueCodeReplace(girl, girlCode.Replace("delta_time", "global.jamm_tick_delta"));

        ModObjects.Compile(compiler);
        foreach (string room in new[] { "NewWorld", "room11" })
            ModObjects.PlaceInstance(data, room, controller, first: true);
    }

    private static void Queue(CompileGroup compiler, UndertaleData data, UndertaleGameObject obj, EventType type, uint subtype, string gml)
        => compiler.QueueCodeReplace(obj.EventHandlerFor(type, subtype, data), gml);

    private static string Create(bool debug) => $$"""
        if (instance_number(object_index) > 1)
        {
            instance_destroy();
            exit;
        }
        persistent = true;
        global.jamm_tick_delta = 33333;
        global.jamm_smooth_debug = {{(debug ? 1 : 0)}};
        jamm_target = 60;
        ini_open("jamm_smooth.ini");
        jamm_target = ini_read_real("Smooth", "TargetFPS", jamm_target);
        ini_close();
        jamm_margin = 400;
        jamm_logic = 30;
        jamm_room_base = 30;
        jamm_elapsed = 0;
        jamm_since = 0;
        jamm_render_dt = 8333;
        jamm_last_dt = 16666;
        jamm_prev_tick = 1;
        jamm_tick_now = 1;
        jamm_frozen = 0;
                jamm_frozen_list = ds_list_create();
        jamm_moving = ds_list_create();
        jamm_restore = ds_list_create();
        jamm_known = ds_map_create();
        jamm_rn = 0;
        for (jamm_i = 0; jamm_i < 8; jamm_i += 1)
        {
            jamm_vpx[jamm_i] = view_xview[jamm_i];
            jamm_vpy[jamm_i] = view_yview[jamm_i];
            jamm_vcx[jamm_i] = view_xview[jamm_i];
            jamm_vcy[jamm_i] = view_yview[jamm_i];
            jamm_vrx[jamm_i] = 0;
            jamm_vry[jamm_i] = 0;
            jamm_vix[jamm_i] = 0;
            jamm_viy[jamm_i] = 0;
            jamm_vset[jamm_i] = 0;
            jamm_rl[jamm_i] = 0;
            jamm_rt[jamm_i] = 0;
            jamm_rr[jamm_i] = 0;
            jamm_rb[jamm_i] = 0;
        }
        jamm_toast_until = current_time + 4000;
        jamm_toast_text = "Smooth mod: " + string(jamm_target) + " FPS  (F7: 60 / 120 / off)";
        if (jamm_target <= 30)
            jamm_toast_text = "Smooth mod: off (F7: 60 / 120 / off)";
        jamm_dbg_time = current_time;
        jamm_dbg_ticks = 0;
        jamm_dbg_frames = 0;
        jamm_dbg_slow = 0;
        jamm_dbg_redeact = 0;
        jamm_dbg_shots = 0;
        jamm_dbg_awake = 0;
        jamm_tm_map = 0;
        jamm_tm_act = 0;
        jamm_tm_redeact = 0;
        jamm_tm_record = 0;
        jamm_tm_interp = 0;
        global.jamm_probe_begin = 0;
        global.jamm_probe_step = 0;
        global.jamm_probe_end = 0;
        global.jamm_probe_draw = 0;
        global.jamm_c_draw = 0;
        if (global.jamm_smooth_debug)
            instance_create(0, 0, {{ProbeName}});
        """;

    private const string RoomStart = """
        jamm_room_base = room_speed;
        jamm_logic = room_speed;
        ds_map_clear(jamm_known);
        ds_list_clear(jamm_moving);
        ds_list_clear(jamm_restore);
        ds_list_clear(jamm_frozen_list);
        jamm_frozen = 0;
                jamm_tick_now = 1;
        jamm_elapsed = 0;
        jamm_since = 0;
        for (jamm_i = 0; jamm_i < 8; jamm_i += 1)
        {
            jamm_vpx[jamm_i] = view_xview[jamm_i];
            jamm_vpy[jamm_i] = view_yview[jamm_i];
            jamm_vcx[jamm_i] = view_xview[jamm_i];
            jamm_vcy[jamm_i] = view_yview[jamm_i];
            jamm_vset[jamm_i] = 0;
        }
        """;

    // First event of every frame. Input on a render-only frame wakes the frozen instances right away,
    // so pressed/released events are never swallowed.
    private const string BeginStep = """
        jamm_last_dt = delta_time;
        if (!jamm_prev_tick)
            jamm_render_dt = (jamm_render_dt * 0.9) + (min(delta_time, 100000) * 0.1);
        jamm_elapsed += delta_time;
        jamm_since += delta_time;
        if (keyboard_check_pressed(vk_f7))
        {
            if (jamm_target == 60)
                jamm_target = 120;
            else if (jamm_target == 120)
                jamm_target = 30;
            else
                jamm_target = 60;
            ini_open("jamm_smooth.ini");
            ini_write_real("Smooth", "TargetFPS", jamm_target);
            ini_close();
            jamm_toast_until = current_time + 2500;
            if (jamm_target <= 30)
                jamm_toast_text = "Smooth mod: off (original 30 FPS)";
            else
                jamm_toast_text = "Smooth mod: " + string(jamm_target) + " FPS";
        }
        if (jamm_frozen)
        {
            if (keyboard_check_pressed(vk_anykey) || keyboard_check_released(vk_anykey) || mouse_check_button_pressed(mb_any) || mouse_check_button_released(mb_any) || mouse_wheel_up() || mouse_wheel_down())
            {
                jamm_n = ds_list_size(jamm_frozen_list);
                for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
                    instance_activate_object(ds_list_find_value(jamm_frozen_list, jamm_i));
                ds_list_clear(jamm_frozen_list);
                jamm_frozen = 0;
                jamm_tick_now = 1;
            }
        }
        jamm_prev_tick = jamm_tick_now;
        if (jamm_tick_now)
        {
            jamm_interval = 1000000 / jamm_logic;
            global.jamm_tick_delta = jamm_since;
            jamm_since = 0;
            jamm_elapsed = clamp(jamm_elapsed - jamm_interval, -jamm_interval, jamm_interval);
            jamm_dbg_ticks += 1;
            room_speed = jamm_room_base;
        }
        jamm_dbg_frames += 1;
        """;

    // (De)activation is applied between events, so frozen instances are woken here, one event before
    // drawing. Exactly the frozen instances are woken: all of them, because draw code freely reads
    // other instances (ParallaxBackground reads Arcade.x), and none the game deactivated itself,
    // because those must not draw (Magmer reads its light, which may be gone).
    private const string EndStep = """
        if (jamm_frozen)
        {
            jamm_t0 = get_timer();
            jamm_n = ds_list_size(jamm_frozen_list);
            for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
                instance_activate_object(ds_list_find_value(jamm_frozen_list, jamm_i));
            ds_list_clear(jamm_frozen_list);
            jamm_frozen = 0;
            jamm_tm_act += get_timer() - jamm_t0;
        }
        """;

    private static readonly string PreDraw = $$"""
        jamm_t0 = get_timer();
        if (jamm_tick_now)
        {
            if (room_speed > 0)
                jamm_logic = room_speed;
            // Particle systems advance once per logic tick, not once per rendered frame.
            for (jamm_i = 0; jamm_i < 64; jamm_i += 1)
            {
                if (part_system_exists(jamm_i))
                {
                    part_system_automatic_update(jamm_i, false);
                    part_system_update(jamm_i);
                }
            }
            // Regions that can be seen: every visible view, plus a margin.
            jamm_rn = 0;
            for (jamm_i = 0; jamm_i < 8; jamm_i += 1)
            {
                if (view_enabled && view_visible[jamm_i])
                {
                    jamm_rl[jamm_rn] = view_xview[jamm_i] - jamm_margin;
                    jamm_rt[jamm_rn] = view_yview[jamm_i] - jamm_margin;
                    jamm_rr[jamm_rn] = view_xview[jamm_i] + view_wview[jamm_i] + jamm_margin;
                    jamm_rb[jamm_rn] = view_yview[jamm_i] + view_hview[jamm_i] + jamm_margin;
                    jamm_rn += 1;
                }
            }
            jamm_old = jamm_known;
            jamm_known = ds_map_create();
            ds_list_clear(jamm_moving);
            with (all)
            {
                jamm_in = other.jamm_rn == 0;
                for (jamm_k = 0; jamm_k < other.jamm_rn && !jamm_in; jamm_k += 1)
                {
                    if (x >= other.jamm_rl[jamm_k] && x <= other.jamm_rr[jamm_k] && y >= other.jamm_rt[jamm_k] && y <= other.jamm_rb[jamm_k])
                        jamm_in = 1;
                }
                if (jamm_in)
                {
                    if (ds_map_exists(other.jamm_old, id))
                    {
                        jamm_px = jamm_cx;
                        jamm_py = jamm_cy;
                        jamm_pa = jamm_ca;
                    }
                    else
                    {
                        jamm_px = x;
                        jamm_py = y;
                        jamm_pa = image_angle;
                    }
                    jamm_cx = x;
                    jamm_cy = y;
                    jamm_ca = image_angle;
                    ds_map_add(other.jamm_known, id, 1);
                    if (jamm_px != jamm_cx || jamm_py != jamm_cy || jamm_pa != jamm_ca)
                        ds_list_add(other.jamm_moving, id);
                }
            }
            ds_map_destroy(jamm_old);
            for (jamm_i = 0; jamm_i < 8; jamm_i += 1)
            {
                jamm_vpx[jamm_i] = jamm_vcx[jamm_i];
                jamm_vpy[jamm_i] = jamm_vcy[jamm_i];
                jamm_vcx[jamm_i] = view_xview[jamm_i];
                jamm_vcy[jamm_i] = view_yview[jamm_i];
            }
        }
        jamm_tm_record += get_timer() - jamm_t0;
        jamm_t0 = get_timer();
        jamm_interval = 1000000 / jamm_logic;
        jamm_t = clamp((jamm_since + jamm_render_dt) / jamm_interval, 0, 1);
        if (jamm_target <= jamm_logic)
            jamm_t = 1;
        ds_list_clear(jamm_restore);
        jamm_n = ds_list_size(jamm_moving);
        for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
        {
            with (ds_list_find_value(jamm_moving, jamm_i))
            {
                if (x == jamm_cx && y == jamm_cy && image_angle == jamm_ca && abs(jamm_cx - jamm_px) < 600 && abs(jamm_cy - jamm_py) < 600)
                {
                    jamm_rx = x;
                    jamm_ry = y;
                    jamm_ra = image_angle;
                    x = jamm_px + ((jamm_cx - jamm_px) * other.jamm_t);
                    y = jamm_py + ((jamm_cy - jamm_py) * other.jamm_t);
                    jamm_da = ((((jamm_ca - jamm_pa) mod 360) + 540) mod 360) - 180;
                    image_angle = jamm_pa + (jamm_da * other.jamm_t);
                    jamm_ix = x;
                    jamm_iy = y;
                    jamm_ia = image_angle;
                    ds_list_add(other.jamm_restore, id);
                }
            }
        }
        for (jamm_i = 0; jamm_i < 8; jamm_i += 1)
        {
            jamm_vset[jamm_i] = 0;
            if (view_xview[jamm_i] == jamm_vcx[jamm_i] && view_yview[jamm_i] == jamm_vcy[jamm_i] && abs(jamm_vcx[jamm_i] - jamm_vpx[jamm_i]) < 600 && abs(jamm_vcy[jamm_i] - jamm_vpy[jamm_i]) < 600)
            {
                jamm_vrx[jamm_i] = view_xview[jamm_i];
                jamm_vry[jamm_i] = view_yview[jamm_i];
                view_xview[jamm_i] = jamm_vpx[jamm_i] + ((jamm_vcx[jamm_i] - jamm_vpx[jamm_i]) * jamm_t);
                view_yview[jamm_i] = jamm_vpy[jamm_i] + ((jamm_vcy[jamm_i] - jamm_vpy[jamm_i]) * jamm_t);
                jamm_vix[jamm_i] = view_xview[jamm_i];
                jamm_viy[jamm_i] = view_yview[jamm_i];
                jamm_vset[jamm_i] = 1;
            }
        }
        jamm_tm_interp += get_timer() - jamm_t0;
        """;

    // End of the frame: undo the interpolation (keeping any change the draw code made), then decide
    // by real time whether the next frame runs a logic tick or only renders.
    private static string GuiEnd(bool debug) => $$"""
        {{(debug ? DebugShots : "")}}
        jamm_n = ds_list_size(jamm_restore);
        for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
        {
            with (ds_list_find_value(jamm_restore, jamm_i))
            {
                x = jamm_rx + (x - jamm_ix);
                y = jamm_ry + (y - jamm_iy);
                image_angle = jamm_ra + (image_angle - jamm_ia);
            }
        }
        ds_list_clear(jamm_restore);
        for (jamm_i = 0; jamm_i < 8; jamm_i += 1)
        {
            if (jamm_vset[jamm_i])
            {
                view_xview[jamm_i] = jamm_vrx[jamm_i] + (view_xview[jamm_i] - jamm_vix[jamm_i]);
                view_yview[jamm_i] = jamm_vry[jamm_i] + (view_yview[jamm_i] - jamm_viy[jamm_i]);
                jamm_vset[jamm_i] = 0;
            }
        }
        {{(debug ? DebugLog : "")}}
        jamm_eff = max(jamm_target, jamm_logic);
        room_speed = jamm_eff;
        jamm_interval = 1000000 / jamm_logic;
        if (jamm_eff <= jamm_logic || (jamm_elapsed + (jamm_render_dt * 1.5)) >= jamm_interval)
        {
            jamm_tick_now = 1;
        }
        else
        {
            jamm_tick_now = 0;
            jamm_t0 = get_timer();
            ds_list_clear(jamm_frozen_list);
            with (all)
            {
                if (id != other.id)
                    ds_list_add(other.jamm_frozen_list, id);
            }
            instance_deactivate_all(true);
            jamm_frozen = 1;
            jamm_tm_map += get_timer() - jamm_t0;
        }
        """;

    private const string DebugShots = """
        if ((current_time > 45000 && jamm_dbg_shots < 6) || (current_time > 75000 && jamm_dbg_shots < 12))
        {
            if (jamm_tick_now)
                screen_save("jamm_shot_" + string(jamm_dbg_shots) + "_tick.png");
            else
                screen_save("jamm_shot_" + string(jamm_dbg_shots) + "_render.png");
            jamm_dbg_shots += 1;
        }
        """;

    private const string DebugLog = """
        if (fps_real < 100)
            jamm_dbg_slow += 1;
        if ((current_time - jamm_dbg_time) >= 1000)
        {
            jamm_f = file_text_open_append("jamm_smooth_log.txt");
            file_text_write_string(jamm_f, "t=" + string(current_time) + " fps=" + string(fps) + " slow=" + string(jamm_dbg_slow) + " frames=" + string(jamm_dbg_frames) + " ticks=" + string(jamm_dbg_ticks) + " probe b/s/e/d=" + string(global.jamm_probe_begin) + "/" + string(global.jamm_probe_step) + "/" + string(global.jamm_probe_end) + "/" + string(global.jamm_probe_draw) + " awake=" + string(jamm_dbg_awake) + " redeact=" + string(jamm_dbg_redeact) + " ms map/act/redeact/record/interp=" + string(jamm_tm_map div 1000) + "/" + string(jamm_tm_act div 1000) + "/" + string(jamm_tm_redeact div 1000) + "/" + string(jamm_tm_record div 1000) + "/" + string(jamm_tm_interp div 1000) + " moving=" + string(ds_list_size(jamm_moving)) + " logic=" + string(jamm_logic) + " target=" + string(jamm_target) + " delta=" + string(global.jamm_tick_delta) + " render_dt=" + string(jamm_render_dt) + " room=" + room_get_name(room));
            file_text_writeln(jamm_f);
            file_text_close(jamm_f);
            jamm_dbg_time = current_time;
            jamm_dbg_ticks = 0;
            jamm_dbg_frames = 0;
            jamm_dbg_slow = 0;
            jamm_dbg_redeact = 0;
            jamm_tm_map = 0;
            jamm_tm_act = 0;
            jamm_tm_redeact = 0;
            jamm_tm_record = 0;
            jamm_tm_interp = 0;
            global.jamm_probe_begin = 0;
            global.jamm_probe_step = 0;
            global.jamm_probe_end = 0;
            global.jamm_probe_draw = 0;
        }
        """;

    private const string Toast = """
        if (current_time < jamm_toast_until)
        {
            draw_set_halign(fa_left);
            draw_set_valign(fa_top);
            draw_set_alpha(min(1, (jamm_toast_until - current_time) / 500));
            draw_set_color(c_black);
            draw_rectangle(8, 8, 20 + string_width(jamm_toast_text), 20 + string_height(jamm_toast_text), false);
            draw_set_color(c_white);
            draw_text(14, 14, jamm_toast_text);
            draw_set_alpha(1);
        }
        """;
}
