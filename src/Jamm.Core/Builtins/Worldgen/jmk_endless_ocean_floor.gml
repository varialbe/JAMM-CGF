// Shared lattice sampled every 512 px: seed, sample index, seam y, room bottom, content chunk.
var seed = argument0;
var tile = max(0, argument1);
var anchor = argument2;
var bottom = argument3;
var content_tile = argument4 * 4;

var cell = floor(tile / 9);
var t = tile / 9 - cell;
t = t * t * (3 - 2 * t);
var broad = lerp(jamm_chunk_unit(seed, cell, 0), jamm_chunk_unit(seed, cell + 1, 0), t);
cell = floor(tile / 3);
t = tile / 3 - cell;
t = t * t * (3 - 2 * t);
var detail = lerp(jamm_chunk_unit(seed, cell, 1), jamm_chunk_unit(seed, cell + 1, 1), t);

var distance = clamp((tile - content_tile) / 320, 0, 1);
distance = distance * distance * (3 - 2 * distance);
var depth_fraction = 0.726 + 0.146 * distance + 0.16 * (broad - 0.5) + 0.052 * (detail - 0.5);
var seeded = lerp(1244, bottom, depth_fraction);

// Ease away from the last native mask over three chunks. Keep the connector
// deep under the reserved area, then ease into the seeded profile.
var connection = clamp(tile / 12, 0, 1);
connection = connection * connection * (3 - 2 * connection);
var open_ocean = clamp((tile - content_tile) / 12, 0, 1);
open_ocean = open_ocean * open_ocean * (3 - 2 * open_ocean);
var connector = max(anchor - 420, bottom * 0.79) + 280 * (broad - 0.5);
return round(clamp(lerp(anchor, lerp(connector, seeded, open_ocean), connection), 1500, bottom - 1));
