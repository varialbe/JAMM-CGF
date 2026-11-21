/// cost(base, current_level)
var level = max(0, floor(argument1));
var cost = argument0 * power(1.6, min(40, level));
cost *= 1 + 0.25 * max(0, level - 40);
return min(1000000 * 1000000000, round(cost));
