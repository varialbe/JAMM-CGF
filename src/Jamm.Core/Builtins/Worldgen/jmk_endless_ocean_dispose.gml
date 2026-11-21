var member = argument0;
// A mod may have deactivated this instance while it was out of view.
instance_activate_object(member);
with (member)
{
    if (variable_instance_exists(id, "jmk_endless_ocean_fish"))
    {
        // Preserve vanilla interactions with fish on the hook, in the net or aboard.
        if (state == 4 || state == 15) return false;
        if (instance_exists(Lure) && Lure.fish == id && Lure.hookedFishSize > 0) return false;
    }
    instance_destroy();
}
return true;
