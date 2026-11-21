if (instance_exists(sub_lamp)) instance_destroy(sub_lamp);
if (instance_exists(sub_halo)) instance_destroy(sub_halo);
view_wview[0] = sub_cam_base_w;
view_hview[0] = sub_cam_base_h;
view_hborder[0] = sub_cam_base_hb;
view_vborder[0] = sub_cam_base_vb;
if (instance_exists(Camera))
{
    view_xview[0] = clamp(Camera.x - sub_cam_base_w * 0.5, 0, max(0, room_width - sub_cam_base_w));
    view_yview[0] = clamp(Camera.y - sub_cam_base_h * 0.5, 0, max(0, room_height - sub_cam_base_h));
}
if (instance_exists(LightingSystem))
{
    if (LightingSystem.width != sub_cam_base_w || LightingSystem.height != sub_cam_base_h)
    {
        LightingSystem.width = sub_cam_base_w;
        LightingSystem.height = sub_cam_base_h;
        if (surface_exists(LightingSystem.light_surface))
            surface_free(LightingSystem.light_surface);
        LightingSystem.light_surface = surface_create(sub_cam_base_w, sub_cam_base_h);
    }
}
if (sub_diving)
{
    Girl.visible = true;
    Girl.rod_anchor_x_root = sub_rod_anchor_x;
    Girl.rod_anchor_y_root = sub_rod_anchor_y;
    Rod.swing_locked = sub_rod_locked;
    Rod.visible = sub_rod_visible;
}
else if (!instance_exists(Handler))
{
    Rod.swing_locked = false;
}
