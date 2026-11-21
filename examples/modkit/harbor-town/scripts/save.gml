/// save(write) runs inside the active native INI. Missing v0.1 keys default to zero.
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
