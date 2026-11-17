// Runs at the end of jmk_example_fish_glowfin Step_2 (after Fish_Step_2).
// - At night (is_it_night()): pulses image_blend between cyan and lime, and darts
//   away when a moving lure gets within 120 px (lure_distance_squared < 14400).
// - During daytime: dims and sinks out of shallow water until nightfall.
if (glow_dart_cd > 0)
{
    glow_dart_cd -= 1;
}

if (is_it_night())
{
    var pulse;
    pulse = 0.5 + (0.5 * sin((current_time * 0.005) + glow_phase));
    image_blend = merge_color(c_aqua, c_lime, pulse);

    if (state == 0 && Lure.state == 2 && lure_distance_squared < 14400 && glow_dart_cd <= 0)
    {
        var away;
        away = point_direction(Lure.x, Lure.y, x, y);
        hspeed += lengthdir_x(4.5, away);
        vspeed += lengthdir_y(4.5, away);
        glow_dart_cd = 45;
    }
}
else
{
    image_blend = c_dkgray;
    if ((state == 0 || state == 2) && y < 4200)
    {
        vspeed += 0.6;
    }
}
