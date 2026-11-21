var match = -1;
for (var i = 0; i < weather_count; i += 1)
{
    if (weather_key[i] == argument0) return i;
    if (weather_short[i] == argument0)
    {
        if (match != -1) return -2;
        match = i;
    }
}
return match;
