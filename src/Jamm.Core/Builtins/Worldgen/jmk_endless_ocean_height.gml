// A position on the continuous lattice, measured east of the first terrain column.
var tile = max(0, argument1) / 512;
var left = floor(tile);
return lerp(jmk_endless_ocean_floor(argument0, left, argument2, argument3, argument4),
            jmk_endless_ocean_floor(argument0, left + 1, argument2, argument3, argument4), tile - left);
