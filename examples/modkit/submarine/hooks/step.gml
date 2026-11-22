if (!sub_diving)
{
    event_inherited();
    exit;
}
// Retain the normal boat's cargo bookkeeping; restore the submerged rider after its wave calculation.
event_inherited();
if (instance_exists(Camera)) Camera.atOrigin = 0;
Girl.x = sub_x;
Girl.y = sub_y;
image_angle = 0;
image_angle_vel = 0;
move_left = 0;
move_right = 0;
fish_limit = base_limit + Girl.gear_boat_size_mod;
