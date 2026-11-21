weather_mix = 0;
if (!instance_exists(Girl) || !instance_exists(Lure)) return 0;
var volume = power(Girl.background_music_volume, 2);
var surface_gain = power(1 - clamp((Lure.y - 1894) / 1606, 0, 1), 2);
for (var i = 0; i < 4; i += 1)
{
    if (weather_voice[i] < 0) continue;
    var w = weather_voice_config[i];
    var music = (i mod 2) == 0;
    var seconds = weather_ambience[w, 2];
    var loudness = weather_ambience[w, 3];
    if (music)
    {
        seconds = weather_music[w, 2];
        loudness = weather_music[w, 3];
    }
    var progress = clamp((weather_now - weather_voice_time[i]) / max(1, 1000 * seconds), 0, 1);
    if (seconds <= 0) progress = 1;
    var mix = progress;
    if (i >= 2) mix = (1 - progress) * weather_voice_gain[i];
    else weather_voice_gain[i] = mix;
    if ((i >= 2 && mix <= 0) || !audio_is_playing(weather_voice[i]))
    {
        audio_stop_sound(weather_voice[i]);
        weather_voice[i] = -1;
        continue;
    }
    if (music) weather_mix = min(1, weather_mix + mix);
    audio_sound_gain(weather_voice[i], loudness * volume * surface_gain * mix, 0);
}
return 0;
