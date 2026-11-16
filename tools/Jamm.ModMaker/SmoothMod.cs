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
        var questBegin = data.Code.ByName("gml_Object_QuestEngine_Step_1")
            ?? throw new InvalidDataException("QuestEngine Begin Step is missing: unsupported game version. Use the original legacy 2023 data.win.");
        string questReset = string.Concat(ModObjects.Decompile(data, questBegin).Where(c => !char.IsWhiteSpace(c)));
        if (questReset != "quest_counter=0;")
            throw new InvalidDataException("QuestEngine Begin Step must contain exactly quest_counter = 0; for Smooth's render-only reset. Unsupported game version or modified input; use the original legacy 2023 data.win.");

        var compiler = new CompileGroup(data);
        var controller = ModObjects.Create(data, ObjectName, persistent: true);
        Queue(compiler, data, controller, EventType.Create, 0, Create(debug, PureFish(data)));
        Queue(compiler, data, controller, EventType.Other, (uint)EventSubtypeOther.RoomStart, RoomStart);
        Queue(compiler, data, controller, EventType.Step, (uint)EventSubtypeStep.BeginStep, BeginStep);
        Queue(compiler, data, controller, EventType.Step, (uint)EventSubtypeStep.EndStep, EndStep);
        Queue(compiler, data, controller, EventType.Alarm, 0, InputBeginReplay);
        Queue(compiler, data, controller, EventType.Draw, (uint)EventSubtypeDraw.PreDraw, PreDraw(data));
        Queue(compiler, data, controller, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, Toast(data));
        Queue(compiler, data, controller, EventType.Draw, (uint)EventSubtypeDraw.DrawGUIEnd, GuiEnd(debug, data.GameObjects.ByName("Fish") is not null));
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

    /// <summary>
    /// Fish types whose only draw code is Fish's own (inherited, not overridden): their draw has no
    /// lasting side effect except frames_since_last_flip++, which is emulated when they are culled.
    /// </summary>
    private static string[] PureFish(UndertaleData data)
    {
        var fish = data.GameObjects.ByName("Fish");
        if (fish is null) return [];
        var drawType = (int)EventType.Draw;
        var result = new List<string>();
        foreach (var obj in data.GameObjects)
        {
            bool descends = false, pure = true;
            for (var o = obj; o is not null; o = o.ParentId)
            {
                if (o == fish) { descends = true; break; }
                if (o.Events[drawType].Count > 0) { pure = false; break; }
            }
            if (descends && pure && fish.Events[drawType].All(e => e.EventSubtype == 0))
                result.Add(obj.Name.Content);
        }
        return result.ToArray();
    }

    /// <summary>Object types that draw only their sprite (no draw code anywhere in their parent chain).</summary>
    private static string[] DefaultDrawn(UndertaleData data)
    {
        int drawType = (int)EventType.Draw;
        bool HasDraw(UndertaleGameObject? o) { for (; o is not null; o = o.ParentId) if (o.Events[drawType].Count > 0) return true; return false; }
        var placed = data.Rooms.SelectMany(r => r.GameObjects).Select(i => i.ObjectDefinition).Where(o => o is not null).ToHashSet();
        return data.GameObjects.Where(o => o.Sprite is not null && placed.Contains(o) && !HasDraw(o) && !o.Name.Content.StartsWith("obj_jamm"))
            .Select(o => o.Name.Content).ToArray();
    }

    private static void Queue(CompileGroup compiler, UndertaleData data, UndertaleGameObject obj, EventType type, uint subtype, string gml)
        => compiler.QueueCodeReplace(obj.EventHandlerFor(type, subtype, data), gml);

    private static string Create(bool debug, string[] pureFish) => $$"""
        if (instance_number(object_index) > 1)
        {
            instance_destroy();
            exit;
        }
        persistent = true;
        global.jamm_tick_delta = 33333;
        global.jamm_smooth_debug = {{(debug ? 1 : 0)}};
        jamm_target = 120;
        ini_open("jamm_smooth.ini");
        jamm_target = ini_read_real("Smooth", "TargetFPS", jamm_target);
        ini_close();
        jamm_bypass = 1;
        jamm_frame_start = get_timer();
        jamm_dbg_tick_us = 0;
        jamm_dbg_render_us = 0;
        jamm_dbg_tick_max = 0;
        jamm_dbg_render_max = 0;
        jamm_dbg_render_n = 0;
        jamm_margin = 900;
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
        jamm_input_begin = ds_map_create();
        jamm_moving = ds_list_create();
        jamm_restore = ds_list_create();
        jamm_known = ds_map_create();
        jamm_cull = ds_list_create();
        jamm_cull_fish = ds_list_create();
        jamm_cand = ds_list_create();
        jamm_scan_age = 99;
        jamm_scan_vx = 0;
        jamm_scan_vy = 0;
        global.jamm_pure = ds_map_create();
        {{string.Concat(pureFish.Select(n => $"ds_map_add(global.jamm_pure, {n}, 1);\n"))}}
        global.jamm_rn = 0;
        global.jamm_fn = 0;
        global.jamm_sn = 0;
        jamm_hidden = ds_list_create();
        jamm_fish_on = ds_list_create();
        jamm_render = 0;
        jamm_now = get_timer();
        jamm_tick_time = jamm_now;
        jamm_tick_span = 33333;
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
        jamm_dbg_maxdt = 0;
        jamm_dbg_long = 0;
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
        ds_map_clear(jamm_input_begin);
        alarm[0] = -1;
        ds_list_clear(jamm_cull);
        ds_list_clear(jamm_cull_fish);
        ds_list_clear(jamm_cand);
        jamm_scan_age = 99;
        ds_list_clear(jamm_hidden);
        ds_list_clear(jamm_fish_on);
        jamm_render = 0;
        jamm_bypass = 1;
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
        jamm_frame_start = get_timer();
        jamm_last_dt = delta_time;
        jamm_dbg_maxdt = max(jamm_dbg_maxdt, delta_time);
        if (delta_time > 12000)
            jamm_dbg_long += 1;
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
                {
                    jamm_woken = ds_list_find_value(jamm_frozen_list, jamm_i);
                    ds_map_add(jamm_input_begin, jamm_woken, 1);
                    instance_activate_object(jamm_woken);
                }
                // Activation takes effect after this event: replay Begin Step in the alarm phase,
                // before keyboard/mouse events, with the tick delta already updated below.
                alarm[0] = 1;
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

    // with(all) retains engine instance order; the membership map excludes the controller,
    // newly created instances, and anything deactivated by the game before Smooth froze it.
    // Do not replay the controller's timing/input bookkeeping or run ordinary Begin Steps twice.
    private const string InputBeginReplay = """
        with (all)
        {
            if (id != other.id && ds_map_exists(other.jamm_input_begin, id))
                event_perform(ev_step, ev_step_begin);
        }
        ds_map_clear(jamm_input_begin);
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

    // Families whose Draw only draws the instance itself (checked by reading their draw code), so they
    // can be skipped while off-screen during render-only frames.
    private static readonly string[] CullFamilies = ["Fish", "SceneItem"];

    private static string PreDraw(UndertaleData data)
    {
        string[] families = CullFamilies.Where(n => data.GameObjects.ByName(n) is not null).ToArray();
        bool fish = families.Contains("Fish");
        string cullRecord = (families.Contains("SceneItem") ? """
                    with (SceneItem)
                    {
                        if (visible)
                        {
                            jamm_in = 0;
                            for (jamm_k = 0; jamm_k < global.jamm_sn && !jamm_in; jamm_k += 1)
                            {
                                if (bbox_right >= global.jamm_sl[jamm_k] && bbox_left <= global.jamm_sr[jamm_k] && bbox_bottom >= global.jamm_st[jamm_k] && bbox_top <= global.jamm_sb[jamm_k])
                                    jamm_in = 1;
                            }
                            if (!jamm_in)
                                ds_list_add(other.jamm_cull, id);
                        }
                    }

            """ : "") + (fish ? """
                    with (Fish)
                    {
                        if (visible)
                        {
                            jamm_in = 0;
                            for (jamm_k = 0; jamm_k < global.jamm_fn && !jamm_in; jamm_k += 1)
                            {
                                if (bbox_right >= global.jamm_fl[jamm_k] && bbox_left <= global.jamm_fr[jamm_k] && bbox_bottom >= global.jamm_ft[jamm_k] && bbox_top <= global.jamm_fb[jamm_k])
                                    jamm_in = 1;
                            }
                            // Sonar can become active between scans. Save every visible fish's
                            // draw state, including fish normally eligible for off-screen culling.
                            ds_list_add(other.jamm_fish_on, id);
                            if (!jamm_in && ds_map_exists(global.jamm_pure, object_index))
                                ds_list_add(other.jamm_cull_fish, id);
                        }
                    }

            """ : "") + string.Concat(DefaultDrawn(data).Select(o => $$"""
                    with ({{o}})
                    {
                        if (visible && object_index == {{o}})
                        {
                            jamm_in = 0;
                            for (jamm_k = 0; jamm_k < global.jamm_rn && !jamm_in; jamm_k += 1)
                            {
                                if (bbox_right >= global.jamm_rl[jamm_k] && bbox_left <= global.jamm_rr[jamm_k] && bbox_bottom >= global.jamm_rt[jamm_k] && bbox_top <= global.jamm_rb[jamm_k])
                                    jamm_in = 1;
                            }
                            if (!jamm_in)
                                ds_list_add(other.jamm_cull, id);
                        }
                    }

            """));
        // Fish draw code has side effects (flip counter, flip, angle, frame, alpha). On render-only
        // frames they are saved here and put back at the end of the frame, so fish behave exactly as
        // with one draw per logic tick.
        string fishSave = fish ? """
                jamm_n = ds_list_size(jamm_fish_on);
                for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
                {
                    with (ds_list_find_value(jamm_fish_on, jamm_i))
                    {
                        jamm_s_flip = frames_since_last_flip;
                        jamm_s_ys = image_yscale;
                        jamm_s_ii = image_index;
                        jamm_s_al = image_alpha;
                        jamm_s_an = image_angle;
                    }
                }
        """ : "";
        return $$"""
        jamm_t0 = get_timer();
        jamm_now = get_timer();
        if (jamm_tick_now)
        {
            if (room_speed > 0)
                jamm_logic = room_speed;
            jamm_tick_span = clamp(jamm_now - jamm_tick_time, 4000, 100000);
            jamm_tick_time = jamm_now;
            // Particle systems advance once per logic tick, not once per rendered frame.
            for (jamm_i = 0; jamm_i < 64; jamm_i += 1)
            {
                if (part_system_exists(jamm_i))
                {
                    part_system_automatic_update(jamm_i, false);
                    part_system_update(jamm_i);
                }
            }
            // A tick that already fills its interval cannot benefit from interpolation. Skip
            // all instance scans/maps/culling; retry on every tick so recovery is automatic.
            jamm_was_bypass = jamm_bypass;
            jamm_bypass = jamm_target <= jamm_logic || (jamm_now - jamm_frame_start + jamm_render_dt * 1.5) >= (1000000 / jamm_logic);
            if (jamm_bypass)
            {
                if (!jamm_was_bypass)
                {
                    ds_map_clear(jamm_known);
                    jamm_scan_age = 99;
                }
                exit;
            }
            // Rescan which instances are near a camera every 4 ticks, or at once when the main camera
            // moved far; the wide margin covers anything that can move between two scans.
            jamm_scan_age += 1;
            if (jamm_scan_age >= 4 || abs(view_xview[0] - jamm_scan_vx) > 250 || abs(view_yview[0] - jamm_scan_vy) > 250)
            {
                jamm_scan_age = 0;
                jamm_scan_vx = view_xview[0];
                jamm_scan_vy = view_yview[0];
                // Regions that can be seen (plus a margin), in globals for fast access inside with().
                // Fish draw in every view but 3, scenery in every view but 2.
                global.jamm_rn = 0;
                global.jamm_fn = 0;
                global.jamm_sn = 0;
                for (jamm_i = 0; jamm_i < 8; jamm_i += 1)
                {
                    if (view_enabled && view_visible[jamm_i])
                    {
                        jamm_l = view_xview[jamm_i] - jamm_margin;
                        jamm_tp = view_yview[jamm_i] - jamm_margin;
                        jamm_r = view_xview[jamm_i] + view_wview[jamm_i] + jamm_margin;
                        jamm_b = view_yview[jamm_i] + view_hview[jamm_i] + jamm_margin;
                        global.jamm_rl[global.jamm_rn] = jamm_l;
                        global.jamm_rt[global.jamm_rn] = jamm_tp;
                        global.jamm_rr[global.jamm_rn] = jamm_r;
                        global.jamm_rb[global.jamm_rn] = jamm_b;
                        global.jamm_rn += 1;
                        if (jamm_i != 3)
                        {
                            global.jamm_fl[global.jamm_fn] = jamm_l;
                            global.jamm_ft[global.jamm_fn] = jamm_tp;
                            global.jamm_fr[global.jamm_fn] = jamm_r;
                            global.jamm_fb[global.jamm_fn] = jamm_b;
                            global.jamm_fn += 1;
                        }
                        if (jamm_i != 2)
                        {
                            global.jamm_sl[global.jamm_sn] = jamm_l;
                            global.jamm_st[global.jamm_sn] = jamm_tp;
                            global.jamm_sr[global.jamm_sn] = jamm_r;
                            global.jamm_sb[global.jamm_sn] = jamm_b;
                            global.jamm_sn += 1;
                        }
                    }
                }
                ds_list_clear(jamm_cand);
                with (all)
                {
                    jamm_in = global.jamm_rn == 0;
                    for (jamm_k = 0; jamm_k < global.jamm_rn && !jamm_in; jamm_k += 1)
                    {
                        if (x >= global.jamm_rl[jamm_k] && x <= global.jamm_rr[jamm_k] && y >= global.jamm_rt[jamm_k] && y <= global.jamm_rb[jamm_k])
                            jamm_in = 1;
                    }
                    if (jamm_in)
                        ds_list_add(other.jamm_cand, id);
                }
                ds_list_clear(jamm_cull);
                ds_list_clear(jamm_cull_fish);
                ds_list_clear(jamm_fish_on);
                if (global.jamm_rn > 0)
                {
        {{cullRecord}}
                }
            }
            jamm_old = jamm_known;
            jamm_known = ds_map_create();
            ds_list_clear(jamm_moving);
            jamm_n = ds_list_size(jamm_cand);
            for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
            {
                with (ds_list_find_value(jamm_cand, jamm_i))
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
        // Show the state between the last two ticks by the real time since the newest one (one tick
        // of latency, but every frame lands exactly where it belongs in time).
        jamm_t = clamp((jamm_now - jamm_tick_time) / jamm_tick_span, 0, 1);
        if (jamm_target <= jamm_logic)
            jamm_t = 1;
        jamm_render = !jamm_tick_now;
        ds_list_clear(jamm_hidden);
        // Off-screen scenery and plain fish are not drawn at all (their draw would produce no pixels).
        jamm_n = ds_list_size(jamm_cull);
        for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
        {
            with (ds_list_find_value(jamm_cull, jamm_i))
            {
                if (visible)
                {
                    visible = false;
                    ds_list_add(other.jamm_hidden, id);
                }
            }
        }
        // SonarPanel draws all visible Fish itself (including advanced/quest silhouettes).
        // Never change their visibility while sonar can draw; check every frame, not per scan.
        jamm_sonar = view_visible[3];
        {{(data.GameObjects.ByName("SonarPanel") is not null ? "if (instance_exists(SonarPanel)) jamm_sonar = jamm_sonar || (SonarPanel.active && SonarPanel.shouldDraw);" : "")}}
        jamm_n = ds_list_size(jamm_cull_fish);
        for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
        {
            with (ds_list_find_value(jamm_cull_fish, jamm_i))
            {
                if (visible && !other.jamm_sonar)
                {
                    visible = false;
                    ds_list_add(other.jamm_hidden, id);
                    if (!other.jamm_render)
                        frames_since_last_flip += global.jamm_fn;
                }
            }
        }
        if (jamm_render)
        {
            // WHY: frozen Begin Step cannot reset the counter consumed by Quest Draw; without
            // this reset each extra render frame pushes the quest labels farther upwards.
            if (instance_exists(QuestEngine))
                QuestEngine.quest_counter = 0;
        {{fishSave}}
        }
        ds_list_clear(jamm_restore);
        jamm_n = ds_list_size(jamm_moving);
        for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
        {
            with (ds_list_find_value(jamm_moving, jamm_i))
            {
                // Casting draws mix custom Rod.angle, cached Lure.tether_x/y, Girl anchors,
                // and hooked fish. Keep that assembly at its complete logic-tick pose.
                jamm_casting = {{string.Join(" || ", new[] { "Girl", "Rod", "Lure", "Boat", "BoatBackground", "CatSwim" }.Where(n => data.GameObjects.ByName(n) is not null).Select(n => $"object_index == {n} || object_is_ancestor(object_index, {n})").DefaultIfEmpty("false"))}};
                {{(data.GameObjects.ByName("Lure") is not null ? "if (instance_exists(Lure)) jamm_casting = jamm_casting || id == Lure.fish;" : "")}}
                if (!jamm_casting && x == jamm_cx && y == jamm_cy && image_angle == jamm_ca && abs(jamm_cx - jamm_px) < 600 && abs(jamm_cy - jamm_py) < 600)
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
                    // Keep the movement vector that draw code derives directions from.
                    xprevious += x - jamm_rx;
                    yprevious += y - jamm_ry;
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
    }

    // End of the frame: undo interpolation, fish draw side effects and culling, then decide by real
    // time whether the next frame runs a logic tick or only renders.
    private static string GuiEnd(bool debug, bool fish) => $$"""
        if (!jamm_bypass)
        {
        jamm_n = ds_list_size(jamm_restore);
        for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
        {
            with (ds_list_find_value(jamm_restore, jamm_i))
            {
                xprevious -= jamm_ix - jamm_rx;
                yprevious -= jamm_iy - jamm_ry;
                x = jamm_rx + (x - jamm_ix);
                y = jamm_ry + (y - jamm_iy);
                image_angle = jamm_ra + (image_angle - jamm_ia);
            }
        }
        ds_list_clear(jamm_restore);
        jamm_n = ds_list_size(jamm_hidden);
        for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
        {
            with (ds_list_find_value(jamm_hidden, jamm_i))
                visible = true;
        }
        ds_list_clear(jamm_hidden);
        if (jamm_render)
        {
        {{(fish ? """
            jamm_n = ds_list_size(jamm_fish_on);
            for (jamm_i = 0; jamm_i < jamm_n; jamm_i += 1)
            {
                with (ds_list_find_value(jamm_fish_on, jamm_i))
                {
                    frames_since_last_flip = jamm_s_flip;
                    image_yscale = jamm_s_ys;
                    image_index = jamm_s_ii;
                    image_alpha = jamm_s_al;
                    image_angle = jamm_s_an;
                }
            }
        """ : "")}}
        }
        for (jamm_i = 0; jamm_i < 8; jamm_i += 1)
        {
            if (jamm_vset[jamm_i])
            {
                view_xview[jamm_i] = jamm_vrx[jamm_i] + (view_xview[jamm_i] - jamm_vix[jamm_i]);
                view_yview[jamm_i] = jamm_vry[jamm_i] + (view_yview[jamm_i] - jamm_viy[jamm_i]);
                jamm_vset[jamm_i] = 0;
            }
        }
        }
        // Bypass runs one tick per frame: let the engine pace it at Girl's logic rate
        // (including Tab's 60), otherwise a 29 ms tick runs faster than the original 30 Hz.
        jamm_eff = jamm_logic;
        if (!jamm_bypass)
            jamm_eff = max(jamm_target, jamm_logic);
        room_speed = jamm_eff;
        jamm_interval = 1000000 / jamm_logic;
        if (jamm_bypass || jamm_eff <= jamm_logic || (jamm_elapsed + (jamm_render_dt * 1.5)) >= jamm_interval)
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
        {{(debug ? DebugLog : "")}}
        """;

    private const string DebugLog = """
        // Active CPU/event time: excludes frame limiting and includes draw, unlike delta_time.
        jamm_frame_us = get_timer() - jamm_frame_start;
        if (jamm_prev_tick)
        {
            jamm_dbg_tick_us += jamm_frame_us;
            jamm_dbg_tick_max = max(jamm_dbg_tick_max, jamm_frame_us);
        }
        else
        {
            jamm_dbg_render_n += 1;
            jamm_dbg_render_us += jamm_frame_us;
            jamm_dbg_render_max = max(jamm_dbg_render_max, jamm_frame_us);
        }
        if (fps_real < 100)
            jamm_dbg_slow += 1;
        if ((current_time - jamm_dbg_time) >= 1000)
        {
            jamm_f = file_text_open_append("jamm_smooth_log.txt");
            file_text_write_string(jamm_f, "t=" + string(current_time) + " tick_ms_avg/max=" + string(jamm_dbg_tick_us / max(1, jamm_dbg_ticks) / 1000) + "/" + string(jamm_dbg_tick_max / 1000) + " render_ms_avg/max=" + string(jamm_dbg_render_us / max(1, jamm_dbg_render_n) / 1000) + "/" + string(jamm_dbg_render_max / 1000) + " render_n=" + string(jamm_dbg_render_n) + " bypass=" + string(jamm_bypass) + " fps=" + string(fps) + " frames>12ms=" + string(jamm_dbg_long) + " max_dt=" + string(jamm_dbg_maxdt div 1000) + " cull=" + string(ds_list_size(jamm_cull)) + "+" + string(ds_list_size(jamm_cull_fish)) + " fish_on=" + string(ds_list_size(jamm_fish_on)) + " frames=" + string(jamm_dbg_frames) + " ticks=" + string(jamm_dbg_ticks) + " probe b/s/e/d=" + string(global.jamm_probe_begin) + "/" + string(global.jamm_probe_step) + "/" + string(global.jamm_probe_end) + "/" + string(global.jamm_probe_draw) + " awake=" + string(jamm_dbg_awake) + " redeact=" + string(jamm_dbg_redeact) + " ms map/act/redeact/record/interp=" + string(jamm_tm_map div 1000) + "/" + string(jamm_tm_act div 1000) + "/" + string(jamm_tm_redeact div 1000) + "/" + string(jamm_tm_record div 1000) + "/" + string(jamm_tm_interp div 1000) + " moving=" + string(ds_list_size(jamm_moving)) + " logic=" + string(jamm_logic) + " target=" + string(jamm_target) + " delta=" + string(global.jamm_tick_delta) + " render_dt=" + string(jamm_render_dt) + " room=" + room_get_name(room));
            file_text_writeln(jamm_f);
            file_text_close(jamm_f);
            jamm_dbg_time = current_time;
            jamm_dbg_tick_us = 0;
            jamm_dbg_render_us = 0;
            jamm_dbg_tick_max = 0;
            jamm_dbg_render_max = 0;
            jamm_dbg_render_n = 0;
            jamm_dbg_ticks = 0;
            jamm_dbg_frames = 0;
            jamm_dbg_slow = 0;
            jamm_dbg_maxdt = 0;
            jamm_dbg_long = 0;
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

    private static string Toast(UndertaleData data) => $$"""
        if (current_time < jamm_toast_until)
        {
            {{(data.Fonts.ByName("fnt_babySmall") is not null ? "draw_set_font(fnt_babySmall);" : "draw_set_font(-1);")}}
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
