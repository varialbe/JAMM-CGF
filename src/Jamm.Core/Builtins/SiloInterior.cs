namespace Jamm.ModMaker;

internal static partial class SiloInterior
{
    internal const int EntryX = 760;
    internal const int EntryY = 122;

    private static readonly (int Left, int Top, int Right, int Bottom)[] Chambers =
    [
        (600, 48, 920, 833),   // Launch shaft
        (70, 234, 636, 510),   // Instrument gallery
        (884, 222, 1450, 512), // Control room
        (86, 616, 636, 834),   // Pump bay
        (884, 610, 1438, 834)  // Stores
    ];

    private static readonly (int X, int Y, int Value, string Name)[] Salvage =
    [
        (158, 391, 1800, "Pressure gauge"),
        (1326, 358, 3200, "Guidance module"),
        (183, 752, 2400, "Pump impeller"),
        (1326, 754, 4600, "Sealed recorder")
    ];

    internal static string Create(bool hasAmbience)
    {
        string dimensions = string.Join("\n", Chambers.Select((c, i) =>
            $"silo_left[{i}] = {c.Left}; silo_top[{i}] = {c.Top}; silo_right[{i}] = {c.Right}; silo_bottom[{i}] = {c.Bottom};"));
        string items = string.Join("\n", Salvage.Select((s, i) =>
            $"silo_item_x[{i}] = {s.X}; silo_item_y[{i}] = {s.Y}; silo_item_value[{i}] = {s.Value}; silo_item_name[{i}] = \"{s.Name}\";"));

        return $$"""
            silo_ctrl = instance_find(obj_jamm_silo_ctrl, 0);
            if (silo_ctrl == noone) { room_goto(NewWorld); exit; }
            {{(hasAmbience ? """
                silo_ctrl.silo_ambient_handle = audio_play_sound(sfx_underwater_loop, 0, true);
                audio_sound_gain(silo_ctrl.silo_ambient_handle, 0.18, 600);
                """ : "")}}
            image_speed = 0;
            silo_clock = 0;
            silo_air = 70;
            silo_vx = 0;
            silo_vy = 0;
            silo_pause = false;
            silo_leaving = false;
            silo_hatch_x = {{EntryX}};
            silo_hatch_y = {{EntryY}};
            silo_message = "";
            silo_message_until = 0;
            silo_room_count = {{Chambers.Length}};
            silo_item_count = {{Salvage.Length}};
            {{dimensions}}
            {{items}}
            """;
    }

    internal const string Step = """
        if (silo_leaving || !instance_exists(silo_ctrl)) exit;
        if (keyboard_check_pressed(vk_escape))
        {
            silo_pause = !silo_pause;
            exit;
        }

        var action = 0;
        var use = keyboard_check_pressed(ord("E"));
        if (silo_pause)
        {
            if (use) action = 1;
            if (keyboard_check_pressed(ord("S"))) action = 2;
        }
        else
        {
            var elapsed = clamp(delta_time / 1000000, 0, 0.14);
            silo_clock += elapsed;
            var left = keyboard_check(vk_left) || keyboard_check(ord("A"));
            var right = keyboard_check(vk_right) || keyboard_check(ord("D"));
            var up = keyboard_check(vk_up) || keyboard_check(ord("W"));
            var down = keyboard_check(vk_down) || keyboard_check(ord("S"));
            var dx = right - left;
            var dy = down - up;
            var length = max(1, point_distance(0, 0, dx, dy));
            var blend = min(1, elapsed * 10);
            silo_vx = lerp(silo_vx, 225 * dx / length, blend);
            silo_vy = lerp(silo_vy, 225 * dy / length, blend);
            if (abs(silo_vx) > 10) image_xscale = sign(silo_vx);
            image_angle = clamp(-silo_vy * 0.07 * image_xscale, -18, 18);
            image_index = floor(silo_clock * (length > 1 || dx != 0 || dy != 0 ? 7 : 3)) mod 2;

            // Resolve each axis independently so the diver can slide past a bulkhead.
            for (var axis = 0; axis < 2; axis += 1)
            {
                var next_x = x + (axis == 0 ? silo_vx * elapsed : 0);
                var next_y = y + (axis == 1 ? silo_vy * elapsed : 0);
                var clear = false;
                for (var i = 0; i < silo_room_count; i += 1)
                {
                    clear = clear || (next_x >= silo_left[i] + 11 && next_x <= silo_right[i] - 11 &&
                        next_y >= silo_top[i] + 11 && next_y <= silo_bottom[i] - 11);
                }
                if (clear) { x = next_x; y = next_y; }
            }

            var by_hatch = point_distance(x, y, silo_hatch_x, silo_hatch_y) < 68;
            var by_air = by_hatch || point_distance(x, y, 1224, 432) < 64 ||
                point_distance(x, y, 278, 737) < 64;
            silo_air = clamp(silo_air + (by_air ? 25 : -1) * elapsed, 0, 70);
            if (by_hatch && use) action = 1;
            if (silo_air <= 0) action = 3;

            if (use && !by_hatch && action == 0)
            {
                var claimed = silo_ctrl.silo_claimed | silo_ctrl.silo_pending;
                for (var i = 0; i < silo_item_count; i += 1)
                {
                    if ((claimed & (1 << i)) == 0 && point_distance(x, y, silo_item_x[i], silo_item_y[i]) < 52)
                    {
                        silo_ctrl.silo_pending |= 1 << i;
                        silo_ctrl.silo_earned += silo_item_value[i];
                        silo_message = silo_item_name[i] + " recovered";
                        silo_message_until = silo_clock + 2.5;
                        break;
                    }
                }
            }
        }

        if (action != 0)
        {
            silo_leaving = true;
            silo_ctrl.silo_exit = action;
            if (action == 3)
            {
                silo_ctrl.silo_earned = 0;
                silo_ctrl.silo_pending = 0;
            }
            room_goto(NewWorld);
        }
        """;

    internal static string DrawGui(string font) => $$"""
        if (!instance_exists(silo_ctrl)) exit;
        var width = display_get_gui_width();
        var height = display_get_gui_height();
        var pale = make_color_rgb(198, 231, 215);
        var amber = make_color_rgb(246, 181, 103);
        draw_set_font({{font}});
        draw_set_halign(fa_left);
        draw_set_valign(fa_middle);
        draw_set_alpha(0.86);
        draw_set_color(make_color_rgb(8, 20, 25));
        draw_rectangle(14, 14, 218, 70, false);
        draw_set_alpha(1);
        draw_set_color(pale);
        draw_text(25, 30, "AIR");
        draw_text(96, 30, string(ceil(silo_air)) + " s");
        draw_text(25, 54, "SALVAGE  $" + string(silo_ctrl.silo_earned));
        draw_set_color(make_color_rgb(36, 59, 61));
        draw_rectangle(146, 24, 204, 36, false);
        draw_set_color(silo_air > 14 ? pale : amber);
        draw_rectangle(146, 24, 146 + 58 * silo_air / 70, 36, false);

        var hint = "";
        if (point_distance(x, y, silo_hatch_x, silo_hatch_y) < 68)
            hint = "Air bell     E: Return through hatch";
        if (point_distance(x, y, 1224, 432) < 64 || point_distance(x, y, 278, 737) < 64)
            hint = "Air bell  /  refilling";
        var claimed = silo_ctrl.silo_claimed | silo_ctrl.silo_pending;
        for (var i = 0; i < silo_item_count; i += 1)
            if ((claimed & (1 << i)) == 0 && point_distance(x, y, silo_item_x[i], silo_item_y[i]) < 52)
                hint = "E: Recover " + silo_item_name[i] + "  ($" + string(silo_item_value[i]) + ")";
        if (silo_clock < silo_message_until) hint = silo_message;

        draw_set_halign(fa_center);
        var cx = width / 2;
        var controls = "WASD / Arrows: Swim     E: Interact     Esc: Pause";
        var half = max(string_width(controls), string_width(hint)) / 2 + 15;
        draw_set_alpha(0.86);
        draw_set_color(make_color_rgb(8, 20, 25));
        draw_rectangle(cx - half, height - (hint == "" ? 42 : 65), cx + half, height - 13, false);
        draw_set_alpha(1);
        draw_set_color(pale);
        if (hint != "") draw_text(cx, height - 51, hint);
        draw_text(cx, height - 28, controls);

        if (silo_pause)
        {
            var cy = height / 2;
            draw_set_alpha(0.76);
            draw_set_color(make_color_rgb(6, 14, 18));
            draw_rectangle(0, 0, width, height, false);
            draw_set_alpha(1);
            draw_set_color(make_color_rgb(21, 40, 45));
            draw_rectangle(cx - 165, cy - 71, cx + 165, cy + 71, false);
            draw_set_color(amber);
            draw_text(cx, cy - 45, "DIVE PAUSED");
            draw_set_color(pale);
            draw_text(cx, cy - 13, "Esc  /  Resume");
            draw_text(cx, cy + 12, "E  /  Return to NewWorld");
            draw_text(cx, cy + 37, "S  /  Save and quit");
        }
        draw_set_color(c_white);
        draw_set_font(-1);
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
        """;
}
