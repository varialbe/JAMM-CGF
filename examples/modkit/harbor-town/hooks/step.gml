if (!harbor_ready || !instance_exists(Girl)) exit;

// Apply the capped research modifiers from stable per-instance baselines.
var sell_bonus = jmk_harbor_town_script_research(harbor_level[3], 0);
var bite_bonus = jmk_harbor_town_script_research(harbor_level[3], 1);
var luck_bonus = jmk_harbor_town_script_research(harbor_level[3], 2);

// The first patron tier gives the equipped vanilla or ModKit hat a persistent gold recolour.
if (harbor_donated >= 1000000 && variable_instance_exists(Girl, "gear") && instance_exists(Girl.gear))
{
    with (Girl.gear)
    {
        if (!variable_instance_exists(id, "harbor_hat_native_blend")) harbor_hat_native_blend = image_blend;
        var patron_tint = make_color_rgb(255, 232, 168);
        if (other.harbor_donated >= 1000000000) patron_tint = make_color_rgb(255, 211, 111);
        if (other.harbor_donated >= 1000000000000) patron_tint = make_color_rgb(255, 191, 67);
        if (other.harbor_donated >= 1000000000000000) patron_tint = make_color_rgb(255, 228, 120);
        image_blend = merge_color(harbor_hat_native_blend, patron_tint, 0.42);
    }
}
with (Fish)
{
    if (variable_instance_exists(id, "base_lunge_mod"))
    {
        if (!variable_instance_exists(id, "harbor_base_lunge")) harbor_base_lunge = base_lunge_mod;
        base_lunge_mod = harbor_base_lunge * (1 + bite_bonus);
    }
    if (hasGivenExperience && (!variable_instance_exists(id, "harbor_sale_scaled") || !harbor_sale_scaled))
    {
        harbor_sale_scaled = true;
        value = min(110000000, round(min(100000000, value) * (1 + sell_bonus)));
    }
}
if (instance_exists(Lure))
{
    if (!variable_instance_exists(Lure, "harbor_base_lunge_scale")) Lure.harbor_base_lunge_scale = Lure.lunge_scale;
    Lure.lunge_scale = Lure.harbor_base_lunge_scale * (1 + luck_bonus);
}

// The Lighthouse improves the lure's native light only at night or in deep water.
if (instance_exists(Lightbulb))
{
    var bulb = Lightbulb.light;
    if (instance_exists(bulb))
    {
        if (!variable_instance_exists(bulb, "harbor_native_scale"))
        {
            bulb.harbor_native_scale = bulb.image_scale;
            bulb.harbor_native_glow = bulb.special_scale;
        }
        var multiplier = 1;
        if (Lure.y >= 2000 || is_it_night())
            multiplier += jmk_harbor_town_script_bonus(harbor_level[0]);
        bulb.image_scale = bulb.harbor_native_scale * multiplier;
        bulb.special_scale = bulb.harbor_native_glow * multiplier;
    }
}

var console_object = asset_get_index("obj_jamm_console");
var console = noone;
if (console_object >= 0) console = instance_find(console_object, 0);
var console_open = instance_exists(console) && console.jamm_console_open;
harbor_near = abs(Girl.x - (x + 53)) < 92 && abs(Girl.y - y) < 90
    && Lure.state == 0 && !instance_exists(CatSwim) && !instance_exists(Handler);
if (!harbor_near || console_open)
{
    harbor_menu = false;
}
else
{
    var pad = gamepad_is_supported() && gamepad_is_connected(0);
    var toggle = keyboard_check_pressed(ord("E")) || (pad && gamepad_button_check_pressed(0, gp_face3));
    if (toggle)
    {
        harbor_menu = !harbor_menu;
        harbor_status = "";
    }
    else if (harbor_menu)
    {
        var close = keyboard_check_pressed(vk_escape) || (pad && gamepad_button_check_pressed(0, gp_face2));
        if (close)
        {
            keyboard_clear(vk_escape);
            harbor_menu = false;
        }
        else
        {
            var up = keyboard_check_pressed(vk_up) || (pad && gamepad_button_check_pressed(0, gp_padu));
            var down = keyboard_check_pressed(vk_down) || (pad && gamepad_button_check_pressed(0, gp_padd));
            harbor_selected = (harbor_selected + down - up + 5) mod 5;
            var buy = keyboard_check_pressed(vk_space) || (pad && gamepad_button_check_pressed(0, gp_face1));
            if (keyboard_check_pressed(ord("D")) || (pad && gamepad_button_check_pressed(0, gp_face2) && keyboard_check(vk_shift)))
            {
                var needed = 1000000;
                if (harbor_donated >= 1000000) needed = 1000000000;
                if (harbor_donated >= 1000000000) needed = 1000000000000;
                if (harbor_donated >= 1000000000000) needed = 1000000000000000;
                harbor_status = jmk_harbor_town_script_donate(min(needed - harbor_donated, Girl.dollars));
            }
            if (mouse_check_button_pressed(mb_left))
            {
                var mx = device_mouse_x_to_gui(0);
                var my = device_mouse_y_to_gui(0);
                if (point_in_rectangle(mx, my, 535, 102, 586, 130)) harbor_menu = false;
                if (point_in_rectangle(mx, my, 496, 360, 573, 390))
                {
                    harbor_selected = 4;
                    buy = true;
                }
                for (var i = 0; i < 4; i += 1)
                {
                    if (!point_in_rectangle(mx, my, 496, 140 + i * 57, 573, 180 + i * 57)) continue;
                    harbor_selected = i;
                    buy = true;
                    break;
                }
            }
            if (buy && harbor_menu)
            {
                keyboard_clear(vk_space);
                if (harbor_selected == 4)
                {
                    var needed = 1000000;
                    if (harbor_donated >= 1000000) needed = 1000000000;
                    if (harbor_donated >= 1000000000) needed = 1000000000000;
                    if (harbor_donated >= 1000000000000) needed = 1000000000000000;
                    harbor_status = jmk_harbor_town_script_donate(min(needed - harbor_donated, Girl.dollars));
                }
                else harbor_status = jmk_harbor_town_script_buy(harbor_selected);
            }
        }
    }
}
if (abs(x - Camera.x) < 850 && current_time >= harbor_fish_refresh)
{
    jmk_harbor_town_script_fish();
    harbor_fish_refresh = current_time + 1000;
}
