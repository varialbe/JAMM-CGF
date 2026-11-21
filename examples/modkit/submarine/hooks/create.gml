sub_diving = false;
sub_ascending = false;
sub_oxygen_max = 110;
sub_oxygen = sub_oxygen_max;
sub_depth_limit = 4800;
sub_entry_y = 1338;
sub_dive_min_x = 1550;
sub_lamp = noone;
sub_halo = noone;
sub_lamp_on = true;
sub_light_pitch = 0;
sub_light_angle = 0;
sub_peek_timer = 0;
sub_peek_held = 0;
sub_peek_amount = 0;
sub_cam_zoom = 1;
sub_cam_blend = 0;
sub_cam_look_x = 0;
sub_cam_x = x;
sub_cam_y = y;
if (instance_exists(Camera))
{
    sub_cam_x = Camera.x;
    sub_cam_y = Camera.y;
}
sub_cam_base_w = view_wview[0];
sub_cam_base_h = view_hview[0];
sub_cam_base_hb = view_hborder[0];
sub_cam_base_vb = view_vborder[0];
sub_winch_slot = jamm_item_find("tool", "submarine:bow-winch");
sub_has_winch = (sub_winch_slot >= 0 && instance_exists(ToolData)
    && sub_winch_slot < array_length_1d(ToolData.unlocked) && ToolData.unlocked[sub_winch_slot] > 0);
sub_prev_winch = sub_has_winch;
if (instance_exists(icon_Tools) && !icon_Tools.unlocked)
    icon_Tools.unlocked = 1;
sub_facing = 1;
sub_cast_armed = false;
sub_drop = 0;
sub_drop_length = 150;
sub_blocked_time = 0;
sub_notice = "";
sub_notice_until = 0;
sub_dt = 1 / 30;
boat_bg.visible = false;
