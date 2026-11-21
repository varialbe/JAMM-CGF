if (room != NewWorld || !instance_exists(SaveManager) || !SaveManager.loaded
    || !instance_exists(AmbianceController) || !instance_exists(Girl))
{
    if (weather_live >= 0 || weather_previous >= 0) jamm_weather_change(-1, true);
    exit;
}
if (weather_slot_owner != SaveManager.id || weather_slot_number != SaveManager.saveflag)
{
    jamm_weather_change(-1, true);
    weather_slot_owner = SaveManager.id;
    weather_slot_number = SaveManager.saveflag;
    weather_override = -2;
    jamm_weather_slot();
}
weather_now = current_time;
weather_second = AmbianceController.minute * 60 + AmbianceController.second;
var was_night = weather_night;
weather_night = is_it_night();
if (weather_night && !was_night)
{
    jamm_weather_restore_fish();
    for (var i = 0; i < 4; i += 1)
    {
        if (weather_voice[i] >= 0 && (weather_rule[weather_voice_config[i], 1] & 8) == 0)
        {
            audio_stop_sound(weather_voice[i]);
            weather_voice[i] = -1;
        }
    }
}
weather_phase = 2;
if (weather_night) weather_phase = 8;
else if (weather_second < 90) weather_phase = 1;
else if (weather_second >= 2940 && weather_second < 3060) weather_phase = 4;

var day = floor(date_current_datetime() * 24 + 0.0000001);
if (day != weather_day)
{
    weather_day = day;
    jamm_weather_schedule();
    ini_open(weather_schedule_file);
    ini_write_real("Weather", "day", weather_day);
    var key = "";
    if (weather_plan >= 0) key = weather_key[weather_plan];
    ini_write_string("Weather", "id", key);
    ini_write_real("Weather", "start", weather_begin);
    ini_write_real("Weather", "end", weather_finish);
    ini_close();
}

var target = jamm_weather_active();
if (target != weather_live)
    jamm_weather_change(target, weather_night);
if (weather_night && weather_previous >= 0 && (weather_rule[weather_previous, 1] & 8) == 0)
{
    weather_previous = -1;
    weather_tail = 0;
}
if (weather_previous >= 0)
{
    var seconds = weather_rule[weather_previous, 4];
    weather_tail = weather_outgoing * (1 - clamp((weather_now - weather_started) / max(1, seconds * 1000), 0, 1));
    if (weather_tail <= 0 || seconds <= 0)
    {
        weather_previous = -1;
        weather_tail = 0;
    }
}
if (weather_live >= 0)
{
    var seconds = weather_rule[weather_live, 4];
    weather_alpha = 1;
    if (seconds > 0) weather_alpha = clamp((weather_now - weather_started) / (seconds * 1000), 0, 1);
    var hook = weather_hook[weather_live, 1];
    if (hook >= 0) script_execute(hook, weather_key[weather_live], weather_alpha);
}
if (weather_now >= weather_check_population)
{
    weather_check_population = weather_now + 600;
    jamm_weather_population();
}
jamm_weather_audio();
