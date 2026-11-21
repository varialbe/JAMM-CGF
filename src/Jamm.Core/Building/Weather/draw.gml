var plane = argument0;
var vx = view_xview[0];
var vw = view_wview[0];
var vy = view_yview[0];
var vh = view_hview[0];
if (vy > 2100 || vy + vh < 300) return 0;
for (var pass = 0; pass < 2; pass += 1)
{
    var w = weather_previous;
    var fade = weather_tail;
    if (pass == 1)
    {
        w = weather_live;
        fade = weather_alpha;
    }
    if (w < 0 || fade <= 0) continue;
    for (var j = 0; j < weather_layers[w]; j += 1)
    {
        var k = j * 12;
        if (weather_layer[w, k + 1] != plane) continue;
        var jamm_weather_sprite = weather_layer[w, k];
        var xs = weather_layer[w, k + 7];
        var dx = weather_layer[w, k + 2] - vx * weather_layer[w, k + 4];
        dx += weather_layer[w, k + 5] * (weather_now / 1000);
        var dy = 1294 + weather_layer[w, k + 3];
        var width = sprite_get_width(jamm_weather_sprite) * xs;
        var frame = floor(weather_now * weather_layer[w, k + 6] / 1000) mod sprite_get_number(jamm_weather_sprite);
        var tint = merge_color(weather_layer[w, k + 10], weather_layer[w, k + 11], clamp(weather_second / 3060, 0, 1));
        var opacity = fade * weather_layer[w, k + 8];
        if (plane == 1 && pass == 1) opacity *= weather_foreground_scale;
        var origin_x = sprite_get_xoffset(jamm_weather_sprite) * xs;
        var origin_y = sprite_get_yoffset(jamm_weather_sprite) * xs;
        if (weather_layer[w, k + 9])
        {
            dx -= floor(dx / width) * width;
            dx += vx - width;
            for (var px = dx; px < vx + vw; px += width)
                draw_sprite_ext(jamm_weather_sprite, frame, px + origin_x, dy + origin_y, xs, xs, 0, tint, opacity);
        }
        else
        {
            dx += vx + (vw - width) * 0.5;
            draw_sprite_ext(jamm_weather_sprite, frame, dx + origin_x, dy + origin_y, xs, xs, 0, tint, opacity);
        }
    }
}
return 0;
