var pad, controls, dive, lamp;
pad = gamepad_is_supported() && gamepad_is_connected(0);
controls = !instance_exists(Handler);
var console_object = asset_get_index("obj_jamm_console");
if (console_object >= 0 && instance_exists(console_object))
    controls = controls && !console_object.jamm_console_open;
dive = controls && (keyboard_check_pressed(ord("V")) || (pad && gamepad_button_check_pressed(0, gp_face4)));
lamp = controls && (keyboard_check_pressed(ord("L")) || (pad && gamepad_button_check_pressed(0, gp_face3)));

if (!sub_diving)
{
    if (dive && !instance_exists(CatSwim) && Lure.state == 0 && Rod.state == 0)
    {
        if (Girl.x < sub_dive_min_x
            || collision_rectangle(Girl.x - 100, sub_entry_y - 28, Girl.x + 100, sub_entry_y + 28, Collidable, true, true) != noone)
        {
            sub_notice = "Take the sub out to open water";
            sub_notice_until = current_time + 2500;
        }
        else
        {
            sub_diving = true;
            sub_entry_x = Girl.x;
            sub_x = Girl.x;
            sub_y = sub_entry_y;
            sub_facing = 1;
            sub_rod_anchor_x = Girl.rod_anchor_x_root;
            sub_rod_anchor_y = Girl.rod_anchor_y_root;
            sub_rod_visible = Rod.visible;
            sub_rod_locked = Rod.swing_locked;
            sub_lamp = instance_create(sub_x, sub_y, Light);
            sub_lamp.image_scale = 3.1;
            sub_lamp.special_scale = 0.18;
            sub_lamp.add_blend = make_color_rgb(61, 54, 27);
            sub_lamp.sub_blend = make_color_rgb(205, 198, 154);
            sub_lamp.image_blend = sub_lamp.sub_blend;
            sub_lamp.image_xscale = sub_lamp.image_scale;
            sub_lamp.image_yscale = sub_lamp.image_scale;
        }
    }
    if (!sub_diving)
    {
        event_inherited();
        exit;
    }
}
else if (dive && sub_oxygen > 0)
{
    sub_ascending = !sub_ascending;
    sub_cast_armed = false;
}

if (lamp) sub_lamp_on = !sub_lamp_on;
sub_dt = clamp(delta_time / 1000000, 0, 0.05);
sub_oxygen = max(0, sub_oxygen - sub_dt);
if (sub_oxygen <= 0)
{
    sub_ascending = true;
    sub_drop = 0;
    if (Lure.state != 0)
    {
        with (Lure)
        {
            line_snap();
            line_reset();
            fowled = 0;
        }
    }
}

var dx, dy, can_move, next_x, next_y;
dx = 0;
dy = 0;
can_move = Lure.state == 0 && sub_drop == 0;
if (can_move && sub_ascending)
{
    dy = -575 * sub_dt;
}
else if (can_move && controls)
{
    dx = (keyboard_check(vk_right) || keyboard_check(ord("D"))) - (keyboard_check(vk_left) || keyboard_check(ord("A")));
    dy = (keyboard_check(vk_down) || keyboard_check(ord("S"))) - (keyboard_check(vk_up) || keyboard_check(ord("W")));
    if (pad)
    {
        dx += gamepad_button_check(0, gp_padr) - gamepad_button_check(0, gp_padl);
        dy += gamepad_button_check(0, gp_padd) - gamepad_button_check(0, gp_padu);
        var stick_x = gamepad_axis_value(0, gp_axislh);
        var stick_y = gamepad_axis_value(0, gp_axislv);
        if (abs(stick_x) > 0.25) dx += stick_x;
        if (abs(stick_y) > 0.25) dy += stick_y;
    }
    dx = clamp(dx, -1, 1);
    dy = clamp(dy, -1, 1);
    var diagonal = max(1, point_distance(0, 0, dx, dy));
    dx *= 325 * sub_dt / diagonal;
    dy *= 520 * sub_dt / diagonal;
}

// Advance in short increments so a slow frame cannot step through a thin wall.
var steps = max(1, ceil(max(abs(dx), abs(dy)) / 7));
var start_x = sub_x;
var start_y = sub_y;
for (var i = 0; i < steps; i += 1)
{
    next_x = clamp(sub_x + dx / steps, sub_dive_min_x, min(MAX_X, room_width - 100));
    next_y = clamp(sub_y + dy / steps, sub_entry_y, min(room_height - 100, 1294 + sub_depth_limit));
    if (next_x != sub_x && collision_rectangle(next_x - 100, sub_y - 28, next_x + 100, sub_y + 28, Collidable, true, true) == noone)
        sub_x = next_x;
    if (next_y != sub_y && collision_rectangle(sub_x - 100, next_y - 28, sub_x + 100, next_y + 28, Collidable, true, true) == noone)
        sub_y = next_y;
}
if (sub_x != start_x) sub_facing = sign(sub_x - start_x);
if (sub_ascending && can_move)
{
    if (sub_y == start_y) sub_blocked_time += sub_dt;
    else sub_blocked_time = 0;
    if (sub_y <= sub_entry_y || sub_blocked_time >= 2)
    {
        if (sub_blocked_time >= 2) sub_x = sub_entry_x;
        sub_diving = false;
        sub_ascending = false;
        sub_blocked_time = 0;
        sub_oxygen = sub_oxygen_max;
        if (instance_exists(sub_lamp)) instance_destroy(sub_lamp);
        sub_lamp = noone;
        Girl.x = sub_x;
        Girl.y = home_y;
        Girl.rod_anchor_x_root = sub_rod_anchor_x;
        Girl.rod_anchor_y_root = sub_rod_anchor_y;
        Rod.swing_locked = sub_rod_locked;
        Rod.visible = sub_rod_visible;
        image_xscale = 0.5;
        Girl.visible = true;
        exit;
    }
}
else sub_blocked_time = 0;

x = sub_x;
y = sub_y;
Girl.x = sub_x;
Girl.y = sub_y;
Girl.hspeed = 0;
Girl.vspeed = 0;
Girl.hspeed_base = 0;
Girl.catswim_cooldown = max(Girl.catswim_cooldown, Girl.delta_fixed + 1);
wasJustMoving = true;
image_xscale = 0.5 * sub_facing;

// Keep the stock tether calculation at the bow and handle underwater casting here.
sub_port_x = sub_x + 104 * sub_facing;
sub_port_y = sub_y + 9;
Girl.rod_anchor_x_root = sub_port_x - sub_x - lengthdir_x(Lure.ROD_LENGTH, Lure.ROD_ANGLE);
Girl.rod_anchor_y_root = sub_port_y - sub_y - lengthdir_y(Lure.ROD_LENGTH, Lure.ROD_ANGLE);
Girl.rod_anchor_x = Girl.rod_anchor_x_root;
Girl.rod_anchor_y = Girl.rod_anchor_y_root;
Rod.x = sub_x + Girl.rod_anchor_x;
Rod.y = sub_y + Girl.rod_anchor_y;
Rod.hspeed = 0;
Rod.vspeed = 0;
Rod.swing_locked = true;
Rod.angle = 0;
if (Lure.state == 0 && sub_drop == 0)
{
    Lure.x = sub_port_x;
    Lure.y = sub_port_y;
    Lure.x_previous = Lure.x;
    Lure.y_previous = Lure.y;
    Lure.spd = 0;
}
if (instance_exists(sub_lamp))
{
    sub_lamp.x = sub_x + 165 * sub_facing;
    sub_lamp.y = sub_y + 8;
    sub_lamp.visible = sub_lamp_on && sub_oxygen > 0;
}

if (!controls || sub_ascending || Lure.state != 0 || sub_drop > 0)
{
    sub_cast_armed = false;
    exit;
}
if (keyboard_check_pressed(vk_space) || mouse_check_button_pressed(mb_left) || (pad && gamepad_button_check_pressed(0, gp_shoulderrb)))
    sub_cast_armed = true;
if (sub_cast_armed && (keyboard_check_released(vk_space) || mouse_check_button_released(mb_left)
    || (pad && gamepad_button_check_released(0, gp_shoulderrb))))
{
    sub_cast_armed = false;
    if (collision_line(sub_port_x, sub_port_y, sub_port_x + 0.6 * sub_drop_length * sub_facing,
        sub_port_y + 0.8 * sub_drop_length, Collidable, true, true) == noone)
        sub_drop = 1;
    else
    {
        sub_notice = "Move away from the rocks to cast";
        sub_notice_until = current_time + 2500;
    }
}
