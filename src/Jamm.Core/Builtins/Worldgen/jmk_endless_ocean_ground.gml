// Instantiate one strip. The slope cap reaches the two shared lattice heights.
// The rectangular piece starts at the lower end of the slope and fills to the bottom.
var owned = argument0;
var px = argument1;
var west = argument2;
var east = argument3;
var tint = argument4;
var base_y = max(west, east);
var underlay = instance_create(px, base_y, obj_jmk_ocean_ground);
underlay.image_yscale = (room_height + 2 - base_y) / 256;
underlay.image_blend = tint;
underlay.depth = 6;
ds_list_add(owned, underlay);

if (west != east)
{
    var cap = instance_create(px, min(west, east), obj_jmk_ocean_ground);
    cap.sprite_index = west < east ? spr_jmk_ocean_slope_down : spr_jmk_ocean_slope_up;
    cap.image_yscale = abs(east - west) / 255;
    cap.image_blend = tint;
    ds_list_add(owned, cap);
}
