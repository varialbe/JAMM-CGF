// World-grid Wang tile selector (0..15): seed, cell x, cell y.
// Shared edge hashes guarantee matching rock seams between neighboring cells.
var seed = argument0;
var cx = argument1;
var cy = argument2;

var top = jamm_chunk_unit(seed, cx + cy * 8192, 91) >= 0.5;
var bottom = jamm_chunk_unit(seed, cx + (cy + 1) * 8192, 91) >= 0.5;
var left = jamm_chunk_unit(seed, cx + cy * 8192, 92) >= 0.5;
var right = jamm_chunk_unit(seed, (cx + 1) + cy * 8192, 92) >= 0.5;

return top * 8 + right * 4 + bottom * 2 + left;
