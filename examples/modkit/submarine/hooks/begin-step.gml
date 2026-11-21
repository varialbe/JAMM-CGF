var pad, controls, dive, lamp, dt_us;
pad = gamepad_is_supported() && gamepad_is_connected(0);
controls = !instance_exists(Handler);
var console_object = asset_get_index("obj_jamm_console");
if (console_object >= 0 && instance_exists(console_object))
    controls = controls && !console_object.jamm_console_open;
dt_us = delta_time;
if (variable_global_exists("jamm_tick_delta")) dt_us = global.jamm_tick_delta;
sub_dt = clamp(dt_us / 1000000, 0, 0.05);

if (sub_winch_slot < 0) sub_winch_slot = jamm_item_find("tool", "submarine:bow-winch");
sub_has_winch = (sub_winch_slot >= 0 && instance_exists(ToolData)
    && sub_winch_slot < array_length_1d(ToolData.unlocked) && ToolData.unlocked[sub_winch_slot] > 0);
if (sub_has_winch && !sub_prev_winch && !sub_diving && !instance_exists(Handler) && controls)
    Rod.swing_locked = false;
sub_prev_winch = sub_has_winch;
if (instance_exists(icon_Tools) && !icon_Tools.unlocked)
    icon_Tools.unlocked = 1;

dive = controls && (keyboard_check_pressed(ord("V")) || (pad && gamepad_button_check_pressed(0, gp_face4)));
lamp = controls && (keyboard_check_pressed(ord("L")) || (pad && gamepad_button_check_pressed(0, gp_face3)));

if (!sub_diving)
{
    if (dive && !instance_exists(CatSwim) && Lure.state == 0 && Rod.state == 0)
    {
        if (Girl.x < sub_dive_min_x
            || collision_rectangle(Girl.x - 105, sub_entry_y - 40, Girl.x + 105, sub_entry_y + 56, Collidable, true, true) != noone)
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
            sub_light_pitch = 0;
            sub_light_angle = 0;
            sub_peek_timer = 0;
            sub_peek_held = 0;
            sub_peek_amount = 0;
            sub_cam_look_x = 150;
            if (instance_exists(Camera))
            {
                sub_cam_x = Camera.x;
                sub_cam_y = Camera.y;
            }
            sub_rod_anchor_x = Girl.rod_anchor_x_root;
            sub_rod_anchor_y = Girl.rod_anchor_y_root;
            sub_rod_visible = Rod.visible;
            sub_rod_locked = Rod.swing_locked;
            sub_lamp = instance_create(sub_x + 114, sub_y + 10, Light);
            sub_lamp.sprite_index = jmk_submarine_recipe_beam;
            sub_lamp.image_scale = 1.0;
            sub_lamp.special_scale = 0.85;
            sub_lamp.add_blend = make_color_rgb(95, 86, 42);
            sub_lamp.sub_blend = make_color_rgb(235, 226, 175);
            sub_lamp.image_blend = sub_lamp.sub_blend;
            sub_lamp.image_xscale = sub_lamp.image_scale;
            sub_lamp.image_yscale = sub_lamp.image_scale;
            sub_lamp.image_angle = sub_light_angle;
            sub_halo = instance_create(sub_x, sub_y + 12, Light);
            sub_halo.image_scale = 1.65;
            sub_halo.special_scale = 0.08;
            sub_halo.add_blend = make_color_rgb(38, 34, 18);
            sub_halo.sub_blend = make_color_rgb(145, 138, 108);
            sub_halo.image_blend = sub_halo.sub_blend;
            sub_halo.image_xscale = sub_halo.image_scale;
            sub_halo.image_yscale = sub_halo.image_scale;
        }
    }
    if (!sub_diving)
    {
        if (!sub_has_winch && !instance_exists(CatSwim) && Lure.state == 0)
        {
            Rod.swing_locked = true;
            var over_btn = collision_point(mouse_x, mouse_y, Buttons, 0, 0);
            if (controls && (keyboard_check_pressed(vk_space)
                || (mouse_check_button_pressed(mb_left) && (!over_btn || !over_btn.visible))
                || (pad && gamepad_button_check_pressed(0, gp_shoulderrb))))
            {
                sub_notice = "Exploration only - buy Bow Winch in Tools shop";
                sub_notice_until = current_time + 2800;
            }
        }
        event_inherited();
        exit;
    }
}
else if (dive && sub_oxygen > 0)
{
    sub_ascending = !sub_ascending;
    sub_cast_armed = false;
    sub_peek_timer = 0;
}

if (lamp) sub_lamp_on = !sub_lamp_on;
sub_oxygen = max(0, sub_oxygen - sub_dt);
if (sub_oxygen <= 0)
{
    sub_ascending = true;
    sub_drop = 0;
    sub_peek_timer = 0;
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

if (controls)
{
    var tilt = (keyboard_check(ord("I")) || keyboard_check(ord("E")))
        - (keyboard_check(ord("K")) || keyboard_check(ord("C")));
    if (pad)
    {
        tilt += gamepad_button_check(0, gp_shoulderr) - gamepad_button_check(0, gp_shoulderl);
        var rx = gamepad_axis_value(0, gp_axisrh);
        var ry = gamepad_axis_value(0, gp_axisrv);
        if (point_distance(0, 0, rx, ry) > 0.35)
            sub_light_pitch = clamp(darctan2(-ry, max(0.15, abs(rx))), -75, 75);
    }
    if (tilt != 0)
        sub_light_pitch = clamp(sub_light_pitch + clamp(tilt, -1, 1) * 95 * sub_dt, -75, 75);
}

var peek_pressed = controls && !sub_ascending && Lure.state == 0 && sub_drop == 0
    && (keyboard_check_pressed(ord("F")) || (pad && gamepad_button_check_pressed(0, gp_shoulderlb)));
var peek_held = controls && !sub_ascending && Lure.state == 0 && sub_drop == 0
    && (keyboard_check(ord("F")) || (pad && gamepad_button_check(0, gp_shoulderlb)));
if (peek_pressed)
{
    sub_peek_timer = 1.6;
    sub_peek_held = 0;
}
else if (peek_held && sub_peek_timer > 0 && sub_peek_held < 2.5)
{
    sub_peek_held += sub_dt;
    sub_peek_timer = max(sub_peek_timer, 0.35);
}
else
{
    sub_peek_timer = max(0, sub_peek_timer - sub_dt);
}
var peek_target = 0;
if (sub_peek_timer > 0 && !sub_ascending && Lure.state == 0 && sub_drop == 0) peek_target = 1;
sub_peek_amount += (peek_target - sub_peek_amount) * min(1, 9 * sub_dt);

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
    next_x = clamp(sub_x + dx / steps, sub_dive_min_x, min(MAX_X, room_width - 115));
    next_y = clamp(sub_y + dy / steps, sub_entry_y, min(room_height - 115, 1294 + sub_depth_limit));
    if (next_x != sub_x && collision_rectangle(next_x - 105, sub_y - 40, next_x + 105, sub_y + 56, Collidable, true, true) == noone)
        sub_x = next_x;
    if (next_y != sub_y && collision_rectangle(sub_x - 105, next_y - 40, sub_x + 105, next_y + 56, Collidable, true, true) == noone)
        sub_y = next_y;
}
if (sub_x != start_x) sub_facing = sign(sub_x - start_x);
if (sub_facing == 1) sub_light_angle = sub_light_pitch;
else sub_light_angle = 180 - sub_light_pitch;

if (sub_ascending && can_move)
{
    if (sub_y == start_y) sub_blocked_time += sub_dt;
    else sub_blocked_time = 0;
    if (sub_y <= sub_entry_y || sub_blocked_time >= 2)
    {
        if (sub_blocked_time >= 2) sub_x = sub_entry_x;
        sub_diving = false;
        sub_ascending = false;
        sub_peek_timer = 0;
        sub_peek_amount = 0;
        sub_blocked_time = 0;
        sub_oxygen = sub_oxygen_max;
        if (instance_exists(sub_lamp)) instance_destroy(sub_lamp);
        sub_lamp = noone;
        if (instance_exists(sub_halo)) instance_destroy(sub_halo);
        sub_halo = noone;
        Girl.x = sub_x;
        Girl.y = home_y;
        Girl.rod_anchor_x_root = sub_rod_anchor_x;
        Girl.rod_anchor_y_root = sub_rod_anchor_y;
        if (sub_has_winch) Rod.swing_locked = sub_rod_locked;
        else Rod.swing_locked = true;
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
sub_port_x = sub_x + 116 * sub_facing;
sub_port_y = sub_y + 18;
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
    sub_lamp.x = sub_x + 114 * sub_facing;
    sub_lamp.y = sub_y + 10;
    sub_lamp.image_angle = sub_light_angle;
    sub_lamp.visible = sub_lamp_on && sub_oxygen > 0;
}
if (instance_exists(sub_halo))
{
    sub_halo.x = sub_x;
    sub_halo.y = sub_y + 12;
    sub_halo.visible = sub_oxygen > 0;
}

if (!controls || sub_ascending || Lure.state != 0 || sub_drop > 0)
{
    sub_cast_armed = false;
    exit;
}
if (!sub_has_winch)
{
    sub_cast_armed = false;
    if (keyboard_check_pressed(vk_space) || mouse_check_button_pressed(mb_left) || (pad && gamepad_button_check_pressed(0, gp_shoulderrb)))
    {
        sub_notice = "Buy Bow Winch in Tools shop to cast";
        sub_notice_until = current_time + 2800;
    }
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
