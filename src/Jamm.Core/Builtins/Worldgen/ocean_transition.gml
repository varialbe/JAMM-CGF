if (!instance_exists(Girl) || !instance_exists(SaveManager) || !SaveManager.loaded) exit;
if (!ocean_inside_initialized)
{
    ocean_was_inside = Girl.x >= ocean_start;
    ocean_inside_initialized = true;
    exit;
}
if (!ocean_hint_shown && instance_exists(Boat))
{
    var ocean_boat = instance_find(Boat, 0);
    if (ocean_boat.object_index != Boat7 && ocean_boat.MAX_X >= 30000 && ocean_boat.MAX_X < ocean_start && Girl.x >= ocean_boat.MAX_X - 50)
    {
        ocean_hint_shown = true;
        var ocean_hint = instance_create(0, 0, obj_jmk_endless_ocean_message);
        if (instance_exists(ocean_hint)) ocean_hint.text = "Endless Ocean lies further east - Tech Boat needed";
    }
}
if (!ocean_was_inside && Girl.x >= ocean_start)
{
    ocean_was_inside = true;
    var ocean_message = instance_create(0, 0, obj_jmk_endless_ocean_message);
    if (instance_exists(ocean_message)) ocean_message.text = "Endless Ocean";
}
else if (ocean_was_inside && Girl.x < ocean_start - 300)
{
    ocean_was_inside = false;
    var ocean_message = instance_create(0, 0, obj_jmk_endless_ocean_message);
    if (instance_exists(ocean_message)) ocean_message.text = "Leaving the Endless Ocean";
}
