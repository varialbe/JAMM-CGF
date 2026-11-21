var key = argument0;
if (!instance_exists(obj_jamm_weather) || room != NewWorld)
    return "Enter the fishing world to set weather.";
with (obj_jamm_weather)
{
    if (key == "auto")
    {
        weather_override = -2;
        return "Weather schedule enabled.";
    }
    if (key == "clear")
    {
        weather_override = -1;
        return "Weather cleared. Use weather auto to resume the schedule.";
    }
    var target = jamm_weather_find(key);
    if (target == -2) return "Weather id is ambiguous. Use package:id.";
    if (target < 0)
    {
        var options = "";
        for (var i = 0; i < weather_count; i += 1)
            options += " " + weather_key[i];
        return "Weather clear, auto, or:" + options;
    }
    weather_override = target;
    if (weather_night && (weather_rule[target, 1] & 8) == 0)
        return weather_display[target] + " queued until daytime.";
    return "Weather set to " + weather_display[target] + ".";
}
return "";
