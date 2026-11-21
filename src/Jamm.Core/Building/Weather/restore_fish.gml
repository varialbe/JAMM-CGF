for (var i = 0; i < ds_list_size(weather_parked); i += 1)
    instance_activate_object(ds_list_find_value(weather_parked, i));
ds_list_clear(weather_parked);
return 0;
