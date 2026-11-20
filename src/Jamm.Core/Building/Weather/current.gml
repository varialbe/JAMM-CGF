if (instance_exists(obj_jamm_weather))
    with (obj_jamm_weather)
        if (weather_live >= 0) return weather_key[weather_live];
return "";
