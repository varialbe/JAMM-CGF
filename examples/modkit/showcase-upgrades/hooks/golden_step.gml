// Mechanism: fish_was_caught() sets Fish.hasGivenExperience = 1 and runs fish_appraise()
// to compute Fish.value right as the catch completes, and fish_die() pays Girl.dollars += value
// on deposit. fish_respawn() resets hasGivenExperience = 0.
// This step hook (run every Begin Step by obj_jamm_api when Golden Touch is equipped)
// resets jmk_golden_touch_scaled = 0 while !hasGivenExperience, and scales Fish.value
// by (1 + 0.25 * stacks) exactly once when hasGivenExperience becomes 1.
// argument0 = installed stack count on the active rod.
var jamm_stacks;
jamm_stacks = argument0;
if (instance_exists(Lure) && Lure.hookedFishSize > 0 && Lure.fish > 0 && instance_exists(Lure.fish))
{
    with (Lure.fish)
    {
        if (!hasGivenExperience)
        {
            jmk_golden_touch_scaled = 0;
        }
        else if (!variable_instance_exists(id, "jmk_golden_touch_scaled") || !jmk_golden_touch_scaled)
        {
            jmk_golden_touch_scaled = 1;
            value = round(value * (1 + (0.25 * jamm_stacks)));
        }
    }
}
