/// command(arguments) is shared by JAMM Console and the benchmark scene.
var harbor = instance_find(jmk_harbor_town_object_harbor, 0);
if (!instance_exists(harbor) || !harbor.harbor_ready || !instance_exists(Girl) || !instance_exists(SaveManager)
    || !variable_instance_exists(SaveManager.id, "loaded") || !SaveManager.loaded)
    return "Load a fishing save before using harbor.";

var input = argument0;
if (input == "")
{
    return "Lighthouse " + string_format(harbor.harbor_level[0], 0, 0)
        + " | Aquarium " + string_format(harbor.harbor_level[1], 0, 0)
        + " | Golden Cat " + string_format(harbor.harbor_level[2], 0, 0)
        + " | Research " + string_format(harbor.harbor_level[3], 0, 0)
        + " | Donated " + jmk_harbor_town_script_format(harbor.harbor_donated)
        + " | " + jmk_harbor_town_script_title(harbor.harbor_donated)
        + " | harbor <lighthouse|aquarium|statue|research|donate|money> <n>";
}
var space = string_pos(" ", input);
var target = string_copy(input, 1, max(0, space - 1));
var digits = string_delete(input, 1, space);
var length = string_length(digits);
if (space == 0 || length == 0 || length > 16)
    return "Usage: harbor <lighthouse|aquarium|statue|research|donate|money> <whole number>.";
for (var i = 1; i <= length; i += 1)
    if (string_pos(string_char_at(digits, i), "0123456789") == 0)
        return "Enter a non-negative whole number.";
var amount = real(digits);
var max_money = 1000000 * 1000000000;
if (target == "money")
{
    Girl.dollars = min(max_money, Girl.dollars + amount);
    with (SaveManager) event_user(1);
    return "Wallet: " + jmk_harbor_town_script_format(Girl.dollars) + ". Native saves keep this change.";
}
if (target == "donate") return jmk_harbor_town_script_donate(amount);
if (amount >= 9007199254740992) return "Enter a level below 9,007,199,254,740,992.";
with (harbor)
{
    for (var i = 0; i < 4; i += 1)
    {
        if (harbor_key[i] != target) continue;
        harbor_level[i] = amount;
        harbor_fish_refresh = 0;
        with (SaveManager) event_user(1);
        return harbor_name[i] + " level " + string_format(amount, 0, 0) + ". Native saves keep this change.";
    }
}
return "Choose lighthouse, aquarium, statue, research, donate, or money.";
