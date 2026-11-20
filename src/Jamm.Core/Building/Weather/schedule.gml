// Park-Miller steps with products below the exact integer range of a GM real.
var modulus = 2147483647;
var seed = 1 + ((weather_seed + weather_day * 131071) mod (modulus - 1));
seed = (seed * 48271) mod modulus;
seed = (seed * 48271) mod modulus;
var total = 1;
for (var i = 0; i < weather_count; i += 1)
    total += weather_rule[i, 0];
var ticket = floor(seed / modulus * total);
weather_plan = -1;
weather_begin = -1;
weather_finish = -1;
for (var i = 0; i < weather_count && ticket > 0; i += 1)
{
    ticket -= weather_rule[i, 0];
    if (ticket <= 0) weather_plan = i;
}
if (weather_plan < 0) return -1;

// Pick uniformly across the allowed start seconds. Actual activation also honors vanilla night.
var mask = weather_rule[weather_plan, 1];
var span = 0;
if ((mask & 1) != 0) span += 90;
if ((mask & 2) != 0) span += 2850;
if ((mask & 4) != 0) span += 120;
if ((mask & 8) != 0) span += 540;
seed = (seed * 48271) mod modulus;
var offset = floor(seed / modulus * span);
if ((mask & 1) != 0)
{
    if (offset < 90) weather_begin = offset;
    else offset -= 90;
}
if (weather_begin < 0 && (mask & 2) != 0)
{
    if (offset < 2850) weather_begin = 90 + offset;
    else offset -= 2850;
}
if (weather_begin < 0 && (mask & 4) != 0)
{
    if (offset < 120) weather_begin = 2940 + offset;
    else offset -= 120;
}
if (weather_begin < 0) weather_begin = 3060 + offset;
seed = (seed * 48271) mod modulus;
var shortest = weather_rule[weather_plan, 2];
var longest = weather_rule[weather_plan, 3];
weather_finish = min(3600, weather_begin + shortest + floor(seed / modulus * (longest - shortest + 1)));
return weather_plan;
