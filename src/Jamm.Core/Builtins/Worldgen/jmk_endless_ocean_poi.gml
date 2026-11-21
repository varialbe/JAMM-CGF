// One independently seeded site per 15 content chunks, with 10–20 chunks between sites.
var chunk = argument1 - argument2;
if (chunk < 15) return false;
var group = floor(chunk / 15);
return chunk % 15 == 5 + floor(6 * jamm_chunk_unit(argument0, group, 31));
