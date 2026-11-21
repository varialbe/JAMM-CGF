weather_schedule_file = "jamm-weather-" + string(weather_slot_number) + ".ini";
ini_open(weather_schedule_file);
weather_seed = ini_read_real("Weather", "seed", 0);
if (weather_seed <= 0 || weather_seed >= 2147483647)
{
    weather_seed = 1 + ((floor(date_current_datetime() * 86400000) + weather_slot_number * 8191 + current_time) mod 2147483646);
    ini_write_real("Weather", "seed", weather_seed);
}
weather_day = ini_read_real("Weather", "day", -1);
var key = ini_read_string("Weather", "id", "");
weather_plan = jamm_weather_find(key);
weather_begin = ini_read_real("Weather", "start", -1);
weather_finish = ini_read_real("Weather", "end", -1);
ini_close();
// An absent mod leaves that day's forecast clear. Forecasts are keyed by package id.
if (weather_begin < 0 || weather_finish <= weather_begin || weather_finish > 3600)
    weather_plan = -1;
