if (!instance_exists(obj_jamm_weather)) return false;
var active = obj_jamm_weather.weather_live;
var free = state == 0 || state == 2 || state == 5 || state == 10;
if (variable_instance_exists(id, "jamm_weather_owner"))
{
    // A school retires at the end of its weather. Keep a hooked or landed fish intact.
    var swimming = free || state == 1 || state == 9;
    if (swimming && (jamm_weather_owner != active
        || (instance_exists(Girl) && abs(jamm_weather_school_anchor - max(930, Girl.x)) > 1050)))
    {
        instance_destroy();
        return true;
    }
    return false;
}
if (active < 0 || obj_jamm_weather.weather_night || !obj_jamm_weather.weather_rule[active, 5])
    return false;
if (!free || stayOutAtNight || TRUE_START_Y < 1294 || TRUE_START_Y > 1950 || y > 1950)
    return false;
if (!ds_map_exists(obj_jamm_weather.weather_vanilla_fish, object_index)) return false;
var parked = obj_jamm_weather.weather_parked;
if (ds_list_find_index(parked, id) < 0) ds_list_add(parked, id);
instance_deactivate_object(id);
return true;
