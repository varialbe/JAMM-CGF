var amplitude = 1;
if (instance_exists(obj_jamm_weather) && room == NewWorld)
    with (obj_jamm_weather)
    {
        if (weather_previous >= 0) amplitude = lerp(amplitude, weather_paint[weather_previous, 3], weather_tail);
        if (weather_live >= 0) amplitude = lerp(amplitude, weather_paint[weather_live, 3], weather_alpha);
    }
return amplitude;
