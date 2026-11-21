if (instance_exists(obj_jamm_weather))
    return argument0 * (1 - obj_jamm_weather.weather_mix);
return argument0;
