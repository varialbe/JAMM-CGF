if (!harbor_ready || !instance_exists(Girl)) exit;

// The bulb follows the lure. Keep both of its existing render passes in proportion.
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
            harbor_selected = (harbor_selected + down - up + 3) mod 3;
            var buy = keyboard_check_pressed(vk_space) || (pad && gamepad_button_check_pressed(0, gp_face1));
            if (mouse_check_button_pressed(mb_left))
            {
                var mx = device_mouse_x_to_gui(0);
                var my = device_mouse_y_to_gui(0);
                if (point_in_rectangle(mx, my, 535, 102, 586, 130)) harbor_menu = false;
                for (var i = 0; i < 3; i += 1)
                {
                    if (!point_in_rectangle(mx, my, 496, 146 + i * 64, 573, 191 + i * 64)) continue;
                    harbor_selected = i;
                    buy = true;
                    break;
                }
            }
            if (buy && harbor_menu)
            {
                keyboard_clear(vk_space);
                harbor_status = jmk_harbor_town_script_buy(harbor_selected);
            }
        }
    }
}

if (abs(x - Camera.x) < 850 && current_time >= harbor_fish_refresh)
{
    jmk_harbor_town_script_fish();
    harbor_fish_refresh = current_time + 1000;
}
