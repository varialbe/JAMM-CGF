if (instance_exists(sub_lamp)) instance_destroy(sub_lamp);
if (sub_diving)
{
    Girl.visible = true;
    Girl.rod_anchor_x_root = sub_rod_anchor_x;
    Girl.rod_anchor_y_root = sub_rod_anchor_y;
    Rod.swing_locked = sub_rod_locked;
    Rod.visible = sub_rod_visible;
}
