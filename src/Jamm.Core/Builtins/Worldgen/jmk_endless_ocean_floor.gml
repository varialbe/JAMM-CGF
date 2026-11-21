// The lattice is shared across chunks. Each tile samples the same continuous profile.
var seed = argument0;
var tile = max(0, argument1);
var cell = floor(tile / 5);
var t = tile / 5 - cell;
t = t * t * (3 - 2 * t);
var broad = lerp(jamm_chunk_unit(seed, cell, 0), jamm_chunk_unit(seed, cell + 1, 0), t);

cell = floor(tile / 2);
t = tile / 2 - cell;
t = t * t * (3 - 2 * t);
var detail = lerp(jamm_chunk_unit(seed, cell, 1), jamm_chunk_unit(seed, cell + 1, 1), t);
return round(10700 + broad * 1140 + detail * 170);
