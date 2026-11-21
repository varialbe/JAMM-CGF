var chunk = argument0;
var owned = argument1;
var left = argument2;
var biome = argument3;
var wreck = argument4;
var seed = ocean_seed;
var offset = chunk * 2048;

// A single grounded outcrop per chunk leaves open water above and on both sides.
var roll = jamm_chunk_unit(seed, chunk, 21);
if (biome != 0 || roll > 0.45 || wreck)
{
    var kinds;
    kinds[0] = CaveRockBig;
    kinds[1] = CaveRockWide;
    kinds[2] = CaveRockLeg;
    kinds[3] = CaveRockSig;
    kinds[4] = CaveRockTear;
    kinds[5] = CaveRockTee;
    kinds[6] = CaveRockWedge;
    var rock_kind = kinds[floor(jamm_chunk_unit(seed, chunk, 22) * 7)];
    var rx = wreck ? 1460 : 900 + floor(jamm_chunk_unit(seed, chunk, 23) * 248);
    var ry = jmk_endless_ocean_height(seed, offset + rx, ocean_anchor_y, room_height, ocean_content_chunk);
    var scale = (biome == 0 ? 0.4 : 0.56) + jamm_chunk_unit(seed, chunk, 24) * 0.24;
    var sx = roll > 0.5 ? scale : -scale;
    var sy = roll > 0.78 ? -scale : scale;
    var rotation = (jamm_chunk_unit(seed, chunk, 25) - 0.5) * 54;
    var sprite = object_get_sprite(rock_kind);
    var dx = (sprite_get_width(sprite) / 2 - sprite_get_xoffset(sprite)) * sx;
    var dy = (sprite_get_height(sprite) / 2 - sprite_get_yoffset(sprite)) * sy;
    var rock = instance_create(left + rx - dx * dcos(rotation) - dy * dsin(rotation),
                               ry + 70 + dx * dsin(rotation) - dy * dcos(rotation), rock_kind);
    rock.image_xscale = sx;
    rock.image_yscale = sy;
    rock.image_angle = rotation;
    ds_list_add(owned, rock);
}

// Use the placed NewWorld species as scenery, with separately owned native lighting.
var plants;
plants[0] = JWeedSmall;
plants[1] = JWeedMedium;
plants[2] = Coral;
plants[3] = Coral2;
plants[4] = CoralDark;
plants[5] = CoralFG3;
plants[6] = GlowPlant;
plants[7] = BulbPlantShort;
plants[8] = ChainPlant;
for (var i = 0; i < 2; i += 1)
{
    var local_x = 360 + i * 1280 + floor(jamm_chunk_unit(seed, chunk, 70 + i) * 116);
    var plant_y = jmk_endless_ocean_height(seed, offset + local_x, ocean_anchor_y, room_height, ocean_content_chunk);
    var plant_type = plants[biome * 3 + floor(jamm_chunk_unit(seed, chunk, 73 + i) * 3)];
    var plant = instance_create(left + local_x, plant_y + 12, obj_jmk_ocean_decor);
    plant.sprite_index = object_get_sprite(plant_type);
    plant.image_xscale = jamm_chunk_unit(seed, chunk, 76 + i) > 0.5 ? 1 : -1;
    plant.image_speed = 0;
    ds_list_add(owned, plant);

    if (i == 0 && biome == 2)
    {
        var light = instance_create(plant.x, plant.y - 120, Light);
        light.image_scale = 2.8;
        light.special_scale = 1.4;
        light.add_blend = make_color_rgb(28, 93, 78);
        light.sub_blend = make_color_rgb(130, 208, 176);
        ds_list_add(owned, light);
    }
}

if (wreck)
{
    var wreck_y = jmk_endless_ocean_height(seed, offset + 1040, ocean_anchor_y, room_height, ocean_content_chunk);
    var hull = instance_create(left + 1040, wreck_y - 65, obj_jmk_ocean_decor);
    hull.sprite_index = spr_jmk_ocean_wreck;
    hull.image_speed = 0;
    ds_list_add(owned, hull);
    var beacon = instance_create(hull.x - 120, hull.y - 70, Light);
    beacon.image_scale = 3.3;
    beacon.special_scale = 1.7;
    beacon.add_blend = make_color_rgb(107, 57, 19);
    beacon.sub_blend = make_color_rgb(235, 176, 99);
    ds_list_add(owned, beacon);
}
