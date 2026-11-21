/// stage(level)
var level = argument0;
if (level >= 100) return 4;
if (level >= 20) return 3;
if (level >= 5) return 2;
return level > 0;
