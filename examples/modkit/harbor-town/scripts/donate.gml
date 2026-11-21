/// donate(amount): apply money to the next cumulative town milestone.
var amount = argument0;
if (!harbor_ready || !instance_exists(SaveManager) || SaveManager.saving_locked)
    return "Wait for the save slot to finish loading.";
if (amount <= 0 || amount > Girl.dollars) return "Donation must be affordable and greater than zero.";
var next = 1000000;
if (harbor_donated >= 1000000) next = 1000000000;
if (harbor_donated >= 1000000000) next = 1000000000000;
if (harbor_donated >= 1000000000000) next = 1000000000000000;
if (harbor_donated >= next) return "All Harbor Town donation titles are unlocked.";
var paid = min(amount, next - harbor_donated);
Girl.dollars -= paid;
harbor_donated += paid;
with (SaveManager) event_user(1);
return "Donated " + jmk_harbor_town_script_format(paid) + ". " + jmk_harbor_town_script_title(harbor_donated);
