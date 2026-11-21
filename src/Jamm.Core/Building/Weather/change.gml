var target = argument0;
var immediate = argument1;
var previous = weather_live;
if (previous >= 0)
{
    var hook = weather_hook[previous, 2];
    if (hook >= 0) script_execute(hook, weather_key[previous], weather_alpha);
}
if (target < 0 || !weather_rule[target, 5])
    jamm_weather_restore_fish();
weather_previous = previous;
weather_outgoing = weather_alpha;
weather_tail = weather_alpha;
if (immediate)
{
    weather_previous = -1;
    weather_tail = 0;
    for (var i = 0; i < 4; i += 1)
    {
        if (weather_voice[i] >= 0) audio_stop_sound(weather_voice[i]);
        weather_voice[i] = -1;
        weather_voice_config[i] = -1;
        weather_voice_gain[i] = 0;
    }
    weather_mix = 0;
}
for (var channel = 0; channel < 2; channel += 1)
{
    var old = channel + 2;
    if (weather_voice[channel] >= 0)
    {
        if (weather_voice[old] >= 0) audio_stop_sound(weather_voice[old]);
        weather_voice[old] = weather_voice[channel];
        weather_voice_config[old] = weather_voice_config[channel];
        weather_voice_gain[old] = weather_voice_gain[channel];
        weather_voice_time[old] = current_time;
    }
    weather_voice[channel] = -1;
    weather_voice_config[channel] = -1;
    weather_voice_gain[channel] = 0;
    weather_voice_time[channel] = current_time;
}
weather_live = target;
weather_alpha = 0;
weather_started = current_time;
weather_foreground_scale = 1;
weather_check_population = 0;
if (target >= 0)
{
    if (weather_rule[target, 4] <= 0) weather_alpha = 1;
    var hook = weather_hook[target, 0];
    if (hook >= 0) script_execute(hook, weather_key[target], weather_alpha);
    for (var channel = 0; channel < 2; channel += 1)
    {
        var sound = weather_music[target, 0];
        var loop = weather_music[target, 1];
        if (channel == 1)
        {
            sound = weather_ambience[target, 0];
            loop = weather_ambience[target, 1];
        }
        if (sound >= 0)
        {
            weather_voice[channel] = audio_play_sound(sound, 1, loop);
            audio_sound_gain(weather_voice[channel], 0, 0);
            weather_voice_config[channel] = target;
        }
    }
}
jamm_weather_population();
