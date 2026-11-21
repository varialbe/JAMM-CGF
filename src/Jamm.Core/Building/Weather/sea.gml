if (instance_exists(obj_jamm_weather) && room == NewWorld
    && (obj_jamm_weather.weather_alpha > 0 || obj_jamm_weather.weather_tail > 0))
{
    if (!variable_instance_exists(id, "jamm_weather_sea_blend"))
        jamm_weather_sea_blend = -1;
    if (jamm_weather_sea_blend < 0) jamm_weather_sea_blend = image_blend;
    image_blend = jamm_weather_color(jamm_weather_sea_blend, 1);
}
else if (variable_instance_exists(id, "jamm_weather_sea_blend") && jamm_weather_sea_blend >= 0)
{
    image_blend = jamm_weather_sea_blend;
    jamm_weather_sea_blend = -1;
}
