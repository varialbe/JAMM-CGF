// Seed, chunk index, seam y, room bottom, content chunk. Positions are chunk-local.
var seed = argument0;
var chunk = argument1;
var bottom = argument3;
var content_chunk = argument4;

// Slots 0–4 are shared floor heights. Slot 5 is the biome. Slot 6 flags wrecks.
// Slots 7–18 contain three [x, y, species, path phase] records.
var layout;
layout[18] = 0;
var shallowest = bottom;
for (var i = 0; i <= 4; i += 1)
{
    layout[i] = jmk_endless_ocean_floor(seed, chunk * 4 + i, argument2, bottom, content_chunk);
    shallowest = min(shallowest, layout[i]);
}
var biome = jmk_endless_ocean_biome(layout[2], bottom);
layout[5] = biome;
var wreck = jmk_endless_ocean_poi(seed, chunk, content_chunk);
layout[6] = wreck;
if (chunk < content_chunk) return layout;

for (var band = 0; band < 3; band += 1)
{
    var channel = 60 + band * 4;
    var slot = 7 + band * 4;
    layout[slot] = 600 + floor(jamm_chunk_unit(seed, chunk, channel) * 848);
    var offset = jamm_chunk_unit(seed, chunk, channel + 1);
    var spawn_y = 2050 + offset * 1150;
    if (band == 1) spawn_y = lerp(1244, shallowest, 0.32 + offset * 0.15);
    if (band == 2)
    {
        spawn_y = shallowest - 1080 - offset * 460;
        if (wreck) layout[slot] = 1120;
    }
    layout[slot + 1] = round(spawn_y);
    layout[slot + 2] = jmk_endless_ocean_species(seed, chunk - content_chunk, band, biome, wreck);
    layout[slot + 3] = jamm_chunk_unit(seed, chunk, channel + 3);
}
return layout;
