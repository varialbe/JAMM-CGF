namespace Jamm.ModMaker;

internal static partial class SiloInterior
{
    internal static string Draw(string font) => $$"""
        if (!instance_exists(silo_ctrl)) exit;
        var dark = make_color_rgb(8, 15, 21);
        var rail = make_color_rgb(78, 118, 111);
        var rust = make_color_rgb(155, 99, 64);
        var brass = make_color_rgb(227, 170, 94);
        var teal = make_color_rgb(112, 206, 179);
        var paper = make_color_rgb(172, 199, 179);

        draw_set_color(dark);
        draw_rectangle(0, 0, room_width, room_height, false);
        // Pressure bulkheads, with open passages at the shaft intersections.
        for (var i = 0; i < silo_room_count; i += 1)
        {
            var l = silo_left[i], t = silo_top[i], r = silo_right[i], b = silo_bottom[i];
            draw_set_color(rail);
            draw_rectangle(l - 7, t - 7, r + 7, b + 7, false);
            draw_set_color(dark);
            draw_rectangle(l - 4, t - 4, r + 4, b + 4, false);
            draw_rectangle_colour(l, t, r, b, make_color_rgb(31, 61, 68), make_color_rgb(31, 61, 68),
                make_color_rgb(16, 36, 48), make_color_rgb(16, 36, 48), false);
            draw_set_color(make_color_rgb(55, 83, 83));
            for (var rivet = l + 18; rivet < r; rivet += 39)
            {
                draw_circle(rivet, t + 8, 1.7, false);
                draw_circle(rivet, b - 9, 1.7, false);
            }
        }

        // Riveted shaft plates and a rusted ladder.
        draw_set_color(make_color_rgb(36, 62, 65));
        for (var plate = 160; plate < 820; plate += 64)
        {
            draw_line(622, plate, 898, plate);
            draw_circle(632, plate + 6, 2, false);
            draw_circle(889, plate + 6, 2, false);
        }
        draw_set_color(make_color_rgb(107, 72, 55));
        draw_line_width(637, 128, 637, 800, 5);
        draw_line_width(659, 128, 659, 800, 5);
        for (var rung = 134; rung < 800; rung += 15)
            draw_line_width(637, rung, 659, rung, 3);

        // Launch vehicle on its cradle. The shaft is flooded in front of it.
        draw_set_color(make_color_rgb(14, 29, 33));
        draw_rectangle(697, 218, 819, 777, false);
        draw_set_color(make_color_rgb(71, 100, 100));
        draw_triangle(713, 391, 763, 265, 807, 391, false);
        draw_rectangle(713, 391, 807, 695, false);
        draw_set_color(make_color_rgb(123, 143, 125));
        draw_triangle(720, 391, 763, 265, 745, 391, false);
        draw_rectangle(720, 391, 745, 695, false);
        draw_set_color(make_color_rgb(137, 91, 67));
        draw_rectangle(713, 428, 807, 449, false);
        draw_triangle(713, 611, 684, 705, 713, 695, false);
        draw_triangle(807, 611, 836, 705, 807, 695, false);
        draw_set_color(make_color_rgb(23, 40, 46));
        draw_rectangle(717, 695, 803, 729, false);
        draw_line_width(682, 738, 839, 738, 8);
        draw_line_width(700, 735, 685, 804, 6);
        draw_line_width(819, 735, 834, 804, 6);

        // Thick signal cable routed over the shaft and into the east control room.
        draw_set_color(make_color_rgb(15, 25, 31));
        var px = 828, py = 183;
        for (var n = 1; n <= 40; n += 1)
        {
            var qx = 828 + n * 13;
            var qy = 182 + min(78, n * 5) + 14 * sin(n * 0.21);
            draw_line_width(px, py, qx, qy, 6);
            px = qx; py = qy;
        }

        // Instrument gallery and its three dead pressure vessels.
        for (var tank = 0; tank < 3; tank += 1)
        {
            var tx = 252 + tank * 107;
            draw_set_color(make_color_rgb(21, 39, 44));
            draw_roundrect(tx, 287, tx + 83, 463, false);
            draw_set_color(make_color_rgb(59, 91, 88));
            draw_roundrect(tx + 5, 291, tx + 72, 454, false);
            draw_set_color(rail);
            draw_line_width(tx + 20, 308, tx + 20, 436, 4);
            draw_set_color(rust);
            draw_rectangle(tx + 1, 391, tx + 77, 401, false);
        }

        // Observation windows, banks of gauges and sloped control desks.
        draw_set_color(make_color_rgb(17, 29, 38));
        draw_rectangle(1010, 314, 1405, 468, false);
        for (var panel = 0; panel < 4; panel += 1)
        {
            var sx = 1028 + panel * 91;
            draw_set_color(rail);
            draw_rectangle(sx, 320, sx + 70, 374, false);
            draw_set_color(make_color_rgb(10, 31, 36));
            draw_rectangle(sx + 4, 324, sx + 66, 371, false);
            draw_set_color(merge_color(teal, dark, panel == 1 ? 0.18 : 0.55));
            draw_line(sx + 12, 358, sx + 24, 351);
            draw_line(sx + 24, 351, sx + 36, 357);
            draw_line(sx + 36, 357, sx + 54, 339);
            draw_set_color(rust);
            draw_rectangle(sx, 384, sx + 76, 411, false);
            draw_set_color(brass);
            draw_circle(sx + 12, 392, 2, false);
            draw_set_color(teal);
            draw_circle(sx + 21, 399, 2, false);
        }

        // Pump manifold and stores. Bars mark the floor without blocking the diver.
        draw_set_color(make_color_rgb(78, 78, 65));
        draw_line_width(117, 693, 555, 693, 15);
        draw_line_width(511, 693, 511, 788, 15);
        draw_line_width(138, 772, 625, 772, 12);
        for (var valve = 0; valve < 4; valve += 1)
        {
            var vx = 125 + valve * 113;
            draw_set_color(rust);
            draw_circle(vx, 694, 18, false);
            draw_set_color(dark);
            draw_circle(vx, 694, 13, false);
            draw_set_color(brass);
            draw_line_width(vx, 683, vx, 706, 2);
            draw_line_width(vx - 11, 694, vx + 11, 694, 2);
        }
        for (var crate = 0; crate < 6; crate += 1)
        {
            var bx = 945 + crate * 71;
            var by = 789 - (crate mod 3) * 13;
            draw_set_color(make_color_rgb(61, 69, 61));
            draw_rectangle(bx, by - 37, bx + 62, by + 22, false);
            draw_set_color(rust);
            draw_rectangle(bx + 4, by - 33, bx + 58, by + 18, true);
            draw_line(bx + 5, by - 32, bx + 57, by + 16);
        }

        // Area signs and isolated pools of light.
        draw_set_font({{font}});
        draw_set_halign(fa_center);
        draw_set_valign(fa_middle);
        draw_set_color(paper);
        draw_text(760, 74, "MAINTENANCE HATCH");
        draw_text(344, 262, "INSTRUMENTS");
        draw_text(1251, 289, "CONTROL");
        draw_text(328, 647, "PUMPS");
        draw_text(1196, 640, "STORES");
        draw_text(763, 485, "03");

        draw_set_blend_mode(bm_add);
        draw_sprite_ext(spr_jamm_silo_glow, 0, 760, 126, 2.1, 2.7, 0, teal, 0.79);
        draw_sprite_ext(spr_jamm_silo_glow, 0, 357, 313, 3.6, 2.4, 0, teal, 0.36);
        draw_sprite_ext(spr_jamm_silo_glow, 0, 766, 503, 2.5, 4.8, 0, brass, 0.27);
        draw_sprite_ext(spr_jamm_silo_glow, 0, 1224, 412, 3.3, 2.5, 0, teal, 0.64);
        draw_sprite_ext(spr_jamm_silo_glow, 0, 278, 730, 3.2, 2.6, 0, teal, 0.54);
        draw_sprite_ext(spr_jamm_silo_glow, 0, 1272, 708, 3.1, 2.6, 0, brass, 0.51);
        draw_sprite_ext(spr_jamm_silo_glow, 0, x, y, 2.8, 2.4, 0, make_color_rgb(117, 157, 166), 0.38);
        draw_set_blend_mode(bm_normal);

        // Air bells give off visible bubbles.
        for (var air = 0; air < 3; air += 1)
        {
            var ax = 760, ay = 122;
            if (air == 1) { ax = 1224; ay = 432; }
            if (air == 2) { ax = 278; ay = 737; }
            draw_set_color(dark);
            draw_roundrect(ax - 26, ay + 9, ax + 26, ay + 24, false);
            draw_set_color(teal);
            draw_line_width(ax - 18, ay + 12, ax + 18, ay + 12, 3);
            draw_set_alpha(0.48);
            for (var bubble = 0; bubble < 6; bubble += 1)
            {
                var rise = (silo_clock * 29 + bubble * 13) mod 69;
                draw_circle(ax + 12 * sin(bubble * 3 + rise * 0.08), ay + 6 - rise, 1.4 + (bubble mod 3), true);
            }
            draw_set_alpha(1);
        }

        var claimed = silo_ctrl.silo_claimed | silo_ctrl.silo_pending;
        for (var salvage = 0; salvage < silo_item_count; salvage += 1)
        {
            if ((claimed & (1 << salvage)) != 0) continue;
            var rx = silo_item_x[salvage], ry = silo_item_y[salvage] + 3 * sin(silo_clock * 2 + salvage);
            draw_sprite_ext(spr_jamm_silo_glow, 0, rx, ry, 1.1, 1.1, 0, brass, 0.67);
            draw_set_color(dark);
            draw_roundrect(rx - 13, ry - 10, rx + 13, ry + 10, false);
            draw_set_color(brass);
            draw_roundrect(rx - 12, ry - 9, rx + 12, ry + 9, true);
            draw_line_width(rx - 6, ry, rx + 6, ry, 2);
        }

        // Small shoals stay in their own galleries.
        draw_set_alpha(0.56);
        for (var fish = 0; fish < 12; fish += 1)
        {
            var west = fish < 6;
            var fx = (west ? 334 : 1130) + 167 * sin(silo_clock * 0.34 + fish * 0.7);
            var fy = (west ? 422 : 681) + 33 * sin(silo_clock * 0.6 + fish * 1.3);
            var facing = -sign(cos(silo_clock * 0.34 + fish * 0.7));
            draw_sprite_ext(spr_jamm_silo_fish, floor(silo_clock * 6 + fish) mod 2, fx, fy,
                facing * 0.55, 0.55, 0, make_color_rgb(128, 185, 172), 1);
        }
        draw_set_alpha(1);
        draw_set_color(c_white);
        draw_set_font(-1);
        draw_set_halign(fa_left);
        draw_set_valign(fa_top);
        draw_sprite_ext(sprite_index, image_index, x, y, 0.84 * image_xscale, 0.84, image_angle, c_white, 1);
        """;
}
