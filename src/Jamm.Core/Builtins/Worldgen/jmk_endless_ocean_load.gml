var owned = argument1;
var layout = jmk_endless_ocean_layout(ocean_seed, argument0, ocean_start, 1244);
for (var i = 0; i < 16; i += 4)
{
    var rock = instance_create(layout[i], layout[i + 1], CaveRockBig);
    rock.image_xscale = layout[i + 2];
    rock.image_yscale = layout[i + 3];
    ds_list_add(owned, rock);
}

var catalog_find = asset_get_index("jamm_catalog_find");
for (var i = 16; i < 28; i += 4)
{
    var sx = layout[i];
    var sy = layout[i + 1];
    // Check the fish's spawn envelope against actual live collision masks.
    if (collision_rectangle(sx - 180, sy - 180, sx + 180, sy + 180, Collidable, true, false) == noone)
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
