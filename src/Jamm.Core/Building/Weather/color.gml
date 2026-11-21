var color = argument0;
var channel = argument1;
if (!instance_exists(obj_jamm_weather) || room != NewWorld) return color;
with (obj_jamm_weather)
{
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
        var tint = weather_paint[w, channel];
        if (tint < 0) continue;
        if (channel != 0)
            tint = make_color_rgb(color_get_red(color) * color_get_red(tint) / 255,
                color_get_green(color) * color_get_green(tint) / 255,
                color_get_blue(color) * color_get_blue(tint) / 255);
        color = merge_color(color, tint, fade);
    }
}
return color;
