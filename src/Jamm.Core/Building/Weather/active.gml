var target = weather_plan;
if (weather_override != -2) target = weather_override;
else if (weather_second < weather_begin || weather_second >= weather_finish) return -1;
if (target < 0) return -1;
var allowed = weather_rule[target, 1];
// Debug forcing bypasses daytime scheduling, with the same night opt-in as the forecast.
if (weather_override >= 0 && !weather_night) return target;
if ((allowed & weather_phase) != 0) return target;
return -1;
