/// research_bonus(level, kind): sell=0, bite=1, luck=2.
var level = max(0, argument0);
switch (argument1)
{
    case 0: return min(0.10, 2 * ln(1 + level) / 100);
    case 1: return min(0.08, 1.5 * ln(1 + level) / 100);
    default: return min(0.05, ln(1 + level) / 100);
}
