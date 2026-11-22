// Sandy shelf, rocky slope, trench. Depth is relative to the input room's water column.
var depth_fraction = (argument0 - 1244) / (argument1 - 1244);
if (depth_fraction < 0.77) return 0;
if (depth_fraction < 0.85) return 1;
return 2;
