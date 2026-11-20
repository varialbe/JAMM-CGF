if (instance_number(obj_jamm_weather) > 1)
{
    instance_destroy();
    exit;
}
weather_vanilla_fish = ds_map_create();
weather_parked = ds_list_create();
weather_slot_owner = -1;
weather_slot_number = -1;
weather_seed = 0;
weather_day = -1;
weather_plan = -1;
weather_begin = -1;
weather_finish = -1;
weather_schedule_file = "";
weather_override = -2;
weather_live = -1;
weather_previous = -1;
weather_started = current_time;
weather_outgoing = 0;
weather_alpha = 0;
weather_tail = 0;
weather_night = false;
weather_phase = 2;
weather_second = 0;
weather_now = 0;
weather_check_population = 0;
weather_foreground_scale = 1;
weather_mix = 0;
for (var channel = 0; channel < 4; channel += 1)
{
    weather_voice[channel] = -1;
    weather_voice_config[channel] = -1;
    weather_voice_time[channel] = 0;
    weather_voice_gain[channel] = 0;
}
