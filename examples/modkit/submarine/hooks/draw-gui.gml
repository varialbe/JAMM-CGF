if (!sub_diving && current_time >= sub_notice_until) exit;
var left, top, width, colour, status, light_str, pitch_deg;
left = 16;
top = 62;
width = 186;
colour = make_color_rgb(174, 220, 201);
if (sub_oxygen < 25) colour = make_color_rgb(253, 167, 111);
status = "";
if (sub_ascending)
{
    if (Lure.state != 0 || sub_drop > 0) status = "Reel in to surface";
    else if (sub_oxygen <= 0) status = "Emergency ascent";
    else status = "Surfacing";
}
else if (current_time < sub_notice_until) status = sub_notice;
else if (sub_peek_timer > 0) status = "Peeking";

var old_col = draw_get_color();
var old_alpha = draw_get_alpha();
draw_set_font(fnt_babyVerySmall);
draw_set_halign(fa_left);
draw_set_valign(fa_top);
draw_set_alpha(0.78);
draw_set_color(make_color_rgb(25, 39, 48));
if (sub_diving) draw_rectangle(left - 6, top - 6, left + width + 6, top + 49 + 14 * (status != ""), false);
else draw_rectangle(left - 6, top - 6, left + 278, top + 20, false);
draw_set_alpha(1);
if (sub_diving)
{
    draw_set_color(make_color_rgb(234, 240, 228));
    draw_text(left, top, "O2 " + string(ceil(sub_oxygen)) + " s");
    draw_set_color(make_color_rgb(66, 87, 98));
    draw_rectangle(left, top + 18, left + width, top + 23, false);
    draw_set_color(colour);
    if (sub_oxygen > 0) draw_rectangle(left, top + 18, left + width * sub_oxygen / sub_oxygen_max, top + 23, false);
    draw_set_color(make_color_rgb(234, 240, 228));
    pitch_deg = round(sub_light_pitch);
    if (sub_lamp_on && sub_oxygen > 0)
    {
        if (pitch_deg > 0) light_str = "Light +" + string(pitch_deg) + " deg";
        else light_str = "Light " + string(pitch_deg) + " deg";
    }
    else
    {
        light_str = "Light off";
    }
    draw_text(left, top + 27, string(floor(max(0, sub_y - 1294))) + " depth  |  " + light_str);
    if (status != "") draw_text(left, top + 43, status);
}
else
{
    draw_set_color(make_color_rgb(234, 240, 228));
    draw_text(left, top, status);
}
draw_set_alpha(old_alpha);
draw_set_color(old_col);
