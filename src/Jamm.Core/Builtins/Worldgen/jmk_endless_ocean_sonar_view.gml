if (!jmk_endless_ocean_inside()) return;
var girl_object = asset_get_index("Girl");
var lure_object = asset_get_index("Lure");
if (girl_object < 0 || lure_object < 0) return;
if (!instance_exists(girl_object) || !instance_exists(lure_object)) return;
var girl = instance_find(girl_object, 0);
var lure = instance_find(lure_object, 0);
if (girl.hide_ui) return;
if (!((lure.state == 1 || lure.state == 2 || drawOverride) ||
    (lure.fish > 0 && lure.fish.display_size == 4))) return;
shouldDraw = 1;
if (lure.deep && !cave_sonar)
{
    fixed_y = min(lure.y, room_height - 1000);
    view_xview[3] = lure.x - 1000;
    view_yview[3] = max(fixed_y - 1000, 4000);
    view_wview[3] = 2000;
    view_hview[3] = 2000;
}
else if (!lure.deep)
{
    view_yview[3] = clamp(lure.y - 1850, 0, max(0, room_height - 3700));
    view_wview[3] = 3700;
    view_hview[3] = 3700;
}
if (active && shouldDraw) view_visible[3] = true;
