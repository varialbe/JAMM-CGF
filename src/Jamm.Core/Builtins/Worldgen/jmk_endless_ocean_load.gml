var chunk = argument0;
var owned = argument1;
var left = ocean_start + chunk * 2048;
var layout = jmk_endless_ocean_layout(ocean_seed, chunk, ocean_anchor_y, room_height, ocean_content_chunk);
var biome = layout[5];

for (var i = 0; i < 4; i += 1)
{
    var depth_fraction = ((layout[i] + layout[i + 1]) / 2 - 1244) / (room_height - 1244);
    var tint = merge_color(make_color_rgb(246, 223, 183), make_color_rgb(153, 176, 191),
                           clamp((depth_fraction - 0.73) * 5, 0, 1));
    jmk_endless_ocean_ground(owned, left + i * 512, layout[i], layout[i + 1], tint);
}
if (chunk < ocean_content_chunk) exit;
jmk_endless_ocean_scenery(chunk, owned, left, biome, layout[6]);

var catalog_find = asset_get_index("jamm_catalog_find");
for (var i = 7; i < 19; i += 4)
{
    var sx = left + layout[i];
    var sy = layout[i + 1];
    // Check the fish's spawn envelope against actual live collision masks.
    if (collision_rectangle(sx - 260, sy - 260, sx + 260, sy + 260, Collidable, true, false) == noone)
    {
        var species = layout[i + 2];
        var swimmer = instance_create(sx, sy, species);
        swimmer.jmk_endless_ocean_fish = true;
        swimmer.path_position = layout[i + 3];
        if (catalog_find >= 0 && script_exists(catalog_find))
            swimmer.catalog_index = script_execute(catalog_find, species);
        else
            swimmer.catalog_index = get_index_for_name(fish_object_to_name(species));
        ds_list_add(owned, swimmer);
    }
}
