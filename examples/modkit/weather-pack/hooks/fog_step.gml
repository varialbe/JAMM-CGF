// An equipped lightbulb burns a gap through the foreground haze near the sea.
weather_foreground_scale = 1;
if (instance_exists(Lightbulb) && Lightbulb.y > 1020 && Lightbulb.y < 1580)
    weather_foreground_scale = 0.52;
