var member = argument0;
// A mod may have deactivated this instance while it was out of view.
instance_activate_object(member);
with (member)
{
    if (variable_instance_exists(id, "jmk_endless_ocean_fish"))
    {
        // Preserve vanilla interactions with fish on the hook, in the net or aboard.
        var keep = (state == 4 || state == 15);
        if (!keep && instance_exists(Lure))
        {
            if (Lure.fish == id && Lure.hookedFishSize > 0) keep = true;
        }
        if (!keep && instance_exists(CatSwim))
        {
            if (CatSwim.holding == id) keep = true;
        }
        if (keep)
        {
            // FishThinner may have marked this fish deactivated; clear it so Fish_Step_2 stays active.
            deactivated = 0;
            return false;
        }
        if (instance_exists(light)) with (light) instance_destroy();
    }
    instance_destroy();
}
return true;
