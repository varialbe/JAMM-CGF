// seed, chunk index, origin x, water surface y
var seed = argument0;
var chunk = argument1;
var left = argument2 + chunk * 2048;
var surface = argument3;

// [x, y, horizontal scale, vertical scale]
var layout;
layout[27] = 0;
for (var i = 0; i < 4; i += 1)
{
    var tile = chunk * 4 + i;
    var top = jmk_endless_ocean_floor(seed, tile);
    var slot = i * 4;
    layout[slot] = left + i * 512;
    layout[slot + 1] = top;
    layout[slot + 2] = 0.74 + jamm_chunk_unit(seed, tile, 2) * 0.12;
    layout[slot + 3] = 1.35;
}

// [x, y, vanilla species, starting path phase]. One fish in each of three depth bands.
for (var band = 0; band < 3; band += 1)
{
    var channel = 3 + band * 4;
    var local_x = 350 + floor(jamm_chunk_unit(seed, chunk, channel) * 1348);
    var offset = jamm_chunk_unit(seed, chunk, channel + 1);
    var alternate = jamm_chunk_unit(seed, chunk, channel + 2) >= 0.5;
    var spawn_y = surface + 850 + offset * 750;
    var species = alternate ? Celad : Inno;
    if (band == 1)
    {
        spawn_y = surface + 3700 + offset * 1700;
        species = alternate ? Mustardfish : Elder_Mustardfish;
    }
    if (band == 2)
    {
        var tile = chunk * 4 + floor(local_x / 512);
        var ceiling = min(jmk_endless_ocean_floor(seed, tile - 2), jmk_endless_ocean_floor(seed, tile - 1),
                          jmk_endless_ocean_floor(seed, tile), jmk_endless_ocean_floor(seed, tile + 1));
        spawn_y = ceiling - 355 - offset * 240;
        species = alternate ? Dijon : Garm;
    }
    var slot = 16 + band * 4;
    layout[slot] = left + local_x;
    layout[slot + 1] = round(spawn_y);
    layout[slot + 2] = species;
    layout[slot + 3] = jamm_chunk_unit(seed, chunk, channel + 3);
}
return layout;
