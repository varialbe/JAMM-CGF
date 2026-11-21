var chunk = argument0;
var owned = argument1;
var left = ocean_start + chunk * 2048;
var layout = jmk_endless_ocean_layout(ocean_seed, chunk, ocean_anchor_y, room_height, ocean_content_chunk);
var biome = layout[5];

for (var i = 0; i < 4; i += 1)
{
    jmk_endless_ocean_ground(owned, left + i * 512, layout[i], layout[i + 1]);
}
if (chunk < ocean_content_chunk) exit;
jmk_endless_ocean_scenery(chunk, owned, left, biome, layout[6]);

var catalog_find = asset_get_index("jamm_catalog_find");
var rod_inst = noone;
var girl_inst = noone;
var rod_obj = asset_get_index("Rod");
if (rod_obj >= 0 && instance_exists(rod_obj)) rod_inst = instance_find(rod_obj, 0);
var girl_obj = asset_get_index("Girl");
if (girl_obj >= 0 && instance_exists(girl_obj)) girl_inst = instance_find(girl_obj, 0);

for (var i = 7; i < 19; i += 4)
{
    var sx = left + layout[i];
    var sy = layout[i + 1];
    // Check the fish's spawn envelope against actual live collision masks.
    if (collision_rectangle(sx - 260, sy - 260, sx + 260, sy + 260, Collidable, true, false) == noone)
    {
        var species = layout[i + 2];
        var swimmer = instance_create(sx, sy, species);
        // SonarPanel normally initializes these fields in its Begin Step. A fish streamed after
        // that event can reach Draw before the panel's next pass (notably on Smooth render frames).
        jmk_endless_ocean_sonar(swimmer);
        // Initialize base_lunge_mod/offset at creation with the exact Rod_Other_10 formula so
        // generated fish never rely on Rod_Other_10 having run before Draw or deactivation.
        swimmer.base_lunge_mod = 1;
        swimmer.base_lunge_offset = 0;
        if (rod_inst != noone && girl_inst != noone &&
            variable_instance_exists(rod_inst, "upgrades") && variable_instance_exists(girl_inst, "gear_lunge_scalar"))
        {
            for (var upg = 5; upg <= 7; upg += 1)
            {
                if (rod_inst.upgrades[upg] == swimmer.object_id)
                {
                    swimmer.base_lunge_mod *= power(1.58, girl_inst.gear_attract_scalar);
                    swimmer.base_lunge_offset += 100;
                }
            }
            for (var upg = 8; upg <= 10; upg += 1)
            {
                if (rod_inst.upgrades[upg] == swimmer.object_id)
                {
                    swimmer.base_lunge_mod *= power(0.5, girl_inst.gear_repel_scalar);
                    swimmer.base_lunge_offset -= 60;
                }
            }
            swimmer.base_lunge_mod *= girl_inst.gear_lunge_scalar[swimmer.display_size - 1];
            if (girl_inst.gear_target_fish == swimmer.object_id)
            {
                swimmer.base_lunge_mod *= girl_inst.gear_target_lunge_scalar;
            }
            swimmer.base_lunge_mod *= power(1.2, rod_inst.upgrades[11]);
            swimmer.base_lunge_mod *= power(0.8, rod_inst.upgrades[12]);
        }
        swimmer.jmk_endless_ocean_fish = true;
        swimmer.path_position = layout[i + 3];
        if (catalog_find >= 0 && script_exists(catalog_find))
            swimmer.catalog_index = script_execute(catalog_find, species);
        else
            swimmer.catalog_index = get_index_for_name(fish_object_to_name(species));
        ds_list_add(owned, swimmer);
    }
}
