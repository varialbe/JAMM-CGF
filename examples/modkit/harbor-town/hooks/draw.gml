if (view_current != 0 || abs(x - Camera.x) > 850) exit;

for (var i = 0; i < 3; i += 1)
{
    var level = harbor_level[i];
    var stage = jmk_harbor_town_script_stage(level);
    var xx = x + harbor_offset[i];
    var tail = ln(max(100, level) / 100);
    var scale = 1 + 0.12 * tail / (1 + tail);
    var yy = y + (i == 0 ? 1 : 0);

    if (i == 0 && stage > 0 && is_it_night())
    {
        var lamp_y = yy - (26 + stage * 12) * scale;
        var theta = 360 * frac(current_time / 8500);
        var reach = 110 + stage * 16;
        draw_set_color(Colors.ui_light);
        draw_set_alpha(0.13);
        draw_triangle(xx, lamp_y,
            xx + lengthdir_x(reach, theta - 8), lamp_y + lengthdir_y(reach * 0.29, theta - 8),
            xx + lengthdir_x(reach, theta + 8), lamp_y + lengthdir_y(reach * 0.29, theta + 8), false);
        draw_set_alpha(1);
        draw_set_color(c_white);
    }
    draw_sprite_ext(harbor_sprite[i], stage, xx, yy, scale, scale, 0, c_white, 1);

    if (i == 1 && stage > 0)
    {
        // Draw the collection in the glass. These sprites have no world instances or collision.
        var tank_width = (56 + 12 * (stage - 1)) * scale;
        var tank_y = yy - (25 + 3 * (stage - 1)) * scale;
        for (var fish = 0; fish < harbor_fish_count; fish += 1)
        {
            var sprite = harbor_fish_sprite[fish];
            var w = sprite_get_width(sprite);
            var h = sprite_get_height(sprite);
            var size = min(14 / w, 9 / h) * scale;
            var lane = fish mod 3;
            var phase = current_time / (510 + lane * 90) + lane * 2.1;
            var px = xx + sin(phase) * (tank_width / 2 - 9 * scale);
            var py = tank_y + ((fish div 3) * 11 - 5 + 2 * sin(phase * 1.25)) * scale;
            var flip = cos(phase) < 0 ? 1 : -1;
            var frame = floor(current_time / 145 + fish) mod sprite_get_number(sprite);
            draw_sprite_ext(sprite, frame,
                px + (sprite_get_xoffset(sprite) - w / 2) * size * flip,
                py + (sprite_get_yoffset(sprite) - h / 2) * size,
                size * flip, size, 0, c_white, 0.92);
        }
    }
}

var sign_x = x + 53;
draw_sprite(jmk_harbor_town_recipe_sign, 0, sign_x, y);
if (harbor_near && !harbor_menu)
{
    draw_set_color(Colors.ui_dark);
    draw_set_font(fnt_babyVerySmall);
    draw_set_halign(fa_center);
    draw_set_valign(fa_bottom);
    draw_text(sign_x, y - 20, gamepad_is_connected(0) ? "X  Harbor Town" : "E  Harbor Town");
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    draw_set_color(c_white);
}
