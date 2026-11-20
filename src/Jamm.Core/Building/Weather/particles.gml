if (weather_live < 0 || weather_alpha <= 0) return 0;
var w = weather_live;
var kind = weather_emitter[w, 0];
if (kind < 0) return 0;
var left = view_xview[0];
var span = view_wview[0];
if (view_yview[0] > 1480 || view_yview[0] + view_hview[0] < 500) return 0;
var col = weather_emitter[w, 3];
var opacity = weather_emitter[w, 4] * weather_alpha * weather_foreground_scale;
var time = weather_now * weather_emitter[w, 2] / 1000;
for (var i = 0; i < weather_emitter[w, 1]; i += 1)
{
    var u = frac((i + 1) * 0.61803398875);
    var v = frac((i + 1) * 0.41421356237);
    var px = left + span * frac(u + time * (0.015 + v * 0.08));
    var py = 1280 + x_pos_to_y_off(px);
    if (kind == 0)
    {
        var cycle = frac(v + time * (0.73 + u * 0.55));
        if (cycle < 0.87)
        {
            var sy = py - 820 + cycle / 0.87 * 820;
            draw_set_alpha(opacity * (0.45 + 0.55 * u));
            draw_line_color(px - 5, sy - 22, px, sy, col, col);
        }
        else
        {
            var radius = 3 + (cycle - 0.87) * 95;
            draw_set_alpha(opacity * (1 - (cycle - 0.87) / 0.13));
            draw_ellipse_color(px - radius, py - 1, px + radius, py + 3, col, col, true);
        }
    }
    else if (kind == 1)
    {
        var py = 770 + v * 580;
        var gust = power(max(0, sin(time * 1.7 + i)), 2);
        draw_set_alpha(opacity * gust);
        if (i mod 7 == 0)
            draw_ellipse_color(px, py + 5 * sin(time * 9), px + 8, py + 5 * sin(time * 9) + 4, $88a5bd, col, false);
        else
            draw_line_color(px, py, px + 25 + 85 * u, py - 4, col, col);
    }
    else if (kind == 2)
    {
        var glint = power(max(0, sin(time * 2 + i)), 5);
        draw_set_alpha(opacity * glint);
        draw_line_color(px - 9 - u * 12, py + v * 85, px + 9 + u * 12, py + v * 85, col, col);
        draw_line_color(px, py + v * 85 - 3, px, py + v * 85 + 3, col, col);
        if (i mod 5 == 0)
            draw_line_color(px, py - 24 - u * 70, px + 18, py - 26 - u * 70 + 2 * sin(time * 7 + i), col, col);
    }
    else
    {
        var py = 1100 + v * 330;
        draw_set_alpha(opacity * 0.4 * power(max(0, sin(time * 0.5 + i)), 2));
        draw_ellipse_color(px - 42, py - 4, px + 42, py + 4, col, col, false);
    }
}
draw_set_alpha(1);
return 0;
