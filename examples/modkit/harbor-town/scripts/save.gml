/// save(write) runs inside the active native INI. Missing v0.1 keys default to zero.
/// SaveManager can call this during an old-save load before the Harbor instance has completed Create.
if (!variable_instance_exists(id, "harbor_key") || !is_array(harbor_key)
    || !variable_instance_exists(id, "harbor_level") || !is_array(harbor_level))
{
    harbor_key = array_create(4);
    harbor_key[0] = "lighthouse";
    harbor_key[1] = "aquarium";
    harbor_key[2] = "statue";
    harbor_key[3] = "research";
    harbor_level = array_create(4, 0);
    harbor_donated = 0;
    harbor_ready = false;
}
if (argument0 && !harbor_ready) exit;
for (var i = 0; i < 4; i += 1)
{
    if (argument0)
        ini_write_string("JAMM_Harbor", harbor_key[i], string_format(harbor_level[i], 0, 0));
    else
        harbor_level[i] = max(0, floor(ini_read_real("JAMM_Harbor", harbor_key[i], 0)));
}
if (argument0)
    ini_write_string("JAMM_Harbor", "donated", string_format(harbor_donated, 0, 0));
else
    harbor_donated = max(0, ini_read_real("JAMM_Harbor", "donated", 0));
if (!argument0)
{
    harbor_ready = true;
    harbor_menu = false;
    harbor_fish_refresh = 0;
    harbor_fish_count = 0;
}
