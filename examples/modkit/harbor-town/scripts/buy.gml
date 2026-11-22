/// buy(building)
var building = argument0;
if (!harbor_ready || !instance_exists(SaveManager) || !SaveManager.loaded || SaveManager.saving_locked)
    return "Wait for the save slot to finish loading.";
var level = harbor_level[building];
var price = jmk_harbor_town_script_cost(harbor_base[building], level);
if (Girl.dollars < price) return "Not enough money.";
if (level + 1 == level) return "This save cannot store a higher level.";

Girl.dollars -= price;
harbor_level[building] = level + 1;
harbor_fish_refresh = 0;
with (SaveManager) event_user(1);
return harbor_name[building] + " upgraded to level " + string_format(level + 1, 0, 0) + ".";
